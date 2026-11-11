"""Extracts ServUO's gathering rules (mining, lumberjacking, fishing; data only) into Content/Harvest.json.

  python -I export_servuo_harvest.py <ServUO Scripts folder> <Items.json> <output json>

Reads Services/Harvest/{Mining,Lumberjacking,Fishing}.cs: each HarvestDefinition built in the system's constructor
(bank size and respawn, tiles, range, amounts, effect timing, messages, resources, veins, bonus resources), the static
tile arrays, Fishing's mutate tables, and the tool classes whose HarvestSystem is Mining/Lumberjacking/Fishing (found by
walking every class under Scripts). High Seas fish (FishInfo.cs, Fish.cs) and the mining gem list (Loot.GemTypes) are
read too. Core.<expansion> checks are taken as true and Siege as false, the same as the sibling exporters. Behaviour
that lives in overridden C# methods (stone/gem mining, the Gargoyle's pickaxe, the Harvester's axe...) is not parseable
as data; it is written out here by hand from the source (marked "hand table" below) and cross-checked where it can be.
Nothing of ServUO's code is reused: the game re-implements the rules; these are the numbers.

Output:
{"Source": "...",
 "Rules": {...how ServUO turns the numbers below into a harvest, in prose...},
 "Systems": {
   "<Mining|Lumberjacking|Fishing>": {
     "Skill": "<skill>",
     "Tools": {"<Items.json Type>": {                  every constructable class whose HarvestSystem is this system
        "UsesRemaining": <int> | [min, max],          the default uses (omitted when the class sets none)
        "UsesPerHarvest": 0 | 1,                       uses taken per successful skill roll (0 = never worn by use)
        "ExceptionalUsesMultiplier": 2,                 crafted exceptional doubles the uses (BaseHarvestTool, poles)
        "DurabilityLossChance": <percent>,              shard rule: chance per successful roll to lose 1 durability
        "Effect": "<text>"}},                           tool-specific behaviour (hand table)
     "SystemMessages": {"<key>": {"Cliloc": n, "Text": "..."}},
     "Notes": [...],
     "Definitions": [                                    in ServUO's order: a tile matching several belongs to the first
       {"Name": "Ore", "Skill": "Mining",
        "Tools": [<Type>, ...],
        "TileIds": "expanded",                          every id listed individually (ranges were expanded)
        "Tiles": {"Land": [ids], "Static": [ids]},      land tile ids / static graphic ids (ServUO's 0x4000 offset removed)
        "Bank": {"Width", "Height", "MinTotal", "MaxTotal", "MinRespawnSeconds", "MaxRespawnSeconds", "RandomizeVeins"},
        "MaxRange": n, "ConsumedPerHarvest": n, "ConsumedPerFeluccaHarvest": n, "PlaceAtFeetIfFull": bool,
        "HumanBonus": {"Chance": 10, "Multiplier": 1.1, "Rounding": "Ceiling"} | absent,
        "Effect": {"Actions": [..], "Sounds": [..], "Counts": [..], "DelaySeconds", "SoundDelaySeconds",
                   "DurationSeconds": [min, max]},
        "Resources": [{"Name", "ReqSkill", "MinSkill", "MaxSkill", "Types": [..], "Message": {"Cliloc": n}, ...}],
        "Veins": [{"Chance", "Primary", "Fallback", "FallbackChance"}],
        "BonusResources": [{"ReqSkill", "Chance", "Type" (null = nothing), "Message", "Map"}],
        "Messages": {"NoResources"|"Fail"|"OutOfRange"|"TimedOutOfRange"|"DoubleHarvest"|"PackFull"|"ToolBroke":
                     {"Cliloc": n, "Text": "<the source comment>"}},
        ...definition extras (Requires, ResourceSelection, Mutations, LavaTiles, SpecialFish...),
        "Notes": [...]}]}}}
Item types are checked against Items.json (and the elementals against Creatures.json next to it); misses are printed.
"""
import json
import os
import re
import sys

SYSTEMS = ["Mining", "Lumberjacking", "Fishing"]
# hand table: variable name in the system constructor -> exported definition name
DEF_NAMES = {"oreAndStone": "Ore", "sand": "Sand", "lumber": "Logs", "fish": "Fish"}
MESSAGE_KEYS = {
    "NoResourcesMessage": "NoResources", "OutOfRangeMessage": "OutOfRange",
    "TimedOutOfRangeMessage": "TimedOutOfRange", "DoubleHarvestMessage": "DoubleHarvest",
    "FailMessage": "Fail", "PackFullMessage": "PackFull", "ToolBrokeMessage": "ToolBroke",
}
# hand table: messages the system scripts send outside the definitions (texts are looked up from the source comments)
SYSTEM_MESSAGES = {
    "Mining": {"Prompt": 503033, "CantMineThere": 501862, "CantMineThat": 501863, "Mounted": 501864,
               "Polymorphed": 501865, "StoneFound": 1044606, "GemFound": 1112233},
    "Lumberjacking": {"BadTarget": 500489, "MustBeInPack": 1080058, "BoardsMade": 1158776},
    "Fishing": {"Prompt": 500974, "AlreadyFishing": 500972, "Mounted": 500971, "BigFish": 1042635,
                "Chest": 503175, "Serpent": 503170},
}
LAVA_LAND = set(range(0x1F4, 0x1F8))  # hand table: the lava land tiles in Fishing.m_LavaTiles; the rest are statics
GATHER_WEAR_PERCENT = None  # read from Custom/Gathering/GatherMods.cs


def matching(text, start, open_c="{", close_c="}"):
    depth = 0
    for i in range(start, len(text)):
        if text[i] == open_c:
            depth += 1
        elif text[i] == close_c:
            depth -= 1
            if depth == 0:
                return i + 1
    return len(text)


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def num(s):
    s = s.strip()
    if re.fullmatch(r"-?0x[0-9A-Fa-f]+", s):
        return int(s, 16)
    if re.fullmatch(r"-?\d+", s):
        return int(s)
    return float(s)


