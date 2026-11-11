"""One-time migration of UO's other spell schools (Necromancy, Chivalry, Bushido, Ninjitsu, Spellweaving, Mysticism) into
Content/Spells.json, beside Magery: a "Schools" table (cast skill, damage skill, book, skill window, recovery, costs)
and one record per spell or move in "Spells" with its "School". The game owns the file from then on.

  python -I export_servuo_schools.py <ServUO root> <Spells.json (read and rewritten)>

Read from the scripts and asserted: the registry (Spells/Initializer.cs: id -> class), each class's SpellInfo (name,
words, reagents), RequiredSkill, RequiredMana (or BaseMana for Bushido/Ninjitsu moves), RequiredTithing, MantraNumber,
CastDelayBase (newest era branch); each school's base class rules (NecromancerSpell, PaladinSpell, SamuraiSpell,
NinjaSpell, ArcanistSpell, MysticSpell). What each spell does is written below (EFFECTS) from the spell's code, with its
numbers; schools whose effects aren't written yet export with Effect null (shown in the book, not castable).
"""
import json
import os
import re
import sys

SCHOOLS = {
    # id range, ServUO folder, book item, cast skill, damage skill, skill window above the requirement, notes
    "Necromancy": {"Ids": [100, 116], "Folder": "Necromancy", "Book": "NecromancerSpellbook", "Skill": "Necromancy", "DamageSkill": "SpiritSpeak",
                   "Window": 40, "Base": "NecromancerSpell", "Reagents": True, "Karma": "-(40 + trunc(10 * CastDelay / 0.25))",
                   "Notes": "Necromancy isn't sped by Faster Casting in the newest era (it is: CastDelayFastScalar 1 since SE); it uses reagents (pig iron, bat wing, grave dust, daemon blood, nox crystal); casting costs karma."},
    "Chivalry": {"Ids": [200, 209], "Folder": "Chivalry", "Book": "BookOfChivalry", "Skill": "Chivalry", "DamageSkill": "Chivalry",
                 "Window": 50, "Base": "PaladinSpell", "Tithing": True, "RecoveryBase": 7, "Undisturbable": True,
                 "Power": "trunc(sqrt(Karma + 20000 + ChivalryFixed * 10)) / Div",
                 "Notes": "Chivalry costs tithing points (gold given at an ankh) and mana, no reagents; its words are spoken by everyone; blows can't disturb it; recovery is 7 quarter seconds; spell power comes from karma and Chivalry (ComputePowerValue)."},
    "Bushido": {"Ids": [400, 405], "Folder": "Bushido", "Book": "BookOfBushido", "Skill": "Bushido", "DamageSkill": "Bushido", "Window": 37.5, "Base": "SamuraiSpell",
                "Below": 12.5, "NeedsSkill": True, "RecoveryBase": 7, "NoFasterCasting": True,
                "Notes": "Bushido: stances (Confidence, Evasion, Counter Attack; one at a time) and moves readied for the next blow; the skill must reach the requirement, success runs from 12.5 below it to 37.5 above; casting isn't sped by faster casting; recovery is 7 quarter seconds."},
    "Ninjitsu": {"Ids": [500, 507], "Folder": "Ninjitsu", "Book": "BookOfNinjitsu", "Skill": "Ninjitsu", "DamageSkill": "Ninjitsu", "Window": 37.5, "Base": "NinjaSpell",
                 "Below": 12.5, "NeedsSkill": True, "RecoveryBase": 7, "Hidden": True,
                 "Notes": "Ninjitsu: moves readied for the next blow (Backstab and Surprise Attack from stealth), Animal Form, Mirror Image and Shadowjump; casting doesn't reveal you; recovery is 7 quarter seconds."},
    "Spellweaving": {"Ids": [600, 615], "Folder": "Spellweaving", "Book": "SpellweavingBook", "Skill": "Spellweaving", "DamageSkill": "Spellweaving", "Window": 37.5, "Base": "ArcanistSpell",
                     "Below": 12.5, "NeedsSkill": True, "ArcaneFocus": True,
                     "Notes": "Spellweaving: power grows with an arcane focus (Focus 0-6 in the formulas), made by Arcane Circle; success from 12.5 below the requirement to 37.5 above; resisted at (50 + 2 x (target's Magic Resist - caster's Spellweaving))%. The epic arcanist quest that unlocks it in UO isn't required (quests come later)."},
    "Mysticism": {"Ids": [677, 692], "Folder": "Mysticism", "Book": "MysticBook", "Skill": "Mysticism", "DamageSkill": "Focus|Imbuing", "Window": 40, "Base": "MysticSpell",
                  "Circles": {"Length": 14.2857, "Offset": 20, "ScrollDrop": 2, "Mana": [4, 6, 9, 11, 14, 20, 40, 50]},
                  "Notes": "Mysticism: eight circles like Magery (mana 4-50, cast time (4 + circle) quarter seconds); success from 100/7 x circle - 20 to + 20 (a scroll counts two circles lower); its damage skill is the better of Focus and Imbuing (Boost in the formulas); resisted like Magery by circle."},
}


def X(expr, **fields):
    d = {"Expr": expr}
    d.update(fields)
    return d


