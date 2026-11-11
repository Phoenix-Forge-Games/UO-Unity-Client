"""Extracts the Magery rules (the 64 spells, casting, mana/stamina/hit point pools and regeneration) from ServUO's C# scripts
into Content/Spells.json.

  python -I export_servuo_spells.py <ServUO root> <Items.json> <output json>

ServUO is going away: this file has to describe Magery completely on its own, and the game re-implements every spell
from it (nothing of ServUO's code is reused). What can be read reliably from the scripts is parsed: the registry
(Spells/Initializer.cs: id -> class), each spell's SpellInfo (name, power words, reagents, town rule), circle, target
(range, harmful/beneficial, ground), cast-delay overrides, follower-slot checks, AOS damage dice and damage types, reflect
checks; the mana table, cast-skill window and cast/recovery timing (MagerySpell.cs, Spell.cs); the spellbook layout
(Spellbook.cs); each spell's scroll (SpellScroll subclasses); poison levels (Misc/Poison.cs); Polymorph forms
(Gumps/PolymorphGump.cs); Summon Creature's and Create Food's lists; stat caps (Config/PlayerCaps.cfg). What each spell
*does* is a hand-written table (EFFECTS below) whose numbers are asserted against the parsed source. Era: the newest
branches (Core.AOS/SE/ML/SA/TOL all true, Siege false); Notes say where pre-AOS differed when it matters. Shard
("SeriousFun") changes in the scripts are kept and flagged in Notes.

Output:
  {"Source": str,
   "Expressions": {...the formula language, below...},
   "Pools":   {"HitsMax", "StamMax", "ManaMax", "StatCaps", "Regen": {"Hits", "Stam", "Mana", "ArmorPenalty", "Meditation"}},
   "Casting": {"CircleMana": [8 ints], "ManaCost": {...}, "Reagents": {...}, "CastDelay": {...}, "Recovery": {...},
               "SkillRange": [{"Circle", "Min", "Max", "ScrollMin", "ScrollMax"}...], "SkillCheck": {...},
               "Fizzle": {...}, "ResistedFormula": {...}, "EvalIntScaling": {...}, "Reflection": {...},
               "DelayedDamage": {...}, "Targeting": {...}, "Notes": [...]},
   "Spellbook": {"Type": "Spellbook", "Offset": 0, "Count": 64, "Bit": str},
   "PoisonLevels": [{"Level", "Name", "Min", "Max", "Percent", "Delay", "Interval", "Ticks"}...],
   "Spells": [ one record per spell, in id order:
     {"Id", "Name", "Key" (ServUO class), "Circle" (1-8), "Mantra", "Mana",
      "Reagents": [{"Type": <Items.json Type>, "Amount": 1}...], "Scroll": <Items.json Type>,
      "Target": "Harmful" | "Beneficial" | "Self" | "Location" | "Item" | "None",
      "Range": tiles (absent when there is no target), "AllowedInTown": bool, "Reflectable": bool,
      "CastDelay": {"Base": seconds at 0 Faster Casting, "Multiplier": n (only when not 1)},
      "Effect": {"Kind": ..., kind fields...}, "Resistable": bool, "Notes": str}]}

Formulas. A number, or {"Expr": "<expression>", ...} evaluated when the spell lands. The language:
  numbers; + - * / (real division) ; ( ) ; comparisons < <= > >= == != (1 or 0) ; c ? a : b ;
  functions floor, ceil, trunc (toward zero: C#'s (int) cast), round, min, max, abs, sqrt, clamp(x, lo, hi),
  rand(lo, hi) (uniform integer, both ends included), randf() (uniform real in [0, 1)).
  Variables: Caster.X / Target.X where X is a skill name (Magery, EvalInt, MagicResist, Inscribe, Poisoning, Meditation,
  Focus, DetectHidden, Hiding, Stealth: the value, 0.0-120.0 in tenths) or that name + "Fixed" (the same in integer
  tenths, e.g. 100.0 -> 1000); Str, Dex, Int (current, with modifiers); RawStr, RawDex, RawInt (base); Hits, HitsMax,
  Stam, StamMax, Mana, ManaMax; IsPlayer (1 for a player character, 0 otherwise); PoisonLevel (0-4, of the current
  poison). Distance = straight-line distance in tiles between caster and target; Range = tile range
  (max(|dx|, |dy|)). Count = number of targets an area spell hit. Other names a formula uses are explained beside it.
  Integer steps of the original are written with trunc()/floor() so results match exactly.
"""
import json
import math
import os
import re
import sys

STAT_CAPS = ("StrCap", "DexCap", "IntCap", "StrMaxCap", "DexMaxCap", "IntMaxCap", "TotalStatCap")
CIRCLE_FOLDERS = ("First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth")
DAMAGE_TYPES = ("Physical", "Fire", "Cold", "Poison", "Energy")


# ---------------------------------------------------------------------------------------------------------------------
# Small text helpers
# ---------------------------------------------------------------------------------------------------------------------

def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def matching(text, start):
    """Index just past the brace block opening at text[start] == '{'."""
    depth = 0
    for i in range(start, len(text)):
        c = text[i]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return i + 1
    return len(text)


def class_body(text, name):
    m = re.search(r"class\s+" + name + r"\b[^{]*\{", text)
    if not m:
        return ""
    return text[m.end() - 1:matching(text, m.end() - 1)]


def era(expr):
    """Value of a C# era conditional with every Core.* flag true: 'Core.ML ? 10 : 12' -> '10'."""
    expr = expr.strip()
    while expr.startswith("(") and expr.endswith(")"):
        expr = expr[1:-1].strip()
    if "?" in expr:
        cond, rest = expr.split("?", 1)
        assert re.fullmatch(r"\s*\(?\s*Core\.\w+\s*\)?\s*", cond), expr
        return rest.split(":", 1)[0].strip()
    return expr


# ---------------------------------------------------------------------------------------------------------------------
# Formula language: a small parser used to check every Expr the file emits (syntax, known names, sample values).
# ---------------------------------------------------------------------------------------------------------------------

SKILLS = ("Magery", "EvalInt", "MagicResist", "Inscribe", "Poisoning", "Meditation", "Focus", "DetectHidden", "Hiding",
          "Stealth")
MOBILE_VARS = SKILLS + tuple(s + "Fixed" for s in SKILLS) + (
    "Str", "Dex", "Int", "RawStr", "RawDex", "RawInt", "Hits", "HitsMax", "Stam", "StamMax", "Mana", "ManaMax",
    "IsPlayer", "PoisonLevel")
FUNCS = {
    "floor": math.floor, "ceil": math.ceil, "trunc": math.trunc, "round": round, "min": min, "max": max, "abs": abs,
    "sqrt": math.sqrt, "clamp": lambda x, lo, hi: max(lo, min(hi, x)),
    "rand": lambda lo, hi: (lo + hi) / 2, "randf": lambda: 0.5,  # checking only: the mean
}
TOKEN = re.compile(r"\s*(?:(\d+(?:\.\d+)?)|([A-Za-z_][\w.]*)|(<=|>=|==|!=|[-+*/()?:,<>]))")


class Expr:
    def __init__(self, text, extra=()):
        self.tokens, pos = [], 0
        text = text.strip()
        while pos < len(text):
            m = TOKEN.match(text, pos)
            assert m and m.end() > pos, "bad expression: " + text
            self.tokens.append(m.group(1) and ("num", float(m.group(1))) or m.group(2) and ("name", m.group(2))
                               or ("op", m.group(3)))
            pos = m.end()
        self.i, self.text, self.extra = 0, text, set(extra)
        self.names = set()
        self.tree = self.ternary()
        assert self.i == len(self.tokens), "trailing tokens in: " + text

    def peek(self):
        return self.tokens[self.i] if self.i < len(self.tokens) else (None, None)

    def take(self, op=None):
        t = self.peek()
        if op is not None:
            assert t == ("op", op), "expected %s in: %s" % (op, self.text)
        self.i += 1
        return t

    def ternary(self):
        c = self.compare()
        if self.peek() == ("op", "?"):
            self.take("?")
            a = self.ternary()
            self.take(":")
            b = self.ternary()
            return ("?", c, a, b)
        return c

    def compare(self):
        a = self.additive()
        while self.peek()[0] == "op" and self.peek()[1] in ("<", "<=", ">", ">=", "==", "!="):
            a = (self.take()[1], a, self.additive())
        return a

    def additive(self):
        a = self.term()
        while self.peek() in (("op", "+"), ("op", "-")):
            a = (self.take()[1], a, self.term())
        return a

    def term(self):
        a = self.unary()
        while self.peek() in (("op", "*"), ("op", "/")):
            a = (self.take()[1], a, self.unary())
        return a

    def unary(self):
        if self.peek() == ("op", "-"):
            self.take()
            return ("neg", self.unary())
        return self.atom()

    def atom(self):
        kind, value = self.take()
        if kind == "num":
            return ("num", value)
        if kind == "op" and value == "(":
            e = self.ternary()
            self.take(")")
            return e
        assert kind == "name", "unexpected token in: " + self.text
        if self.peek() == ("op", "("):
            assert value in FUNCS, "unknown function %s in: %s" % (value, self.text)
            self.take("(")
            args = []
            if self.peek() != ("op", ")"):
                args.append(self.ternary())
                while self.peek() == ("op", ","):
                    self.take(",")
                    args.append(self.ternary())
            self.take(")")
            return ("call", value, args)
        parts = value.split(".")
        ok = (len(parts) == 2 and parts[0] in ("Caster", "Target") and parts[1] in MOBILE_VARS) or \
             value in ("Distance", "Range", "Count") or value in self.extra
        assert ok, "unknown name %s in: %s" % (value, self.text)
        self.names.add(value)
        return ("var", value)

    def eval(self, env, node=None):
        node = self.tree if node is None else node
        op = node[0]
        if op == "num":
            return node[1]
        if op == "var":
            return env[node[1]]
        if op == "neg":
            return -self.eval(env, node[1])
        if op == "call":
            return FUNCS[node[1]](*[self.eval(env, a) for a in node[2]])
        if op == "?":
            return self.eval(env, node[2]) if self.eval(env, node[1]) else self.eval(env, node[3])
        a, b = self.eval(env, node[1]), self.eval(env, node[2])
        return {"+": a + b, "-": a - b, "*": a * b, "/": a / b if b else 0.0, "<": a < b, "<=": a <= b, ">": a > b,
                ">=": a >= b, "==": a == b, "!=": a != b}[op]


def sample_env(caster_skill=100.0, target_skill=0.0, **over):
    env = {"Distance": 1.0, "Range": 1, "Count": 1}
    for who, skill in (("Caster", caster_skill), ("Target", target_skill)):
        for s in SKILLS:
            env[who + "." + s] = skill
            env[who + "." + s + "Fixed"] = int(round(skill * 10))
        for s in ("Str", "Dex", "Int", "RawStr", "RawDex", "RawInt", "Hits", "HitsMax", "Stam", "StamMax", "Mana",
                  "ManaMax"):
            env[who + "." + s] = 100
        env[who + ".IsPlayer"] = 1
        env[who + ".PoisonLevel"] = 0
    env.update(over)
    return env


