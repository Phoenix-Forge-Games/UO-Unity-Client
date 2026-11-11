"""Extracts ServUO's character-creation rules and its skill table (data only) into Content/Skills.json and Creation.json.

  python -I export_servuo_creation.py <ServUO root> <Items.json> <out folder>

One-time migration: ServUO is going away, so everything the game needs to create a character is written out here and
nothing of ServUO's code is reused. Humans only: elf and gargoyle branches are dropped. Core.<expansion> checks are taken
as true (so no Newbied loot types) and Siege as false. TestCenter is ignored.

What is parsed from the source and what is written by hand:
  parsed   the SkillName enum and SkillInfo table (Server/Skills.cs), which skills have a use callback (Scripts/**),
           Config/PlayerCaps.cfg, the spell registry (Scripts/Spells/Initializer.cs) and spellbook BookOffsets (to turn
           spellbook content masks into spell names), reagent bag contents, the name word lists
           (Scripts/Misc/NameVerification.cs), the PackScroll switch (CharacterCreation.cs).
  by hand  the profession kits, AddBackpack, the default clothes and AddSkillItems (human branches). They are
           cross-checked against CharacterCreation.cs: every Type written must appear in that case of the source, and
           source types left out (non-human branches) are printed for review.

Skills.json
  {"Source", "Skills": [{
     "Id", "Key" (SkillName enum), "Name", "Title", "Cliloc" (1044060 + Id),
     "StrScale", "DexScale", "IntScale"   stat bonus, in percent of the stat, as written in the table
     "StatTotal"                          StrScale + DexScale + IntScale: the most the stats can add
     "StrGain", "DexGain", "IntGain"      weights of the stat-gain roll when the skill is used
     "GainFactor", "Primary", "Secondary" (Str/Dex/Int), "Mastery" (has a mastery), "UseWhileCasting",
     "Active"                             true when the skill has a use button (a Callback is assigned in the scripts)
   }, ...],                               all 58, in Id order
   "ValueFormula": how a skill's displayed value comes from its base and stats,
   "Defaults": {"Cap": per-skill cap, "TotalCap", "StatCap": {"Total", "Str", "Dex", "Int"},
                "StatMaxCap": {"Str", "Dex", "Int"} (with item bonuses), "Gains": the other PlayerCaps.cfg values}}

Creation.json
  {"Source",
   "Stats":  {"Total", "Min", "Max", "LegacyTotal", "Invalid", "Normalize"},
   "Skills": {"MaxEach", "Totals", "MaxCount", "LegacyMaxCount", "Excluded", "ExcludedExceptProfession", "Invalid"},
   "StartingGold",
   "Backpack":   [entry...]    what every character gets in the pack first
   "Clothes":    [entry...]    equipped last, only for "ClothesForProfessions", only into empty layers
   "Professions":[{"Id", "Name", "Stats", "Skills": [{"Skill", "Value"}], "Equip", "Pack", "SkipSkillItems"}]
   "SkillItems": {"<SkillKey>": {"Equip": [...], "Pack": [...], "Notes"?, ...}}
   "Character":  fixed per-character values (hunger, young status, name rules, appearance rules)
   "Rules":      ordering and equip-fallback rules, in words}
  An item entry is {"Type": <Items.json Type>, "Hue": <hue>} (Hue omitted = the item's default) plus, when needed,
  "Amount" (stack size of a stackable item), "Count" (that many separate items), "LootType", "Spells" (a spellbook's
  starting spells, by registry name) with "Content" (the mask), "Contents" (a container's items), and type-specific
  fields (RedBook: Title/Author/Pages/Writable; Pitcher: Beverage). Choice forms as in Creatures.json outfits:
  {"OneOf": [entry or [entries], ...]} (uniform), {"IfFemale": [...], "IfMale": [...]}.
  Hues: an int; a rule name ("Yellow", "Pink", "Blue": Utility.Random<Name>Hue); {"OneOf": [hues]};
  {"Player": "Shirt"|"Pants", "Min": 2, "Max": 1001}: the hue the player picked (& 0x3FFF), clamped to Min..Max.
"""
import json
import os
import re
import sys

HUE_RULE_RANGES = {"Yellow": [1701, 1754], "Pink": [1201, 1254], "Blue": [1301, 1354]}


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def num(s):
    f = float(s)
    return int(f) if f.is_integer() and "." not in s else f


# ---- skills ------------------------------------------------------------------------------------------------------------