# What each spell does, from its code. Variables: Caster.<Skill>, Target.<Skill> (MagicResist...), Caster.Karma,
# Power(div) for Chivalry's ComputePowerValue; Distance in tiles.
EFFECTS = {
    # ------------------------------------------------------------------ Necromancy
    "Animate Dead": {"Kind": "AnimateDead", "Target": "Corpse", "Range": 12,
                     "Note": "raises a creature corpse in range as an undead of its kind's group (by the corpse's fame), controlled, for (Spirit Speak + Necromancy) x 1.5... in the table below; corpses of players and summons can't be raised"},
    "Blood Oath": {"Kind": "BloodOath", "Duration": X("((Caster.SpiritSpeak - Target.MagicResist) / 8) + 8"), "Increase": 1.2,
                   "Note": "the target's blows to the caster do 20% more, and the target takes the blow's damage too (AOS.Damage)"},
    "Corpse Skin": {"Kind": "ResistCurse", "Key": "CorpseSkin", "Duration": X("((Caster.SpiritSpeak - Target.MagicResist) / 2.5) + 40"),
                    "Malus": X("min(15, (Caster.Necromancy + Caster.SpiritSpeak) * 0.075)"), "Resists": {"Physical": 10, "Fire": "-Malus", "Cold": 10, "Poison": "-Malus", "Energy": 0}},
    "Curse Weapon": {"Kind": "CurseWeapon", "Duration": X("Caster.SpiritSpeak / 3.4 + 1"), "Leech": 50, "Note": "the weapon in hand heals its wielder half the damage it does"},
    "Evil Omen": {"Kind": "EvilOmen", "Duration": X("Caster.SpiritSpeak / 12 + 1"), "DamageBonus": 25, "ResistFixed": 50,
                  "Note": "the next harmful thing to the target does 25% more damage (a poison one level more); its Magic Resist counts as 50 for it"},
    "Horrific Beast": {"Kind": "Transform", "Form": "HorrificBeast", "Body": 746, "Bonus": {"WeaponDamage": 25, "RegenHits": 20}, "Fists": [5, 15], "BlocksSpells": True},
    "Lich Form": {"Kind": "Transform", "Form": "LichForm", "Body": 749, "Resists": {"Fire": -10, "Cold": 10, "Poison": 10}, "Bonus": {"RegenMana": 13}, "HitsLoss": {"Every": 2, "Amount": 1}},
    "Mind Rot": {"Kind": "MindRot", "Duration": X("((Caster.SpiritSpeak - Target.MagicResist) / 5 + 20) * (Target.IsPlayer ? 1 : 2)"), "Scalar": X("Target.IsPlayer ? 1.25 : 2.0"),
                 "Note": "the target's spells cost this times the mana"},
    "Pain Spike": {"Kind": "PainSpike", "Damage": X("max(1, (Caster.SpiritSpeak - Target.MagicResist) / 10 + (Target.IsPlayer ? 18 : 30))"), "Again": [3, 7],
                   "Restore": 10, "Note": "direct damage (no resist); the hit points come back after 10 s if the target lives; again while it lasts: 3-7 more and +2 s"},
    "Poison Strike": {"Kind": "AreaDamage", "Damage": X("rand(32, 40) * (300 + Caster.SpiritSpeak * 9) / 1000"), "Types": {"Poison": 100}, "Radius": 2, "Falloff": [1, 2, 3],
                      "Note": "the target full damage, a tile away half, two tiles a third"},
    "Strangle": {"Kind": "Strangle", "Rounds": X("max(4, trunc(Caster.SpiritSpeak / 10))"), "Min": X("Caster.SpiritSpeak / 10 - 2"), "Max": X("Caster.SpiritSpeak / 10 + 1"),
                 "FirstDelay": 5, "CreatureScale": 1.75, "Types": {"Poison": 100},
                 "Note": "poison damage each round, more the more tired the target: x (3 - stamina share x 2), x1.75 on creatures; the delay drops from 5 s by ceil((1 + 5 x rounds left) / rounds)"},
    "Summon Familiar": {"Kind": "Familiar", "Forms": [
        {"Creature": "HordeMinionFamiliar", "Name": 1060146, "Necromancy": 30, "SpiritSpeak": 30},
        {"Creature": "ShadowWispFamiliar", "Name": 1060142, "Necromancy": 50, "SpiritSpeak": 50},
        {"Creature": "DarkWolfFamiliar", "Name": 1060143, "Necromancy": 60, "SpiritSpeak": 60},
        {"Creature": "DeathAdder", "Name": 1060145, "Necromancy": 80, "SpiritSpeak": 80},
        {"Creature": "VampireBatFamiliar", "Name": 1060144, "Necromancy": 100, "SpiritSpeak": 100}],
        "Note": "one familiar at a time, controlled; choose which (by your Necromancy and Spirit Speak)"},
    "Vampiric Embrace": {"Kind": "Transform", "Form": "VampiricEmbrace", "Hue": 33918, "Resists": {"Fire": -25}, "Bonus": {"RegenStam": 15, "RegenMana": 3}, "LifeLeech": 20,
                         "PoisonImmuneBelow": 4, "SkillAbove": [80, 120]},
    "Vengeful Spirit": {"Kind": "Revenant", "Creature": "Revenant", "Duration": X("(Caster.SpiritSpeak * 80 / 120) + 10"), "Slots": 3,
                        "Note": "a revenant hunts the target until one of them dies or the time runs out"},
    "Wither": {"Kind": "AreaDamage", "Damage": X("rand(30, 35) * (300 + Target.Karma / 100 + Caster.SpiritSpeak * 10) / 1000"), "Types": {"Cold": 100}, "Radius": 4, "Center": "Caster"},
    "Wraith Form": {"Kind": "Transform", "Form": "WraithForm", "BodyFemale": 747, "Body": 748, "Resists": {"Physical": 15, "Fire": -5, "Energy": -5},
                    "ManaLeech": X("Caster.SpiritSpeak / 5"), "Note": "your blows drain mana: Spirit Speak / 5 % of the damage"},
    "Exorcism": {"Kind": None, "Note": "banishes ghosts from champion spawns; waits for champion spawns"},
    # ------------------------------------------------------------------ Chivalry
    "Cleanse By Fire": {"Kind": "CleanseByFire", "CureChance": X("(10000 + Caster.ChivalryFixed * 7.5 - (PoisonLevel + 1) * 2000) / 100"),
                        "SelfDamage": X("clamp(50 - Power(4), 13, 55)"), "Types": {"Fire": 100}},
    "Close Wounds": {"Kind": "CloseWounds", "Reach": 2, "Heal": X("clamp(Power(6) + rand(0, 2), 7, 39)")},
    "Consecrate Weapon": {"Kind": "Consecrate", "Duration": X("Power(20) + (Caster.Karma > 5000 ? 11 : Caster.Karma >= 4999 ? 10 : Caster.Karma >= 3999 ? 8 : Caster.Karma >= 2999 ? 6 : Caster.Karma >= 1999 ? 4 : Caster.Karma >= 999 ? 2 : 0)"),
                          "DamageBonus": X("Caster.Chivalry >= 90 ? trunc((Caster.Chivalry - 90) / 2) : 0"),
                          "Note": "the weapon's damage turns wholly to whichever of the target's resists is lowest"},
    "Dispel Evil": {"Kind": "DispelEvil", "Radius": 8, "Chance": X("(50 + 100 * (Caster.Chivalry - DispelDifficulty) / (DispelFocus * 2)) / 100 * Power(2) / 100"),
                    "Note": "dispels summoned creatures near you; evil creatures may flee"},
    "Divine Fury": {"Kind": "DivineFury", "Duration": X("Power(10)"), "Bonus": {"AttackChance": 10, "WeaponDamage": 10, "WeaponSpeed": 10, "DefendChance": -20},
                    "Grand": {"Chivalry": 120, "Karma": 10000, "AttackChance": 15, "WeaponDamage": 20, "WeaponSpeed": 15, "DefendChance": -10}, "Note": "your stamina is filled"},
    "Enemy of One": {"Kind": "EnemyOfOne", "Duration": X("clamp(Power(1), 67, 228)"), "DamageScalar": X("10 + (Caster.Chivalry - 40) * 9 / 10"),
                     "Note": "the first creature you hit names its kind; your blows do this much more damage to that kind"},
    "Holy Light": {"Kind": "AreaDamage", "Damage": X("clamp(Power(10) + rand(0, 2), 8, 24)"), "Types": {"Energy": 100}, "Radius": 3, "Center": "Caster"},
    "Noble Sacrifice": {"Kind": "NobleSacrifice", "Radius": 6, "Resurrect": X("0.1 + 0.9 * Caster.Karma / 10000"), "Heal": X("clamp(Power(10) + rand(0, 2), 8, 24)"),
                        "Note": "resurrects, cures and heals everyone friendly around you and lifts their curses; you're left with 1 hit, stamina and mana"},
    "Remove Curse": {"Kind": "RemoveCurse", "Chance": X("Caster.Karma < -5000 ? 0 : Caster.Karma < 0 ? trunc(sqrt(20000 + Caster.Karma)) - 122 : Caster.Karma < 5625 ? trunc(sqrt(Caster.Karma)) + 25 : 100")},
    "Sacred Journey": {"Kind": "Travel", "Mode": "Recall", "Note": "Recall by chivalry (a marked rune), not while in combat"},
    # ------------------------------------------------------------------ Spellweaving (ArcanistSpell; Focus = arcane focus level 0-6)
    "Arcane Circle": {"Kind": "ArcaneCircle", "Station": "ArcaneCircle", "Reach": 1, "MinArcanists": 2, "WithinSkill": 20, "Item": "ArcaneFocus",
                      "Hours": X("max(1, trunc(Caster.Spellweaving / 24)) + min(6, Arcanists)"), "Strength": X("min(5, Arcanists)"),
                      "Note": "standing on an arcane circle or pentagram with at least one other spellweaver beside you (within 20 skill): each gets an arcane focus (strength = how many of you, up to 5) and that much strength while it lasts"},
    "Gift of Renewal": {"Kind": "GiftOfRenewal", "PerTick": X("5 + trunc(Caster.Spellweaving / 24) + Focus"), "Every": 2, "Seconds": X("30 + Focus * 10"), "Cooldown": 60,
                        "Note": "heals every 2 s while it lasts (cures poison instead if poisoned); again 60 s after it ends"},
    "Immolating Weapon": {"Kind": "ImmolatingWeapon", "Seconds": X("10 + trunc(Caster.Spellweaving / 24) + Focus"), "Damage": X("5 + trunc(Caster.Spellweaving / 24) + Focus"),
                          "Types": {"Fire": 100}, "BurnDelay": 0.25, "Note": "your melee blows burn for extra fire damage"},
    "Attune Weapon": {"Kind": "AttuneWeapon", "Absorb": X("trunc(18 + (Caster.Spellweaving - 10) / 10 * 3 + Focus * 6)"), "Seconds": X("60 + Focus * 12"), "Cooldown": 120,
                      "Note": "absorbs melee damage up to the amount; again 120 s after it ends"},
    "Thunderstorm": {"Kind": "WeaveArea", "Center": "Caster", "Radius": X("3 + Focus"), "Damage": X("max(11, 10 + trunc(Caster.Spellweaving / 24)) + Focus"), "Types": {"Energy": 100},
                     "Debuff": {"CastRecovery": "-6"}, "DebuffSeconds": X("5 + Focus"), "Note": "those who don't resist recover from casting more slowly"},
    "Nature's Fury": {"Kind": "WeaveSummon", "Creature": "NatureFury", "Count": 1, "Seconds": X("Caster.Spellweaving / 24 + 25 + Focus * 2")},
    "Summon Fey": {"Kind": "WeaveSummon", "Creature": "ArcaneFey", "Count": X("1 + Focus"), "Seconds": X("(Caster.Spellweaving / 24 + Focus * 2) * 60")},
    "Summon Fiend": {"Kind": "WeaveSummon", "Creature": "ArcaneFiend", "Count": X("1 + Focus"), "Seconds": X("(Caster.Spellweaving / 24 + Focus * 2) * 60")},
    "Reaper Form": {"Kind": "Transform", "Form": "ReaperForm", "Body": 285,
                    "Resists": {"Physical": "5 + Focus", "Fire": -25, "Cold": "5 + Focus", "Poison": "5 + Focus", "Energy": "5 + Focus"},
                    "Bonus": {"WeaponSpeed": "10 + Focus", "SpellDamage": "10 + Focus"}},
    "Wildfire": {"Kind": "Wildfire", "Radius": X("5 + Focus"), "Damage": X("10 + max(1, trunc(Caster.Spellweaving / 24)) + Focus"),
                 "Seconds": X("max(1, trunc(Caster.Spellweaving / 24)) + Focus"), "Types": {"Fire": 100}, "SplitMax": 3,
                 "Note": "a burning area around your target: every second its fire damage, shared when several stand in it (up to a third each)"},
    "Essence of Wind": {"Kind": "WeaveArea", "Center": "Caster", "Radius": X("5 + Focus"), "Damage": X("10 + Focus + max(1, trunc(Caster.Spellweaving / 24))"),
                        "Types": {"Cold": 100}, "Debuff": {"CastSpeed": "-(Focus + 1)", "WeaponSpeed": "-2 * (Focus + 1)"}, "DebuffSeconds": X("trunc(Caster.Spellweaving / 24) + Focus")},
    "Dryad Allure": {"Kind": "Allure", "Slayer": "Repond", "Chance": X("Caster.Spellweaving / 150 + Focus / 50"), "Slots": 3,
                     "Note": "charms a humanoid (orcs, ogres, ratmen...) into your service; failing enrages it"},
    "Ethereal Voyage": {"Kind": "EtherealVoyage", "Body": 770, "Seconds": X("12 + trunc(Caster.Spellweaving / 24) + Focus * 2"), "Cooldown": 300,
                        "Note": "creatures ignore you until it ends or you act against anyone; not in combat"},
    "Word of Death": {"Kind": "WordOfDeath", "Threshold": X("0.05 * Focus"), "ThresholdDamage": 300, "Min": X("trunc(Caster.Spellweaving / 5)"),
                      "Max": X("trunc(Caster.Spellweaving / 3)"), "Note": "chaos damage; a creature below Focus x 5% of its hits takes 300 direct"},
    "Gift of Life": {"Kind": "GiftOfLife", "Minutes": X("trunc(Caster.Spellweaving / 24) * 2 + Focus"), "HitsScalar": X("(Caster.Spellweaving / 2.4 + Focus) / 100"), "RiseDelay": [2, 4],
                     "Note": "you (or your bonded pet) come back to life soon after dying while it lasts"},
    "Arcane Empowerment": {"Kind": "ArcaneEmpowerment", "Seconds": X("15 + trunc(Caster.Spellweaving / 24) + Focus * 2"), "Bonus": X("trunc(Caster.Spellweaving / 12) + Focus * 5"),
                           "HealBonus": 10, "Note": "spell damage increase by Bonus, and heals 10 + Bonus % stronger"},
    # ------------------------------------------------------------------ Mysticism (MysticSpell; Boost = the better of Focus and Imbuing)
    "Nether Bolt": {"Kind": "Damage", "Base": 10, "Dice": 1, "Sides": 4, "Types": {"Chaos": 100}},
    "Healing Stone": {"Kind": "HealingStone", "Item": "HealingStone", "LifeForce": X("trunc((Caster.Mysticism + Boost) * 1.5)"), "MaxHeal": X("trunc((Caster.Mysticism + Boost) / 5)"),
                      "CurePerLevel": 25, "Lock": 2, "Note": "a stone that heals up to MaxHeal a use from its life force, or cures poison for 25 a level"},
    "Purge": {"Kind": "Purge", "Immunity": X("max(1, trunc((Caster.Mysticism + Boost) / 15))"), "Note": "takes away one of the target's magical blessings"},
    "Enchant": {"Kind": "Enchant", "Options": ["HitLightning", "HitFireball", "HitHarm", "HitMagicArrow", "HitDispel"],
                "Value": X("trunc(60 * (Caster.Mysticism + Boost) / 240)"), "Seconds": X("(Caster.Mysticism + Boost) / 2 + 30"), "Channeling": 80,
                "Note": "your weapon gains the chosen hit spell for a while; at 80 in both skills it also channels spells (-1 faster casting)"},
    "Sleep": {"Kind": "Sleep", "Seconds": X("(Caster.Mysticism + Boost) / 20 + 2 - Target.MagicResist / 10"),
              "Penalty": {"CastSpeed": -2, "CastRecovery": -3, "WeaponSpeed": -45, "AttackChance": -45}, "Immunity": X("Target.MagicResist / 10"),
              "Note": "slows casting and swings and spoils aim; any harm wakes it; then immune for a while"},
    "Eagle Strike": {"Kind": "Damage", "Base": 19, "Dice": 1, "Sides": 5, "Types": {"Energy": 100}},
    "Animated Weapon": {"Kind": "MysticSummon", "Creature": "AnimatedWeapon", "Slots": 4, "Level": X("trunc((Caster.Mysticism + Boost) / 2)"), "Seconds": X("10 + Level"),
                        "Stats": {"Str": "10 + Level", "Dex": "10 + Level", "Hits": "20 + trunc(Level * 3 / 2)",
                                  "DamageMin": "Level >= 120 ? 14 : Level >= 105 ? 13 : Level >= 90 ? 12 : Level >= 75 ? 11 : Level >= 60 ? 10 : Level >= 45 ? 9 : Level >= 30 ? 8 : 7",
                                  "DamageMax": "Level >= 120 ? 18 : Level >= 105 ? 17 : Level >= 90 ? 15 : Level >= 75 ? 14 : Level >= 60 ? 12 : Level >= 45 ? 11 : Level >= 30 ? 9 : 8",
                                  "Skill": "Level"}},
    "Stone Form": {"Kind": "Transform", "Form": "StoneForm", "Body": 705,
                   "Resists": {e: "max(2, trunc((Caster.Mysticism + Boost) / 24))" for e in ("Physical", "Fire", "Cold", "Poison", "Energy")},
                   "Bonus": {"CastSpeed": -2, "WeaponSpeed": -10, "WeaponDamage": "max(0, trunc((Caster.Mysticism + Boost) / 12))"}, "PoisonImmune": True, "SleepImmune": True,
                   "Note": "stone skin: resists up, slower casting and swings, harder blows, no poison or sleep"},
    "Spell Trigger": {"Kind": "SpellTrigger", "Item": "SpellStone", "RankSkill": 40, "Cooldown": 300,
                      "Note": "a stone holding one Mysticism spell (as your Mysticism + Boost allow: 40 a rank) that casts it at once when used; one every 5 minutes"},
    "Mass Sleep": {"Kind": "Sleep", "Radius": 3, "Seconds": X("(Caster.Mysticism + Boost) / 20 + 3 - Target.MagicResist / 10"),
                   "Penalty": {"CastSpeed": -2, "CastRecovery": -3, "WeaponSpeed": -45, "AttackChance": -45}, "Immunity": X("Target.MagicResist / 10")},
    "Cleansing Winds": {"Kind": "CleansingWinds", "Heal": X("trunc((Caster.Mysticism + Boost) / 4) + rand(-3, 3)"), "Extra": 3, "Radius": 2,
                        "CureChance": X("(10000 + (Caster.Mysticism + Boost) / 2 * 75 - PoisonLevel * 1750) / 100"), "PoisonCut": 0.15, "CurseCut": 3,
                        "Note": "heals, cures and lifts curses from the target and up to 3 friends near it, shared between them"},
    "Bombard": {"Kind": "Damage", "Base": 40, "Dice": 1, "Sides": 5, "Types": {"Physical": 100}, "Paralyze": X("trunc(Boost / 10) - trunc(Target.MagicResist / 10)"),
                "Note": "and holds the target if it doesn't resist"},
    "Spell Plague": {"Kind": "SpellPlague", "Seconds": 8, "Chances": [0.9, 0.6, 0.3], "Every": 1.5, "Damage": X("trunc((Caster.Mysticism + Boost) / 12) + rand(1, 6)"),
                     "Types": {"Chaos": 100}, "Note": "while it lasts, harm to the target may set it off (up to 3 times), each weaker"},
    "Hail Storm": {"Kind": "MysticArea", "Radius": 2, "Base": 51, "Dice": 1, "Sides": 5, "Types": {"Cold": 100}, "Split": True, "Note": "several victims share it (x2 / count)"},
    "Nether Cyclone": {"Kind": "MysticArea", "Radius": 3, "Damage": X("(Caster.Mysticism + Boost / 2) * 0.66 + rand(1, 6)"), "Types": {"Chaos": 100},
                       "Sap": {"Share": 0.3333, "Return": 10}, "Note": "also saps a third of the damage as stamina and mana, returned after 10 s"},
    "Rising Colossus": {"Kind": "MysticSummon", "Creature": "RisingColossus", "Slots": 5, "Level": X("Caster.Mysticism + Boost"), "Seconds": X("Level / 3"),
                        "Stats": {"Str": "677 + trunc((Caster.Mysticism - 83) / 1.3 + (Boost - 30) / 1.3 + 6)", "Dex": "107 + trunc((Caster.Mysticism - 83) / 1.3 + (Boost - 30) / 1.3 + 6)",
                                  "Hits": "315 + trunc((Caster.Mysticism - 83) * 1.14 + (Boost - 30) * 1.03 + 20)", "DamageMin": "trunc(Level / 12)", "DamageMax": "trunc(Level / 10)",
                                  "Skill": "Boost > 0 ? (Caster.Mysticism + Boost) / 2 : (Caster.Mysticism + 20) / 2"}},
    # ------------------------------------------------------------------ Bushido (SamuraiSpell stances; SamuraiMove moves)
    "Confidence": {"Kind": "Stance", "Stance": "Confidence", "Seconds": 15,
                   "ParryHeal": X("rand(1, max(1, trunc(Caster.Bushido / 12)))"), "ParryStam": X("rand(1, max(1, trunc(Caster.Bushido / 5)))"),
                   "Regen": X("15 + trunc(Caster.BushidoFixed * Caster.BushidoFixed / 57600)"), "RegenSeconds": 5,
                   "Note": "15 s: each parried blow gives back hits and stamina; meanwhile the regen hits come back over 5 s (a fifth a second), stopped by a blow"},
    "Evasion": {"Kind": "Stance", "Stance": "Evasion", "WeaponSkill": 50, "Cooldown": 20,
                "Seconds": X("trunc(3 + (Caster.Bushido > 60 ? (Caster.Bushido - 60) / 20 : 0) + (Caster.Anatomy >= 100 && Caster.Tactics >= 100 && Caster.Bushido > 100 ? 1 : 0))"),
                "ParryScalar": X("1 + (Caster.Bushido >= 60 ? (Caster.Bushido - 60) * 0.004 + 0.16 : 0) + (Caster.Anatomy >= 100 && Caster.Tactics >= 100 && Caster.Bushido > 100 ? 0.1 : 0)"),
                "Note": "parrying is likelier, and a parry check can turn aside harmful spells; needs a shield or 50 in your weapon's skill; again after 20 s"},
    "Counter Attack": {"Kind": "Stance", "Stance": "CounterAttack", "Seconds": 30, "Reach": 1, "NeedsWeaponOrShield": True,
                       "Note": "the next blow you parry from a foe beside you, you strike straight back"},
    "Honorable Execution": {"Kind": "Move", "Move": "HonorableExecution", "DamageScalar": X("1 + Caster.Bushido * 20 / 10000"),
                            "KillHeal": X("20 + trunc(Caster.Bushido * Caster.Bushido / 480)"), "KillSwing": X("max(1, trunc(Caster.Bushido * Caster.Bushido / 720))"), "KillSeconds": 20,
                            "PenaltyResists": -40, "PenaltySeconds": 7,
                            "Note": "kill with this blow: hits back and faster swings for 20 s; fail: -40 to every resist and no Magic Resist for 7 s (a parry ends it)"},
    "Lightning Strike": {"Kind": "Move", "Move": "LightningStrike", "Accuracy": 50, "Critical": X("Caster.Bushido * Caster.Bushido / 72000"),
                         "Note": "+50% hit chance (within the 45% cap); a chance to ignore armour"},
    "Momentum Strike": {"Kind": "Move", "Move": "MomentumStrike", "DamageBonus": X("Caster.Bushido / 100"), "KilledScalar": 1.5,
                        "Note": "the blow carries on into another foe in reach, with a damage bonus (half again if the first fell)"},
    # ------------------------------------------------------------------ Ninjitsu (NinjaSpell; NinjaMove moves)
    "Focus Attack": {"Kind": "Move", "Move": "FocusAttack", "Melee": True, "NoShield": True, "DamageScalar": X("1 + Caster.Ninjitsu * Caster.Ninjitsu / 43636"),
                     "PropertyScalar": X("1 + Caster.Ninjitsu * Caster.Ninjitsu / 43636 * 3 + 0.01"),
                     "Note": "more damage, and the weapon's hit properties (leech, hit spells, areas) are stronger"},
    "Death Strike": {"Kind": "Move", "Move": "DeathStrike", "DamageScalar": 0.5, "Seconds": 5, "Steps": 5,
                     "Chance": X("Caster.Ninjitsu < 100 ? 30 + (Caster.Ninjitsu - 85) * 2.2 : 63 + (Caster.Ninjitsu - 100) * 1.1"),
                     "Full": X("trunc(min(60, Caster.Ninjitsu / 3 * (0.3 + 0.7 * min(1, (Caster.Hiding + Caster.Stealth) / 220))))"),
                     "Partial": X("trunc(min(30, Caster.Ninjitsu / 9 * (0.3 + 0.7 * min(1, (Caster.Hiding + Caster.Stealth) / 220))))"),
                     "Note": "half damage now; after 5 s, or 5 steps by the target, direct damage (full if it moved 5 steps, halved with a ranged weapon)"},
    "Ki Attack": {"Kind": "Move", "Move": "KiAttack", "Melee": True, "Seconds": 2, "MaxTiles": 20, "DamageScalar": X("1 + Tiles / 10"),
                  "Note": "rush your target within 2 s: +10% damage a tile from where you started (up to 20 tiles); not from stealth"},
    "Surprise Attack": {"Kind": "Move", "Move": "SurpriseAttack", "Stealthed": True, "DefenseMalus": X("trunc(Caster.NinjitsuFixed / 60)"), "Seconds": 8,
                        "Note": "from stealth: the target's defence chance drops for 8 s"},
    "Backstab": {"Kind": "Move", "Move": "Backstab", "Stealthed": True, "DamageScalar": X("1 + Caster.Ninjitsu / 360"), "Note": "from stealth: more damage"},
    "Mirror Image": {"Kind": "MirrorImage", "Creature": "Clone", "Seconds": X("30 + Caster.NinjitsuFixed / 40"), "Slots": 1,
                     "Deflect": X("Caster.Ninjitsu / 150"), "DeflectRange": 4, "InAnimalForm": True, "Undisturbable": True,
                     "Note": "a copy of you follows you; a blow at you may land on an image within 4 tiles instead; any harm ends an image"},
    "Shadowjump": {"Kind": "Shadowjump", "Range": 11, "Stealthed": True, "InAnimalForm": True, "Undisturbable": True,
                   "Note": "from stealth: jump up to 11 tiles ahead and stay hidden (a stealth check after)"},
}