def walk_exprs(node, found, path=""):
    if isinstance(node, dict):
        if isinstance(node.get("Expr"), str):
            found.append((path, node))
        for k, v in node.items():
            walk_exprs(v, found, path + "." + k)
    elif isinstance(node, list):
        for i, v in enumerate(node):
            walk_exprs(v, found, "%s[%d]" % (path, i))


def X(expr, **fields):
    d = {"Expr": expr}
    d.update(fields)
    return d


# ---------------------------------------------------------------------------------------------------------------------
# Hand-written spell effects (newest era). Numbers that also appear in the source are asserted after parsing.
# ---------------------------------------------------------------------------------------------------------------------

CURSE_PERCENT = X("max(0, 8 + floor(Caster.EvalIntFixed / 100) - Target.MagicResistFixed / 100)",
                  Unit="percent of the target's base stat",
                  Note="Target.MagicResist here is lowered by Evil Omen's malus. Each curse also gives the target a "
                       "Resisting Spells gain check.")
BUFF_PERCENT = X("1 + floor(Caster.EvalIntFixed / 100)", Unit="percent of the target's base stat")
STAT_DURATION = X("floor(6 * Caster.EvalIntFixed / 50) + 1", Unit="s")


def stat_curse(*stats):
    return {"Kind": "StatCurse", "Stats": list(stats), "Percent": CURSE_PERCENT,
            "Amount": {s: X("ceil(Target.Raw%s * Percent / 100)" % s, Note="Percent = the Percent above")
                       for s in stats},
            "Duration": STAT_DURATION,
            "Stacking": "One curse modifier per stat ('[Magic] <Stat> Curse'). A new curse applies only if it is stronger "
                        "than the existing one for that stat; if it is weaker the spell fizzles (mana spent); if equal "
                        "nothing changes but the harmful side effects still happen.",
            "BreaksParalysis": True, "DisruptsTargetCasting": True,
            "ImmuneIf": "target is in Stone Form (Mysticism)"}


def stat_buff(*stats):
    return {"Kind": "StatBuff", "Stats": list(stats), "Percent": BUFF_PERCENT,
            "Amount": {s: X("ceil(Target.Raw%s * Percent / 100)" % s, Note="Percent = the Percent above")
                       for s in stats},
            "Duration": STAT_DURATION,
            "Stacking": "One buff modifier per stat ('[Magic] <Stat> Buff'); the new offset is max(existing, new) and "
                        "its timer restarts. Fizzles (mana spent) if the new offset is lower than the existing one" +
                        (" for all three stats" if len(stats) > 1 else "") + " or is 0."}


def damage(base, dice, sides, typ, delay=0.0, **extra):
    d = {"Kind": "Damage", "Base": base, "Dice": dice, "Sides": sides, "Types": {typ: 100}, "Delay": delay,
         "Scaling": "EvalIntScaling"}
    d.update(extra)
    return d


SUMMON_DURATION = X("trunc(2 * Caster.MageryFixed / 5)", Unit="s", Note="= Magery x 4 seconds")
STAT_FIZZLE_NOTE = "Harmful; no resist roll in the AOS era (Resisting Spells lowers the percent instead)."

