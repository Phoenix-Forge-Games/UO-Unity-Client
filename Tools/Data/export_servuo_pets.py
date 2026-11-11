"""One-time migration of UO's pet rules (taming, control, loyalty, bonding, feeding, followers, stables) into
Content/Pets.json. The game owns that file from then on; nothing of ServUO's code is reused.

  python -I export_servuo_pets.py <ServUO root> <Cliloc.enu or a JSON dump of it> <output json>

Numbers are read from the scripts and asserted, so a different ServUO fails loudly rather than exporting wrong values:
  Skills/AnimalTaming.cs       taming range, ticks, anger chance, owner penalty, skill check window, skill scaling
  Mobiles/Normal/BaseCreature.cs  control chance (the ML formula), loyalty, bonding, food lists, max owners
  Mobiles/NPCs/AnimalTrainer.cs   stable fee and stall count
  Server/Mobile.cs             followers cap
The tamer's coaxing lines are UO's own (cliloc 502790-502793, 1005608-1005613, 1010593-1010596), read from Cliloc.enu.
"""
import json
import os
import re
import struct
import sys


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def need(pattern, text, what, flags=0):
    m = re.search(pattern, text, flags)
    if not m:
        raise SystemExit(f"not found: {what}")
    return m


def cliloc(path):
    """UO's text: an uncompressed Cliloc.enu, or a JSON {number: text} dump (newer clients compress the file; the editor's
    Cliloc reader can dump the numbers needed)."""
    if path.lower().endswith(".json"):
        return {int(k): v for k, v in json.load(open(path, encoding="utf-8")).items()}
    out = {}
    with open(path, "rb") as f:
        f.read(6)
        while True:
            head = f.read(7)
            if len(head) < 7:
                break
            number, _, length = struct.unpack("<iBH", head)
            out[number] = f.read(length).decode("utf-8", errors="replace")
    return out


def types(text, name):
    m = need(r"Type\[\]\s+" + name + r"\s*=\s*(?:new\s*\[\]\s*)?\{(.*?)\};", text, name, re.S)
    return re.findall(r"typeof\((\w+)\)", m.group(1))


CONTROL = "700 + ((Taming - Tame) * 10 * (Taming < Tame ? 28 : 6) + (Lore - Tame) * 10 * (Lore < Tame ? 14 : 6)) / 2"