def ints(s):
    return [num(x) for x in re.findall(r"-?0x[0-9A-Fa-f]+|-?\d+(?:\.\d+)?", s)]


def resolve_flags(text):
    """Core.<x> = true, Siege.SiegeShard = false: if/else blocks and ternaries keep the branch that runs."""
    cond = r"(!?)\s*(Core\.\w+|Siege\.SiegeShard)"
    while True:
        m = re.search(r"if\s*\(\s*" + cond + r"\s*\)\s*\{", text)
        if not m:
            break
        truth = (m.group(2) != "Siege.SiegeShard") != (m.group(1) == "!")
        then_end = matching(text, m.end() - 1)
        then_body = text[m.end():then_end - 1]
        e = re.match(r"\s*else\s*\{", text[then_end:])
        else_body, end = "", then_end
        if e:
            end = matching(text, then_end + e.end() - 1)
            else_body = text[then_end + e.end():end - 1]
        text = text[:m.start()] + (then_body if truth else else_body) + text[end:]
    while True:
        m = re.search(cond + r"\s*\?", text)
        if not m:
            break
        truth = (m.group(2) != "Siege.SiegeShard") != (m.group(1) == "!")
        parts, depth, i, start = [], 0, m.end(), m.end()
        while i < len(text):
            c = text[i]
            if c in "([{":
                depth += 1
            elif c in ")]}":
                if depth == 0:
                    break
                depth -= 1
            elif depth == 0 and c == ":" and not parts:
                parts.append(text[start:i])
                start = i + 1
            elif depth == 0 and c in ",;":
                break
            i += 1
        parts.append(text[start:i])
        text = text[:m.start()] + (parts[0] if truth else parts[1]) + text[i:]
    return re.sub(r"\bCore\.\w+\b", "true", text)


def statements(code):
    out, depth, start = [], 0, 0
    for i, c in enumerate(code):
        if c in "({[":
            depth += 1
        elif c in ")}]":
            depth -= 1
        elif c == ";" and depth == 0:
            out.append(" ".join(code[start:i].split()))
            start = i + 1
    return [s for s in out if s]


def comment_texts(raw):
    """cliloc number -> the text in the '// ...' comment after it, from the raw source."""
    texts = {}
    for m in re.finditer(r"\b(\d{6,7})\b[^\n]*?//\s*([^\n]+)", raw):
        texts.setdefault(int(m.group(1)), m.group(2).strip())
    return texts


def message(value, texts):
    if value is None or value == "null":
        return None
    v = str(value).strip()
    if re.fullmatch(r"\d+", v):
        msg = {"Cliloc": int(v)}
        if int(v) in texts:
            msg["Text"] = texts[int(v)]
        return msg
    if v.startswith('"'):
        return {"Text": v.strip('"')}
    return {"Expr": v}


def static_array(text, name):
    m = re.search(r"\b" + re.escape(name) + r"\s*=\s*new\s*(?:\w+\s*)?\[\s*\]\s*\{", text)
    if not m:
        raise SystemExit(f"static array {name} not found")
    return text[m.end():matching(text, m.end() - 1) - 1]


def typeofs(s):
    return re.findall(r"typeof\s*\(\s*([\w.]+)\s*\)", s)


def seconds(expr):
    m = re.match(r"TimeSpan\.From(Minutes|Seconds|Milliseconds)\(\s*([\d.]+)\s*\)", expr)
    if m:
        return float(m.group(2)) * {"Minutes": 60, "Seconds": 1, "Milliseconds": 0.001}[m.group(1)]
    if expr == "TimeSpan.Zero":
        return 0.0
    raise SystemExit("unreadable TimeSpan: " + expr)


def validate(tiles, ranged, tile_id):
    """HarvestDefinition.Validate, as written (the list form stops at the first entry >= the id)."""
    if ranged:
        return any(tiles[i] <= tile_id <= tiles[i + 1] for i in range(0, len(tiles), 2))
    dist = -1
    for t in tiles:
        dist = t - tile_id
        if dist >= 0:
            break
    return dist == 0


def split_tiles(ids):
    land = sorted(i for i in ids if i < 0x4000)
    static = sorted(i & 0x3FFF for i in ids if i >= 0x4000)
    return {"Land": land, "Static": static}


def parse_system(name, raw, notes_out):
    text = resolve_flags(strip_comments(raw))
    texts = comment_texts(raw)
    m = re.search(r"private\s+" + name + r"\s*\(\s*\)\s*\{", text)
    body = text[m.end():matching(text, m.end() - 1) - 1]
    body = re.sub(r"^\s*#.*$", "", body, flags=re.M)
    defs, order, res, veins = {}, [], [], []
    for st in statements(body):
        d = re.match(r"HarvestDefinition\s+(\w+)\s*=\s*(?:\w+\s*=\s*)?new\s+HarvestDefinition\(\)", st)
        if d:
            defs[d.group(1)] = {"_props": {}}
            order.append(d.group(1))
            continue
        if re.match(r"(HarvestResource|HarvestVein)\[\]\s+\w+$", st):
            continue
        r = re.match(r"res\s*=\s*new\s+HarvestResource\[\]\s*\{(.*)\}$", st)
        if r:
            res = []
            for h in re.finditer(r"new\s+HarvestResource\(([^()]*(?:\([^()]*\)[^()]*)*)\)", r.group(1)):
                args = [a.strip() for a in h.group(1).split(",")]
                res.append({"ReqSkill": num(args[0]), "MinSkill": num(args[1]), "MaxSkill": num(args[2]),
                            "Message": message(args[3], texts), "Types": typeofs(h.group(1))})
            continue
        v = re.match(r"veins\s*=\s*new\s+HarvestVein\[\]\s*\{(.*)\}$", st)
        if v:
            veins = []
            for h in re.finditer(r"new\s+HarvestVein\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*res\[(\d+)\]\s*,\s*(null|res\[(\d+)\])\s*\)",
                                 v.group(1)):
                veins.append({"Chance": num(h.group(1)), "FallbackChance": num(h.group(2)),
                              "Primary": int(h.group(3)), "Fallback": int(h.group(5)) if h.group(5) else None})
            continue
        a = re.match(r"(\w+)\.(\w+)\s*=\s*(.*)$", st, flags=re.S)
        if a and a.group(1) in defs:
            var, prop, expr = a.groups()
            props = defs[var]["_props"]
            if prop == "Resources":
                props["Resources"] = res
            elif prop == "Veins":
                props["Veins"] = veins
            elif prop == "BonusResources":
                bonus = []
                for h in re.finditer(r"new\s+BonusHarvestResource\(([^()]*(?:\([^()]*\)[^()]*)*)\)", expr):
                    args = [x.strip() for x in h.group(1).split(",")]
                    b = {"ReqSkill": num(args[0]), "Chance": num(args[1]), "Type": (typeofs(args[3]) or [None])[0],
                         "Message": message(args[2], texts)}
                    if len(args) > 4:
                        b["Map"] = args[4].split(".")[-1]
                    bonus.append(b)
                props["BonusResources"] = bonus
            else:
                props[prop] = expr
            continue
        if re.match(r"(this\.)?(Definitions\.Add|m_Definition\s*=)", st):
            continue
        notes_out.append(f"{name}: unread constructor statement: {st}")
    return text, texts, [(var, defs[var]["_props"]) for var in order]