EFFECTS = {
    "ClumsySpell": (stat_curse("Dex"), STAT_FIZZLE_NOTE),
    "CreateFoodSpell": ({"Kind": "CreateItem", "OneOf": "FOOD", "Amount": 1, "Into": "CasterBackpack"},
                        "Picks one food uniformly."),
    "FeeblemindSpell": (stat_curse("Int"), STAT_FIZZLE_NOTE),
    "HealSpell": ({"Kind": "Heal", "Base": 8, "Dice": 1, "Sides": 4, "Scaling": "HealScaling",
                   "OtherTargetMultiplier": 1.5,
                   "NonPlayerCaster": X("trunc(Caster.MageryFixed / 120) + rand(1, 4)", OtherTargetMultiplier=1.5),
                   "FailsIf": ["target is poisoned", "target has a Mortal Strike wound", "target is a dead bonded pet",
                               "target is animated dead", "target is a repairable construct (golem)"],
                   "Note": "Player casters: 1d4+8 scaled by HealScaling, x1.5 (trunc) on someone else. The target "
                           "must not be at full hits for the act to count as beneficial."},
                  "SeriousFun: player heals scale like damage spells (stock AOS: Magery/12 + 1d4)."),
    "MagicArrowSpell": (damage(10, 1, 4, "Fire", 1.0, NoStackWith=["MagicArrowSpell", "NetherBoltSpell"],
                               StackRule="A second Magic Arrow (or Nether Bolt) on a target that already has one in "
                                         "flight fizzles (mana spent)."),
                        "Pre-AOS: 4-7 damage (Random(4,4)), x0.75 if resisted."),
    "NightSightSpell": ({"Kind": "NightSight", "LightLevel": X("max(0, trunc(26 * Target.Magery / 100))",
                                                               Note="26 = full dungeon brightness; uses the TARGET's "
                                                                    "Magery in AOS"),
                         "Duration": X("rand(15, 39) * 60", Unit="s"),
                         "Stacking": "No effect if the target already has night sight (mana still spent)."},
                        "Pre-AOS used the caster's Magery."),
    "ReactiveArmorSpell": ({"Kind": "ResistToggle",
                            "Resists": {"Physical": X("15 + trunc(Caster.Inscribe / 20)"), "Fire": -5, "Cold": -5,
                                        "Poison": -5, "Energy": -5},
                            "Duration": "UntilRecast",
                            "Note": "Casting again removes it. Persists through logout and death."},
                           "Pre-AOS: absorbs melee damage = clamp((Magery+Meditation+Inscribe)/3, 1, 75) points."),
    "WeakenSpell": (stat_curse("Str"), STAT_FIZZLE_NOTE),
    "AgilitySpell": (stat_buff("Dex"), ""),
    "CunningSpell": (stat_buff("Int"), ""),
    "CureSpell": ({"Kind": "Cure",
                   "Chance": X("trunc((10000 + trunc(Caster.Magery * 75) - (Target.PoisonLevel + 1) * "
                               "(Target.PoisonLevel < 4 ? 3300 : 3100)) / 100)",
                               Unit="percent", Roll="cures if Chance > rand(0, 99)"),
                   "Note": "No effect on a target that isn't poisoned (the beneficial act needs a poisoned target)."},
                  "Pre-AOS: 1750 per poison level."),
    "HarmSpell": (damage(17, 1, 5, "Cold", 0.0,
                         RangeFalloff=[{"MaxRange": 1, "Multiplier": 1.0}, {"MaxRange": 2, "Multiplier": 0.5},
                                       {"MaxRange": None, "Multiplier": 0.25}],
                         SlayerApplies=False),
                  "Range is tile range (max(|dx|,|dy|)): full at 1, half at 2, quarter beyond. Pre-AOS: 1-15."),
    "MagicTrapSpell": ({"Kind": "TrapContainer", "Accepts": "a trappable container (chest, box, crate...)",
                        "TrapPower": X("rand(10, 50)"),
                        "Trigger": "Opening the container: TrapPower direct damage (no resist) to the opener if "
                                   "within 1 tile; the trap is then spent.",
                        "FailsIf": "the container already has a non-magic trap (fizzle)"},
                       "Pre-AOS power was 1."),
    "RemoveTrapSpell": ({"Kind": "DisarmContainer", "Accepts": "a trappable container",
                         "Removes": "MagicTrap only",
                         "FailsIf": "the container has a non-magic trap (fizzle)"}, ""),
    "ProtectionSpell": ({"Kind": "ProtectionToggle", "AppliesTo": "Caster",
                         "Resists": {"Physical": X("-15 + min(trunc(Caster.Inscribe / 20), 15)")},
                         "SkillMods": {"MagicResist": X("-35 + min(trunc(Caster.Inscribe / 20), 35)")},
                         "DisruptImmunity": 100, "FasterCasting": -2, "Duration": "UntilRecast"},
                        "While active, damage never disrupts the caster's spells, at the cost of resists and 2 "
                        "points of Faster Casting. Pre-AOS: chance-based disrupt protection with a timer."),
    "StrengthSpell": (stat_buff("Str"), ""),
    "BlessSpell": (stat_buff("Str", "Dex", "Int"), ""),
    "FireballSpell": (damage(19, 1, 5, "Fire", 1.0), "Pre-AOS: 10-16."),
    "MagicLockSpell": ({"Kind": "MagicLock", "Accepts": "an unlocked lockable container that has a lock (not locked "
                                                        "down/secured in a house, not a paragon chest)",
                        "Sets": {"Locked": True, "LockLevel": -255},
                        "Note": "LockLevel -255 marks a magic lock (Unlock restores it to RequiredSkill - 10)."}, ""),
    "PoisonSpell": ({"Kind": "Poison",
                     "Total": X("trunc((Caster.MageryFixed + Caster.PoisoningFixed) / 2)",
                                Note="tenths, so 1000 = 100.0"),
                     "BaseLevel": [{"MinTotal": 1000, "Level": X("randf() < 0.1 ? 4 : 3",
                                                                 Note="randf() <= 0.1 in the source")},
                                   {"MinTotal": 851, "Level": 2}, {"MinTotal": 651, "Level": 1},
                                   {"MinTotal": None, "Level": 0}],
                     "Level": X("Range > 8 ? 0 : max(0, BaseLevel - (Range > 2 ? trunc(trunc(Distance) / 2) : 0))",
                                Note="BaseLevel from the table; levels index PoisonLevels"),
                     "BreaksParalysis": True, "DisruptsTargetCasting": True,
                     "ImmuneIf": "target is in Stone Form, or resists (ResistedFormula, circle 3) -> no poison"},
                    "The only Magery spell with a full resist roll in the AOS era. Pre-AOS: level from "
                    "Magery+Poisoning minus distance."),
    "TelekinesisSpell": ({"Kind": "Telekinesis",
                          "Accepts": "an item that reacts to telekinesis (trapped/levered objects) or a container",
                          "Effect": "Uses the item from range as if double-clicked; a container opens remotely "
                                    "(snooping rules if it is on another mobile; corpse loot rules apply)."}, ""),
    "TeleportSpell": ({"Kind": "Teleport", "To": "TargetLocation (surface top)",
                       "FailsIf": ["carrying a faction sigil", "overloaded", "travel rules forbid leaving here or "
                                   "arriving there", "the spot cannot hold a mobile", "the spot is in a house or "
                                   "multi"],
                       "OnArrival": "Steps on any fire or poison field at the destination (their move-over effect)."},
                      "Range 11 in ML+ (12 before)."),
    "UnlockSpell": ({"Kind": "Unlock",
                     "Power": X("trunc(Caster.Magery * 0.8) - 4"),
                     "Succeeds": "Power >= the container's RequiredSkill",
                     "TreasureChests": {"RequiredMagery": {"0": 50, "1": 80, "2": 100}, "Level3Plus": "immune"},
                     "Note": "Unlocking a magic-locked container sets its LockLevel to RequiredSkill - 10. Secured "
                             "containers, mobiles and lockless containers are refused (mana still spent)."},
                    "ServUO's display name is 'Unlock Spell'."),
    "WallOfStoneSpell": ({"Kind": "Wall", "Tiles": 3, "Layout": "PerpendicularToCaster", "Duration": 10.0,
                          "BlocksMovement": True,
                          "Note": "Center tile plus one each side, across the line from caster to target point. "
                                  "Tiles on water or blocked spots are skipped."}, "Range 15 (TOL+)."),
    "ArchCureSpell": ({"Kind": "Cure", "Area": {"Center": "TargetLocation", "Radius": 2},
                       "Chance": X("trunc((10000 + trunc(Caster.Magery * 75) - (Target.PoisonLevel + 1) * 1750) / 100)"
                                   " - 1", Unit="percent", Roll="cures if Chance > rand(0, 99)"),
                       "AreaFilter": "A mobile targeted directly is always included (beneficial rules). Others in "
                                     "the area: not the caster's aggressors or victims, innocent both ways or an ally; "
                                     "on Felucca-rules maps players only."},
                      "Cast delay is 0.25 s shorter than its circle's."),
    "ArchProtectionSpell": ({"Kind": "ProtectionToggle", "Area": {"Center": "TargetLocation", "Radius": 2},
                             "AppliesTo": "the caster and party members in the area",
                             "Same": "ProtectionSpell (per target; toggles - already protected targets lose it)"},
                            "Pre-AOS: +Magery/10+1 armor to everyone in radius 3 for min(Magery*1.2, 144) s."),
    "CurseSpell": ({**stat_curse("Str", "Dex", "Int"),
                    "Duration": X("Target.Resilience ? trunc((floor(6 * Caster.EvalIntFixed / 50) + 1) / 2) : "
                                  "floor(6 * Caster.EvalIntFixed / 50) + 1", Unit="s",
                                  Note="Target.Resilience = 1 when the target has the Resilience mastery active"),
                    "MaxResists": {"Fire": -10, "Cold": -10, "Poison": -10, "Energy": -10,
                                   "Note": "player targets only, and only when that max resist is above 60"},
                    "Stacking": "Applies when at least one stat's curse would be stronger (fizzles if all three are "
                                "weaker or all are 0); replaces weaker Weaken/Clumsy/Feeblemind."},
                   STAT_FIZZLE_NOTE),
    "FireFieldSpell": ({"Kind": "Field", "Field": "Fire", "Tiles": 5, "Layout": "PerpendicularToCaster",
                        "TileDelay": 0.3, "Duration": X("trunc((15 + trunc(Caster.MageryFixed / 5)) / 4)", Unit="s"),
                        "Damage": {"Amount": 2, "Types": {"Fire": 100}, "OnEnter": True, "Interval": 1.0,
                                   "FirstTick": 0.5},
                        "AffectsCaster": False,
                        "Note": "Center tile at once, then tiles +/-1 after 0.3 s and +/-2 after 0.6 s. Hits mobiles "
                                "whose height overlaps the tile; reveals the caster when it hurts someone."},
                       "Pre-AOS duration: 4 + Magery/2 s."),
    "GreaterHealSpell": ({"Kind": "Heal", "Base": 40, "Dice": 1, "Sides": 10, "Scaling": "HealScaling",
                          "NonPlayerCaster": X("trunc(Caster.Magery * 0.4) + rand(1, 10)"),
                          "FailsIf": ["target is poisoned", "target has a Mortal Strike wound",
                                      "target is a dead bonded pet", "target is animated dead",
                                      "target is a repairable construct (golem)"]},
                         "SeriousFun: player casters 1d10+40 scaled by HealScaling."),
    "LightningSpell": (damage(23, 1, 4, "Energy", 0.0), "Pre-AOS: 12-20."),
    "ManaDrainSpell": ({"Kind": "ManaDrain",
                        "Amount": X("clamp(40 + trunc(Caster.EvalInt - Target.MagicResist), 0, Target.Mana)"),
                        "ReturnAfter": 5.0,
                        "Note": "The mana comes back to the target after 5 s if it is alive. A target still under a "
                                "drain cannot be drained again.",
                        "BreaksParalysis": True, "DisruptsTargetCasting": True},
                       "Pre-AOS: random 1-100 (resistable), permanent."),
    "RecallSpell": ({"Kind": "Travel", "Mode": "Recall", "Destination": "MarkedLocation",
                     "Sources": ["a marked RecallRune", "a Runebook's default entry (uses a charge, no Magery check)",
                                 "a boat key / galleon", "a writ of lease"],
                     "FailsIf": ["criminal", "in combat (aggressed a player or pet in the last 2 minutes)",
                                 "overloaded", "holding an item on the cursor", "carrying a faction sigil",
                                 "travel rules forbid leaving here or arriving there", "the spot cannot hold a mobile",
                                 "a house/multi is there", "a Young player going to Felucca"],
                     "TakesPets": True},
                    "SeriousFun: double-clicking a marked rune casts Recall at it."),
    "BladeSpiritsSpell": ({"Kind": "Summon", "Creature": "BladeSpirits", "At": "TargetLocation", "Duration": 120.0,
                           "Controlled": False, "ControlSlots": 2},
                          "Cast delay x3. Uncontrolled: it attacks what is near."),
    "DispelFieldSpell": ({"Kind": "DispelField",
                          "Accepts": "a dispellable field item: Fire/Poison/Paralyze/Energy fields, Wall of Stone "
                                     "tiles, Gate Travel gates",
                          "Effect": "Deletes that one tile/item."}, ""),
    "IncognitoSpell": ({"Kind": "Disguise", "Randomizes": ["Name", "SkinHue", "Hair", "FacialHair", "HairHue"],
                        "Duration": X("min(144, floor(6 * Caster.MageryFixed / 50) + 1)", Unit="s"),
                        "FailsIf": ["carrying a faction sigil", "already incognito", "under a disguise kit",
                                    "polymorphed or otherwise body-modded (fizzle)"]}, ""),
    "MagicReflectSpell": ({"Kind": "ResistToggle",
                           "Resists": {"Physical": X("-25 + trunc(Caster.Inscribe / 20)"), "Fire": 10, "Cold": 10,
                                       "Poison": 10, "Energy": 10},
                           "Duration": "UntilRecast",
                           "Note": "In the AOS era this does NOT reflect spells; it only shifts resists."},
                          "Pre-AOS: reflects the next 8-15 circles of spells (MagicDamageAbsorb)."),
    "MindBlastSpell": ({"Kind": "Damage", "Formula": X("min(60, trunc((Caster.Magery + min(200, Caster.Int)) / 5) + "
                                                       "rand(2, 6))", Note="Then at impact: rand(F, F + 4)"),
                        "Final": X("rand(F, F + 4)", Note="F = Formula"), "Types": {"Cold": 100}, "Delay": 1.0,
                        "Scaling": None, "SlayerApplies": False},
                       "No Evaluating Intelligence scaling. Pre-AOS: based on the target's stat spread."),
    "ParalyzeSpell": ({"Kind": "Paralyze",
                       "Duration": X("max(0, trunc(Caster.EvalInt / 10 - Target.MagicResist / 10) * "
                                     "(Target.IsPlayer ? 1 : 3))", Unit="s"),
                       "FailsIf": "the target is already frozen/paralyzed or is casting a non-Chivalry spell "
                                  "(refused before mana is spent)",
                       "Special": "Plague Beast Lord: 120 s",
                       "BreaksOn": "damage (any paralysis ends when the paralyzed mobile is damaged)"},
                      "Pre-AOS: 7 + Magery/5 s, x0.75 if resisted."),
    "PoisonFieldSpell": ({"Kind": "Field", "Field": "Poison", "Tiles": 5, "Layout": "PerpendicularToCaster",
                          "TileDelay": 0.3, "Duration": X("3 + trunc(Caster.MageryFixed / 25)", Unit="s"),
                          "Poison": {"Total": X("trunc((Caster.MageryFixed + Caster.PoisoningFixed) / 2)"),
                                     "Level": [{"MinTotal": 1000, "Level": 3}, {"MinTotal": 851, "Level": 2},
                                               {"MinTotal": 651, "Level": 1}, {"MinTotal": None, "Level": 0}],
                                     "OnEnter": True, "Interval": 1.5, "FirstTick": 0.5},
                          "AffectsCaster": False,
                          "Note": "Each tick poisons mobiles standing on the tile (its 1x2 footprint along the wall)."},
                         ""),
    "SummonCreatureSpell": ({"Kind": "Summon", "Creature": {"OneOf": "SUMMON_CREATURES"}, "At": "NearCaster",
                             "Duration": SUMMON_DURATION, "Controlled": True, "RequiresFreeSlots": 2,
                             "ControlSlots": "the creature's own (1 for every listed creature)"},
                            "Cast delay x5. Duration is not scaled."),
    "DispelSpell": ({"Kind": "Dispel", "Accepts": "a dispellable summoned creature",
                     "Chance": X("(50 + 100 * (Caster.Magery - DispelDifficulty) / (DispelFocus * 2)) / 100 - "
                                 "EnchantedSummoning / 100", Roll="dispels if Chance > randf()",
                                 Note="DispelDifficulty / DispelFocus are the creature's own numbers; "
                                      "EnchantedSummoning is its summoner's mastery bonus"),
                     "OnFail": "nothing (the act is still harmful)",
                     "Note": "Harmful unless it is the caster's own summon."}, ""),
    "EnergyBoltSpell": (damage(40, 1, 5, "Energy", 1.0), "Pre-AOS: 24-41."),
    "ExplosionSpell": (damage(40, 1, 5, "Fire", 3.0, ComputedAt="detonation"),
                       "The 3 s fuse starts after casting; damage is rolled when it goes off (if the caster can "
                       "still harm the target). Pre-AOS: 2.5 s, 23-44."),
    "InvisibilitySpell": ({"Kind": "Invisibility", "Duration": X("1.2 * Caster.MageryFixed / 10", Unit="s",
                                                                 Note="= 1.2 x Magery seconds (not rounded)"),
                           "Also": "clears the target's combatant and war mode; ends early on any revealing action",
                           "FailsIf": "target is a vendor or higher staff level"}, ""),
    "MarkSpell": ({"Kind": "Mark", "Accepts": "a RecallRune in the caster's backpack",
                   "Effect": "The rune remembers the caster's location and map.",
                   "FailsIf": ["travel rules forbid marking here", "standing in a house/multi (on a non-galleon boat "
                               "too)"]}, ""),
    "MassCurseSpell": ({"Kind": "AreaCurse", "Area": {"Center": "TargetLocation", "Radius": 2},
                        "Same": "CurseSpell, on every valid harmful target in the area (line of sight from the "
                                "caster), each at its own Resisting Spells",
                        "Targets": "mobiles only"}, ""),
    "ParalyzeFieldSpell": ({"Kind": "Field", "Field": "Paralyze", "Tiles": 5, "Layout": "PerpendicularToCaster",
                            "TileDelay": 0.3, "Duration": X("3 + Caster.Magery / 3", Unit="s"),
                            "Paralyze": {"Duration": X("max(0, (2 + trunc(Caster.EvalInt / 10) - "
                                                       "trunc(Target.MagicResist / 10)) * (Target.IsPlayer ? 1 : 3))",
                                                       Unit="s"),
                                         "OnEnter": True, "Interval": None},
                            "AffectsCaster": False}, "Paralyzes only when stepped onto (no repeating tick)."),
    "RevealSpell": ({"Kind": "Reveal", "Area": {"Center": "TargetLocation",
                                                "Radius": X("1 + trunc(Caster.Magery / 20)")},
                     "Chance": X("Target.HidingFixed + Target.StealthFixed > 0 ? trunc(50 * (Caster.MageryFixed + "
                                 "Caster.DetectHiddenFixed) / (Target.HidingFixed + Target.StealthFixed)) : 100",
                                 Unit="percent", Roll="reveals if Chance > rand(0, 99)"),
                     "AlwaysReveals": "anyone hidden by the Invisibility spell",
                     "Note": "Only mobiles the caster could detect with Detect Hidden rules; players, or lower staff."},
                    ""),
    "ChainLightningSpell": ({"Kind": "ChainDamage", "Base": 51, "Dice": 1, "Sides": 5, "Types": {"Energy": 100},
                             "Delay": 1.0, "Scaling": "EvalIntScaling (without the target damage scalar)",
                             "Jumps": X("3 + trunc(max(0, Caster.Magery - 100) / 25)"),
                             "JumpRange": "JUMP_RANGE", "JumpFalloff": "JUMP_FALLOFF", "JumpDelay": "JUMP_DELAY",
                             "Next": "the nearest valid harmful target within JumpRange of the last one, in its line "
                                     "of sight, never one already hit"},
                            "SeriousFun: a jumping bolt instead of stock's area blast."),
    "EnergyFieldSpell": ({"Kind": "Wall", "Field": "Energy", "Tiles": 5, "Layout": "PerpendicularToCaster",
                          "TileDelay": 0.3, "Duration": X("trunc((15 + trunc(Caster.MageryFixed / 5)) / 7)", Unit="s"),
                          "BlocksMovement": True}, "Pre-AOS: 2 + Magery x 0.28 s."),
    "FlameStrikeSpell": (damage(48, 1, 5, "Fire", 1.0), "Pre-AOS: 27-48, x0.6 if resisted."),
    "GateTravelSpell": ({"Kind": "Travel", "Mode": "Gate", "Destination": "MarkedLocation",
                         "Sources": ["a marked RecallRune", "a Runebook's default entry (no charge used)",
                                     "a galleon", "a writ of lease"],
                         "Gate": {"OpensAfter": 1.0, "Duration": 30.0, "TwoWay": True, "Dispellable": True},
                         "FailsIf": ["criminal", "in combat (2 minutes)", "carrying a faction sigil",
                                     "a gate already stands at either end", "travel rules", "Young player to Felucca"]},
                        ""),
    "ManaVampireSpell": ({"Kind": "ManaDrain",
                          "Amount": X("min(clamp(trunc(trunc(Caster.EvalInt - Target.MagicResist) / "
                                      "(Target.IsPlayer ? 1 : 2)), 0, Target.Mana), Caster.ManaMax - Caster.Mana)"),
                          "GivesToCaster": True, "BreaksParalysis": True, "DisruptsTargetCasting": True},
                         "Pre-AOS: all the target's mana unless resisted."),
    "MassDispelSpell": ({"Kind": "Dispel", "Area": {"Center": "TargetLocation", "Radius": 8},
                         "Same": "DispelSpell's chance, per dispellable summon (own, or harmful-able)",
                         "OnFail": "the caster becomes harmful to it"}, ""),
    "MeteorSwarmSpell": ({"Kind": "Damage", "Base": 51, "Dice": 1, "Sides": 5, "Types": {"Fire": 100}, "Delay": 1.0,
                          "Area": {"Center": "TargetLocation", "Radius": 2},
                          "Scaling": "EvalIntScaling",
                          "Split": X("Count > 2 ? D * 2 / Count : D", Note="D = the scaled damage"),
                          "Note": "Every valid harmful target in the area (line of sight from the caster) is hit."},
                         "Pre-AOS: 27-48 divided by the number of targets."),
    "PolymorphSpell": ({"Kind": "Polymorph", "Forms": "POLYMORPH_FORMS", "Duration": "UntilRecast",
                        "Hue": "random skin hue for human forms, otherwise none",
                        "Dismounts": "for non-human forms",
                        "FailsIf": ["flying", "carrying a faction sigil", "under a transformation spell",
                                    "disguised", "incognito or body-modded (fizzle)"],
                        "Recast": "casting it while polymorphed ends the polymorph (no new form)"},
                       "The form is picked before casting. Pre-ML: lasted min(Magery, 120) s."),
    "EarthquakeSpell": ({"Kind": "Damage", "Area": {"Center": "Caster", "Radius": X("1 + trunc(Caster.Magery / 15)")},
                         "Formula": X("(Target.IsPlayer ? trunc(Target.Hits / 2) : clamp(trunc(Target.Hits / 2), 15, "
                                      "100)) + rand(0, 15)"),
                         "Types": {"Physical": 100}, "Delay": 0.0, "Scaling": None},
                        "Hits every valid harmful target around the caster (line of sight). Pre-AOS: 60% of current "
                        "hits, 10-75."),
    "EnergyVortexSpell": ({"Kind": "Summon", "Creature": "EnergyVortex", "At": "TargetLocation", "Duration": 90.0,
                           "Controlled": False, "ControlSlots": 2}, "Uncontrolled."),
    "ResurrectionSpell": ({"Kind": "Resurrect", "Accepts": "a dead player (not the caster), adjacent",
                           "Confirm": "the ghost must accept",
                           "Result": {"Hits": 10, "Stam": "StamMax", "Mana": 0,
                                      "CompassionBonus": "if the caster is a Seeker/Follower/Knight of Compassion: "
                                                         "Hits = 20/40/80 percent of HitsMax"},
                           "FailsIf": ["the body cannot fit there", "in Khaldun"]}, "Target range 1."),
    "AirElementalSpell": ({"Kind": "Summon", "Creature": "SummonedAirElemental", "At": "NearCaster",
                           "Duration": SUMMON_DURATION, "Controlled": True, "ControlSlots": 2}, ""),
    "SummonDaemonSpell": ({"Kind": "Summon", "Creature": "SummonedDaemon", "At": "NearCaster",
                           "Duration": SUMMON_DURATION, "Controlled": True, "ControlSlots": 4}, ""),
    "EarthElementalSpell": ({"Kind": "Summon", "Creature": "SummonedEarthElemental", "At": "NearCaster",
                             "Duration": SUMMON_DURATION, "Controlled": True, "ControlSlots": 2}, ""),
    "FireElementalSpell": ({"Kind": "Summon", "Creature": "SummonedFireElemental", "At": "NearCaster",
                            "Duration": SUMMON_DURATION, "Controlled": True, "ControlSlots": 4}, ""),
    "WaterElementalSpell": ({"Kind": "Summon", "Creature": "SummonedWaterElemental", "At": "NearCaster",
                             "Duration": SUMMON_DURATION, "Controlled": True, "ControlSlots": 3}, ""),
}