# Animal Form: the forms from the spell's m_Entries (type, name, item, hue, tooltip, skill, body, hue range, stealth,
# speed, stealing); what each does beyond its body is written here from RegenRates, PlayerMobile, BaseWeapon, Poison
# and the form's timer.
ANIMAL_EXTRAS = {
    "GreyWolf": {"BonusHits": 20}, "BakeKitsune": {"BonusHits": 20},
    "Dog": {"RegenHits": "NinjitsuFixed / 30"}, "Cat": {"RegenHits": "NinjitsuFixed / 30"},
    "Kirin": {"RegenStam": 20}, "Unicorn": {"CuresPoison": True},
    "GiantSerpent": {"PoisonOnHit": 0}, "BullFrog": {"PoisonAttacker": 1},  # poison levels: 0 lesser, 1 regular
    "CuSidhe": {"Bandage": {"Every": 8, "Min": 20, "Max": 50}}, "Reptalon": {"Breath": {"Damage": 20, "Types": {"Fire": 100}, "MaxEvery": 10}},
}


def animal_forms(root):
    src = read(os.path.join(root, "Scripts/Spells/Ninjitsu/AnimalForm.cs"))
    forms = []
    num_ = r"(0x[0-9A-Fa-f]+|\d+)"
    pat = (r"new AnimalFormEntry\(typeof\((\w+)\),\s*\"([^\"]+)\",\s*(\d+),\s*(\d+),\s*(\d+),\s*([\d.]+),\s*" + num_ + r",\s*" + num_ + r",\s*" + num_ +
           r",\s*(true|false),\s*(true|false),\s*(true|false)\)")
    for m in re.finditer(pat, src):
        kind = m.group(1)
        if kind == "WildWhiteTiger":
            continue  # a skill mastery's form, not Animal Form's list
        f = {"Creature": kind, "Name": m.group(2), "Skill": float(m.group(6)), "Body": int(m.group(7), 0),
             "HueMin": int(m.group(8), 0), "HueMax": int(m.group(9), 0), "Tooltip": int(m.group(5))}
        if m.group(10) == "true":
            f["StealthBonus"] = 20
        if m.group(11) == "true":
            f["Speed"] = True
        if m.group(12) == "true":
            f["StealingBonus"] = 10
        f.update(ANIMAL_EXTRAS.get(kind, {}))
        forms.append(f)
    if len(forms) < 16:
        raise SystemExit(f"Animal Form: only {len(forms)} forms")
    assert "double chance = (ninjitsu - entry.ReqSkill) / 37.5;" in src
    blocked = re.search(r"m_AnimalFormRestrictedSkills = new\[\]\s*\{([^}]*)\}", read(os.path.join(root, "Scripts/Mobiles/PlayerMobile.cs")))
    return forms, re.findall(r"SkillName\.(\w+)", blocked.group(1))