def skills(root):
    text = read(os.path.join(root, "Server", "Skills.cs"))
    enum = re.search(r"enum\s+SkillName\s*\{(.*?)\}", text, re.S).group(1)
    keys = {int(v): k for k, v in re.findall(r"(\w+)\s*=\s*(\d+)", enum)}
    active = set()
    for folder, _, files in os.walk(os.path.join(root, "Scripts")):
        for f in files:
            if f.endswith(".cs"):
                t = read(os.path.join(folder, f))
                for m in re.finditer(r"SkillInfo\.Table\[\s*(?:\(int\)\s*SkillName\.(\w+)|(\d+))\s*\]\.Callback\s*=", t):
                    active.add(m.group(1) or keys[int(m.group(2))])
    rows = []
    pat = re.compile(r"new\s+SkillInfo\(\s*(\d+)\s*,\s*\"([^\"]*)\"\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*\"([^\"]*)\"\s*,"
                     r"\s*null\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*StatCode\.(\w+)\s*,\s*StatCode\.(\w+)"
                     r"\s*(?:,\s*(true|false)\s*)?(?:,\s*(true|false)\s*)?\)")
    for m in pat.finditer(text):
        sid = int(m.group(1))
        s, d, i = float(m.group(3)), float(m.group(4)), float(m.group(5))
        rows.append({
            "Id": sid, "Key": keys[sid], "Name": m.group(2), "Title": m.group(6), "Cliloc": 1044060 + sid,
            "StrScale": s, "DexScale": d, "IntScale": i, "StatTotal": round(s + d + i, 4),
            "StrGain": float(m.group(7)), "DexGain": float(m.group(8)), "IntGain": float(m.group(9)),
            "GainFactor": float(m.group(10)), "Primary": m.group(11), "Secondary": m.group(12),
            "Mastery": m.group(13) == "true", "UseWhileCasting": m.group(14) == "true", "Active": keys[sid] in active,
        })
    rows.sort(key=lambda r: r["Id"])
    assert [r["Id"] for r in rows] == list(range(len(keys))), "SkillInfo table not fully parsed"
    return rows, keys


def player_caps(root):
    cfg = {}
    for line in read(os.path.join(root, "Config", "PlayerCaps.cfg")).splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            k, v = line.split("=", 1)
            v = v.strip()
            if v.lower() in ("true", "false"):
                cfg[k.strip()] = v.lower() == "true"
            elif re.fullmatch(r"-?\d+(\.\d+)?", v):
                cfg[k.strip()] = num(v)
            else:
                cfg[k.strip()] = v
    return cfg


def defaults(cfg):
    used = {"SkillCap", "TotalSkillCap", "TotalStatCap", "StrCap", "DexCap", "IntCap", "StrMaxCap", "DexMaxCap",
            "IntMaxCap", "StatCap"}
    return {
        "Cap": cfg.get("SkillCap", 1000) / 10.0,
        "TotalCap": cfg.get("TotalSkillCap", 7000) / 10.0,
        "StatCap": {"Total": cfg.get("TotalStatCap", 225), "Str": cfg.get("StrCap", 125), "Dex": cfg.get("DexCap", 125),
                    "Int": cfg.get("IntCap", 125)},
        "StatMaxCap": {"Str": cfg.get("StrMaxCap", 150), "Dex": cfg.get("DexMaxCap", 150), "Int": cfg.get("IntMaxCap", 150)},
        "Gains": {k: v for k, v in sorted(cfg.items()) if k not in used},
        "Notes": "From Config/PlayerCaps.cfg (the shard's values; code defaults are the same caps). Caps are in skill "
                 "points (the server stores tenths). Times are dd:hh:mm:ss.",
    }


# ---- spells, bags, names -----------------------------------------------------------------------------------------------

def spell_registry(root):
    text = strip_comments(read(os.path.join(root, "Scripts", "Spells", "Initializer.cs")))
    reg = {}
    for m in re.finditer(r"Register\(\s*(\d+)\s*,\s*typeof\(\s*([\w.]+)\s*\)\s*\)", text):
        name = m.group(2).split(".")[-1]
        reg[int(m.group(1))] = re.sub(r"Spell$", "", name)
    return reg