def build_definition(sysname, var, props, text, texts, unread):
    def take(k):
        return props.pop(k, None)

    d = {"Name": DEF_NAMES.get(var, var[:1].upper() + var[1:]), "Skill": take("Skill").split(".")[-1]}
    ranged = take("RangedTiles") == "true"
    tiles_name = take("Tiles")
    raw_tiles = ints(static_array(text, tiles_name))
    if re.search(r"Array\.Sort\(\s*" + tiles_name + r"\s*\)", text):
        raw_tiles.sort()  # sorted at startup (Initialize), before any Validate
    reachable = [t for t in raw_tiles if validate(raw_tiles, ranged, t)] if not ranged else raw_tiles
    if ranged:
        ids = set()
        for i in range(0, len(raw_tiles), 2):
            ids.update(range(raw_tiles[i], raw_tiles[i + 1] + 1))
    else:
        ids = set(reachable)
    d["TileIds"] = "expanded"
    d["Tiles"] = split_tiles(ids)
    d["_raw"] = (raw_tiles, ranged)
    notes = []
    dup = sorted({t for t in raw_tiles if raw_tiles.count(t) > 1}) if not ranged else []
    if dup:
        notes.append(f"Tile list {tiles_name} repeats {dup} (harmless).")
    lost = sorted(set(raw_tiles) - set(reachable))
    if lost:
        notes.append(f"Tiles {lost} are listed in {tiles_name} but ServUO's Validate never matches them (list out "
                     "of order); they are left out here.")
    special = take("SpecialTiles")
    if special:
        lava = ints(static_array(text, special))
        d["LavaTiles"] = {"Land": sorted({i for i in lava if i in LAVA_LAND}),
                          "Static": sorted({i for i in lava if i not in LAVA_LAND})}
    d["Bank"] = {"Width": num(take("BankWidth")), "Height": num(take("BankHeight")),
                 "MinTotal": num(take("MinTotal")), "MaxTotal": num(take("MaxTotal")),
                 "MinRespawnSeconds": seconds(take("MinRespawn")), "MaxRespawnSeconds": seconds(take("MaxRespawn")),
                 "RandomizeVeins": take("RandomizeVeins") == "true"}
    d["MaxRange"] = num(take("MaxRange"))
    d["ConsumedPerHarvest"] = num(take("ConsumedPerHarvest"))
    d["ConsumedPerFeluccaHarvest"] = num(take("ConsumedPerFeluccaHarvest"))
    d["PlaceAtFeetIfFull"] = take("PlaceAtFeetIfFull") == "true"
    if take("RaceBonus") == "true":
        d["HumanBonus"] = {"Chance": 10.0, "Multiplier": 1.1, "Rounding": "Ceiling"}
    counts = ints(take("EffectCounts"))
    delay, sdelay = seconds(take("EffectDelay")), seconds(take("EffectSoundDelay"))
    d["Effect"] = {"Actions": ints(take("EffectActions")), "Sounds": ints(take("EffectSounds")), "Counts": counts,
                   "DelaySeconds": delay, "SoundDelaySeconds": sdelay,
                   "DurationSeconds": [round((min(counts) - 1) * delay + sdelay, 3),
                                       round((max(counts) - 1) * delay + sdelay, 3)]}
    resources = take("Resources") or []
    names = []
    for r in resources:
        names.append(re.sub(r"(Ore|Log|Granite)$", "", r["Types"][0]) or r["Types"][0])
    d["Resources"] = [dict({"Name": n}, **r) for n, r in zip(names, resources)]
    d["Veins"] = [{"Chance": v["Chance"], "Primary": names[v["Primary"]],
                   "Fallback": names[v["Fallback"]] if v["Fallback"] is not None else None,
                   "FallbackChance": v["FallbackChance"]} for v in (take("Veins") or [])]
    total = round(sum(v["Chance"] for v in d["Veins"]), 6)
    if total != 100.0:
        notes.append(f"Vein chances add up to {total}, not 100.")
    bonus = take("BonusResources")
    if bonus:
        d["BonusResources"] = bonus
        btotal = round(sum(b["Chance"] for b in bonus), 6)
        if btotal != 100.0:
            notes.append(f"Bonus chances add up to {btotal}%; the remainder gives nothing.")
    msgs = {}
    for k, key in MESSAGE_KEYS.items():
        v = take(k)
        if v is not None:
            msgs[key] = message(v, texts)
    d["Messages"] = msgs
    for k in list(props):
        unread.append(f"{sysname}.{d['Name']}: unread property {k} = {props[k]}")
    d["Notes"] = notes
    return d