# Hand classification of the target kind; cross-checked against each spell's Target class.
TARGET_KIND = {
    "Harmful": {"ClumsySpell", "FeeblemindSpell", "MagicArrowSpell", "WeakenSpell", "HarmSpell", "FireballSpell",
                "PoisonSpell", "CurseSpell", "LightningSpell", "ManaDrainSpell", "MindBlastSpell", "ParalyzeSpell",
                "DispelSpell", "EnergyBoltSpell", "ExplosionSpell", "ChainLightningSpell", "FlameStrikeSpell",
                "ManaVampireSpell"},
    "Beneficial": {"HealSpell", "NightSightSpell", "AgilitySpell", "CunningSpell", "CureSpell", "StrengthSpell",
                   "BlessSpell", "GreaterHealSpell", "InvisibilitySpell", "ResurrectionSpell"},
    "Location": {"TeleportSpell", "WallOfStoneSpell", "ArchCureSpell", "ArchProtectionSpell", "FireFieldSpell",
                 "BladeSpiritsSpell", "PoisonFieldSpell", "MassCurseSpell", "ParalyzeFieldSpell", "RevealSpell",
                 "EnergyFieldSpell", "MassDispelSpell", "MeteorSwarmSpell", "EnergyVortexSpell"},
    "Item": {"MagicTrapSpell", "RemoveTrapSpell", "MagicLockSpell", "TelekinesisSpell", "UnlockSpell", "RecallSpell",
             "DispelFieldSpell", "MarkSpell", "GateTravelSpell"},
    "Self": {"ReactiveArmorSpell", "ProtectionSpell", "IncognitoSpell", "MagicReflectSpell", "PolymorphSpell",
             "EarthquakeSpell"},
    "None": {"CreateFoodSpell", "SummonCreatureSpell", "AirElementalSpell", "SummonDaemonSpell", "EarthElementalSpell",
             "FireElementalSpell", "WaterElementalSpell"},
}
# Expected AOS damage dice per spell (bonus, dice, sides) -- asserted against GetNewAosDamage(...) in the source.
AOS_DICE = {"MagicArrowSpell": (10, 1, 4), "HarmSpell": (17, 1, 5), "FireballSpell": (19, 1, 5),
            "LightningSpell": (23, 1, 4), "EnergyBoltSpell": (40, 1, 5), "ExplosionSpell": (40, 1, 5),
            "FlameStrikeSpell": (48, 1, 5), "ChainLightningSpell": (51, 1, 5), "MeteorSwarmSpell": (51, 1, 5)}
HEAL_DICE = {"HealSpell": (8, 1, 4), "GreaterHealSpell": (40, 1, 10)}
# Follower slots each summon needs free before casting (CheckCast) -- asserted.
FOLLOWER_CHECK = {"BladeSpiritsSpell": 2, "SummonCreatureSpell": 2, "AirElementalSpell": 2, "EarthElementalSpell": 2,
                  "FireElementalSpell": 4, "WaterElementalSpell": 3, "SummonDaemonSpell": 4, "EnergyVortexSpell": 2}
# Cast-delay changes, asserted against the overrides found.
CAST_BASE_DELTA = {"ArchCureSpell": -0.25}
CAST_MULTIPLIER = {"BladeSpiritsSpell": 3, "SummonCreatureSpell": 5}


# ---------------------------------------------------------------------------------------------------------------------
# Parsing
# ---------------------------------------------------------------------------------------------------------------------

def parse_registry(spells_dir):
    text = strip_comments(read(os.path.join(spells_dir, "Initializer.cs")))
    reg = {}
    for m in re.finditer(r"Register\(\s*(\d+)\s*,\s*typeof\(\s*(\w+)\.(\w+)\s*\)\s*\)", text):
        sid = int(m.group(1))
        if sid < 64:
            reg[sid] = (m.group(2), m.group(3))
    assert sorted(reg) == list(range(64)), "registry ids 0-63 incomplete"
    return reg