# What a move says when readied, and what stances say (the messages' cliloc text, from each class's comments).
TEXT = {
    "Honorable Execution": {"Ready": "You better kill your enemy with your next hit or you'll be rather sorry..."},
    "Lightning Strike": {"Ready": "You prepare to strike quickly.", "Hit": "You attack with lightning precision!", "Struck": "Your opponent's quick strike causes extra damage!"},
    "Momentum Strike": {"Ready": "You prepare to strike two enemies with one blow.", "Hit": "You transfer the momentum of your weapon into another enemy!",
                        "Struck": "You were hit by the momentum of a Samurai's weapon!", "None": "There are no valid targets to attack!"},
    "Focus Attack": {"Ready": "You prepare to focus all of your abilities into your next strike.", "Hit": "You focus all of your abilities and strike with deadly force!",
                     "Shield": "You cannot use this ability while holding a shield.", "Weapon": "You must be wielding a melee weapon without a shield to use this ability."},
    "Death Strike": {"Ready": "You prepare to hit your opponent with a Death Strike.", "Hit": "You inflict a Death Strike upon your opponent!",
                     "Struck": "You have been hit by a Death Strike!  Move with caution!", "Again": "Your opponent lands another Death Strike!",
                     "Missed": "You missed your opponent with a Death Strike."},
    "Ki Attack": {"Ready": "Your Ki Attack must be complete within 2 seconds for the damage bonus!", "Hit": "Your quick flight to your target causes extra damage as you strike!",
                  "Close": "You were too close to your target to cause any additional damage.", "Late": "You failed to complete your Ki Attack in time.",
                  "Stealth": "You cannot use this ability while in stealth mode.", "Ranged": "You can only use this with melee attacks."},
    "Surprise Attack": {"Ready": "You prepare to surprise your prey.", "Hit": "You catch your opponent off guard with your Surprise Attack!",
                        "Struck": "Your defenses are lowered as your opponent surprises you!", "Ends": "Your defenses have returned to normal.",
                        "Missed": "You failed to properly use the element of surprise."},
    "Backstab": {"Ready": "You prepare to Backstab your opponent.", "Hit": "You quickly stab your opponent as you come out of hiding!",
                 "Missed": "You failed to properly use the element of surprise."},
    "Confidence": {"Begin": "You exude confidence.", "Ends": "Your confidence wanes.", "Parried": "Your confidence reassures you as you successfully block your opponent's blow."},
    "Evasion": {"Begin": "You feel that you might be able to deflect any attack!", "Ends": "You no longer feel that you could deflect any attack.",
                "Skill": "Your skill with your equipped weapon must be 50 or higher to use Evasion.", "Wait": "You must wait before trying again.", "Evades": "*evades*"},
    "Counter Attack": {"Begin": "You prepare to respond immediately to the next blocked blow.", "Ends": "You return to your normal stance."},
    "Mirror Image": {"Begin": "You begin to summon a mirror image of yourself.", "Mounted": "You cannot use this ability while mounted.",
                     "Followers": "You cannot summon a mirror image because you have too many followers.",
                     "Diverted": "Your attack has been diverted to a nearby mirror image of your target!", "Divert": "You manage to divert the attack onto one of your nearby mirror images."},
    "Shadowjump": {"Begin": "You prepare to perform a Shadowjump."},
    "Animal Form": {"Polymorphed": "You can't do that while polymorphed.", "Transformed": "You cannot mimic an animal while in that form.",
                    "Skill": "You cannot use that skill in this form."},
}


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def need(pattern, text, what, flags=0):
    m = re.search(pattern, text, flags)
    if not m:
        raise SystemExit(f"not found: {what}")
    return m