def class_index(root):
    """name -> {"bases": [...], "text": class body, "abstract": bool, "file": path}"""
    classes = {}
    decl = re.compile(r"\b(?:public|internal)\s+((?:sealed\s+|abstract\s+|partial\s+|static\s+)*)class\s+(\w+)"
                      r"\s*(?::\s*([\w.,\s<>]+?))?\s*\{")
    for folder, _, files in os.walk(root):
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(folder, f)
            text = strip_comments(read(path))
            for m in decl.finditer(text):
                name = m.group(2)
                if name in classes:
                    continue
                bases = [b.strip().split(".")[-1] for b in (m.group(3) or "").split(",") if b.strip()]
                classes[name] = {"bases": bases, "abstract": "abstract" in m.group(1),
                                 "text": text[m.end() - 1:matching(text, m.end() - 1)],
                                 "file": os.path.relpath(path, root).replace("\\", "/")}
    return classes


def ancestors(classes, name):
    out, seen = [], set()
    while name in classes and name not in seen:
        seen.add(name)
        out.append(name)
        bases = classes[name]["bases"]
        name = bases[0] if bases else None
    if name and name not in classes:
        out.append(name)  # a core (Server.dll) class such as Item
    return out


def implements(classes, name, iface):
    return any(iface in classes[c]["bases"] for c in ancestors(classes, name) if c in classes)


def own_harvest_system(body):
    m = re.search(r"override\s+HarvestSystem\s+HarvestSystem\s*(?:=>\s*([\w.]+)\s*;|\{\s*get\s*\{\s*return\s+([\w.]+)\s*;)",
                  body) or re.search(r"virtual\s+HarvestSystem\s+HarvestSystem\s*(?:=>\s*([\w.]+)\s*;|\{\s*get\s*\{\s*return\s+([\w.]+)\s*;)",
                                     body)
    if m:
        v = m.group(1) or m.group(2)
        return "null" if v == "null" else v.split(".")[0]
    m = re.search(r"(\w+)\.System\.BeginHarvesting\(\s*from\s*,\s*this\s*\)", body)
    if m and m.group(1) in SYSTEMS:
        return m.group(1)
    return None


def harvest_system(classes, name):
    for c in ancestors(classes, name):
        if c not in classes:
            break
        s = own_harvest_system(classes[c]["text"])
        if s:
            return None if s == "null" else s
    return None


def default_uses(classes, name):
    for c in ancestors(classes, name):
        if c in ("BaseWeapon", "Item") or c not in classes:
            return None
        body = classes[c]["text"]
        ctor = re.search(r"public\s+" + c + r"\s*\(\s*\)\s*(?::\s*(base|this)\s*\(([^{]*)\))?\s*\{", body)
        if not ctor:
            continue
        code = body[ctor.end():matching(body, ctor.end() - 1) - 1]
        u = re.search(r"UsesRemaining\s*=\s*(\d+)\s*;", code)
        if u:
            return int(u.group(1))
        if ctor.group(1) == "this" and ctor.group(2):
            rng = re.search(r"Utility\.RandomMinMax\(\s*(\d+)\s*,\s*(\d+)\s*\)", ctor.group(2))
            if rng:
                return [int(rng.group(1)), int(rng.group(2))]
            if re.fullmatch(r"\s*\d+\s*", ctor.group(2)):
                return int(ctor.group(2))
    return None


def tools_for(classes, system, wear_percent):
    """Mirrors HarvestSystem.FinishHarvesting's use counting and GatherMods' tool rules (Siege false)."""
    def isa(name, base):
        return base in ancestors(classes, name)

    tools = {}
    for name in sorted(classes):
        c = classes[name]
        if c["abstract"] or harvest_system(classes, name) != system or not isa(name, "Item"):
            continue
        mining_tool = any(isa(name, b) for b in ("Pickaxe", "Shovel", "SturdyPickaxe", "GargoylesPickaxe"))
        gather_tool = mining_tool or isa(name, "Hatchet")
        unlimited = mining_tool
        counted = (implements(classes, name, "IUsesRemaining") and not unlimited and
                   any(isa(name, b) for b in ("BaseHarvestTool", "Pickaxe", "SturdyPickaxe", "GargoylesPickaxe")))
        rec = {}
        uses = default_uses(classes, name)
        if uses is not None and (counted or isa(name, "FishingPole") or isa(name, "BaseHarvestTool") or mining_tool):
            rec["UsesRemaining"] = uses
        rec["UsesPerHarvest"] = 1 if counted else 0
        if counted and (isa(name, "BaseHarvestTool") or isa(name, "FishingPole")):
            rec["ExceptionalUsesMultiplier"] = 2
        if gather_tool and isa(name, "BaseWeapon"):
            rec["DurabilityLossChance"] = float(wear_percent)
        tools[name] = rec
    return tools


# hand table: tool-specific behaviour coded in the system overrides (Mining.GetResourceType/MutateVein/
# OnHarvestFinished, Lumberjacking.MutateType, Fishing.HasTypeHook uses, GatherMods.CheckEquipped)
TOOL_EFFECTS = {
    "Mining": {
        "GargoylesPickaxe": "Ore: the bank's vein is treated as the next vein in the list (Iron->Dull Copper ... Verite->"
                            "Valorite; Valorite stays). On a harvest of the vein's primary resource, 10% chance to "
                            "spawn that resource's Elemental (25% ore-elemental creature) next to the miner, attacking.",
        "RockHammer": "Ore: stone mining (when learned and toggled, Mining base >= 100) succeeds 50% instead of 15%.",
        "ImprovedRockHammer": "Ore: always gives the resource's granite when Mining base >= 100, otherwise nothing.",
    },
    "Lumberjacking": {
        "HarvestersAxe": "While Charges > 0, logs come out as the matching boards (Log->Board, OakLog->OakBoard, "
                         "AshLog->AshBoard, YewLog->YewBoard, HeartwoodLog->HeartwoodBoard, BloodwoodLog->BloodwoodBoard, "
                         "FrostwoodLog->FrostwoodBoard), one charge per conversion; message 1158776.",
    },
    "Fishing": {},
}
HARVESTERS_AXE_BOARDS = {"Log": "Board", "OakLog": "OakBoard", "AshLog": "AshBoard", "YewLog": "YewBoard",
                         "HeartwoodLog": "HeartwoodBoard", "BloodwoodLog": "BloodwoodBoard",
                         "FrostwoodLog": "FrostwoodBoard"}