def book_offsets(root):
    out = {}
    folder = os.path.join(root, "Scripts", "Items", "Equipment", "Spellbooks")
    for f in os.listdir(folder):
        t = strip_comments(read(os.path.join(folder, f)))
        for m in re.finditer(r"class\s+(\w+)\s*:\s*(\w+)", t):
            body = t[m.end():]
            o = re.search(r"BookOffset\s*(?:=>\s*(\d+)|\{\s*get\s*\{\s*return\s+(\d+)\s*;)", body)
            nxt = re.search(r"\bclass\s+\w+", body)
            if o and (not nxt or o.start() < nxt.start()):
                out[m.group(1)] = int(o.group(1) or o.group(2))
            elif m.group(2) in out:
                out[m.group(1)] = out[m.group(2)]
    return out


def bag_contents(root, cls):
    t = strip_comments(read(os.path.join(root, "Scripts", "Items", "Containers", cls + ".cs")))
    m = re.search(r"public\s+" + cls + r"\s*\(\s*int\s+(\w+)\s*\)[^{]*\{(.*?)\}", t, re.S)
    return [mm.group(1) for mm in re.finditer(r"DropItem\(\s*new\s+(\w+)\s*\(\s*" + m.group(1) + r"\s*\)\s*\)", m.group(2))]


def name_rules(root):
    t = read(os.path.join(root, "Scripts", "Misc", "NameVerification.cs"))

    def arr(name):
        body = re.search(name + r"\s*=\s*new\s+\w+\[\]\s*\{(.*?)\}", t, re.S).group(1)
        return body

    words = lambda n: re.findall(r'"([^"]*)"', arr(n))
    chars = re.findall(r"'(\\'|[^'])'", arr("SpaceDashPeriodQuote"))
    return {
        "MinLength": 2, "MaxLength": 16, "Letters": True, "Digits": False,
        "Separators": [c.replace("\\'", "'") for c in chars], "NoSeparatorAtStart": True, "MaxSeparatorsInARow": 1,
        "Disallowed": words("m_Disallowed"), "StartDisallowed": words("m_StartDisallowed"),
        "Fallback": "Generic Player",
        "Notes": "Name is trimmed first. Letters a-z (case-insensitive) plus the separators; a separator may not start "
                 "the name and never two in a row. A Disallowed word is rejected when it stands alone (bounded by the "
                 "start/end of the name or a separator); a name starting with a StartDisallowed word is rejected. An "
                 "invalid name becomes Fallback.",
    }


# ---- creation kits (human branches, hand-written from CharacterCreation.cs) --------------------------------------------

def E(t, hue=None, **kw):
    e = {"Type": t}
    if hue is not None:
        e["Hue"] = hue
    e.update(kw)
    return e


INSTRUMENT = {"OneOf": [E("Drums"), E("Harp"), E("LapHarp"), E("Lute"), E("Tambourine"), E("TambourineTassel")]}
CLOAK_455 = E("Cloak", 0x455)
NECRO_HUE = 0x2C3


def pack_scroll_table(cc):
    body = re.search(r"void\s+PackScroll\s*\(\s*int\s+circle\s*\)\s*\{(.*?)\n\t\t\}", cc, re.S).group(1)
    return {int(a): b for a, b in re.findall(r"case\s+(\d+)\s*:\s*PackItem\(\s*new\s+(\w+)\s*\(", body)}


def pack_scroll(table, circle):
    # switch (Utility.Random(8) * (circle + 1)): circle 0 -> cases 0..7, circle 1 -> 0,2,..,14, circle 2 -> 0,3,..,21
    return {"OneOf": [E(table[r * (circle + 1)]) for r in range(8) if r * (circle + 1) in table]}