def parse_spell(path, cls):
    raw = read(path)
    text = strip_comments(raw)
    body = class_body(text, cls)
    assert body, cls
    info = re.search(r"new\s+SpellInfo\s*\((.*?)\)\s*;", body, re.S)
    assert info, cls
    args = [a.strip() for a in re.split(r",(?![^(]*\))", info.group(1))]
    name, mantra = args[0].strip('"'), args[1].strip('"')
    regs = [a.split(".", 1)[1] for a in args if a.startswith("Reagent.")]
    town = not any(a == "false" for a in args[2:])
    circle = re.search(r"SpellCircle\.(\w+)", body).group(1)
    rec = {"Name": name, "Mantra": mantra, "Reagents": regs, "Town": town, "Circle": CIRCLE_FOLDERS.index(circle) + 1}

    # the first Target-derived nested class: base(range, allowGround, flags)
    t = re.search(r"class\s+\w+\s*:\s*Target\b[^{]*\{", body)
    if t:
        tb = body[t.end() - 1:matching(body, t.end() - 1)]
        b = re.search(r":\s*base\s*\((.*?),\s*(true|false)\s*,\s*TargetFlags\.(\w+)\s*\)", tb, re.S)
        assert b, cls
        rec["Range"] = int(era(b.group(1)))
        rec["Ground"] = b.group(2) == "true"
        rec["Flags"] = b.group(3)
    else:
        rec["Range"] = None

    m = re.search(r"CastDelayBase\s*\{\s*get\s*\{\s*return\s+base\.CastDelayBase\s*-\s*TimeSpan\.FromSeconds\(\s*"
                  r"([\d.]+)\s*\)", body)
    rec["CastDelta"] = -float(m.group(1)) if m else 0.0
    m = re.search(r"override\s+TimeSpan\s+GetCastDelay\s*\(\s*\)\s*\{(.*?)\n\s*\}", body, re.S)
    rec["CastMult"] = 1
    if m:
        mm = re.search(r"base\.GetCastDelay\(\)\.Ticks\s*\*\s*(\([^;]*?\)|\d+)\s*\)", m.group(1))
        assert mm, cls
        rec["CastMult"] = int(era(mm.group(1)))
    m = re.search(r"Followers\s*\+\s*(\([^)]*\)|\d+)\s*\)\s*>\s*(?:this\.)?Caster\.FollowersMax", body)
    rec["Followers"] = int(era(m.group(1))) if m else None
    rec["AosDice"] = [tuple(int(x) for x in m.groups()) for m in
                      re.finditer(r"GetNewAosDamage\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,", body)]
    rec["HealDice"] = [tuple(int(x) for x in m.groups()) for m in
                       re.finditer(r"GetNewAosHealing\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", body)]
    types = set()
    for m in re.finditer(r"SpellHelper\.Damage\(\s*[^;]*?,\s*damage\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,"
                         r"\s*(\d+)\s*\)", body):
        types.add(tuple(int(x) for x in m.groups()))
    for m in re.finditer(r"SpellHelper\.Damage\(\s*this\s*,\s*\w+\s*,\s*[^,]+,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,"
                         r"\s*(\d+)\s*,\s*(\d+)\s*\)", body):
        types.add(tuple(int(x) for x in m.groups()))
    for m in re.finditer(r"AOS\.Damage\(\s*\w+\s*,\s*[\w.]+\s*,\s*\w+\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,"
                         r"\s*(\d+)\s*\)", body):
        types.add(tuple(int(x) for x in m.groups()))
    rec["DamageTypes"] = sorted(types)
    rec["Reflect"] = "CheckReflect(" in body
    m = re.search(r"override\s+bool\s+DelayedDamage\s*(?:\{\s*get\s*\{\s*return\s+([^;]+);|=>\s*([^;]+);)", body)
    rec["Delayed"] = None if not m else (m.group(1) or m.group(2)).strip()
    rec["Body"] = body
    return rec


def parse_scrolls(scripts):
    out = {}
    for folder, _, files in os.walk(scripts):
        for f in files:
            if not f.endswith(".cs"):
                continue
            text = read(os.path.join(folder, f))
            if "SpellScroll" not in text:
                continue
            text = strip_comments(text)
            for m in re.finditer(r"class\s+(\w+)\s*:\s*SpellScroll\b", text):
                body = class_body(text, m.group(1))
                b = re.search(r":\s*base\(\s*(\d+)\s*,\s*(0x[0-9A-Fa-f]+|\d+)\s*,\s*amount\s*\)", body)
                if b and int(b.group(1)) < 64:
                    sid = int(b.group(1))
                    assert sid not in out, "two scrolls for spell %d" % sid
                    out[sid] = m.group(1)
    assert sorted(out) == list(range(64)), "missing scrolls: %s" % sorted(set(range(64)) - set(out))
    return out


def parse_spellbook(scripts):
    text = strip_comments(read(os.path.join(scripts, "Items", "Equipment", "Spellbooks", "Spellbook.cs")))
    body = class_body(text, "Spellbook")
    off = int(re.search(r"BookOffset\s*\{\s*get\s*\{\s*return\s+(\d+)", body).group(1))
    cnt = int(re.search(r"BookCount\s*\{\s*get\s*\{\s*return\s+(\d+)", body).group(1))
    assert re.search(r"m_Content\s*&\s*\(\(ulong\)1\s*<<\s*spellID\)", body), "spellbook bit layout changed"
    return off, cnt


def parse_base(spells_dir):
    mag = strip_comments(read(os.path.join(spells_dir, "Base", "MagerySpell.cs")))
    sp = strip_comments(read(os.path.join(spells_dir, "Base", "Spell.cs")))
    mana = [int(x) for x in re.search(r"m_ManaTable\s*=\s*new\s+int\[\]\s*\{([^}]*)\}", mag).group(1).split(",")]
    off, length, den = re.search(r"ChanceOffset\s*=\s*([\d.]+)\s*,\s*ChanceLength\s*=\s*([\d.]+)\s*/\s*([\d.]+)",
                                 mag).groups()
    base_ticks = int(re.search(r"\(\(\s*(\d+)\s*\+\s*\(int\)Circle\s*\)\s*\*\s*CastDelaySecondsPerTick", mag).group(1))
    gem = re.search(r"ArcaneGem\.ConsumeCharges\(\s*Caster\s*,\s*\(Core\.SE\s*\?\s*(\d+)", mag).group(1)

    def prop(name, kind=r"[\d.]+"):
        m = re.search(r"\b" + name + r"\s*\{\s*get\s*\{\s*return\s+(?:TimeSpan\.FromSeconds\()?\s*(" + kind + ")", sp)
        assert m, name
        return float(m.group(1))
    return {
        "Mana": mana, "ChanceOffset": float(off), "ChanceLengthNum": float(length), "ChanceDen": float(den),
        "BaseTicks": base_ticks, "SecondsPerTick": prop("CastDelaySecondsPerTick"),
        "FastScalar": prop("CastDelayFastScalar"), "Minimum": prop("CastDelayMinimum"),
        "RecoveryBase": prop("CastRecoveryBase"), "RecoveryFastScalar": prop("CastRecoveryFastScalar"),
        "RecoveryPerSecond": prop("CastRecoveryPerSecond"), "RecoveryMinimum": prop("CastRecoveryMinimum"),
        "Variation": float(re.search(r"GetCastDelay\(\)\.Subtract\(TimeSpan\.FromMilliseconds\((\d+)\)\)", sp).group(1)),
        "NextSpellDelay": float(re.search(r"NextSpellDelay\s*=\s*TimeSpan\.FromSeconds\(([\d.]+)\)", sp).group(1)),
        "LmcCap": int(re.search(r"if\s*\(\s*lmc\s*>\s*(\d+)\s*\)", sp).group(1)),
        "FreeRegs": re.search(r'"Spells\.FreeBasicReagents"\s*,\s*(true|false)', sp).group(1) == "true",
        "FreeRegScale": float(re.search(r'"Spells\.FreeReagentManaScale"\s*,\s*([\d.]+)', sp).group(1)),
        "WeaponCasting": re.search(r'"Spells\.WeaponCasting"\s*,\s*(true|false)', sp).group(1) == "true",
        "GemCharges": int(gem),
    }


def parse_poisons(scripts):
    text = strip_comments(read(os.path.join(scripts, "Misc", "Poison.cs")))
    m = re.search(r"if\s*\(\s*Core\.AOS\s*\)\s*\{(.*?)\}", text, re.S)
    rows = []
    for r in re.finditer(r'new\s+PoisonImpl\(\s*"(\w+)"\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)'
                         r'\s*,\s*([\d.]+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)', m.group(1)):
        rows.append({"Level": int(r.group(2)), "Name": r.group(1), "Min": int(r.group(3)), "Max": int(r.group(4)),
                     "Percent": float(r.group(5)), "Delay": float(r.group(6)), "Interval": float(r.group(7)),
                     "Ticks": int(r.group(8))})
    assert [r["Level"] for r in rows] == [0, 1, 2, 3, 4], rows
    assert re.search(r"damage\s*=\s*1\s*\+\s*\(int\)\(m_Mobile\.Hits\s*\*\s*m_Poison\.m_Scalar\)", text)
    return rows


def parse_polymorph(scripts):
    text = strip_comments(read(os.path.join(scripts, "Gumps", "PolymorphGump.cs")))
    entries = {m.group(1): int(m.group(2), 16) for m in re.finditer(
        r"PolymorphEntry\s+(\w+)\s*=\s*new\s+PolymorphEntry\(\s*\d+\s*,\s*(0x[0-9A-Fa-f]+)", text)}
    new = class_body(text, "NewPolymorphGump")
    order = re.findall(r"PolymorphEntry\.(\w+)", re.search(r"m_Entries\s*=\s*new\s+PolymorphEntry\[\]\s*\{(.*?)\}",
                                                           new, re.S).group(1))
    return [{"Name": n, "Body": entries[n]} for n in order]


def parse_type_list(body, field):
    m = re.search(field + r"\s*=\s*new\s+\w+\[\]\s*\{(.*?)\}\s*;", body, re.S)
    assert m, field
    return re.findall(r"typeof\(\s*(\w+)\s*\)", m.group(1))


def parse_caps(root):
    caps = {}
    path = os.path.join(root, "Config", "PlayerCaps.cfg")
    for line in read(path).splitlines():
        m = re.match(r"\s*(\w+)\s*=\s*(\d+)\s*$", line)
        if m and m.group(1) in STAT_CAPS:
            caps[m.group(1)] = int(m.group(2))
    assert set(caps) == set(STAT_CAPS), caps
    return caps