def mutate_table(text, name):
    entries, arr = [], static_array(text, name)
    for m in re.finditer(r"new\s+MutateEntry\s*\(", arr):
        inner = arr[m.end():matching(arr, m.end() - 1, "(", ")") - 1]
        a = re.match(r"\s*([-\d.]+)\s*,\s*([-\d.]+)\s*,\s*([-\d.]+)\s*,\s*(true|false)\s*,(.*)$", inner, flags=re.S)
        types = typeofs(a.group(5))
        e = {"ReqSkill": num(a.group(1)), "MinSkill": num(a.group(2)), "MaxSkill": num(a.group(3)),
             "DeepWater": a.group(4) == "true", "Types": types}
        if not types:
            e["Nothing"] = True
        entries.append(e)
    return entries


def fishing_extras(d, text, root, texts):
    m = mutate_table(text, "m_MutateTable")
    # hand table: Fishing.MutateType's per-index conditions; checked against the entry's types
    assert m[5]["Types"][0] == "Boots" and m[6]["Types"] == ["MudPuppy", "RedHerring"], "mutate table changed"
    m[5]["JunkProofHookSkips"] = 80.0
    m[6]["Region"] = "Underworld"
    d["Mutations"] = m
    lava = mutate_table(text, "m_LavaMutateTable")
    for e in lava:
        if e["Types"] == ["BaseWeapon"]:
            e["Types"] = []
            e["Special"] = "RandomWeaponWithSearingSocket"
    d["LavaMutations"] = lava
    hs = os.path.join(root, "Services", "Expansions", "High Seas", "Items", "Fish")
    info_raw = strip_comments(read(os.path.join(hs, "FishInfo.cs")))
    chances = {k: num(v) for k, v in re.findall(r"static\s+readonly\s+double\s+(\w+Chance)\s*=\s*([\d.]+)\s*;", info_raw)}
    rare = []
    for f in re.finditer(r"new\s+FishInfo\(\s*(\d+)\s*,\s*typeof\((\w+)\)\s*,\s*(\d+)\s*,\s*\"([^\"]+)\"\s*,\s*(true|false)\s*,"
                         r"\s*(\w+)\s*,\s*([\d.]+)\s*\)", info_raw):
        if f.group(4).lower() == "cannotfishup":
            continue
        rare.append({"Type": f.group(2), "Hue": int(f.group(1)), "Cliloc": int(f.group(3)), "Where": f.group(4),
                     "DeepWater": f.group(5) == "true", "BaseChance": chances[f.group(6)],
                     "Tier": f.group(6).replace("Chance", ""), "MinSkill": num(f.group(7))})
    fish_raw = strip_comments(read(os.path.join(hs, "Fish.cs")))
    common = {}
    for key, arr, skill in (("Dungeon", "m_DungeonFish", 106.0), ("DeepWater", "m_DeepWaterFish", 80.0),
                            ("Shore", "m_ShoreFish", 50.0)):
        common[key] = {"MinSkill": skill, "Types": typeofs(static_array(fish_raw, arr))}
    d["SpecialFish"] = {
        "Named": rare,
        "Common": common,
        "Notes": ["Rolled before the normal catch (FishInfo.GetSpecialItem), with a fishing pole only (crabs/lobsters "
                  "are lobster-trap catches and are skipped). Named fish: in list order, the baited fish first; each "
                  "needs Fishing >= MinSkill, deep water when DeepWater, and the place in Where (a map name, a region "
                  "name, 'T2A' = the Lost Lands, 'TrammelAndFelucca' = those maps outside the Lost Lands, 'Fire Island', "
                  "'Gravewater Lake' (Malas; no roll, always matches)); it is caught when BaseChance x bait strength "
                  "> random (bait strength 1, or 2 when the pole is baited for it, 3 with enhanced bait, plus fishing "
                  "attire bonuses; BaseChance x10 when a magical fish finder shows a school). A later match overwrites "
                  "an earlier one.",
                  "Common: if no named fish, chance Fishing/121.5 to catch one random type from the first group that "
                  "applies: Dungeon (a dungeon region, skill >= 106), DeepWater (skill >= 80), Shore (not deep, not "
                  "dungeon, skill >= 50).",
                  "Lava fishing (lava hook) only rolls the named list."],
    }
    d["Requires"] = {"NotMounted": True, "NotFlying": True}
    d["Effect"]["Splash"] = {"AfterSeconds": 1.5, "Sound": 0x364, "Effect": 0x352D, "LavaSound": 0x60A,
                             "LavaEffect": 0x1A75}
    d["Notes"] += [
        "Mutations: after a successful skill roll, entries are tried in order; an entry needs Fishing base >= ReqSkill "
        "and DeepWater entries need deep water (Trammel, Felucca or Tokuno deep sea); it hits when (Fishing - MinSkill) "
        "/ (MaxSkill - MinSkill) > random, giving one of its Types at random (Nothing = the catch fails). SpecialFish "
        "Named catches are never mutated (Common ones can be).",
        "Skill gain: in deep water below 75 Fishing the catch always succeeds with no gain; outside deep water at 75+ "
        "it always succeeds with no gain; otherwise the normal ReqSkill + skill check on MinSkill..MaxSkill.",
        "TreasureMap / MessageInABottle / SpecialFishingNet catches arrive inside a sea serpent (25% deep sea serpent) "
        "spawned within 10 tiles on deep water, with message 503170. BigFish and SOS chests are placed at the feet "
        "when the pack is full.",
        "Lava (LavaTiles, only with a lava hook on the pole; ServUO matches the raw graphic id of land or static "
        "against the whole list): no normal fish; LavaMutations are tried in order (skill capped at 120, chance x1.5 on "
        "Felucca-rules maps), then the named SpecialFish list. Bank consumption = item amount (halved, rounded up, on "
        "Felucca rules). On failure the hook loses a use with chance 0.5 / (Fishing / 50).",
        "Fishing.m_LavaTiles contains 4560, 4561, 4562 between 4859 and 4864 (probably meant 4860-4862); kept as "
        "written.",
        "SOS: with an SOS in the pack whose target is within 60 tiles (Trammel/Felucca), resources are always "
        "available and every catch is shipwreck loot or the SOS chest (16 or 17 outcomes) - not exported.",
        "Quests (Collector's rainbow pearls, Underworld SomethingFishy/ScrapingtheBottom) and Charybdis (High Seas, "
        "Oracle of the Sea + Charybdis bait, 100 Fishing) take over the harvest - not exported.",
        "Hooks: each catch attempt uses one hook charge; bait loses a use when something is caught. Fishing pole uses "
        "(UsesRemaining) only count on Siege.",
        "The pole is moved into the hand (unequipping both hands) when fishing from the pack.",
    ]