def skill_items(spells, bags, scrolls):
    regs = lambda: E("BagOfReagents", Contents=[E(t, Amount=50) for t in bags["BagOfReagents"]], LootType="Regular")
    nregs = lambda: E("BagOfNecroReagents", Contents=[E(t, Amount=50) for t in bags["BagOfNecroReagents"]],
                      LootType="Regular")
    return {
        "Alchemy": {"Pack": [E("Bottle", Amount=4), E("MortarPestle")], "Equip": [E("Robe", "Pink")]},
        "Anatomy": {"Pack": [E("Bandage", Amount=3)], "Equip": [E("Robe", "Yellow")]},
        "AnimalLore": {"Equip": [E("ShepherdsCrook"), E("Robe", "Blue")]},
        "Archery": {"Pack": [E("Arrow", Amount=25)], "Equip": [E("Bow")]},
        "ArmsLore": {"Equip": [{"OneOf": [E("Kryss"), E("Katana"), E("Club")]}]},
        "Begging": {"Equip": [E("GnarledStaff")]},
        "Blacksmith": {"Pack": [E("Tongs"), E("Pickaxe", Count=2), E("IronIngot", Amount=50)],
                       "Equip": [E("HalfApron", "Yellow")]},
        "Bushido": {"Equip": [E("Hakama"), E("Kasa"), spells("BookOfBushido", None)]},
        "Fletching": {"Pack": [E("Board", Amount=14), E("Feather", Amount=5), E("Shaft", Amount=5)]},
        "Camping": {"Pack": [E("Bedroll"), E("Kindling", Amount=5)]},
        "Carpentry": {"Pack": [E("Board", Amount=10), E("Saw")], "Equip": [E("HalfApron", "Yellow")]},
        "Cartography": {"Pack": [E("BlankMap", Count=4), E("Sextant")]},
        "Cooking": {"Pack": [E("Kindling", Amount=2), E("RawLambLeg"), E("RawChickenLeg"), E("RawFishSteak"),
                             E("SackFlour"), E("Pitcher", Beverage="Water")]},
        "Chivalry": {"Pack": [spells("BookOfChivalry", 0x3FF)]},
        "DetectHidden": {"Equip": [CLOAK_455]},
        "Discordance": {"Pack": [INSTRUMENT]},
        "Fencing": {"Equip": [E("Kryss")]},
        "Fishing": {"Equip": [E("FishingPole"), E("FloppyHat", "Yellow")]},
        "Healing": {"Pack": [E("Bandage", Amount=50), E("Scissors")]},
        "Herding": {"Equip": [E("ShepherdsCrook")]},
        "Hiding": {"Equip": [CLOAK_455]},
        "Inscribe": {"Pack": [E("BlankScroll", Amount=2), E("BlueBook")]},
        "ItemID": {"Equip": [E("GnarledStaff")]},
        "Lockpicking": {"Pack": [E("Lockpick", Amount=20)]},
        "Lumberjacking": {"Equip": [E("Hatchet")]},
        "Macing": {"Equip": [E("Club")]},
        "Magery": {"Pack": [regs(), pack_scroll(scrolls, 0), pack_scroll(scrolls, 1), pack_scroll(scrolls, 2)],
                   "Equip": [dict(spells("Spellbook", 0x382A8C38), LootType="Blessed"), E("WizardsHat"), E("Robe", "Blue")],
                   "Notes": "Three random scrolls from PackScroll(0), (1), (2). ServUO's switch is on Random(8) * (circle + 1), "
                            "so circles 1 and 2 only reach every 2nd / 3rd scroll of circles 1-3; the OneOf lists are "
                            "exactly what that code can give."},
        "Mining": {"Pack": [E("Pickaxe")]},
        "Musicianship": {"Pack": [INSTRUMENT]},
        "Necromancy": {"Pack": [nregs(), dict(spells("NecromancerSpellbook", 0x8981), LootType="Blessed")]},
        "Ninjitsu": {"Equip": [E("Hakama", NECRO_HUE), E("Kasa"), spells("BookOfNinjitsu", None)]},
        "Parry": {"Equip": [E("WoodenShield")]},
        "Peacemaking": {"Pack": [INSTRUMENT]},
        "Poisoning": {"Pack": [E("LesserPoisonPotion", Count=2)]},
        "Provocation": {"Pack": [INSTRUMENT]},
        "Snooping": {"Pack": [E("Lockpick", Amount=20)]},
        "SpiritSpeak": {"Equip": [CLOAK_455]},
        "Stealing": {"Pack": [E("Lockpick", Amount=20)]},
        "Swords": {"Equip": [E("Katana")]},
        "Tactics": {"Equip": [E("Katana")]},
        "Tailoring": {"Pack": [E("BoltOfCloth"), E("SewingKit")]},
        "Tinkering": {"Pack": [E("TinkerTools"), E("IronIngot", Amount=50), E("Axle"), E("AxleGears"), E("Springs"),
                               E("ClockFrame")]},
        "Tracking": {"Equip": [E("Boots", "Yellow"), E("SkinningKnife")], "RemoveLayer": "Shoes",
                     "Notes": "Deletes whatever is on the Shoes layer before equipping the boots. Skill items are given "
                              "before the default clothes, so in practice the boots stay and the default Shoes are not given."},
        "Veterinary": {"Pack": [E("Bandage", Amount=5), E("Scissors")]},
        "Wrestling": {"Equip": [E("LeatherGloves")]},
        "Mysticism": {"Pack": [spells("MysticBook", 0xAB)]},
    }