def main():
    root, cliloc_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
    scripts = os.path.join(root, "Scripts")
    taming = read(os.path.join(scripts, "Skills", "AnimalTaming.cs"))
    creature = read(os.path.join(scripts, "Mobiles", "Normal", "BaseCreature.cs"))
    trainer = read(os.path.join(scripts, "Mobiles", "NPCs", "AnimalTrainer.cs"))
    mobile = read(os.path.join(root, "Server", "Mobile.cs"))
    text = cliloc(cliloc_path)

    # Taming (AnimalTaming.cs), asserted.
    need(r"base\(Core\.AOS \? 3 : 2", taming, "target range 3")
    need(r"InRange\(m_Creature, Core\.AOS \? 7 : 6\)", taming, "keep range 7")
    need(r"new InternalTimer\(from, creature, Utility\.Random\(3, 2\)\)", taming, "3-4 ticks")
    need(r"TimeSpan\.FromSeconds\(3\.0\), TimeSpan\.FromSeconds\(3\.0\)", taming, "3 s ticks")
    need(r"CanAngerOnTame && 0\.95 >= Utility\.RandomDouble\(\)", taming, "anger 95%")
    need(r"CurrentTameSkill \+ \(m_Creature\.Owners\.Count \* 6\.0\)", taming, "owner penalty 6")
    need(r"minSkill \+= 24\.9", taming, "+24.9")
    need(r"minSkill - 25\.0, minSkill \+ 25\.0", taming, "check window")
    need(r"ScaleSkills\(m_Creature, 0\.86, true\)", taming, "paralysed scale")
    need(r"ScaleSkills\(m_Creature, 0\.90, true\)", taming, "first tame scale")
    need(r"Hits > \(\(double\)bc\.HitsMax / 10\)", taming, "subdue to 10%")
    need(r"CheckTargetSkill\(SkillName\.AnimalLore, m_Creature, 0\.0, 120\.0\)", taming, "lore gain")
    max_owners = int(need(r"MaxOwners = (\d+)", creature, "MaxOwners").group(1))
    max_loyalty = int(need(r"MaxLoyalty = (\d+)", creature, "MaxLoyalty").group(1))
    bond_days = float(need(r"BondingDelay \{ get \{ return TimeSpan\.FromDays\(([\d.]+)\)", creature, "bonding").group(1))
    abandon_days = float(need(r"BondingAbandonDelay \{ get \{ return TimeSpan\.FromDays\(([\d.]+)\)", creature, "abandon").group(1))
    need(r"Loyalty -= \(BaseCreature\.MaxLoyalty / 10\)", creature, "hourly loyalty loss")
    need(r"m_NextHourlyCheck = DateTime\.UtcNow \+ TimeSpan\.FromHours\(1\.0\)", creature, "hourly")
    need(r"m_CurrentTameSkill <= 29\.1 \|\| m_bSummoned", creature, "control: easy below 29.1")
    need(r"int SkillMod = 6, LoreMod = 6", creature, "control mods")
    need(r"SkillMod = 28", creature, "control mod under")
    need(r"LoreMod = 14", creature, "lore mod under")
    need(r"chance = 700", creature, "control base")
    need(r"chance -= \(MaxLoyalty - m_Loyalty\) \* 10", creature, "loyalty penalty")
    followers = int(need(r"m_FollowersMax = (\d+);", mobile, "followers").group(1))
    walk_foot = int(need(r"m_WalkFoot = (\d+);", mobile, "walk foot").group(1))
    run_foot = int(need(r"m_RunFoot = (\d+);", mobile, "run foot").group(1))
    walk_mount = int(need(r"m_WalkMount = (\d+);", mobile, "walk mount").group(1))
    run_mount = int(need(r"m_RunMount = (\d+);", mobile, "run mount").group(1))
    container = read(os.path.join(scripts, "Items", "Containers", "Container.cs"))
    strong = need(r"class StrongBackpack.*?DefaultMaxWeight\s*\{\s*get\s*\{\s*return (\d+);", container, "pack weight", re.S)
    fee = int(need(r"ConsumeTotal\(typeof\(Gold\), (\d+)\)", trainer, "stable fee").group(1))
    need(r"sklsum >= 240\.0\)\s*\{\s*max \+= 5", trainer, "stalls 240")
    need(r"Core\.SA\)\s*\{\s*max \+= 2", trainer, "SA stalls")
    need(r"Core\.TOL\)\s*\{\s*max \+= 2", trainer, "TOL stalls")
    need(r"max \+= \(int\)\(\(taming - 90\.0\) / 10\)", trainer, "taming stalls")

    foods = {k: types(creature, "m_" + k) for k in ("Meat", "FruitsAndVegies", "GrainsAndHay", "Fish", "Eggs", "Metal", "BlackrockStew")}
    gold = need(r"m_Gold\s*=\s*new\[\]\s*\{(.*?)\};", creature, "gold", re.S)
    foods["Gold"] = re.findall(r"typeof\((\w+)\)", gold.group(1))

    lines = [text[n] for n in list(range(502790, 502794)) + list(range(1005608, 1005614)) + list(range(1010593, 1010597)) if n in text]

    data = {
        "Source": "ServUO Skills/AnimalTaming.cs, Mobiles/Normal/BaseCreature.cs, Mobiles/NPCs/AnimalTrainer.cs, Server/Mobile.cs; "
                  "UO's Cliloc.enu for the coaxing lines (one-time migration; this file is canonical now)",
        "Followers": {"Max": followers, "Text": "Control slots a character may have in followers (each creature's ControlSlots)."},
        "Taming": {
            "Reach": 3, "KeepReach": 7, "TickSeconds": 3.0, "Ticks": [3, 4], "AngerChance": 0.95,
            "OwnerPenalty": 6.0, "MaxOwners": max_owners, "SubdueTo": 0.10,
            "Check": {"Min": "Tame + Owners * 6 - 0.1", "Max": "Tame + Owners * 6 + 49.9"},
            "SkillScale": {"FirstTame": 0.90, "Paralyzed": 0.86, "Retame": 0.90},
            "LoreGain": [0.0, 120.0],
            "Lines": lines,
            "Text": "Use Animal Taming with a tamable creature targeted, within Reach tiles. The tamer coaxes it every TickSeconds "
                    "for 3-4 ticks (a line each tick, an Animal Lore gain check each tick); it fails if they move past KeepReach, "
                    "die, lose sight of it, or it's hurt. Creatures with CanAngerOnTame turn on the tamer AngerChance of the "
                    "time instead. Some must be beaten below SubdueTo of their hits first (SubdueBeforeTame). The tamer must "
                    "have at least the creature's MinTameSkill to try. The last tick is an Animal Taming check across "
                    "Check.Min..Check.Max; success makes the tamer its master and scales its skills (SkillScale; a creature "
                    "tamed before keeps getting scaled). Retaming a creature you once owned always works. A creature can't "
                    "have more than MaxOwners different owners.",
        },
        "Control": {
            "Easy": 29.1,
            "Formula": "((%s) < 0 ? (%s) : clamp(%s, 200, 990)) - (100 - Loyalty) * 10" % ((CONTROL,) * 3),
            "Scale": 1000,
            "Text": "Chance an order is obeyed, out of Scale: the ML formula (Taming and Lore are the owner's skills, Tame the "
                    "creature's taming requirement). Below 0 stays below 0 (never obeys); otherwise clamped to 200..990. Always obeyed when "
                    "Tame <= Easy, and by summons.",
        },
        "Loyalty": {"Max": max_loyalty, "LossPerHour": max_loyalty // 10, "WarnBelow": max_loyalty // 10,
                    "Text": "Pets lose LossPerHour loyalty every hour; below WarnBelow they look around desperately, at 0 they go wild. "
                            "Feeding the food they like brings loyalty back to Max."},
        "Bonding": {"Days": bond_days, "AbandonDays": abandon_days,
                    "Text": "The owner feeding a pet (with Animal Taming at least its requirement, or any pet easier than 29.1) starts "
                            "bonding; fed again after Days it bonds. A bonded pet that dies becomes a ghost that follows its owner "
                            "and can be brought back (Veterinary, Resurrection); left more than AbandonDays away from its owner it's gone."},
        "Foods": foods,
        "FoodsText": "Item types each food preference accepts (a creature's FavoriteFood, from Creatures.json Overrides; several joined with |).",
        "Stable": {
            "Fee": fee,
            "Stalls": "(Taming + Lore + Veterinary >= 240 ? 5 : Taming + Lore + Veterinary >= 200 ? 4 : Taming + Lore + Veterinary >= 160 ? 3 : 2) + 2 + 2 + (Taming >= 100 ? trunc((Taming - 90) / 10) : 0)",
            "Text": "An animal trainer keeps pets for Fee gold each (paid when stabled; UO charged it per real week, here once). "
                    "Stalls: how many pets a character may stable (skill-based, plus the SA and TOL bonus stalls).",
        },
        "Mount": {
            "WalkSpeed": walk_foot / walk_mount, "RunSpeed": run_foot / run_mount,
            "Text": "Riding multiplies walking and running speed (UO: %d/%d ms a tile on foot, %d/%d mounted). Mount from the Pets "
                    "window beside the mount; it's carried while ridden (not attackable) and comes back beside you when you "
                    "get off or die." % (walk_foot, run_foot, walk_mount, run_mount),
        },
        "Pack": {
            "MaxWeight": int(strong.group(1)),
            "Text": "Pack animals (pack horses, pack llamas) carry a pack holding up to MaxWeight stones (StrongBackpack).",
        },
        "Orders": {
            "List": ["Follow", "Stay", "Guard", "Attack", "Stop", "Release"],
            "Text": "UO's spoken orders become buttons (no targeting cursor): Follow (its owner), Stay (where it is), Guard (attacks "
                    "whatever attacks its owner), Attack (the owner's current target), Stop (stops fighting, waits), Release "
                    "(goes wild for good). Each order is checked against the control chance.",
        },
    }
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
    print(f"followers {followers}, max owners {max_owners}, loyalty {max_loyalty}, bond {bond_days} days, fee {fee}, "
          f"{sum(len(v) for v in foods.values())} food types, {len(lines)} lines -> {out}")


if __name__ == "__main__":
    main()