def mining_extras(defs, root, texts):
    ore = next(d for d in defs if d["Name"] == "Ore")
    sand = next(d for d in defs if d["Name"] == "Sand")
    loot = strip_comments(read(os.path.join(root, "Misc", "Loot.cs")))
    gems = typeofs(static_array(loot, "m_GemTypes"))
    for r in ore["Resources"]:
        types = r["Types"]
        r["Types"] = [types[0]]
        if len(types) > 1:
            r["Stone"] = types[1]
        if len(types) > 2:
            r["Elemental"] = types[2]
    ore["ResourceSelection"] = {
        "Default": "the resource's Types[0] (its ore)",
        "Gem": {"Learned": "GemMining", "Toggle": "ToggleMiningGem", "MinSkillBase": 100.0, "Chance": 10.0,
                "Types": gems, "Message": {"Cliloc": 1112233, "Text": texts.get(1112233)}},
        "Stone": {"Learned": "StoneMining", "Toggle": "ToggleMiningStone or ToggleStoneOnly", "MinSkillBase": 100.0,
                  "Chance": 15.0, "RockHammerChance": 50.0, "Type": "the resource's Stone",
                  "StoneOnlyOtherwise": "nothing (fail)",
                  "FeluccaAmount": 3, "Message": {"Cliloc": 1044606, "Text": texts.get(1044606)}},
        "Order": "ImprovedRockHammer rule, then Gem, then Stone, then Default",
    }
    ore["Requires"] = {"NotMounted": True, "NotPolymorphed": True, "MiningToolInHand": True}
    sand["Requires"] = {"NotMounted": True, "NotPolymorphed": True, "MiningToolInHand": True,
                        "Learned": "SandMining", "MinSkillBase": 100.0}
    ore["Notes"] += [
        "Every resource's Stone (granite) and Elemental (a creature, see the Gargoyle's pickaxe) come from the third "
        "and second types of ServUO's HarvestResource; normal mining gives only the ore.",
        "Bonus resources: CrystallineBlackrock only on Ter Mur (Map). Rolled after every successful harvest; the "
        "find needs Mining base >= ReqSkill and is placed at the feet if the pack is full.",
        "High Seas niter: on a boat or in a dungeon (not the Trammel/Felucca surface; Ilshenar counts as dungeon) "
        "a harvest may uncover a NiterDeposit of size 1-5 (+1 by luck) instead, chance Mining/9999 + luck/150000 "
        "(a third less on a boat); a dungeon bank rolls only once - not exported as a rule.",
        "Void-pool HarvestMaps, Caddellite (Khaldun) and the region's resource swap (Region.GetResource) can change "
        "the result - not exported.",
        "Iron beetles mine the land tile in front of them with this definition.",
        "Mining reveals the miner when it starts.",
    ]
    sand["Notes"] += ["Sand is mined with the same tools; it is only allowed for players who learned SandMining and "
                      "have Mining base >= 100 (otherwise the tile counts as a bad target)."]
    # shard rule (SeriousFun, Custom/Gathering/OreVeins.cs): visible ore-vein nodes, mined as Ore tile 0x053B
    veins_raw = strip_comments(read(os.path.join(root, "Custom", "Gathering", "OreVeins.cs")))
    w_out = ints(re.search(r"OutdoorWeights\s*=\s*\{([^}]*)\}", veins_raw).group(1))
    w_dun = ints(re.search(r"DungeonWeights\s*=\s*\{([^}]*)\}", veins_raw).group(1))
    amount = ints(re.search(r"MaxAmount\s*=\s*Utility\.RandomMinMax\(([^)]*)\)", veins_raw).group(1))
    rmin = re.search(r"MinRespawn\s*=\s*(TimeSpan\.\w+\([^)]*\))", veins_raw).group(1)
    rmax = re.search(r"MaxRespawn\s*=\s*(TimeSpan\.\w+\([^)]*\))", veins_raw).group(1)
    names = [r["Name"] for r in ore["Resources"]][1:]
    ore["ShardOreVeins"] = {
        "Amount": amount, "RespawnSeconds": [seconds(rmin), seconds(rmax)],
        "OutdoorWeights": dict(zip(names, w_out)), "DungeonWeights": dict(zip(names, w_dun)),
        "Notes": "SeriousFun custom: a visible outcrop (graphic 0x9CB0-0x9CB3, or spire 0x9CAC for Agapite and up "
                 "40% of the time) that always yields its own metal (its vein = that resource with 100% chance, no "
                 "fallback), holds Amount ore, needs Mining >= that resource's ReqSkill, ignores the area bank, and "
                 "regrows elsewhere in its zone after RespawnSeconds. Mined like an Ore tile (gems, stone, tool "
                 "mods, human bonus all apply)."}