def professions(spells, bags):
    nregs = E("BagOfNecroReagents", Contents=[E(t, Amount=50) for t in bags["BagOfNecroReagents"]], LootType="Regular")
    ninja_hues = {"OneOf": [0x1A8, 0xEC, 0x99, 0x90, 0xB5, 0x336, 0x89]}
    S = lambda *pairs: [{"Skill": k, "Value": v} for k, v in pairs]
    st = lambda s, d, i: {"Str": s, "Dex": d, "Int": i}
    return [
        {"Id": 0, "Name": "Advanced", "Custom": True, "SkipSkillItems": False,
         "Notes": "The player picks stats and skills (see Stats/Skills rules). Any unknown profession id becomes 0."},
        {"Id": 1, "Name": "Warrior", "Stats": st(45, 35, 10),
         "Skills": S(("Anatomy", 30), ("Healing", 30), ("Swords", 30), ("Tactics", 30)),
         "Equip": [E("LeatherChest")], "Pack": [], "SkipSkillItems": False},
        {"Id": 2, "Name": "Magician", "Stats": st(25, 20, 45),
         "Skills": S(("EvalInt", 30), ("Wrestling", 30), ("Magery", 30), ("Meditation", 30)),
         "Equip": [], "Pack": [], "SkipSkillItems": False},
        {"Id": 3, "Name": "Blacksmith", "Stats": st(60, 15, 15),
         "Skills": S(("Mining", 30), ("ArmsLore", 30), ("Blacksmith", 30), ("Tinkering", 30)),
         "Equip": [], "Pack": [], "SkipSkillItems": False},
        {"Id": 4, "Name": "Necromancer", "Stats": st(25, 20, 45),
         "Skills": S(("Necromancy", 30), ("SpiritSpeak", 30), ("Swords", 30), ("Meditation", 20)),
         "Equip": [E("BoneHelm"), E("BoneHarvester"), E("LeatherChest", NECRO_HUE), E("LeatherArms", NECRO_HUE),
                   E("LeatherGloves", NECRO_HUE), E("LeatherGorget", NECRO_HUE), E("LeatherLegs", NECRO_HUE),
                   E("Skirt", NECRO_HUE), E("Sandals", 0x8FD)],
         "Pack": [nregs, dict(spells("NecromancerSpellbook", 0x8981), LootType="Blessed")], "SkipSkillItems": True,
         "Notes": "The reagent bag is packed before the equipment, the spellbook after it."},
        {"Id": 5, "Name": "Paladin", "Stats": st(45, 20, 25),
         "Skills": S(("Chivalry", 30), ("Swords", 30), ("Focus", 30), ("Tactics", 30)),
         "Equip": [E("Broadsword"), E("Helmet"), E("PlateGorget"), E("RingmailArms"), E("RingmailChest"),
                   E("RingmailLegs"), E("ThighBoots", 0x748), E("Cloak", 0xCF), E("BodySash", 0xCF)],
         "Pack": [dict(spells("BookOfChivalry", 0x3FF), LootType="Blessed")], "SkipSkillItems": True},
        {"Id": 6, "Name": "Samurai", "Stats": st(40, 30, 20),
         "Skills": S(("Bushido", 30), ("Swords", 30), ("Anatomy", 30), ("Healing", 30)),
         "Equip": [E("HakamaShita", NECRO_HUE), E("Hakama", NECRO_HUE), E("SamuraiTabi", NECRO_HUE),
                   E("TattsukeHakama", 0x22D), E("Bokuto"), E("LeatherJingasa")],
         "Pack": [E("Scissors"), E("Bandage", Amount=50), spells("BookOfBushido", None)], "SkipSkillItems": True},
        {"Id": 7, "Name": "Ninja", "Stats": st(40, 30, 20),
         "Skills": S(("Ninjitsu", 30), ("Hiding", 30), ("Fencing", 30), ("Stealth", 30)),
         "Equip": [E("Kasa"), E("TattsukeHakama", ninja_hues), E("HakamaShita", NECRO_HUE), E("NinjaTabi", NECRO_HUE),
                   E("Tekagi")],
         "Pack": [E("SmokeBomb"), spells("BookOfNinjitsu", None)], "SkipSkillItems": True},
    ]