def check_source_rules(root, spells_dir):
    """Asserts for the formulas written by hand in Pools/Casting, so a different ServUO fails loudly."""
    sp = strip_comments(read(os.path.join(spells_dir, "Base", "Spell.cs")))
    helper = strip_comments(read(os.path.join(spells_dir, "Base", "SpellHelper.cs")))
    mag = strip_comments(read(os.path.join(spells_dir, "Base", "MagerySpell.cs")))
    regen = strip_comments(read(os.path.join(root, "Scripts", "Misc", "RegenRates.cs")))
    mob = strip_comments(read(os.path.join(root, "Server", "Mobile.cs")))
    pm = strip_comments(read(os.path.join(root, "Scripts", "Mobiles", "PlayerMobile.cs")))
    checks = [
        (sp, r"int\s+evalScale\s*=\s*30\s*\+\s*\(\(9\s*\*\s*evalSkill\)\s*/\s*100\)"),
        (sp, r"scribeBonus\s*=\s*inscribeSkill\s*>=\s*1000\s*\?\s*10\s*:\s*inscribeSkill\s*/\s*200"),
        (sp, r"\(Caster\.Int\s*/\s*10\)"),
        (sp, r"(?s)!ConsumeReagents\(\)\).*?m_Caster\.Mana\s*<\s*mana.*?CheckFizzle\(\)\)\s*\{\s*m_Caster\.Mana\s*-="),
        (sp, r"int\s+evalScale\s*=\s*30\s*\+\s*\(\(9\s*\*\s*GetDamageFixed\(m_Caster\)\)\s*/\s*100\)"),
        (helper, r"percent\s*=\s*8\s*\+\s*\(caster\.Skills\.EvalInt\.Fixed\s*/\s*100\)\s*-\s*\(resistFixed\s*/\s*100\)"),
        (helper, r"percent\s*=\s*1\s*\+\s*\(caster\.Skills\.EvalInt\.Fixed\s*/\s*100\)"),
        (helper, r"int\s+span\s*=\s*\(\(\(6\s*\*\s*caster\.Skills\.EvalInt\.Fixed\)\s*/\s*50\)\s*\+\s*1\)"),
        (helper, r"AosDamageDelay\s*=\s*TimeSpan\.FromSeconds\(1\.0\)"),
        (helper, r"return\s+HasSpellFocus\(m,\s*castskill\)|return\s+30;"),
        (mag, r"double\s+firstPercent\s*=\s*value\s*/\s*5\.0"),
        (mag, r"secondPercent\s*=\s*value\s*-\s*\(\(\(Caster\.Skills\[CastSkill\]\.Value\s*-\s*20\.0\)\s*/\s*5\.0\)\s*\+"
              r"\s*\(1\s*\+\s*\(int\)circle\)\s*\*\s*5\.0\)"),
        (regen, r"DefaultHitsRate\s*=\s*TimeSpan\.FromSeconds\(11\.0\)"),
        (regen, r"DefaultStamRate\s*=\s*TimeSpan\.FromSeconds\(7\.0\)"),
        (regen, r"DefaultManaRate\s*=\s*TimeSpan\.FromSeconds\(7\.0\)"),
        (regen, r"1\.0\s*/\s*\(0\.1\s*\*\s*\(1\s*\+\s*HitPointRegen\(from\)\)\)"),
        (regen, r"1\.0\s*/\s*\(1\.42\s*\+\s*\(bonus\s*/\s*100\)\)"),
        (regen, r"medBonus\s*=\s*\(0\.0075\s*\*\s*med\)\s*\+\s*\(0\.0025\s*\*\s*from\.Int\)"),
        (regen, r"itemBase\s*=\s*\(\(\(\(med\s*/\s*2\)\s*\+\s*\(focus\s*/\s*4\)\)\s*/\s*90\)\s*\*\s*\.65\)\s*\+\s*2\.35"),
        (regen, r"rate\s*=\s*1\.0\s*/\s*\(0\.2\s*\+\s*focusBonus\s*\+\s*medBonus\s*\+\s*itemBonus\)"),
        (regen, r"Math\.Min\(points,\s*18\)"),
        (regen, r"Math\.Min\(points,\s*24\)"),
        (mob, r"HitsMax\s*\{\s*get\s*\{\s*return\s+50\s*\+\s*\(Str\s*/\s*2\)"),
        (pm, r"return\s+\(strBase\s*/\s*2\)\s*\+\s*50\s*\+\s*strOffs"),
        (pm, r"strOffs\s*>\s*25"),
        (pm, r"Race\s*==\s*Race\.Elf\)\s*\?\s*20"),
    ]
    for text, pat in checks:
        assert re.search(pat, text), "source rule changed: " + pat


# ---------------------------------------------------------------------------------------------------------------------
# Static sections (formulas written out from the scripts named in each Source; checked by check_source_rules)
# ---------------------------------------------------------------------------------------------------------------------

def pools(caps):
    return {
        "Source": "Server/Mobile.cs, Scripts/Mobiles/PlayerMobile.cs, Scripts/Mobiles/Normal/BaseCreature.cs, "
                  "Scripts/Misc/RegenRates.cs, Scripts/Skills/Meditation.cs, Config/PlayerCaps.cfg",
        "HitsMax": {
            "Player": X("50 + trunc(min(Caster.Str, StrMaxCap) / 2) + min(BonusHits, 25)",
                        Note="Str includes stat modifiers, capped at StrMaxCap. BonusHits = the Hit Point Increase "
                             "item property (capped 25 for players). Wolf/Kitsune animal forms add 20; Toughness and "
                             "Invigorate masteries add their bonus."),
            "Creature": "HitsMaxSeed + its Str modifiers when the creature has a seed (Creatures.json Hits), "
                        "otherwise Str",
            "Default": X("50 + trunc(Caster.Str / 2)", Note="plain mobiles (neither player nor creature)")},
        "StamMax": {
            "Player": X("min(Caster.Dex, DexMaxCap) + BonusStam", Note="BonusStam = Stamina Increase item property"),
            "Creature": "StamMaxSeed + Dex modifiers when seeded, otherwise Dex", "Default": "Dex"},
        "ManaMax": {
            "Player": X("min(Caster.Int, IntMaxCap) + BonusMana + (Elf ? 20 : 0)",
                        Note="BonusMana = Mana Increase item property; Intuition mastery and Urali Trance tonic add "
                             "their bonus"),
            "Creature": "ManaMaxSeed + Int modifiers when seeded, otherwise Int", "Default": "Int"},
        "StatCaps": caps,
        "Regen": {
            "Model": "Each pool gains 1 point every Interval seconds; the interval is recomputed after every tick.",
            "Hits": {
                "Interval": X("1 / (0.1 * (1 + HitPointRegen))", Unit="s"),
                "HitPointRegen": "Hit Point Regeneration item property + creature DefaultHitsRegen + 2 for human "
                                 "players; floored at 0; players capped at 18; then +20 Horrific Beast, + Ninjitsu "
                                 "Fixed/30 in dog/cat form, + mastery bonuses (after the cap)",
                "Stops": "dead, or poisoned (unless the creature regenerates through poison); summons and dead pets "
                         "never regenerate hits",
                "Default": 11.0},
            "Stam": {
                "Interval": X("1 / (1.42 + (Caster.Focus * 0.1 + StamRegen) / 100) * (Monster ? 1.95 : 1)",
                              Unit="s", Note="Monster = a creature flagged as a monster"),
                "StamRegen": "Stamina Regeneration item property + creature DefaultStamRegen + 15 Vampiric Embrace, "
                             "+20 Kirin form; players capped at 24; then mastery bonuses; floored at -1",
                "Default": 7.0,
                "Note": "Each tick also gives a Focus gain check. Paragons and dead pets do not regenerate stamina."},
            "Mana": {
                "Interval": X("1 / (0.2 + FocusBonus + MedBonus + ItemBonus)", Unit="s"),
                "FocusBonus": X("Caster.Focus / 200"),
                "MedBonus": X("ArmorPenalty > 0 ? 0 : (0.0075 * Caster.Meditation + 0.0025 * Caster.Int) * "
                              "(Meditating ? 2 : 1)",
                              Note="The source also multiplies by 1.1 when MedBonus >= 100, which can never happen."),
                "ItemBase": X("((Caster.Meditation / 2 + Caster.Focus / 4) / 90) * 0.65 + 2.35"),
                "Intensity": X("min(5.5, sqrt(ManaRegen))"),
                "ItemBonus": X("(ItemBase * Intensity - (ItemBase - 1)) / 10",
                               Note="negative (between -0.2 and -0.135) when ManaRegen is 0, as in the source"),
                "ManaRegen": "Mana Regeneration item property + creature DefaultManaRegen + 3 Vampiric Embrace or +13 "
                             "Lich form + 2 for gargoyle players (no cap in ML)",
                "Default": 7.0,
                "Note": "Each tick gives Meditation (when not actively meditating) and Focus gain checks. Dead pets "
                        "do not regenerate mana."},
            "ArmorPenalty": {
                "Value": X("(Neck + Hands + Head + Arms + Legs + Chest) / 4",
                           Note="Each worn armor piece adds: 0 if it has Mage Armor or Spell Channeling, otherwise "
                                "its scaled base armor rating (MeditationAllowance None), half of it (Half) or 0 "
                                "(All). Shields don't count in AOS."),
                "Effect": "Any penalty above 0 removes MedBonus entirely and blocks active Meditation."},
            "Meditation": {
                "Chance": X("(50 + (Caster.Meditation - (Caster.ManaMax - Caster.Mana)) * 2) / 100",
                            Roll="succeeds if Chance > randf()"),
                "Effect": "Sets Meditating (doubles MedBonus) until the meditator acts, moves or is hurt.",
                "RefusedIf": ["targeting something", "mana already full (10 s)", "armor penalty > 0 (10 s)"],
                "Hands": "Weapons/shields other than spellbooks, runebooks and Spell Channeling items go to the "
                         "backpack.",
                "SkillDelay": 10.0}}}