def newest(expr):
    """`(Core.ML ? 2.0 : 1.5)` -> the newest era's value."""
    expr = expr.strip().strip("()")
    m = re.match(r"Core\.\w+\s*\?\s*([^:]+):\s*(.+)$", expr)
    return newest(m.group(1)) if m else expr


def num(text, name, kind=r"-?[\d.]+"):
    m = re.search(rf"\b{name}\b\s*(?:\{{\s*get\s*\{{\s*return\s*|=>\s*)\(?\s*([^;]*?)\s*\)?\s*;", text, re.S)
    if not m:
        return None
    v = newest(m.group(1))
    return float(v) if re.fullmatch(kind, v) else None


def registry(root):
    src = read(os.path.join(root, "Scripts/Spells/Initializer.cs"))
    return {int(i): c.split(".")[-1] for i, c in re.findall(r"Register\(\s*(\d+)\s*,\s*typeof\(\s*([\w.]+)\s*\)\s*\)", src)}


def find_class(folder, cls):
    for dirpath, _, files in os.walk(folder):
        for f in files:
            if f.endswith(".cs"):
                src = read(os.path.join(dirpath, f))
                if re.search(rf"class\s+{cls}\b", src):
                    return src
    raise SystemExit(f"class {cls} not found under {folder}")