def lumber_extras(d, texts):
    d["Requires"] = {"ToolInPackOrHand": True}
    d["BladedKindling"] = {"ToolBases": ["BaseKnife", "BaseSword"], "BankCost": 5, "MinBankRemaining": 5,
                           "Type": "Kindling", "SkillCheck": False,
                           "Messages": {"Success": {"Cliloc": 500491}, "Hint": {"Cliloc": 500492},
                                        "PackFull": {"Cliloc": 500490}, "BadTarget": {"Cliloc": 500494}}}
    d["Notes"] += [
        "BladedKindling: using any knife or sword on a tree tile of this definition takes 5 from the tree's bank "
        "(needs >= 5 left) and gives one Kindling, no skill check (Targets/BladedItemTarget.cs).",
        "Bonus resources: CrystalShards only on Ter Mur (Map). Rolled after every successful harvest; needs "
        "Lumberjacking base >= ReqSkill; placed at the feet if the pack is full.",
        "Void-pool HarvestMaps, the Woodsman's talisman (+logs), Caddellite and the region's resource swap can "
        "change the result - not exported.",
        "Chopping reveals the lumberjack when it starts.",
    ]


RULES = {
    "Target": "The player targets a land tile or static; its id picks the first definition (in order) whose Tiles "
              "contain it. Static ids are matched after masking with 0x3FFF, so a graphic >= 0x4000 counts as "
              "graphic & 0x3FFF.",
    "Range": "The target must be within MaxRange tiles (Chebyshev) when starting and on every effect tick.",
    "Bank": "The world is cut into Width x Height cells per map; each cell holds a random MinTotal..MaxTotal and one "
            "vein. With RandomizeVeins the vein is picked at random (by Veins' Chance) when the cell is created and "
            "again on each refill; without it, it is fixed per cell. The first harvest from a full cell starts a "
            "MinRespawnSeconds..MaxRespawnSeconds timer after which the cell is full again. A harvest needs "
            "ConsumedPerHarvest left and takes ConsumedPerHarvest (whatever the yield).",
    "Timing": "Effect: Count (random from Counts) ticks, the first at once and then every DelaySeconds; each tick "
              "plays an action from Actions and, SoundDelaySeconds later, a sound from Sounds; the last tick's "
              "sound also resolves the harvest. DurationSeconds is therefore (Count-1) x Delay + SoundDelay.",
    "Vein": "Resource = the vein's Fallback when FallbackChance > random, or when the player's skill is below the "
            "Primary's ReqSkill or MinSkill and there is a Fallback; otherwise the Primary.",
    "Skill": "Success needs skill >= ReqSkill and a skill check on MinSkill..MaxSkill (ServUO: chance (skill - Min) / "
             "(Max - Min), gains possible while skill < cap and the chance is between 0 and 1). On failure the "
             "definition's Fail message.",
    "Yield": "One random type of the resource's Types (system-specific selection noted per definition). Stackable "
             "yield amount: on Felucca ConsumedPerFeluccaHarvest (granite 3) when the bank has that many, else "
             "ConsumedPerHarvest; with HumanBonus a human gets ceil(amount x 1.1) 10% of the time (if the bank has "
             "it). Felucca is treated as a special map: everywhere else uses ConsumedPerHarvest.",
    "Bonus": "After each successful harvest one roll on BonusResources (Chance in %, the null-Type entry is "
             "'nothing'); a find needs skill base >= ReqSkill and the Map when one is given.",
    "Tools": "Tool uses: UsesPerHarvest is taken on each successful skill roll and the tool is destroyed at 0 "
             "(ToolBroke message). DurabilityLossChance is the shard's rule for pickaxes and hatchets: that % chance "
             "per successful roll to lose 1 hit point, then 1 max hit point, and break at 1.",
    "ShardToolMods": "SeriousFun (Custom/Gathering/GatherMods.cs, not exported): pickaxes, shovels and hatchets can "
                     "carry modifiers: Yield +5..50%, NodeSaving 5..50% chance not to deplete the bank, SkillBonus, "
                     "CarryWeight, ResourceWeight; artifact ones Weightless, EndlessNode, Motherlode (15% chance of "
                     "also getting the next resource's first type if skill allows), Swift (half DelaySeconds), "
                     "Bountiful (second bonus roll when the first gives nothing).",
}


