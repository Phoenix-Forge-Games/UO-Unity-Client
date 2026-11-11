"""One-time migration of UO's magic item rules (the random loot properties of ServUO's newer loot system, Runic
Reforging's GenerateRandomItem path) into Content/Magic.json. The game owns that file from then on; nothing of ServUO's
code is reused.

  python -I export_servuo_magic.py <ServUO root> <output json>

Read from the scripts (and asserted, so a different ServUO fails loudly):
  Services/LootGeneration/ItemPropertyInfo.cs  every property: id, attribute, label, imbuing weight, scale, start,
                                               max, and per item type (melee, ranged, armour, shield, hat, jewel) its
                                               scale, old-loot max, new-loot max and over-cap ("powerful") range
  Items/Equipment/*/Base*.cs (GetProperties)   the tooltip line (cliloc number) each property shows as
  Abilities/SlayerGroup.cs                     slayer groups: super and lesser slayers and the creatures they hurt
  Config/Loot.cfg                              budgets and the property cap
  Services/LootGeneration/RunicReforging/RunicReforging.cs   the jewel skill list
The generation rules themselves (budget, property counts, scaling, disadvantages, item power) are written below from
RunicReforging.cs; their numbers are checked against the source where they appear as literals.
"""
import json
import os
import re
import sys

ITEM_TYPES = ["Invalid", "Melee", "Ranged", "Armor", "Shield", "Hat", "Jewel"]


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def need(pattern, text, what, flags=0):
    m = re.search(pattern, text, flags)
    if not m:
        raise SystemExit(f"not found: {what}")
    return m


def split_args(s):
    """Top-level comma split of an argument list."""
    out, depth, cur = [], 0, ""
    for ch in s:
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        if ch == "," and depth == 0:
            out.append(cur.strip())
            cur = ""
        else:
            cur += ch
    if cur.strip():
        out.append(cur.strip())
    return out


def balanced(text, start):
    """The text inside the parenthesis opening at text[start]."""
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "(":
            depth += 1
        elif text[i] == ")":
            depth -= 1
            if depth == 0:
                return text[start + 1:i]
    raise SystemExit("unbalanced")


def properties(root):
    src = read(os.path.join(root, "Scripts/Services/LootGeneration/ItemPropertyInfo.cs"))
    src = re.sub(r"//[^\n]*", "", src)
    props = []
    for m in re.finditer(r"Register\((\d+),\s*new ItemPropertyInfo\(", src):
        pid = int(m.group(1))
        args = split_args(balanced(src, m.end() - 1))
        attr = args[0]
        cats = [a for a in args if a.startswith("new PropInfo(")]
        plain = [a for a in args[1:] if not a.startswith("new PropInfo(") and not a.startswith("typeof(")]
        # (attr, label, weight, [res...], scale, start, max[, desc])
        nums = [a for a in plain]
        label = nums[0]
        weight = int(nums[1])
        rest = [int(x) for x in nums[2:] if re.fullmatch(r"-?\d+", x)]
        if len(rest) < 3:
            raise SystemExit(f"property {pid}: can't read scale/start/max from {plain}")
        scale, start, maxi = rest[0], rest[1], rest[2]
        desc = rest[3] if len(rest) > 3 else -1
        group, name = (attr.split(".", 1) if "." in attr else ("Custom", attr.strip('"')))
        entry = {
            "Id": pid, "Group": group, "Name": name,
            "Label": int(label) if re.fullmatch(r"\d+", label) else None,
            "Weight": weight, "Scale": scale, "Start": start, "Max": maxi, "Description": desc,
            "Types": {},
        }
        for c in cats:
            a = split_args(c[len("new PropInfo("):-1])
            t = ITEM_TYPES[int(a[0])]
            powerful = None
            if a[-1].startswith("new int[]"):
                powerful = [int(x) for x in re.findall(r"-?\d+", a[-1].split("{", 1)[1])]
                a = a[:-1]
            ints = [int(x) for x in a[1:]]
            if len(ints) == 3:
                sc, smax, lmax = ints
            elif len(ints) == 2:
                sc, (smax, lmax) = -1, ints
            else:
                raise SystemExit(f"property {pid}: odd PropInfo {c}")
            info = {"Standard": smax, "Loot": lmax}
            if sc >= 0:
                info["Scale"] = sc
            if powerful:
                info["Powerful"] = powerful
            entry["Types"].setdefault(t, info)
        props.append(entry)
    if len(props) < 150:
        raise SystemExit(f"only {len(props)} properties read")
    return props