def casting(base, spells):
    circles = []
    for c in range(8):
        avg = base["ChanceLengthNum"] / base["ChanceDen"] * c
        sc = c - 2
        savg = base["ChanceLengthNum"] / base["ChanceDen"] * sc
        circles.append({"Circle": c + 1, "Min": round(avg - base["ChanceOffset"], 4),
                        "Max": round(avg + base["ChanceOffset"], 4),
                        "ScrollMin": round(savg - base["ChanceOffset"], 4),
                        "ScrollMax": round(savg + base["ChanceOffset"], 4)})
    spt = base["SecondsPerTick"]
    return {
        "Source": "Scripts/Spells/Base/Spell.cs, MagerySpell.cs, SpellHelper.cs",
        "CircleMana": base["Mana"],
        "ManaCost": {
            "Expr": "trunc(Mana * (1 - min(LowerManaCost, %d) / 100 - InherentLMC / 100))" % base["LmcCap"],
            "Mana": X("FreeReagents ? ceil(CircleMana * %g) : CircleMana" % base["FreeRegScale"],
                      Note="SeriousFun: when basic reagents are free (below), a player casting from a book pays %g x "
                           "mana, rounded up, before Lower Mana Cost" % base["FreeRegScale"]),
            "LowerManaCost": "the Lower Mana Cost item property, capped %d; InherentLMC = armor's inherent LMC "
                             "(added after the cap)" % base["LmcCap"],
            "Other": "Mind Rot raises the scalar; Purge Magic's curse adds 0.5; a Mana Phasing Orb makes the next "
                     "spell free. Wands cost no mana.",
            "Checked": "at cast start (to begin) and again when the spell takes effect (spent then)"},
        "Reagents": {
            "FreeBasicReagents": base["FreeRegs"],
            "Rule": "SeriousFun (Spells.FreeBasicReagents, default %s): a player casting from a book does not "
                    "consume the eight basic reagents (Black Pearl, Blood Moss, Garlic, Ginseng, Mandrake Root, "
                    "Nightshade, Spiders' Silk, Sulfurous Ash) -- every Magery spell -- and pays the higher mana "
                    "instead. Otherwise each listed reagent costs 1." % str(base["FreeRegs"]).lower(),
            "Skip": "Scrolls consume the scroll instead. Lower Reagent Cost (percent) skips the reagents entirely. "
                    "Non-player casters never use reagents.",
            "ArcaneGemFallback": "When reagents are missing, an Arcane Gem charge (%d) pays instead." % base["GemCharges"]},
        "CastDelay": {
            "Expr": "max(%g, (%d + Circle - 1) * %g - FasterCasting * %g + BaseDelta) * Multiplier - %g"
                    % (base["Minimum"], base["BaseTicks"], spt, spt * base["FastScalar"], base["Variation"] / 1000),
            "BasePerCircle": [round((base["BaseTicks"] + c) * spt, 4) for c in range(8)],
            "PerFasterCasting": spt * base["FastScalar"], "Minimum": base["Minimum"],
            "Subtract": base["Variation"] / 1000,
            "Notes": "Circle is 1-8. BaseDelta and Multiplier are per spell (Spells[].CastDelay). The %g s "
                     "subtraction happens after the minimum (EA timing). SeriousFun: Faster Casting is uncapped "
                     "(stock caps Magery at 2). Protection (and Urali tonic) costs 2 Faster Casting. Pre-AOS: "
                     "0.5 + 0.25 x (Circle - 1) s, no FC." % (base["Variation"] / 1000),
            "Spellstone": 0.0, "Wand": "the base delay, no Faster Casting"},
        "Recovery": {
            "Expr": "max(%g, %g - FasterCastRecovery * %g) / %g"
                    % (base["RecoveryMinimum"], base["RecoveryBase"], base["RecoveryFastScalar"],
                       base["RecoveryPerSecond"]),
            "Notes": "Seconds after the spell takes effect before another can be started (1.5 s at 0 FCR). Faster "
                     "Cast Recovery is not capped. Wands ignore it. Pre-AOS: %g s." % base["NextSpellDelay"]},
        "SkillRange": circles,
        "SkillCheck": {
            "Chance": X("(Caster.Magery - Min) / (Max - Min)",
                        Note="Below Min always fails, at/above Max always succeeds"),
            "Roll": "succeeds if rand(0, 99) <= trunc(Chance * 100)",
            "Window": "Min/Max = 100/7 x (Circle - 1) -/+ 20 from a book; a scroll uses Circle - 2. Recall from a "
                      "runebook needs no skill.",
            "Also": "Evaluating Intelligence gets a gain check (0-cap) with every cast. Creature casters never fail "
                    "the Magery check."},
        "Fizzle": {
            "SkillCheckFailed": "spell fizzles: the reagents are already used up (they are taken just before the "
                                "skill check), the mana is kept (only spent on success)",
            "Hurt": "AOS: a player caster damaged while casting (or holding a target cursor) is disturbed unless "
                    "under Protection, or a Casting Focus roll succeeds: chance% = min(12, CastingFocus property "
                    "(+2 Fish Pie)) + (Inscribe >= 50 ? trunc(InscribeFixed / 200) : 0); 100%-element damage can "
                    "also be shrugged off by matching Resonance (capped 40). Creature casters are never disturbed by "
                    "damage. No recovery delay after a disturb.",
            "Moving": "A player cannot move while casting (frozen until the spell is cast); there is no fizzle from "
                      "movement.",
            "Equip": "Equipping anything other than a spell-castable weapon while casting disturbs.",
            "UseItem": "Using an object while holding the spell's target disturbs it.",
            "NewCast": "Starting a new spell while the previous one waits for a target cancels the old one.",
            "Paralyzed": "Paralysed/frozen at the moment the spell resolves: fizzle.",
            "Refused": "Cannot start while: dead, calmed (Peacemaking), already casting, in an animal/Horrific Beast "
                       "form, paralysed/frozen, still recovering, short of mana, flying over precarious ground, "
                       "driving a boat.",
            "Hands": "SeriousFun (Spells.WeaponCasting, default %s): weapons go to the backpack on cast, except "
                     "staves stay for non-healing spells and maces/shields stay for healing/buff spells; Spell "
                     "Channeling items always stay. Healing/buff spells: Heal, Cure, Greater Heal, Arch Cure, "
                     "Resurrection, Bless, Strength, Agility, Cunning, Protection, Arch Protection, Reactive Armor, "
                     "Night Sight, Magic Reflection." % str(base["WeaponCasting"]).lower(),
            "TargetTimeout": 30.0},
        "ResistedFormula": {
            "Percent": X("max(Target.MagicResist / 5, Target.MagicResist - ((Caster.Magery - 20) / 5 + Circle * 5)) "
                         "/ 2", Note="Circle 1-8; Target.MagicResist is lowered by Evil Omen"),
            "Roll": "resisted if Percent >= 100, never if <= 0, otherwise if Percent / 100 >= randf()",
            "Gain": "the target gets a Resisting Spells gain check if its skill is below (Circle x 10) + (1 + "
                    "trunc((Circle - 1) / 6)) x 25",
            "UsedBy": "AOS: only Poison (full negation). Pre-AOS most damage spells took x0.75 (Flame Strike x0.6, "
                      "Mind Blast x0.5) and Paralyze x0.75 when resisted."},
        "EvalIntScaling": {
            "Steps": [
                "D = (Dice d Sides + Base) * 100",
                "ScribeBonus = Caster.InscribeFixed >= 1000 ? 10 : trunc(Caster.InscribeFixed / 200)",
                "DamageBonus = ScribeBonus + trunc(Caster.Int / 10) + SDI",
                "EvalScale = 30 + trunc(9 * Caster.EvalIntFixed / 100)",
                "D = trunc(D * EvalScale / 100)",
                "D = trunc(D * (100 + DamageBonus) / 100)",
                "D = trunc(D * trunc(Scalar * 100) / 100)",
                "Damage = trunc(D / 100)"],
            "SDI": "Spell Damage Increase item property, -10 under a Runed Sash's ward, minus the target's Block "
                   "reduction. Player vs player (caster and target both players): capped at 30 if the caster has "
                   "no 30+ skill in another spell school (Spell Focus), else 20.",
            "Scalar": "1.0, x the creature's own damage adjustments, x the caster's spellbook slayer (3.0, or 2.0 for "
                      "a super slayer) or x2.0 if the target's book/weapon is an opposing super slayer, x region "
                      "adjustments; 0 if the target evades (Evasion). Area/chain spells use 1.0 here.",
            "Then": "The result goes through the target's resistances by damage type (combat system).",
            "Expr": X("trunc(trunc(trunc(trunc((rand(Dice, Dice * Sides) + Base) * 100 * EvalScale / 100) * "
                      "(100 + DamageBonus) / 100) * trunc(Scalar * 100) / 100) / 100)",
                      Note="the same as Steps, in one line; rand(Dice, Dice*Sides) stands for the dice roll",
                      Names=["Base", "Dice", "Sides", "EvalScale", "DamageBonus", "Scalar"]),
            "PreAOS": "base x (1 + (EvalInt - Resist)/500 if EvalInt > Resist, else /200) + (Magery - 100)/400; x2 vs "
                      "non-human monsters."},
        "HealScaling": {
            "Steps": [
                "H = (Dice d Sides + Base) * 100",
                "H = trunc(H * (30 + trunc(9 * Caster.EvalIntFixed / 100)) / 100)",
                "H = trunc(H * (100 + ScribeBonus + trunc(Caster.Int / 10) + SDI) / 100)",
                "Heal = trunc(H / 100)"],
            "Note": "SeriousFun: player heal spells scale like damage (no PvP cap, no target scalar). Non-player "
                    "casters use the spell's NonPlayerCaster formula."},
        "Reflection": {
            "AOS": "The Magic Reflection spell does not reflect in AOS. A spell marked Reflectable can still be sent "
                   "back by: SeriousFun Spell Reflection gear (chance% = total of the target's worn items, capped "
                   "75), creatures with their own reflect ability, or a Mirror Image clone taking the hit.",
            "PreAOS": "MagicDamageAbsorb points (8-15): each reflected spell costs Circle points."},
        "DelayedDamage": {
            "Delay": 1.0,
            "Spells": [s["Key"] for s in spells if s["Effect"].get("Delay")],
            "Note": "Damage lands Delay seconds after the spell takes effect (Explosion uses its own 3 s fuse)."},
        "Targeting": {
            "Harmful": "a mobile (or damageable item for damage spells) the caster may harm; makes the caster an "
                       "aggressor",
            "Beneficial": "a mobile the caster may help",
            "Location": "a ground point or object (its location), line of sight required",
            "Item": "an item of the kind the spell names",
            "Self": "no target; acts on the caster",
            "None": "no target",
            "Range": "tile range (max(|dx|, |dy|)) from the caster"},
        "Notes": [
            "SeriousFun also has ExtendedMagery (Scripts/Custom/ExtendedMagery.cs): extra Magery spells with server "
            "ids 1000+ and circles past the 8th. They are not part of this export.",
            "Casting reveals a hidden caster. Order when the spell resolves (after the cast delay and any target "
            "pick): reagents are taken, mana is checked, the Magery check is rolled, and only on success is the mana "
            "spent and the scroll used up.",
            "Vampiric Embrace: casting a spell that uses Garlic deals 17-23 fire damage to the caster.",
            "Every harmful spell breaks Evil Omen and triggers the target's on-harmful reactions."]}


# ---------------------------------------------------------------------------------------------------------------------