def main():
    root, items_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
    harvest_dir = os.path.join(root, "Services", "Harvest")
    items = {i["Type"] for i in json.load(open(items_path, encoding="utf-8"))["Items"]}
    creatures_path = os.path.join(os.path.dirname(items_path), "Creatures.json")
    creatures = ({c["Type"] for c in json.load(open(creatures_path, encoding="utf-8"))["Creatures"]}
                 if os.path.exists(creatures_path) else set())
    gm = read(os.path.join(root, "Custom", "Gathering", "GatherMods.cs"))
    wear = int(re.search(r"DurabilityLossPercent\s*=\s*(\d+)", gm).group(1))
    classes = class_index(root)
    unread = []
    systems = {}
    for name in SYSTEMS:
        raw = read(os.path.join(harvest_dir, name + ".cs"))
        text, texts, parsed = parse_system(name, raw, unread)
        defs = [build_definition(name, var, props, text, texts, unread) for var, props in parsed]
        tools = tools_for(classes, name, wear)
        for t, effect in TOOL_EFFECTS[name].items():
            if t not in tools:
                raise SystemExit(f"hand table names tool {t}, not a {name} tool")
            tools[t]["Effect"] = effect
        for d in defs:
            d["Tools"] = sorted(tools)
        # overlapping tiles: the earlier definition wins
        for i, d in enumerate(defs):
            for e in defs[:i]:
                for kind in ("Land", "Static"):
                    shared = sorted(set(d["Tiles"][kind]) & set(e["Tiles"][kind]))
                    if shared:
                        d["Notes"].append(f"{kind} tiles {shared} are also {e['Name']} tiles; {e['Name']} comes first, "
                                          f"so targeting them harvests {e['Name']}.")
        if name == "Mining":
            mining_extras(defs, root, texts)
        elif name == "Lumberjacking":
            lumber_extras(defs[0], texts)
            defs[0]["Boards"] = HARVESTERS_AXE_BOARDS
        else:
            fishing_extras(defs[0], text, root, texts)
        sysmsg = {k: {"Cliloc": n, "Text": texts[n]} if n in texts else {"Cliloc": n}
                  for k, n in SYSTEM_MESSAGES[name].items()}
        notes = []
        if name == "Lumberjacking":
            notes.append("Tools are every axe (BaseAxe) and polearm (BasePoleArm) except WarAxe, Scythe and "
                         "GargishScythe (their HarvestSystem is null) and the pickaxes (Mining).")
        if name == "Mining":
            notes.append("Pickaxes, shovels, the sturdy and gargoyle's pickaxes have unlimited uses on this shard; "
                         "the sturdy shovel and rock hammers still count uses.")
        for d in defs:
            del d["_raw"]
            # bring the keys into the documented order
            order = ["Name", "Skill", "Tools", "TileIds", "Tiles", "LavaTiles", "Bank", "MaxRange",
                     "ConsumedPerHarvest", "ConsumedPerFeluccaHarvest", "PlaceAtFeetIfFull", "HumanBonus", "Requires",
                     "Effect", "Resources", "Veins", "BonusResources", "ResourceSelection", "Messages"]
            rest = [k for k in d if k not in order and k != "Notes"]
            reordered = {k: d[k] for k in order + rest + ["Notes"] if k in d}
            d.clear()
            d.update(reordered)
        systems[name] = {"Skill": defs[0]["Skill"], "Tools": tools, "SystemMessages": sysmsg, "Notes": notes,
                         "Definitions": defs}

    # every Type we emit
    used, used_creatures = set(), set()
    for s in systems.values():
        used.update(s["Tools"])
        for d in s["Definitions"]:
            for r in d["Resources"]:
                used.update(r["Types"])
                if r.get("Stone"):
                    used.add(r["Stone"])
                if r.get("Elemental"):
                    used_creatures.add(r["Elemental"])
            used.update(b["Type"] for b in d.get("BonusResources", []) if b["Type"])
            for m in d.get("Mutations", []) + d.get("LavaMutations", []):
                used.update(m["Types"])
            sf = d.get("SpecialFish")
            if sf:
                used.update(f["Type"] for f in sf["Named"])
                for g in sf["Common"].values():
                    used.update(g["Types"])
            if "ResourceSelection" in d:
                used.update(d["ResourceSelection"]["Gem"]["Types"])
            if "Boards" in d:
                used.update(d["Boards"].values())
            if "BladedKindling" in d:
                used.add(d["BladedKindling"]["Type"])

    result = {"Source": "ServUO scripts: Services/Harvest (Mining, Lumberjacking, Fishing), tool classes, High Seas "
                        "fish tables, SeriousFun Custom/Gathering. Core expansions on, Siege off. Tile ids are "
                        "expanded (no ranges); seconds for times; chances in percent unless named BaseChance "
                        "(a 0..1 probability).",
              "Rules": RULES, "Systems": systems}
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(result, f, indent=1)

    for name, s in systems.items():
        print(f"{name}: {len(s['Tools'])} tools ({', '.join(sorted(s['Tools']))})")
        for d in s["Definitions"]:
            e = d["Effect"]
            print(f"  {d['Name']}: {len(d['Tiles']['Land'])} land + {len(d['Tiles']['Static'])} static tiles"
                  + (f", lava {len(d['LavaTiles']['Land'])}+{len(d['LavaTiles']['Static'])}" if "LavaTiles" in d else "")
                  + f"; bank {d['Bank']['Width']}x{d['Bank']['Height']} {d['Bank']['MinTotal']}-{d['Bank']['MaxTotal']}"
                  f" respawn {d['Bank']['MinRespawnSeconds']:g}-{d['Bank']['MaxRespawnSeconds']:g}s; range {d['MaxRange']};"
                  f" {d['ConsumedPerHarvest']}/{d['ConsumedPerFeluccaHarvest']} per harvest;"
                  f" effect counts {e['Counts']} delay {e['DelaySeconds']:g}s sound {e['SoundDelaySeconds']:g}s"
                  f" -> {e['DurationSeconds'][0]:g}-{e['DurationSeconds'][1]:g}s")
            print("    resources: " + ", ".join(f"{r['Name']} {r['ReqSkill']:g} ({r['MinSkill']:g}-{r['MaxSkill']:g})"
                                              for r in d["Resources"]))
            print("    veins: " + ", ".join(f"{v['Primary']} {v['Chance']:g}%"
                                          + (f" (fallback {v['Fallback']} {v['FallbackChance']:g})" if v["Fallback"] else "")
                                          for v in d["Veins"]))
            if d.get("BonusResources"):
                print("    bonus: " + ", ".join(f"{b['Type'] or 'nothing'} {b['Chance']:g}%"
                                              + (f" @{b['ReqSkill']:g}" if b["Type"] else "")
                                              + (f" [{b['Map']}]" if b.get("Map") else "")
                                              for b in d["BonusResources"]))
            if d.get("Mutations"):
                print(f"    mutations: {len(d['Mutations'])}, lava mutations: {len(d['LavaMutations'])}, "
                      f"named special fish: {len(d['SpecialFish']['Named'])}, common: "
                      + ", ".join(f"{k} {len(v['Types'])}" for k, v in d["SpecialFish"]["Common"].items()))
    print(f"-> {out}")
    for u in unread:
        print("unread: " + u)
    missing = sorted(t for t in used if t not in items)
    if missing:
        print("item types not in Items.json: " + ", ".join(missing))
    if creatures:
        missing_c = sorted(t for t in used_creatures if t not in creatures)
        if missing_c:
            print("creature types not in Creatures.json: " + ", ".join(missing_c))


if __name__ == "__main__":
    main()