def animate_groups(root, slayer_undead):
    """AnimateDeadSpell.m_Groups: corpse kinds -> undead raised by the caster's ability (requirement, kinds)."""
    src = read(os.path.join(root, "Scripts/Spells/Necromancy/AnimateDeadSpell.cs"))
    body = src[src.index("CreatureGroup[] m_Groups"):]
    body = re.sub(r"//[^\n]*", "", body[:body.index("public void Target")])
    groups = []
    for text in body.split("new CreatureGroup(")[1:]:
        if text.strip().startswith("SlayerGroup"):
            kinds, entries_text = slayer_undead, text
        else:
            first = text[:text.index("new SummonEntry") if "new SummonEntry" in text else len(text)]
            kinds = re.findall(r"typeof\s*\(\s*(\w+)\s*\)", first)
            entries_text = text[len(first):]
        entries = [{"Requirement": int(r), "Creatures": re.findall(r"typeof\s*\(\s*(\w+)\s*\)", rest)}
                   for r, rest in re.findall(r"new SummonEntry\(\s*(\d+)\s*,([^)]*\)(?:\s*,\s*typeof\([^)]*\))*)", entries_text)]
        groups.append({"Corpses": kinds if kinds else "Any", "Raise": entries})
    if len(groups) < 5:
        raise SystemExit(f"Animate Dead: only {len(groups)} groups")
    return groups