def main():
    root, items_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
    scripts = os.path.join(root, "Scripts")
    spells_dir = os.path.join(scripts, "Spells")
    items = {i["Type"] for i in json.load(open(items_path, encoding="utf-8"))["Items"]}
    creatures_path = os.path.join(os.path.dirname(items_path), "Creatures.json")
    creatures = {c["Type"] for c in json.load(open(creatures_path, encoding="utf-8"))["Creatures"]} \
        if os.path.exists(creatures_path) else None

    check_source_rules(root, spells_dir)
    registry = parse_registry(spells_dir)
    scrolls = parse_scrolls(scripts)
    offset, count = parse_spellbook(scripts)
    base = parse_base(spells_dir)
    poisons = parse_poisons(scripts)
    forms = parse_polymorph(scripts)
    caps = parse_caps(root)
    assert set(EFFECTS) == {cls for _, cls in registry.values()}, "EFFECTS keys != registry classes"
    kinds = {k: kind for kind, keys in TARGET_KIND.items() for k in keys}
    assert set(kinds) == set(EFFECTS)

    spells = []
    lists = {}
    for sid in range(64):
        folder, cls = registry[sid]
        path = os.path.join(spells_dir, folder, cls[:-5] + ".cs")
        if not os.path.exists(path):
            hits = [os.path.join(spells_dir, folder, f) for f in os.listdir(os.path.join(spells_dir, folder))
                    if re.search(r"class\s+" + cls + r"\b", read(os.path.join(spells_dir, folder, f)))]
            assert len(hits) == 1, cls
            path = hits[0]
        p = parse_spell(path, cls)
        assert p["Circle"] == sid // 8 + 1 == CIRCLE_FOLDERS.index(folder) + 1, cls
        effect, note = EFFECTS[cls]
        effect = json.loads(json.dumps(effect))
        kind = kinds[cls]

        # target kind vs the Target class found
        if kind in ("Self", "None"):
            assert p["Range"] is None, "%s has a target" % cls
        else:
            assert p["Range"] is not None, "%s has no target" % cls
            if kind == "Location":
                assert p["Ground"], cls
            elif kind in ("Harmful", "Beneficial"):
                assert p["Flags"] == kind and not p["Ground"], cls
            else:
                assert p["Flags"] == "None", cls

        # damage numbers
        if cls in AOS_DICE:
            assert p["AosDice"] and set(p["AosDice"]) == {AOS_DICE[cls]}, (cls, p["AosDice"])
            assert (effect["Base"], effect["Dice"], effect["Sides"]) == AOS_DICE[cls], cls
        else:
            assert not p["AosDice"], (cls, p["AosDice"])
        if cls in HEAL_DICE:
            assert p["HealDice"] == [HEAL_DICE[cls]], (cls, p["HealDice"])
            assert (effect["Base"], effect["Dice"], effect["Sides"]) == HEAL_DICE[cls], cls
        if "Types" in effect:
            want = tuple(effect["Types"].get(t, 0) for t in DAMAGE_TYPES)
            assert p["DamageTypes"] == [want], (cls, p["DamageTypes"], want)
        elif effect.get("Kind") == "Field" and "Damage" in effect:
            want = tuple(effect["Damage"]["Types"].get(t, 0) for t in DAMAGE_TYPES)
            assert p["DamageTypes"] == [want], (cls, p["DamageTypes"])
        if p["Delayed"] is not None and "Delay" in effect and cls != "ExplosionSpell" and cls != "MindBlastSpell":
            delayed = {"true": True, "false": False, "!Core.AOS": False}[p["Delayed"]]
            assert (effect["Delay"] > 0) == delayed, (cls, p["Delayed"])
        assert p["CastDelta"] == CAST_BASE_DELTA.get(cls, 0.0), cls
        assert p["CastMult"] == CAST_MULTIPLIER.get(cls, 1), (cls, p["CastMult"])
        assert p["Followers"] == FOLLOWER_CHECK.get(cls), (cls, p["Followers"])
        if effect.get("Kind") == "Summon" and isinstance(effect.get("Creature"), str):
            assert re.search(r"new\s+" + effect["Creature"] + r"\s*\(", p["Body"]), cls
            if effect.get("Controlled") is False:
                assert re.search(r"BaseCreature\.Summon\(\s*new\s+" + effect["Creature"] + r"\(true\)\s*,\s*false",
                                 p["Body"]), cls
        if cls in ("SummonDaemonSpell", "AirElementalSpell", "EarthElementalSpell", "FireElementalSpell",
                   "WaterElementalSpell", "SummonCreatureSpell"):
            assert re.search(r"FromSeconds\(\(2\s*\*\s*this\.Caster\.Skills\.Magery\.Fixed\)\s*/\s*5\)", p["Body"]), cls

        # lists that live in the source
        if cls == "CreateFoodSpell":
            food = re.findall(r"new\s+FoodInfo\(\s*typeof\(\s*(\w+)\s*\)", p["Body"])
            assert len(food) == 10, food
            lists["FOOD"] = food
        if cls == "SummonCreatureSpell":
            lists["SUMMON_CREATURES"] = parse_type_list(p["Body"], "m_Types")
        if cls == "ChainLightningSpell":
            lists["JUMP_RANGE"] = int(re.search(r"JumpRange\s*=\s*(\d+)", p["Body"]).group(1))
            lists["JUMP_FALLOFF"] = float(re.search(r"JumpFalloff\s*=\s*([\d.]+)", p["Body"]).group(1))
            lists["JUMP_DELAY"] = float(re.search(r"JumpDelay\s*=\s*TimeSpan\.FromMilliseconds\((\d+)\)",
                                                  p["Body"]).group(1)) / 1000
        if cls == "PolymorphSpell":
            lists["POLYMORPH_FORMS"] = forms

        def fill(node):
            if isinstance(node, dict):
                return {k: fill(v) for k, v in node.items()}
            if isinstance(node, list):
                return [fill(v) for v in node]
            if isinstance(node, str) and node in lists:
                return lists[node]
            return node
        effect = fill(effect)

        rec = {"Id": offset + sid, "Name": p["Name"], "Key": cls, "Circle": p["Circle"], "Mantra": p["Mantra"],
               "Mana": base["Mana"][p["Circle"] - 1],
               "Reagents": [{"Type": r, "Amount": 1} for r in p["Reagents"]], "Scroll": scrolls[sid],
               "Target": kind}
        if p["Range"] is not None:
            rec["Range"] = p["Range"]
        rec["AllowedInTown"] = p["Town"]
        rec["Reflectable"] = p["Reflect"]
        cd = {"Base": round((base["BaseTicks"] + p["Circle"] - 1) * base["SecondsPerTick"] + p["CastDelta"], 4)}
        if p["CastMult"] != 1:
            cd["Multiplier"] = p["CastMult"]
        rec["CastDelay"] = cd
        rec["Effect"] = effect
        rec["Resistable"] = cls == "PoisonSpell"
        rec["Notes"] = note
        spells.append(rec)

    # the formula self-check: every Expr parses, uses known names, and a few known values come out right
    doc = {"Source": "ServUO (%s): Magery rules, newest era (AOS..TOL branches), numbers and rules only"
                     % os.path.basename(os.path.normpath(root)),
           "Expressions": {
               "Grammar": "numbers; + - * / (real); ( ); < <= > >= == != ; c ? a : b ; floor ceil trunc round min max "
                          "abs sqrt clamp(x,lo,hi) rand(lo,hi) (integer, inclusive) randf() ([0,1))",
               "Variables": "Caster.<X> / Target.<X>: skill value (" + ", ".join(SKILLS) + ") or <Skill>Fixed "
                            "(integer tenths); Str Dex Int RawStr RawDex RawInt Hits HitsMax Stam StamMax Mana ManaMax "
                            "IsPlayer PoisonLevel. Distance (straight-line tiles), Range (max(|dx|,|dy|)), Count "
                            "(targets hit). Other names are defined next to the formula that uses them.",
               "Trunc": "trunc() reproduces C# integer casts and integer division (toward zero)."},
           "Pools": pools(caps), "Casting": casting(base, spells),
           "Spellbook": {"Type": "Spellbook", "Offset": offset, "Count": count,
                         "Bit": "content is a 64-bit mask: bit i set = the book holds spell id Offset + i"},
           "PoisonLevels": poisons, "Spells": spells}
    found = []
    walk_exprs(doc, found)
    local = {"Percent", "BaseLevel", "DispelDifficulty", "DispelFocus", "EnchantedSummoning", "D", "F", "Mana",
             "LowerManaCost", "InherentLMC", "FreeReagents", "CircleMana", "Circle", "Min", "Max", "StrMaxCap",
             "DexMaxCap", "IntMaxCap", "BonusHits", "BonusStam", "BonusMana", "Elf", "HitPointRegen", "StamRegen",
             "Monster", "FocusBonus", "MedBonus", "ItemBonus", "ItemBase", "Intensity", "ManaRegen", "ArmorPenalty",
             "Meditating", "Neck", "Hands", "Head", "Arms", "Legs", "Chest", "FasterCasting", "BaseDelta",
             "Multiplier", "FasterCastRecovery", "Target.Resilience", "Base", "Dice", "Sides", "EvalScale",
             "DamageBonus", "Scalar"}
    env = sample_env(**{k: 1 for k in local})
    for path, node in found:
        Expr(node["Expr"], local).eval(env)
    by_key = {s["Key"]: s for s in spells}

    def val(key, field_path, **over):
        node = by_key[key]["Effect"]
        for f in field_path.split("."):
            node = node[f]
        return Expr(node["Expr"], local).eval(sample_env(**{**{k: 1 for k in local}, **over}))
    assert val("ClumsySpell", "Duration") == 121                              # 100 Eval Int
    assert val("ClumsySpell", "Percent", **{"Target.MagicResistFixed": 0}) == 18
    assert val("AgilitySpell", "Percent") == 11
    assert val("FireFieldSpell", "Duration") == 53
    assert val("EnergyFieldSpell", "Duration") == 30
    assert val("PoisonFieldSpell", "Duration") == 43
    assert abs(val("InvisibilitySpell", "Duration") - 120) < 1e-9
    assert val("SummonDaemonSpell", "Duration") == 400
    assert val("ParalyzeSpell", "Duration", **{"Target.MagicResist": 50.0, "Target.IsPlayer": 0}) == 15
    assert val("CureSpell", "Chance", **{"Target.PoisonLevel": 2}) == 76
    assert val("IncognitoSpell", "Duration") == 121

    used_items = set()
    for s in spells:
        used_items.update(r["Type"] for r in s["Reagents"])
        used_items.add(s["Scroll"])
    used_items.update(lists["FOOD"])
    used_items.update({"Spellbook", "RecallRune", "Runebook", "ArcaneGem"})
    missing = sorted(t for t in used_items if t not in items)
    summons = set(lists["SUMMON_CREATURES"]) | {s["Effect"]["Creature"] for s in spells
                                                if s["Effect"].get("Kind") == "Summon"
                                                and isinstance(s["Effect"]["Creature"], str)}
    missing_creatures = sorted(t for t in summons if creatures is not None and t not in creatures)

    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=1, ensure_ascii=False)
        f.write("\n")
    regs = sorted({r["Type"] for s in spells for r in s["Reagents"]})
    print("%d spells (ids %d-%d), %d scrolls, %d formulas checked, %d poison levels, %d polymorph forms -> %s"
          % (len(spells), spells[0]["Id"], spells[-1]["Id"], len({s["Scroll"] for s in spells}), len(found),
             len(poisons), len(forms), out))
    print("reagents: " + ", ".join(regs))
    print("item types not in Items.json: " + (", ".join(missing) if missing else "none"))
    if creatures is not None:
        print("summoned creatures not in Creatures.json: " + (", ".join(missing_creatures) if missing_creatures
                                                               else "none"))


if __name__ == "__main__":
    main()