PLAYER_SHIRT = {"Player": "Shirt", "Min": 2, "Max": 1001}
PLAYER_PANTS = {"Player": "Pants", "Min": 2, "Max": 1001}
CLOTHES = [
    {"OneOf": [E("Shirt", PLAYER_SHIRT), E("FancyShirt", PLAYER_SHIRT), E("Doublet", PLAYER_SHIRT)]},
    {"IfFemale": [{"OneOf": [E("Skirt", PLAYER_PANTS), E("Kilt", PLAYER_PANTS)]}],
     "IfMale": [{"OneOf": [E("LongPants", PLAYER_PANTS), E("ShortPants", PLAYER_PANTS)]}]},
    E("Shoes", "Yellow"),
]


def appearance():
    return {
        "Body": {"Male": 400, "Female": 401},
        "Skin": {"Flag": 0x8000, "RaceRange": [1002, 1058],
                 "Notes": "Character creation sets the body hue to the player's pick | 0x8000 without clamping; the human "
                          "race's own skin range (ClipSkinHue/RandomSkinHue) is RaceRange."},
        "Hair": {"Valid": [0x203B, 0x203C, 0x203D, 0x2044, 0x2045, 0x2046, 0x2047, 0x2048, 0x2049, 0x204A],
                 "NotFemale": [0x2048], "NotMale": [0x2046], "None": 0, "HueRange": [1102, 1149],
                 "Random": {"Pick": [0x203B, 0x203C, 0x203D, 0x2044, 0x2045, 0x2047, 0x2049, 0x204A],
                            "Last": {"IfFemale": 0x2046, "IfMale": 0x2048},
                            "Notes": "Utility.Random(9): one of Pick or Last, each 1/9 (no baldness)."},
                 "Notes": "An invalid pick leaves the character bald. The hair hue is the player's pick, not clamped "
                          "(HueRange is the race's ClipHairHue/RandomHairHue range). 0x2048 receding (male only), "
                          "0x2046 buns (female only)."},
        "Beard": {"Valid": [0x203E, 0x203F, 0x2040, 0x2041, 0x204B, 0x204C, 0x204D], "None": 0, "FemaleAllowed": False,
                  "Notes": "Same handling as hair; females can only have none."},
        "Face": {"Valid": [0x3B44, 0x3B4D], "RandomRange": [15172, 15180],
                 "Notes": "Valid is an inclusive ItemID range. A missing or invalid face gets a random one from "
                          "RandomRange (inclusive) with the skin hue; a valid pick keeps the player's face hue."},
    }


# ---- cross-check against the source ------------------------------------------------------------------------------------

def case_types(block):
    types = set(re.findall(r"new\s+(\w+)\s*\(", block))
    if "PackInstrument" in block:
        types |= {e["Type"] for e in INSTRUMENT["OneOf"]}
    return types


def entry_types(entries, out):
    for e in entries or []:
        if isinstance(e, list):
            entry_types(e, out)
            continue
        if "Type" in e:
            out.add(e["Type"])
            entry_types(e.get("Contents"), out)
        for k in ("OneOf", "IfFemale", "IfMale"):
            if k in e:
                entry_types(e[k], out)
    return out


def switch_cases(code, label):
    out = {}
    parts = re.split(r"case\s+" + label + r"\s*:", code)
    for i in range(1, len(parts), 2):
        out[parts[i]] = parts[i + 1]
    return out