def scrolls(root):
    """spell id -> its scroll's class (SpellScroll subclasses: base(spellId, itemId, amount))."""
    out = {}
    for folder, _, files in os.walk(os.path.join(root, "Scripts")):
        for f in files:
            if f.endswith(".cs"):
                text = read(os.path.join(folder, f))
                if "SpellScroll" not in text:
                    continue
                for m in re.finditer(r"class\s+(\w+)\s*:\s*SpellScroll\b[\s\S]*?:\s*base\(\s*(\d+)\s*,", text):
                    sid = int(m.group(2))
                    if sid >= 100:
                        out.setdefault(sid, m.group(1))
    return out


def book_content(root, book):
    """A book's contents when made (its default constructor: this((ulong)0x3FF))."""
    for folder, _, files in os.walk(os.path.join(root, "Scripts/Items")):
        for f in files:
            if f.endswith(".cs"):
                text = read(os.path.join(folder, f))
                m = re.search(rf"public {book}\(\)\s*:\s*this\(\(ulong\)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)", text)
                if m:
                    return int(m.group(1), 0)
    raise SystemExit(f"{book}: default contents not found")


def main():
    root, spells_path = sys.argv[1], sys.argv[2]
    scroll_of = scrolls(root)
    magic = json.load(open(os.path.join(os.path.dirname(spells_path), "Magic.json"), encoding="utf-8"))
    undead = next(g for g in magic["Slayers"]["Groups"] if g["Group"] == "undead")["Super"]["Creatures"]
    EFFECTS["Animate Dead"]["Groups"] = animate_groups(root, undead)
    EFFECTS["Animate Dead"].update({"Ability": X("trunc((trunc(Caster.Necromancy * 30) + trunc(Caster.SpiritSpeak * 70)) / 10) * 18"), "MinFame": 100, "MaxRaised": 3,
                                    "Duration": 86400, "Regen": {"Every": 4, "Amount": "1 + HitsMax / 100"}, "Order": "Guard",
                                    "Shard": "SeriousFun: raised dead are controlled (pet orders, start on Guard), last a day and slowly heal; in plain ServUO they wander uncontrolled and decay"})
    forms, blocked = animal_forms(root)
    EFFECTS["Animal Form"] = {"Kind": "AnimalForm", "Forms": forms, "BlockedSkills": blocked, "Window": 37.5, "InAnimalForm": True, "Undisturbable": True,
                              "NoSkillCheck": True, "OwnMana": True, "Recovery": 10,
                              "Note": "choose an animal (by your Ninjitsu): below its skill + 37.5 the change may fail (no mana spent); in a form no other spells or moves "
                                      "but Mirror Image and Shadowjump, and the listed skills can't be used; cast again to return"}
    for name, text in TEXT.items():
        EFFECTS[name]["Text"] = text
    # Spell Trigger's list: which spells a stone may hold, by rank (SpellTriggerDef).
    trig = read(os.path.join(root, "Scripts/Spells/Mysticism/SpellDefinitions/SpellTriggerSpell.cs"))
    EFFECTS["Spell Trigger"]["Spells"] = [{"Spell": int(i), "Rank": int(r)} for i, r in re.findall(r"new SpellTriggerDef\(\s*(\d+)\s*,\s*\"[^\"]+\"\s*,\s*(\d+)", trig)]
    assert len(EFFECTS["Spell Trigger"]["Spells"]) >= 10
    reg = registry(root)
    data = json.load(open(spells_path, encoding="utf-8"))
    data["Spells"] = [s for s in data["Spells"] if s.get("School") in (None, "Magery") and s["Id"] < 100]
    schools = {}
    for name, school in SCHOOLS.items():
        folder = os.path.join(root, "Scripts/Spells", school["Folder"])
        base = find_class(folder, school["Base"])
        info = {k: v for k, v in school.items() if k not in ("Ids", "Folder", "Base")}
        info["Ids"] = school["Ids"]
        info["Book"] = {"Type": school["Book"], "Offset": school["Ids"][0], "Count": school["Ids"][1] - school["Ids"][0] + 1,
                        "Content": book_content(root, school["Book"]), "Note": "Content: what a new book holds, one bit per spell from Offset"}
        if school.get("RecoveryBase"):
            assert re.search(rf"CastRecoveryBase\s*\{{\s*get\s*\{{\s*return\s*{school['RecoveryBase']}\s*;", base), f"{name} recovery"
        if school.get("Undisturbable"):
            assert "Cannot disturb" in base, f"{name} disturb"
        if name == "Necromancy":
            assert "max = this.Scroll != null ? min : this.RequiredSkill + 40.0;" in base
        if name in ("Bushido", "Ninjitsu"):
            assert "min = this.RequiredSkill - 12.5;" in base and "max = this.RequiredSkill + 37.5;" in base, f"{name} window"
            assert re.search(r"CastRecoveryBase\s*\{\s*get\s*\{\s*return\s*7\s*;", base), f"{name} recovery"
            if name == "Bushido":
                assert re.search(r"CastDelayFastScalar\s*\{\s*get\s*\{\s*return\s*0\s*;", base), "bushido fc"
            else:
                assert re.search(r"RevealOnCast\s*\{\s*get\s*\{\s*return\s*false\s*;", base), "ninjitsu reveal"
        if name == "Mysticism":
            assert "m_ManaTable = new int[] { 4, 6, 9, 11, 14, 20, 40, 50 }" in base and "ChanceLength = 100.0 / 7.0" in base and "20.0 : 30.0" in base, "mysticism circles"
        if name == "Spellweaving":
            assert "min = RequiredSkill - 12.5;" in base and "max = RequiredSkill + 37.5;" in base, "spellweaving window"
        if name == "Chivalry":
            assert "max = RequiredSkill + 50.0;" in base and "Math.Sqrt(from.Karma + 20000 + (from.Skills.Chivalry.Fixed * 10))" in base
        schools[name] = info
        for sid in range(school["Ids"][0], school["Ids"][1] + 1):
            cls = reg.get(sid)
            if cls is None:
                continue
            src = find_class(folder, cls)
            m = re.search(r"new SpellInfo\(\s*\"([^\"]+)\"\s*,\s*(null|\"[^\"]*\")([\s\S]*?)\);", src)
            spell_name = m.group(1) if m else re.sub(r"(?<=[a-z])(?=[A-Z])", " ", cls.replace("Spell", ""))
            if " " not in spell_name and re.search(r"[a-z][A-Z]", spell_name):
                spell_name = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", spell_name)  # CounterAttack -> Counter Attack
            mantra = m.group(2).strip('"') if m and m.group(2) != "null" else None
            reagents = re.findall(r"Reagent\.(\w+)", m.group(3)) if m else []
            delay = None
            dm = re.search(r"CastDelayBase[\s\S]{0,120}?FromSeconds\(([^;]+?)\)\s*;", src)
            if dm:
                v = newest(dm.group(1))
                delay = float(v) if re.fullmatch(r"[\d.]+", v) else None
            rec = {
                "Id": sid, "Name": spell_name, "Key": cls, "School": name, "Mantra": mantra,
                "RequiredSkill": num(src, "RequiredSkill"),
                "Mana": int(num(src, "RequiredMana") or num(src, "BaseMana") or 0),
                "Reagents": [{"Type": r, "Amount": 1} for r in reagents],
                "Move": bool(re.search(r":\s*(SamuraiMove|NinjaMove|SpecialMove)\b", src)),
            }
            if sid in scroll_of:
                rec["Scroll"] = scroll_of[sid]
            tith = num(src, "RequiredTithing")
            if tith is not None:
                rec["Tithing"] = int(tith)
            mn = num(src, "MantraNumber")
            if mn is not None:
                rec["MantraCliloc"] = int(mn)
            cm = re.search(r"SpellCircle\.(First|Second|Third|Fourth|Fifth|Sixth|Seventh|Eighth)", src)
            if cm and school.get("Circles"):
                circle = ["First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth"].index(cm.group(1))
                rec["Circle"] = circle + 1
                rec["Mana"] = school["Circles"]["Mana"][circle]
                rec["RequiredSkill"] = round(max(0.0, school["Circles"]["Length"] * circle - school["Circles"]["Offset"]), 1)
                if delay is None:
                    delay = (4 + circle) * 0.25
                if spell_name == "Hail Storm" and "Core.TOL && this is HailStormSpell" in base:
                    rec["Mana"] = 50  # the newest era's exception (MysticSpell.GetMana)
            if delay is not None:
                rec["CastDelay"] = {"Base": delay}
            eff = EFFECTS.get(spell_name)
            rec["Effect"] = eff if eff and eff.get("Kind") else None
            if eff and not eff.get("Kind"):
                rec["Notes"] = eff.get("Note")
            rec["Target"] = target_of(spell_name)
            rec["Range"] = 10
            data["Spells"].append(rec)
    data["Schools"] = schools
    with open(spells_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write("\n")
    by = {}
    for s in data["Spells"]:
        by.setdefault(s.get("School", "Magery"), [0, 0])
        by[s.get("School", "Magery")][0] += 1
        by[s.get("School", "Magery")][1] += 1 if s.get("Effect") else 0
    print(by)


HARMFUL = {"Blood Oath", "Corpse Skin", "Evil Omen", "Mind Rot", "Pain Spike", "Poison Strike", "Strangle", "Vengeful Spirit",
           "Word of Death", "Dryad Allure", "Nether Bolt", "Purge", "Sleep", "Eagle Strike", "Bombard", "Spell Plague"}
BENEFICIAL = {"Cleanse By Fire", "Close Wounds", "Remove Curse", "Gift of Renewal", "Gift of Life", "Cleansing Winds"}
LOCATION = {"Wildfire", "Mass Sleep", "Hail Storm", "Nether Cyclone"}
ITEM = {"Sacred Journey"}
CORPSE = {"Animate Dead"}


def target_of(name):
    if name in HARMFUL:
        return "Harmful"
    if name in BENEFICIAL:
        return "Beneficial"
    if name in ITEM:
        return "Item"
    if name in CORPSE:
        return "Corpse"
    if name in LOCATION:
        return "Location"
    return "Self"


main()