def tooltips(root):
    """attribute -> the tooltip cliloc it's listed with (first seen)."""
    found = {}
    for rel in ["Scripts/Items/Equipment/Weapons/BaseWeapon.cs", "Scripts/Items/Equipment/Armor/BaseArmor.cs",
                "Scripts/Items/Equipment/Jewelry/BaseJewel.cs", "Scripts/Items/Equipment/Clothing/BaseClothing.cs"]:
        src = read(os.path.join(root, rel))
        for m in re.finditer(r"=\s*(?:\(double\))?\s*(?:m_(Aos\w+|SAAbsorptionAttributes|ExtendedWeaponAttributes|NegativeAttributes)\.(\w+)|(GetLowerStatReq)\(\))[^\n]*?\)\s*!=\s*0\)(?=([\s\S]{0,260}))", src):
            group, name, tail = m.group(1), m.group(2) or "LowerStatReq", m.group(4)
            n = re.search(r"\b(1\d{6})\b", tail)
            if n:
                key = name
                found.setdefault(key, int(n.group(1)))
        # elemental damage lines and resist lines read differently
        for m in re.finditer(r"list\.Add\((10604\d\d), (\w+)\.ToString\(\)\);\s*//\s*([^\n]*)", src):
            found.setdefault("Line:" + m.group(3).strip(), int(m.group(1)))
    return found


def slayer_titles(root):
    """SlayerName -> its AOS title (SlayerEntry.m_AosTitles is indexed by the enum, less one)."""
    entry = read(os.path.join(root, "Scripts/Abilities/SlayerEntry.cs"))
    titles = [int(x) for x in re.findall(r"\b(1\d{6})\b", need(r"m_AosTitles = new int\[\]\s*\{([^}]*)\}", entry, "slayer titles").group(1))]
    names = None
    for dirpath, _, files in os.walk(os.path.join(root, "Scripts")):
        for f in files:
            if f.endswith(".cs"):
                src = read(os.path.join(dirpath, f))
                m = re.search(r"public enum SlayerName\s*\{([^}]*)\}", src)
                if m:
                    names = [n.strip().split("=")[0].strip() for n in re.sub(r"//[^\n]*", "", m.group(1)).split(",") if n.strip()]
                    break
        if names:
            break
    if not names or names[0] != "None":
        raise SystemExit("SlayerName enum not found")
    return {n: titles[i - 1] for i, n in enumerate(names) if 0 < i <= len(titles)}


def slayers(root):
    src = read(os.path.join(root, "Scripts/Abilities/SlayerGroup.cs"))
    src = re.sub(r"//[^\n]*", "", src)
    order = [g.strip() for g in need(r"m_Groups = new SlayerGroup\[\]\s*\{([^}]*)\}", src, "group order").group(1).split(",") if g.strip()]

    def entry(text):
        a = split_args(text)
        return {"Slayer": a[0].split(".")[-1], "Creatures": [re.sub(r"typeof\((\w+)\)", r"\1", x) for x in a[1:] if x.startswith("typeof(")]}

    groups = []
    for g in order:
        sup = need(rf"\b{g}\.Super\s*=\s*new SlayerEntry\s*\(", src, f"{g} super")
        super_entry = entry(balanced(src, sup.end() - 1))
        entries = []
        em = re.search(rf"\b{g}\.Entries = new SlayerEntry\[\]\s*\{{", src)
        if em:
            body_start = em.end()
            depth, i = 1, body_start
            while depth:
                depth += {"{": 1, "}": -1}.get(src[i], 0)
                i += 1
            body = src[body_start:i - 1]
            for e in re.finditer(r"new SlayerEntry\s*\(", body):
                entries.append(entry(balanced(body, e.end() - 1)))
        groups.append({"Group": g, "Super": super_entry, "Entries": entries})
    return groups


def slayer_labels(root):
    src = read(os.path.join(root, "Scripts/Abilities/SlayerGroup.cs")) + read(os.path.join(root, "Scripts/Abilities/SlayerEntry.cs"))
    m = re.search(r"m_SlayerTitles\s*=\s*new int\[\]\s*\{([^}]*)\}", src)
    return [int(x) for x in re.findall(r"\d{7}", m.group(1))] if m else []