def main():
    root, items_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
    items = {i["Type"]: i for i in json.load(open(items_path, encoding="utf-8"))["Items"]}
    skill_rows, keys = skills(root)
    cfg = player_caps(root)
    reg = spell_registry(root)
    offsets = book_offsets(root)
    cc = strip_comments(read(os.path.join(root, "Scripts", "Misc", "CharacterCreation.cs")))
    bags = {b: bag_contents(root, b) for b in ("BagOfReagents", "BagOfNecroReagents")}
    scrolls = pack_scroll_table(cc)
    default_content = {}
    for b in offsets:
        p = os.path.join(root, "Scripts", "Items", "Equipment", "Spellbooks", b + ".cs")
        if os.path.exists(p):
            m = re.search(r"public\s+" + b + r"\s*\(\s*\)\s*:\s*this\s*\(\s*\(ulong\)\s*(0x[0-9A-Fa-f]+|\d+)", read(p))
            if m:
                default_content[b] = int(m.group(1), 0)

    def spells(book, content):
        if content is None:
            content = default_content.get(book, 0)
        off = offsets[book]
        names = [reg.get(off + b, "Unknown%d" % (off + b)) for b in range(64) if content >> b & 1]
        return {"Type": book, "Content": content, "Spells": names}

    profs = professions(spells, bags)
    sitems = skill_items(spells, bags, scrolls)

    # backpack, straight from AddBackpack's human branch
    gold = int(re.search(r"PackItem\(\s*new\s+Gold\(\s*(\d+)\s*\)", cc).group(1))
    backpack = [E("RedBook", Title="a book", Author="<CharacterName>", Pages=20, Writable=True), E("Gold", Amount=gold),
                E("Candle"), E("Dagger")]

    creation = {
        "Source": "ServUO Scripts/Misc/CharacterCreation.cs (human branches; expansions on, Siege off)",
        "Stats": {"Total": 90, "Min": 10, "Max": 60, "LegacyTotal": 80, "Invalid": {"Str": 10, "Dex": 10, "Int": 10},
                  "Normalize": "Advanced profession only. With v = stat - 10 (floored at 0) and target = Total - 30: if "
                               "the v's sum to neither 0 nor target, each v = floor(v * target / sum), then Str, Dex, Int "
                               "in turn absorb the remaining difference (each clamped 0..target). Stats = v + 10. If any "
                               "stat is then outside Min..Max or the sum isn't Total, all stats become Invalid."},
        "Skills": {"MaxEach": 50, "Totals": [100, 120], "MaxCount": 4, "LegacyMaxCount": 3,
                   "Excluded": ["RemoveTrap", "Spellweaving"], "ExcludedExceptProfession": {"Stealth": 7},
                   "Invalid": "Advanced profession only: each value must be 0..MaxEach, no skill twice (with value > 0), "
                              "and the values must total one of Totals; otherwise the character gets no skills and no "
                              "skill items. A skill with value 0 is ignored. Excluded skills are never set and give no "
                              "items (Stealth is allowed only for the profession id given). Totals: 100 came from the old "
                              "3-skill packet, 120 from the 4-skill one; the server accepts either."},
        "StartingGold": gold,
        "Backpack": backpack,
        "Clothes": CLOTHES,
        "ClothesForProfessions": [0, 1, 2, 3],
        "Professions": profs,
        "SkillItems": dict(sorted(sitems.items())),
        "Character": {
            "Hunger": 20, "AutoRenewInsurance": True,
            "SkillCap": "Skills.json Defaults.Cap for every skill",
            "Young": {"When": "the account is Young", "Bank": [E("NewPlayerTicket")],
                      "Notes": "The ticket's Owner is the new character."},
            "Name": name_rules(root),
            "Appearance": appearance(),
            "StartLocation": "The city the player picked from the server's start-city list (not part of this file).",
        },
        "Rules": {
            "Order": ["Backpack", "profession Equip/Pack (in listed order)", "SkillItems for each chosen skill, in the "
                      "order the skills were chosen", "Clothes"],
            "SkillItemsWhen": "the skill's starting value is > 0 and the profession doesn't SkipSkillItems",
            "Equip": "Equip entries go on the character; if the layer is taken they go into the backpack instead.",
            "Clothes": "Clothes are only equipped into a free layer; if the layer is taken the item is not given.",
            "Pack": "Pack entries go into the backpack.",
            "Duplicates": "Skills with the same item (Swords and Tactics both give a Katana; two robes) give both: the "
                          "second one lands in the backpack because its layer is taken.",
            "LootType": "Items are Regular unless the entry says otherwise (spellbooks marked Blessed by the code; the "
                        "item's own default otherwise).",
        },
        "Dropped": "Elf and gargoyle branches (race items, GargishDagger, elven robes...), Throwing's item (gargoyle "
                   "only), TestCenter bank filling, Siege map rules, pre-AOS Newbied loot types.",
    }

    skills_json = {
        "Source": "ServUO Server/Skills.cs (SkillName, SkillInfo table), Config/PlayerCaps.cfg, skill Callback "
                  "assignments in Scripts",
        "Skills": skill_rows,
        "ValueFormula": "inv = max(0, 100 - Base) / 100; bonus = min((Str*StrScale + Dex*DexScale + Int*IntScale) / 100 "
                        "* inv, StatTotal * inv); Value = Base + bonus, then skill mods (relative mods that obey the cap "
                        "only up to Cap).",
        "Defaults": defaults(cfg),
    }

    os.makedirs(out, exist_ok=True)
    for name, data in (("Skills.json", skills_json), ("Creation.json", creation)):
        with open(os.path.join(out, name), "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=1)
            f.write("\n")

    # ---- checks and summary
    known = set(keys.values())
    for k in list(sitems) + [s["Skill"] for p in profs for s in p.get("Skills", [])]:
        assert k in known, "unknown skill key " + k
    for p in profs:
        if "Skills" in p:
            assert sum(s["Value"] for s in p["Skills"]) >= 100 and all(10 <= v <= 60 for v in p["Stats"].values())
    ask = re.search(r"void\s+AddSkillItems\s*\(.*?\{(.*?)\n\t\tprivate class", cc, re.S).group(1)
    cases = switch_cases(ask, r"SkillName\.(\w+)")
    dropped = {}
    for k, kit in sitems.items():
        mine = entry_types(kit.get("Equip"), set()) | entry_types(kit.get("Pack"), set())
        src = case_types(cases.get(k, "")) | ({"Spellbook"} if k == "Magery" else set())
        if k == "Magery":
            src |= set(scrolls.values()) | set(bags["BagOfReagents"])
        if k == "Necromancy":
            src |= set(bags["BagOfNecroReagents"])
        extra = mine - src
        assert not extra, "%s: %s not in the source case" % (k, extra)
        if src - mine:
            dropped[k] = sorted(src - mine)
    for k in cases:
        if k not in sitems:
            dropped[k] = sorted(case_types(cases[k]))
    pro = re.search(r"var\s+addSkillItems.*?switch\s*\(\s*prof\s*\)\s*\{(.*?)\n\t\t\tfor\s*\(", cc, re.S).group(1)
    pcases = switch_cases(pro, r"(\d+)")
    pnum = {1: "1", 4: "4", 5: "5", 6: "6", 7: "7"}
    for p in profs:
        if p["Id"] in pnum:
            src = case_types(pcases.get(pnum[p["Id"]], "")) | set(bags["BagOfNecroReagents"])
            mine = entry_types(p["Equip"], set()) | entry_types(p["Pack"], set())
            extra = mine - src
            assert not extra, "%s: %s not in the source case" % (p["Name"], extra)
            if src - mine - set(bags["BagOfNecroReagents"]):
                dropped[p["Name"]] = sorted(src - mine - set(bags["BagOfNecroReagents"]))

    used = set()
    entry_types(backpack, used)
    entry_types(CLOTHES, used)
    entry_types([E("NewPlayerTicket")], used)
    for p in profs:
        entry_types(p.get("Equip"), used)
        entry_types(p.get("Pack"), used)
    for kit in sitems.values():
        entry_types(kit.get("Equip"), used)
        entry_types(kit.get("Pack"), used)
    missing = sorted(t for t in used if t not in items)

    def amounts(entries, out_):
        for e in entries or []:
            if "Amount" in e and not items.get(e["Type"], {}).get("Stackable"):
                out_.append(e["Type"])
            amounts(e.get("Contents"), out_)
            for k in ("OneOf", "IfFemale", "IfMale"):
                amounts(e.get(k), out_)
        return out_
    unstack = []
    for kit in list(sitems.values()) + profs + [{"Pack": backpack}]:
        amounts(kit.get("Pack"), unstack)
        amounts(kit.get("Equip"), unstack)

    print(f"{len(skill_rows)} skills ({sum(r['Active'] for r in skill_rows)} active, "
          f"{sum(r['Mastery'] for r in skill_rows)} with masteries); caps {skills_json['Defaults']['Cap']} each, "
          f"{skills_json['Defaults']['TotalCap']} total -> {os.path.join(out, 'Skills.json')}")
    print(f"{len(profs)} professions, {len(sitems)} SkillItems entries, {len(used)} item types -> "
          f"{os.path.join(out, 'Creation.json')}")
    print("source types left out (non-human branches): " +
          "; ".join(f"{k}: {', '.join(v)}" for k, v in sorted(dropped.items())))
    if unstack:
        print("Amount on items Items.json doesn't mark Stackable: " + ", ".join(sorted(set(unstack))))
    print("item types not in Items.json: " + (", ".join(missing) if missing else "none"))


if __name__ == "__main__":
    main()