def jewel_skills(root):
    src = read(os.path.join(root, "Scripts/Services/LootGeneration/RunicReforging/RunicReforging.cs"))
    body = need(r"SkillName\[\] m_Skills = new SkillName\[\]\s*\{([^}]*)\}", src, "jewel skills").group(1)
    return re.findall(r"SkillName\.(\w+)", body)


def loot_config(root):
    cfg = {}
    for line in read(os.path.join(root, "Config/Loot.cfg")).splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            k, v = line.split("=", 1)
            if re.fullmatch(r"-?\d+", v.strip()):
                cfg[k.strip()] = int(v.strip())
    for k in ["MaxBaseBudget", "MinBaseBudget", "MaxAdjustedBudget", "MinAdjustedBudget", "MaxProps"]:
        if k not in cfg:
            raise SystemExit(f"Loot.cfg: {k} missing")
    return cfg


def check_rules(root):
    rr = read(os.path.join(root, "Scripts/Services/LootGeneration/RunicReforging/RunicReforging.cs"))
    for literal in ["budget >= 550", "Utility.Random(2700)", "Math.Max(50, Math.Min(500, budget) / mods)", "Math.Max(20, perchigh / 3)",
                    "Utility.RandomMinMax(50, 70)", "Utility.RandomMinMax(100, 140)", "10000 / scaledBy", "1000001",
                    "if (weight < preArty * .4)", "double arty = 1200 - preArty;", "(100.0 - Math.Sqrt(Utility.RandomMinMax(0, 10000))) / 100.0"]:
        if literal not in rr:
            raise SystemExit(f"RunicReforging.cs: expected '{literal}'")
    im = read(os.path.join(root, "Scripts/Services/LootGeneration/Imbuing/Core/Imbuing.cs"))
    m = need(r"int maxWeight = (\d+);", im, "max weight")
    j = need(r"maxWeight = (\d+);[^\n]*\n\s*\}\s*\n\s*return maxWeight", im, "jewel max weight")
    return int(m.group(1)), int(j.group(1))


def main():
    root, out = sys.argv[1], sys.argv[2]
    props = properties(root)
    tips = tooltips(root)
    max_weight, jewel_weight = check_rules(root)
    slayer_tips = slayer_titles(root)
    damage = {"Physical": 1060403, "Fire": 1060405, "Cold": 1060404, "Poison": 1060406, "Energy": 1060407}
    resist = {"Physical": 1060448, "Fire": 1060447, "Cold": 1060445, "Poison": 1060449, "Energy": 1060446}
    for p in props:
        if p["Group"] in ("SlayerName",) and p["Name"] in slayer_tips:
            p["Tooltip"] = slayer_tips[p["Name"]]
        elif p["Group"] == "AosElementAttribute":
            p["Tooltip"] = resist[p["Name"]]
            p["WeaponTooltip"] = damage[p["Name"]]  # on weapons these are damage types, elsewhere resists
        elif p["Name"].startswith("Resist") and p["Name"].endswith("Bonus"):
            p["Tooltip"] = resist[p["Name"][6:-5]]
        elif p["Group"] == "SkillName":
            p["Tooltip"] = "SkillBonus"
        elif p["Name"] in tips:
            p["Tooltip"] = tips[p["Name"]]
        elif p["Name"] == "BalancedWeapon":
            p["Tooltip"] = 1072792  # a flag line: "Balanced"
    data = {
        "Source": "ServUO (SeriousFun build): Services/LootGeneration (ItemPropertyInfo, RunicReforging, Imbuing), Config/Loot.cfg, Abilities/SlayerGroup.cs, Items/Equipment GetProperties. Numbers and rules only.",
        "ItemTypes": ITEM_TYPES[1:],
        "Generation": {
            "Note": "RunicReforging.GenerateRandomItem: base budget from the creature, rolled up by a luck-weighted percent; few props at low budget, 4-10 strong props from 550 up. Each property: a random id from the item type's loot list (not one it has), value Scale(min, max, perclow, perchigh) rounded to its step, lowered until it fits the budget; the budget pays weight / max x value.",
            "Config": loot_config(root),
            "BaseBudget": {"Expr": "Fame > 0 ? max(MinBaseBudget, min(MaxBaseBudget, Fame / (20500 / MaxBaseBudget))) : 0", "Floor": 100},
            "Divisor": [[400, 5], [550, 4], [650, 3], [999999, 2]],
            "Powerful": 550,
            "PrefixChance": "budget >= Random(2700) (named prefixes and suffixes aren't migrated: plain random properties instead)",
            "PropertyCounts": {
                "1": [3, 1], "2": [6, 3, 1], "3": [10, 6, 3, 1], "4": [16, 12, 6, 5, 1], "5": [30, 25, 20, 15, 9, 1],
                "6": [35, 30, 25, 20, 15, 9, 1], "7": [40, 35, 30, 25, 20, 15, 9, 1], "8": [50, 40, 35, 30, 25, 20, 15, 9, 1],
                "9": [70, 55, 45, 35, 30, 25, 20, 15, 9, 1], "10": [90, 70, 55, 45, 40, 35, 25, 15, 10, 5, 1],
                "Note": "GetProperties(max): weights for 0, 1, 2... properties"
            },
            "Weak": {"Mods": "max(1, GetProperties(5))", "PercHigh": "max(50, min(500, budget) / mods)", "PercLow": "max(20, perchigh / 3)"},
            "Strong": {"MaxMods": "max(5, min(MaxProps - 1, ceil(budget / RandomMinMax(100, 140))))", "MinMods": "max(4, maxmods - 4)", "PercHigh": 100, "PercLow": [50, 70]},
            "PercClamp": [10, 80, 100],
            "MaxWeight": {"Base": max_weight, "Exceptional": 50, "Ranged": 50, "TwoHanded": 100, "Jewel": jewel_weight},
            "Power": {"Thresholds": [0.4, 0.6, 0.8, 1.0], "Artifact": [0.2, 0.4, 0.7], "ArtifactTop": 1200, "PreArtifactExtra": 100,
                      "Names": ["None", "Minor", "Lesser", "Greater", "Major", "LesserArtifact", "GreaterArtifact", "MajorArtifact", "LegendaryArtifact"],
                      "Tooltip": 1151488, "Note": "tooltip number = 1151488 + power - 1"},
            "Disadvantages": {
                "Note": "TryApplyRandomDisadvantage: Lesser items 5% (one of Prized, Antique, Unwieldly x2, Cursed x2, Massive x2; jewels without the last two), Greater 25% (75%: one of Prized, Antique, Unwieldly x2, Massive x2; else 25%/50%: Prized; Antique or Brittle (+150 budget); Cursed); Major and up as Greater; MajorArtifact jewels always Antique (75%) or Cursed. Each gives 100 budget for up to 5 more properties.",
                "Ids": {"Prized": 900, "Antique": 901, "Unwieldly": 902, "Brittle": 903, "Massive": 904, "Cursed": 905},
                "Tooltips": {"Prized": 1154910, "Antique": 1076187, "Brittle": 1116209, "Cursed": 1049643}
            },
        },
        "Specials": {"Melee": [1000], "Ranged": [1000], "Jewel": [1001, 1002, 1003, 1004, 1005],
                     "Note": "1000: a random slayer (BaseRunicTool.GetRandomSlayer: one of the first 6 groups, 10% its super slayer); 1001-1005: a random skill bonus from JewelSkills not already on it"},
        "Validation": {
            "Note": "ItemPropertyInfo.ValidateProperty: which properties an item may take besides its type's loot list",
            "TwoHandedWeapon": [206, 61], "Shield": [207], "MeleeOnly": [40],
            "OneOf": [[30, 31, 32, 33, 34], [35, 36, 37, 38, 39], [208, 209, 210, 211, 212, 213]],
            "NotMeditable": [49], "NotBrittle": [500, 501],
            "Excluded": [60, 62, 63, 64, 65, 66, 67, 68, 200],
            "ExcludedNote": "60 velocity and 62-68 are the shard's own ranged and spell additions (custom systems wait); 200 Blood Drinker needs weapons with the bleed attack (weapon abilities aren't in the item data yet)"
        },
        "JewelSkills": jewel_skills(root),
        "SkillBonusTooltip": {"First": 1060451, "SkillNames": 1044060, "Note": "the nth skill line is 1060451 + n (~1_skillname~ +~2_val~); a skill's name is cliloc 1044060 + its id"},
        "Slayers": {"RandomGroups": 6, "SuperChance": 10, "Groups": slayers(root), "Bonus": 100,
                    "Note": "a slayer on a weapon doubles its damage (+100%) against the creatures it names; a super slayer names its whole group"},
        "Properties": props,
    }
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"{len(props)} properties, {sum(1 for p in props if 'Tooltip' in p)} with tooltips, {len(data['Slayers']['Groups'])} slayer groups")


main()
