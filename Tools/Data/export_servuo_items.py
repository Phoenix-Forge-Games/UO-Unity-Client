"""Extracts item definitions and loot packs (data only) from ServUO's C# scripts into Content/Items.json and
Content/LootPacks.json.

  python -I export_servuo_items.py <ServUO Scripts folder> <Items.json out> <LootPacks.json out> [tiledata.mul]

Items: every concrete class that derives (directly or through other items) from Item. Its constructor is followed
the way C# runs it: the [Constructable] constructor with the fewest parameters (else the parameterless one), then
its this(...)/base(...) calls down through each parent constructor with the arguments bound, down to Item(itemID).
From those statements come Graphic (ItemID, or the choices of a Utility.RandomList), Name, Hue, Weight, Stackable,
Amount, Layer, LootType and the attribute assignments (Attributes.X = n...). Property overrides (AosMinDamage,
MlSpeed, DefSkill, ArmorBase, Base*Resistance, MaterialType, DefaultMaxWeight, LabelNumber...) are inherited: a
subclass starts from a deep copy of its parent's values and overrides what it re-declares; "Core.X ? a : b"
picks a (the newest era). Weapons and armour take their Layer from tiledata (ServUO does: Layer =
ItemData.Quality), and an item with no Weight of its own gets tiledata's weight, as Item.DefaultWeight does;
tiledata.mul defaults to <Scripts>/../ClientFiles/tiledata.mul. Container gumps come from <Scripts>/../Data/
containers.cfg. Constructor lines of the item's own class that aren't recognised are listed under "Unparsed".
Multis, addons and their components, hair/beards, invisible system objects (Visible = false), and anything in a
Deprecated/Internal/Test folder, named Internal*/Test*, or marked [Obsolete] are left out (counted in the summary).

Loot packs: Scripts/Misc/LootPack.cs. Every LootPack field with its entries (chance in percent, item group, dice
quantity, max properties, intensity range), every LootPackItem[] group with its types and weights, the public
accessors (Meager -> Core.SE ? SeMeager : Core.AOS ? AosMeager : OldMeager) as Aliases, and the Type[] tables of
Scripts/Misc/Loot.cs that the "random X" group markers (BaseWeapon, BaseArmor, Amber...) draw from.

Nothing of ServUO's code is reused: the game re-implements the rules (ServerDesign.md, decision 4); these are the
numbers.
"""
import copy
import json
import os
import re
import sys

CLASS = re.compile(r"\b(?:public|internal)\s+((?:sealed\s+|abstract\s+|partial\s+|static\s+)*)class\s+(\w+)\s*(<[^>]*>)?\s*(?::\s*([\w.]+))?")
PROP = re.compile(r"(?:public|protected|internal)\s+(?:override|virtual)\s+(?:sealed\s+)?([\w.<>\[\]]+)\s+(\w+)\s*"
                  r"(?:=>\s*([^;{]+);|\{\s*get\s*(?:=>\s*([^;]+);|\{\s*return\s+([^;]+);\s*\}))")
PROP_ANY = re.compile(r"(?:public|protected|internal)\s+(?:override|virtual)\s+(?:sealed\s+)?[\w.<>\[\]]+\s+(\w+)\s*(?:\{|=>)")
NUM = r"-?\d+(?:\.\d+)?"
LAYERS_FALLBACK = ["Invalid", "OneHanded", "TwoHanded", "Shoes", "Pants", "Shirt", "Helm", "Gloves", "Ring", "Talisman",
                   "Neck", "Hair", "Waist", "InnerTorso", "Bracelet", "Face", "FacialHair", "MiddleTorso", "Earrings",
                   "Arms", "Cloak", "Backpack", "OuterTorso", "OuterLegs", "InnerLegs", "Mount", "ShopBuy",
                   "ShopResale", "ShopSell", "Bank"]
# Base class -> category, checked nearest-first along the inheritance chain.
CATEGORY_BASES = [
    ("BaseShield", "Shield"), ("BaseArmor", "Armor"), ("BaseInstrument", "Instrument"), ("BaseWeapon", "Weapon"),
    ("BaseClothing", "Clothing"), ("BaseJewel", "Jewelry"), ("BaseReagent", "Reagent"), ("BasePotion", "Potion"),
    ("SpellScroll", "Scroll"), ("Gold", "Gold"), ("BaseLight", "Light"), ("BaseTool", "Tool"),
    ("BaseHarvestTool", "Tool"), ("Food", "Food"), ("CookableFood", "Food"), ("BaseBeverage", "Food"),
    ("BaseIngot", "Resource"), ("BaseOre", "Resource"), ("BaseLog", "Resource"), ("BaseBoard", "Resource"),
    ("BaseLeather", "Resource"), ("BaseHides", "Resource"), ("BaseGranite", "Resource"), ("BaseScales", "Resource"),
    ("BaseClothMaterial", "Resource"), ("Container", "Container"),
]
CATEGORY_FOLDERS = [("/Resource/", "Resource"), ("/Tools/", "Tool"), ("/Containers/", "Container"), ("/Food", "Food"),
                    ("/Light/", "Light"), ("/Instruments/", "Instrument"), ("/Jewelry/", "Jewelry"),
                    ("/Clothing/", "Clothing"), ("/Armor/", "Armor"), ("/Weapons/", "Weapon")]
EXCLUDE_BASES = {"BaseMulti": "multi", "AddonComponent": "addon", "AddonContainerComponent": "addon",
                 "BaseAddon": "addon", "BaseAddonContainer": "addon", "Hair": "hair/beard", "Beard": "hair/beard"}
EXCLUDE_FOLDERS = ("deprecated", "internal", "test", "tests")
# What LootPackItem.Construct does with the marker types (newest era first; tables are Loot.cs Type[] arrays).
SPECIAL = {
    "BaseWeapon": {"Picks": "random weapon", "Tables": {"SA": ["SAWeaponTypes", "AosWeaponTypes", "WeaponTypes"], "ML": ["MLWeaponTypes", "AosWeaponTypes", "WeaponTypes"], "SE": ["SEWeaponTypes", "AosWeaponTypes", "WeaponTypes"], "AOS": ["AosWeaponTypes", "WeaponTypes"], "Old": ["WeaponTypes"]}},
    "BaseRanged": {"Picks": "random ranged weapon", "Tables": {"SA": ["SARangedWeaponTypes", "AosRangedWeaponTypes", "RangedWeaponTypes"], "ML": ["MLRangedWeaponTypes", "AosRangedWeaponTypes", "RangedWeaponTypes"], "SE": ["SERangedWeaponTypes", "AosRangedWeaponTypes", "RangedWeaponTypes"], "AOS": ["AosRangedWeaponTypes", "RangedWeaponTypes"], "Old": ["RangedWeaponTypes"]}},
    "BaseArmor": {"Picks": "random armour or hat", "Tables": {"SA": ["SAArmorTypes", "ArmorTypes", "AosHatTypes", "HatTypes"], "ML": ["MLArmorTypes", "ArmorTypes", "AosHatTypes", "HatTypes"], "SE": ["SEArmorTypes", "ArmorTypes", "SEHatTypes", "AosHatTypes", "HatTypes"], "AOS": ["ArmorTypes", "AosHatTypes", "HatTypes"], "Old": ["ArmorTypes", "HatTypes"]}},
    "BaseShield": {"Picks": "random shield", "Tables": {"SA": ["AosShieldTypes", "ShieldTypes", "SAShieldTypes"], "AOS": ["AosShieldTypes", "ShieldTypes"], "Old": ["ShieldTypes"]}},
    "BaseJewel": {"Picks": "random jewellery (AOS+); before AOS a random armour, shield or weapon", "Tables": {"SA": ["SAJewelryTypes", "JewelryTypes"], "AOS": ["JewelryTypes"], "Old": ["ArmorTypes", "ShieldTypes", "WeaponTypes", "RangedWeaponTypes"]}},
    "BaseInstrument": {"Picks": "random instrument; kept only if it rolls a slayer", "Tables": {"SE": ["InstrumentTypes", "SEInstrumentTypes"], "Old": ["InstrumentTypes"]}},
    "Amber": {"Picks": "random gem", "Tables": {"Any": ["GemTypes"]}},
    "ClumsyScroll": {"Picks": "random scroll, circles 1-3 (magery weighted 4, plus necromancy/mysticism)", "Tables": {"Any": ["RegularScrollTypes", "NecromancyScrollTypes", "MysticismScrollTypes"]}},
    "ArchCureScroll": {"Picks": "random scroll, circles 4-7", "Tables": {"Any": ["RegularScrollTypes", "NecromancyScrollTypes", "MysticismScrollTypes"]}},
    "SummonAirElementalScroll": {"Picks": "random scroll, circle 8", "Tables": {"Any": ["RegularScrollTypes", "NecromancyScrollTypes", "MysticismScrollTypes"]}},
}


def strip_comments(text):
    """Removes // and /* */ comments and preprocessor lines, leaving string literals alone."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
        elif c == "/" and text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            out.append(" ")
        elif c == '"' or c == "'":
            verbatim = c == '"' and i > 0 and text[i - 1] == "@"
            j = i + 1
            while j < n and text[j] != c and text[j] != "\n":
                if text[j] == "\\" and not verbatim:
                    j += 1
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
        else:
            out.append(c)
            i += 1
    return re.sub(r"(?m)^\s*#\w+[^\n]*", "", "".join(out))


def matching(text, start, open_="{", close="}"):
    """Index just past the bracket block opening at text[start]."""
    depth, i, n = 0, start, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            i += 1
            while i < n and text[i] != '"':
                i += 2 if text[i] == "\\" else 1
        elif c == open_:
            depth += 1
        elif c == close:
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return n


def split_top(s, sep=","):
    """Splits on sep outside brackets and strings."""
    parts, depth, cur, i = [], 0, [], 0
    while i < len(s):
        c = s[i]
        if c == '"':
            j = i + 1
            while j < len(s) and s[j] != '"':
                j += 2 if s[j] == "\\" else 1
            cur.append(s[i:j + 1])
            i = j + 1
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        if c == sep and depth == 0:
            parts.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
        i += 1
    if "".join(cur).strip():
        parts.append("".join(cur).strip())
    return parts


def statements(code):
    """Top-level statements of a block; a block statement (if/switch/...) stays whole."""
    out, start, paren, brace, i, n = [], 0, 0, 0, 0, len(code)
    while i < n:
        c = code[i]
        if c == '"':
            i += 1
            while i < n and code[i] != '"':
                i += 2 if code[i] == "\\" else 1
        elif c in "([":
            paren += 1
        elif c in ")]":
            paren -= 1
        elif c == "{":
            brace += 1
        elif c == "}":
            brace -= 1
            if brace == 0 and paren == 0:
                nxt = code[i + 1:].lstrip()
                if not nxt.startswith(("else", ";", ")", ",")):
                    out.append(code[start:i + 1])
                    start = i + 1
        elif c == ";" and paren == 0 and brace == 0:
            out.append(code[start:i])
            start = i + 1
        i += 1
    out.append(code[start:])
    return [" ".join(s.split()) for s in out if s.strip()]


def literal(v):
    """(True, value) for a constant expression, else (False, None)."""
    v = v.strip()
    while v.startswith("(") and matching(v, 0, "(", ")") == len(v):
        v = v[1:-1].strip()
    v = re.sub(r"^\((?:int|double|float|byte|short|long|uint|ushort)\)\s*", "", v)
    t = re.match(r"^Core\.\w+\s*\?\s*(.+?)\s*:\s*(.+)$", v)
    if t:
        return literal(t.group(1))
    if re.fullmatch(r"-?\d+", v):
        return True, int(v)
    if re.fullmatch(r"-?0x[0-9A-Fa-f]+", v):
        return True, int(v, 16)
    if re.fullmatch(NUM + r"[fFdDmM]?", v) or re.fullmatch(r"-?\d+[fFdDmM]", v):
        return True, float(v.rstrip("fFdDmM"))
    if v in ("true", "false"):
        return True, v == "true"
    if re.fullmatch(r"Core\.[A-Z]+", v):
        return True, True  # an era switch: the newest era is assumed
    if re.fullmatch(r'"(?:[^"\\]|\\.)*"', v):
        return True, v[1:-1].replace('\\"', '"').replace("\\\\", "\\")
    if v in ("null", "string.Empty", "String.Empty"):
        return True, None if v == "null" else ""
    t = re.fullmatch(r"typeof\(\s*([\w.]+)\s*\)", v)
    if t:
        return True, t.group(1).split(".")[-1]
    if re.fullmatch(r"[A-Z]\w*(?:\.[A-Z]\w*)+", v) and not v.startswith(("Utility.", "Core.", "ItemData.")):
        return True, v.split(".")[-1]  # an enum member (SkillName.Swords -> "Swords")
    return False, None


def graphics(v):
    """Graphic choices for an ItemID expression, or None."""
    ok, x = literal(v)
    if ok and isinstance(x, int):
        return [x]
    v = v.strip()
    m = re.fullmatch(r"Utility\.RandomList\((.*)\)", v)
    if m:
        vals = [literal(p) for p in split_top(m.group(1))]
        if vals and all(o and isinstance(x, int) for o, x in vals):
            return [x for _, x in vals]
    m = re.fullmatch(r"\(?\s*Utility\.Random\w*\(\)[^?]*?\)?\s*\?\s*([^:?]+?)\s*:\s*([^:?]+)", v)
    if m:
        a, b = literal(m.group(1)), literal(m.group(2))
        if a[0] and b[0] and isinstance(a[1], int) and isinstance(b[1], int):
            return [a[1], b[1]]
    m = re.fullmatch(r"Utility\.(Random|RandomMinMax)\(\s*(\S+?)\s*,\s*(\S+?)\s*\)", v)
    if m:
        a, b = literal(m.group(2)), literal(m.group(3))
        if a[0] and b[0] and isinstance(a[1], int) and isinstance(b[1], int):
            hi = a[1] + b[1] - 1 if m.group(1) == "Random" else b[1]
            if 0 <= hi - a[1] < 64:
                return list(range(a[1], hi + 1))
    return None


def constructors(body, name):
    """[(params, chain kind, chain args, code, constructable)] for the class body."""
    out = []
    for m in re.finditer(r"(?:public|protected|private|internal)\s+" + name + r"\s*\(", body):
        p_end = matching(body, m.end() - 1, "(", ")")
        params = []
        for p in split_top(body[m.end():p_end - 1]):
            p = re.sub(r"^(?:params|ref|out|this)\s+", "", p)
            d = None
            if "=" in p:
                p, d = [x.strip() for x in p.split("=", 1)]
            bits = p.split()
            if len(bits) >= 2:
                params.append((bits[-2], bits[-1], d))
        rest = body[p_end:]
        ch = re.match(r"\s*:\s*(base|this)\s*\(", rest)
        kind, args, after = None, None, p_end
        if ch:
            a_end = matching(rest, ch.end() - 1, "(", ")")
            kind, args, after = ch.group(1), rest[ch.end():a_end - 1], p_end + a_end
        brace = re.match(r"\s*\{", body[after:])
        if not brace:
            continue
        b_start = after + brace.end() - 1
        code = body[b_start + 1:matching(body, b_start) - 1]
        prefix = body[max(0, m.start() - 300):m.start()]
        prefix = re.split(r"[;{}]", prefix)[-1]
        out.append((params, kind, split_top(args) if args else [], code, "Constructable" in prefix))
    return out


def read_tiledata(path):
    """item id -> (flags, weight, quality), or {} when unavailable."""
    if not path or not os.path.isfile(path):
        return {}
    data = open(path, "rb").read()
    if (len(data) - 512 * 964) % 1316 == 0:
        land, group, entry, fl = 512 * 964, 1316, 41, 8
    elif (len(data) - 512 * 836) % 1188 == 0:
        land, group, entry, fl = 512 * 836, 1188, 37, 4
    else:
        return {}
    count = (len(data) - land) // group * 32

    def get(i):
        if not 0 <= i < count:
            return None
        off = land + (i // 32) * group + 4 + (i % 32) * entry
        return int.from_bytes(data[off:off + fl], "little"), data[off + fl], data[off + fl + 1]
    return get


def main():
    root, out_items, out_loot = sys.argv[1], sys.argv[2], sys.argv[3]
    servuo = os.path.dirname(os.path.abspath(root))
    tile = read_tiledata(sys.argv[4] if len(sys.argv) > 4 else os.path.join(servuo, "ClientFiles", "tiledata.mul"))

    layers = LAYERS_FALLBACK
    item_cs = os.path.join(servuo, "Server", "Item.cs")
    if os.path.isfile(item_cs):
        t = strip_comments(open(item_cs, encoding="utf-8", errors="replace").read())
        m = re.search(r"enum\s+Layer\b[^{]*\{", t)
        if m:
            names = {}
            for n, v in re.findall(r"(\w+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)", t[m.end():matching(t, m.end() - 1)]):
                if n not in ("FirstValid", "LastValid", "LastUserValid"):
                    names.setdefault(int(v, 0), n)
            if names:
                layers = [names.get(i) or "" for i in range(max(names) + 1)]

    gumps = {}
    cfg = os.path.join(servuo, "Data", "containers.cfg")
    default_gump = None
    if os.path.isfile(cfg):
        for line in open(cfg, encoding="utf-8", errors="replace"):
            parts = line.strip().split("\t")
            if not parts[0] or parts[0].startswith("#"):
                continue
            g = int(parts[0], 0)
            if len(parts) < 4:
                default_gump = g
            else:
                for i in parts[3].split(","):
                    if i.strip():
                        gumps[int(i, 0)] = g

    classes, others = {}, {}  # name -> dict (first definition); name -> [later definitions of the same name]
    for top, from_scripts in ((root, True), (os.path.join(servuo, "Server"), False)):
        for folder, _, files in os.walk(top):
            for f in files:
                if not f.endswith(".cs"):
                    continue
                path = os.path.join(folder, f)
                text = strip_comments(open(path, encoding="utf-8", errors="replace").read())
                for m in CLASS.finditer(text):
                    name, base = m.group(2), (m.group(4) or "").split(".")[-1]
                    if m.group(3) or name == "Item" and not from_scripts:
                        continue
                    body_start = text.find("{", m.end())
                    body = text[body_start:matching(text, body_start)] if body_start >= 0 else ""
                    attrs = re.split(r"[;}]", text[max(0, m.start() - 400):m.start()])[-1]
                    rec = {"base": base, "file": os.path.relpath(path, root).replace("\\", "/"),
                           "scripts": from_scripts, "abstract": "abstract" in m.group(1),
                           "obsolete": "Obsolete" in attrs, "body": body}
                    if name in classes:
                        if from_scripts:
                            others.setdefault(name, []).append(rec)
                    else:
                        classes[name] = rec

    # Two scripts classes with one name (different namespaces): keep the item one when only one of them is an item.
    item_collisions = []
    for name, alts in others.items():
        def reaches_item(rec):
            seen, b = {name}, rec["base"]
            while b in classes and b not in seen:
                seen.add(b)
                b = classes[b]["base"]
            return b == "Item"
        cands = [classes[name]] + alts
        hits = [r for r in cands if reaches_item(r)]
        if hits:
            classes[name] = hits[0]
        if len(hits) > 1:
            item_collisions.append(name + " (" + ", ".join(r["file"] for r in hits) + ")")

    def chain(name):
        seen = []
        while name in classes and name not in seen:
            seen.append(name)
            name = classes[name]["base"]
        seen.append(name)
        return seen

    def is_item(name):
        return "Item" in chain(name)[1:]

    ctor_cache, prop_cache = {}, {}

    def ctors(name):
        if name not in ctor_cache:
            c = classes[name]
            # drop nested class bodies so their members don't count as ours
            ctor_cache[name] = constructors(c["body"], name)
        return ctor_cache[name]

    def props(name):
        """Inherited property values (raw expressions): deep copy of the parent's, then our own overrides."""
        if name in prop_cache:
            return prop_cache[name]
        c = classes.get(name)
        if not c:
            return {}
        result = copy.deepcopy(props(c["base"])) if c["base"] in classes and c["base"] != name else {}
        prop_cache[name] = result  # cycle guard
        for m in PROP_ANY.finditer(c["body"]):
            result[m.group(1)] = ""  # re-declared here; unknown unless it's a plain return below
        for m in PROP.finditer(c["body"]):
            result[m.group(2)] = (m.group(3) or m.group(4) or m.group(5) or "").strip()
        return result

    def pick(cands, args):
        best, score_best = None, None
        for c in cands:
            params = c[0]
            if any(t == "Serial" for t, _, _ in params) and not (args and args[0] == "serial"):
                continue
            req = sum(1 for _, _, d in params if d is None)
            if not req <= len(args) <= len(params):
                continue
            score = 0
            for (t, _, _), a in zip(params, args):
                lit_ok, val = literal(a)
                if a.startswith(t + "."):
                    score += 2
                elif t in ("int", "double", "float", "short", "byte") and lit_ok and isinstance(val, (int, float)) and not isinstance(val, bool):
                    score += 1
                elif t == "string" and a.startswith('"'):
                    score += 2
                elif t == "bool" and a in ("true", "false"):
                    score += 1
                elif lit_ok and isinstance(val, (int, float)) and t not in ("int", "double", "float", "short", "byte", "object"):
                    score -= 3
            if score_best is None or score > score_best:
                best, score_best = c, score
        return best

    def static_choices(name, arg):
        """A call to a parameterless static int method of the class that only returns constants
        (e.g. BaseOre.RandomSize) becomes Utility.RandomList(those constants)."""
        m = re.fullmatch(r"(\w+)\(\)", arg.strip())
        if not m:
            return arg
        body = classes[name]["body"]
        d = re.search(r"static\s+int\s+" + m.group(1) + r"\s*\(\s*\)\s*\{", body)
        if not d:
            return arg
        code = body[d.end() - 1:matching(body, d.end() - 1)]
        rets = re.findall(r"return\s+([^;]+);", code)
        if rets and all(re.fullmatch(r"0x[0-9A-Fa-f]+|\d+", r.strip()) for r in rets):
            return "Utility.RandomList(" + ", ".join(dict.fromkeys(r.strip() for r in rets)) + ")"
        return arg

    def run(name, ctor, args, owner, out, depth=0):
        """Appends (statement, is_own) for the constructor chain, base calls first."""
        if depth > 25:
            return
        params, kind, cargs, code, _ = ctor
        binding = {}
        for i, (_, pname, d) in enumerate(params):
            if i < len(args):
                binding[pname] = args[i]
            elif d is not None:
                binding[pname] = d

        def subst(s):
            if not binding:
                return s
            pat = r"(?<![\w.])(" + "|".join(re.escape(k) for k in binding) + r")\b(?!\s*=[^=])"
            return re.sub(pat, lambda m: binding[m.group(1)], s)
        target = name if kind == "this" else classes[name]["base"]
        nargs = [static_choices(name, subst(a)) for a in cargs]
        if target == "Item" or target not in classes:
            if target == "Item" and nargs:
                out.append(("ItemID = " + nargs[0], False))
        else:
            nxt = pick(ctors(target), nargs)
            if nxt:
                run(target, nxt, nargs, owner, out, depth + 1)
            elif kind:
                out.append(("<no matching %s(%s) in %s>" % (kind, ", ".join(nargs), target), name == owner))
        for s in statements(code):
            out.append((subst(s), name == owner))

    excluded = {}
    records = []
    for name in sorted(classes):
        c = classes[name]
        if not c["scripts"] or not is_item(name):
            continue
        ch = chain(name)
        reason = None
        if c["abstract"]:
            reason = "abstract"
        else:
            for b in ch:
                if b in EXCLUDE_BASES:
                    reason = EXCLUDE_BASES[b]
                    break
        folders = [p.lower() for p in c["file"].split("/")[:-1]]
        if not reason and (any(p in EXCLUDE_FOLDERS for p in folders) or name.startswith(("Internal", "Test"))
                           or name.endswith("Test") or c["obsolete"]):
            reason = "internal/test/deprecated"
        if reason:
            excluded[reason] = excluded.get(reason, 0) + 1
            continue

        rec = {"Type": name, "Base": c["base"], "File": c["file"]}
        cs = ctors(name)
        constructable = [k for k in cs if k[4]]
        plain = [k for k in cs if not any(t == "Serial" for t, _, _ in k[0])]
        chosen = min(constructable or plain, key=lambda k: sum(1 for _, _, d in k[0] if d is None)) if (constructable or plain) else None
        stmts = []
        if chosen:
            run(name, chosen, [], name, stmts)
        elif cs:
            pass
        else:  # no constructor written: the implicit one calls base()
            run(name, ([], None, [], "", False), [], name, stmts)
        if not constructable:
            rec["Constructable"] = False

        unparsed, attributes, extra = [], {}, {}
        layer_from_tile = False
        for s, own in stmts:
            s = re.sub(r"^(?:this|base)\.", "", s)
            a = re.match(r"^([\w.]+)\s*(=|\+=|-=|\|=)\s*(.+)$", s)
            if a and a.group(1) == "ItemID" and a.group(2) == "+=" and "Graphic" in rec:
                r = re.fullmatch(r"Utility\.Random\(\s*(\d+)\s*\)", a.group(3).strip())
                if r and int(r.group(1)) <= 64:
                    rec["Graphics"] = [rec["Graphic"] + k for k in range(int(r.group(1)))]
                    continue
            if not a or a.group(2) != "=" or s.startswith(("if ", "if(", "switch", "for", "while", "else", "var ", "int ")):
                if own:
                    unparsed.append(s[:160])
                continue
            key, value = a.group(1), a.group(3).strip()
            key = re.sub(r"^(?:this|base)\.", "", key)
            ok, val = literal(value)
            if key == "ItemID":
                g = graphics(value)
                if g:
                    rec["Graphic"] = g[0]
                    if len(g) > 1:
                        rec["Graphics"] = g
                    else:
                        rec.pop("Graphics", None)
                elif own:
                    unparsed.append(s[:160])
            elif key == "Layer":
                if "ItemData.Quality" in value:
                    layer_from_tile = True
                    rec.pop("Layer", None)
                elif ok and isinstance(val, str):
                    rec["Layer"], layer_from_tile = val, False
                elif ok and isinstance(val, int) and 0 <= val < len(layers):
                    rec["Layer"], layer_from_tile = layers[val], False
                elif own:
                    unparsed.append(s[:160])
            elif key == "Hue":
                if ok and isinstance(val, int):
                    rec["Hue"] = val
                    rec.pop("HueExpr", None)
                elif own:
                    rec["HueExpr"] = value[:80]
                else:
                    rec.pop("Hue", None)
            elif key in ("Weight", "Amount", "Stackable", "Name", "LootType", "Movable", "Visible", "Light"):
                types = {"Weight": (int, float), "Amount": int, "Stackable": bool, "Name": str, "LootType": str,
                         "Movable": bool, "Visible": bool, "Light": str}
                if ok and isinstance(val, types[key]) and not (key != "Stackable" and key not in ("Movable", "Visible") and isinstance(val, bool)):
                    rec[key] = float(val) if key == "Weight" else val
                elif own:
                    unparsed.append(s[:160])
                else:
                    rec.pop(key, None)
            elif "." in key and not key.startswith("m_"):
                group, prop = key.rsplit(".", 1)
                if ok and own:
                    attributes.setdefault(group, {})[prop] = val
                elif own:
                    unparsed.append(s[:160])
            elif own:
                if ok:
                    extra[key[2:] if key.startswith("m_") else key] = val
                else:
                    unparsed.append(s[:160])

        # property overrides (inherited)
        p = props(name)

        def pv(key):
            if key not in p:
                return None
            ok, val = literal(p[key])
            return val if ok else None

        if "Name" not in rec and isinstance(pv("DefaultName"), str) and pv("DefaultName"):
            rec["Name"] = pv("DefaultName")
        if isinstance(pv("LabelNumber"), int) and not isinstance(pv("LabelNumber"), bool):
            rec["Cliloc"] = pv("LabelNumber")
        if isinstance(pv("DefaultWeight"), (int, float)) and "Weight" not in rec:
            rec["Weight"] = float(pv("DefaultWeight"))
        info = tile(rec["Graphic"]) if tile and "Graphic" in rec else None
        if layer_from_tile and info and 0 < info[2] < len(layers):
            rec["Layer"] = layers[info[2]]
        if "Weight" not in rec and info:
            rec["Weight"] = float(info[1] if info[1] not in (0, 255) else 1)
        if rec.get("Visible") is False:
            excluded["system (invisible)"] = excluded.get("system (invisible)", 0) + 1
            continue

        category = None
        for b in ch[1:]:
            for base, cat in CATEGORY_BASES:
                if b == base:
                    category = cat
                    break
            if category:
                break
        if name == "Gold":
            category = "Gold"
        rec["_chain"] = ch
        rec["Category"] = category
        rec["_attributes"], rec["_extra"], rec["_unparsed"], rec["_props"] = attributes, extra, unparsed, p
        records.append(rec)

    # loot tables (also feed the Gem category)
    loot_cs = os.path.join(root, "Misc", "Loot.cs")
    tables = {}
    if os.path.isfile(loot_cs):
        t = strip_comments(open(loot_cs, encoding="utf-8", errors="replace").read())
        for m in re.finditer(r"static\s+(?:readonly\s+)?Type\[\]\s+(\w+)\s*=", t):
            end = t.find(";", m.end())
            types = re.findall(r"typeof\(\s*([\w.]+)\s*\)", t[m.end():end])
            tables[re.sub(r"^m_", "", m.group(1))] = [x.split(".")[-1] for x in types]
    gems = set(tables.get("GemTypes", [])) | set(tables.get("RareGemTypes", []))

    by_name = {r["Type"]: r for r in records}
    items = []
    for rec in records:
        ch = rec.pop("_chain")
        attributes, extra, unparsed, p = rec.pop("_attributes"), rec.pop("_extra"), rec.pop("_unparsed"), rec.pop("_props")
        if not rec["Category"] and (rec["Type"] in gems or any(b in gems for b in ch)):
            rec["Category"] = "Gem"
        if not rec["Category"]:
            path = "/" + rec["File"]
            for frag, cat in CATEGORY_FOLDERS:
                if frag in path:
                    rec["Category"] = cat
                    break
            else:
                rec["Category"] = "Other"

        def pv(key):
            if key not in p:
                return None
            expr = p[key]
            if re.fullmatch(r"m?_?\w+", expr) and not re.fullmatch(r"\d+|true|false|null", expr):
                for b in ch:  # a static constant of a class along the chain (Container.m_GlobalMaxItems = 125)
                    s = re.search(r"\bstatic\s+(?:readonly\s+)?\w+\s+" + re.escape(expr) + r"\s*=\s*([^;]+);",
                                  classes[b]["body"] if b in classes else "")
                    if s:
                        expr = s.group(1)
                        break
            ok, val = literal(expr)
            return val if ok else None
        if rec.get("Hue") == 0:
            del rec["Hue"]
        if "BaseWeapon" in ch:
            w = {}
            for k, src in (("Skill", "DefSkill"), ("MinDamage", "AosMinDamage"), ("MaxDamage", "AosMaxDamage"),
                           ("StrReq", "AosStrengthReq"), ("Range", "DefMaxRange")):
                if pv(src) is not None:
                    w[k] = pv(src)
            sp = pv("MlSpeed") if pv("MlSpeed") not in (None, 0, 0.0) else pv("AosSpeed")
            if sp is not None:
                w["Speed"] = sp
            if "BaseRanged" in ch:
                w["Ranged"] = True
                if pv("AmmoType"):
                    w["Ammo"] = pv("AmmoType")
            w["TwoHanded"] = rec.get("Layer") == "TwoHanded"
            ab = [pv("PrimaryAbility"), pv("SecondaryAbility")]
            if any(ab):
                w["Abilities"] = ab
            rec["Weapon"] = w
        if "BaseArmor" in ch:
            a = {}
            for k, src in (("ArmorBase", "ArmorBase"), ("Physical", "BasePhysicalResistance"), ("Fire", "BaseFireResistance"),
                           ("Cold", "BaseColdResistance"), ("Poison", "BasePoisonResistance"), ("Energy", "BaseEnergyResistance"),
                           ("StrReq", "AosStrReq"), ("Material", "MaterialType"), ("MeditationAllowance", "DefMedAllowance")):
                if pv(src) is not None:
                    a[k] = pv(src)
            rec["Armor"] = a
        if "Container" in ch and rec["Category"] == "Container":
            cn = {}
            g = pv("DefaultGumpID")
            if not isinstance(g, int):
                g = None
            if g is None and "Graphic" in rec and gumps:
                g = gumps.get(rec["Graphic"], default_gump)
            if g is not None:
                cn["Gump"] = g
            if isinstance(pv("DefaultMaxItems"), int):
                cn["MaxItems"] = pv("DefaultMaxItems")
            if isinstance(pv("DefaultMaxWeight"), int):
                cn["MaxWeight"] = pv("DefaultMaxWeight")
            if cn:
                rec["Container"] = cn
        if attributes:
            rec["Attributes"] = attributes
        if extra:
            rec["Props"] = extra
        if unparsed:
            rec["Unparsed"] = unparsed
        order = ["Type", "Base", "File", "Category", "Graphic", "Graphics", "Name", "Cliloc", "Hue", "HueExpr", "Weight",
                 "Stackable", "Amount", "Layer", "LootType", "Movable", "Light", "Weapon", "Armor", "Container",
                 "Attributes", "Props", "Constructable", "Unparsed"]
        items.append({k: rec[k] for k in order if k in rec})

    os.makedirs(os.path.dirname(out_items), exist_ok=True)
    with open(out_items, "w", encoding="utf-8") as f:
        json.dump({"Source": "ServUO scripts (numbers only)", "Count": len(items), "Items": items}, f, indent=1)

    # ---- loot packs
    lp = strip_comments(open(os.path.join(root, "Misc", "LootPack.cs"), encoding="utf-8", errors="replace").read())
    groups, weights = {}, {}
    for m in re.finditer(r"static\s+readonly\s+LootPackItem\[\]\s+(\w+)\s*=", lp):
        end = lp.find(";", m.end())
        entries = re.findall(r"new\s+LootPackItem\(\s*typeof\(\s*([\w.]+)\s*\)\s*,\s*(\d+)\s*\)", lp[m.end():end])
        groups[m.group(1)] = [t.split(".")[-1] for t, _ in entries]
        if len(entries) > 1 and len({w for _, w in entries}) > 1:
            weights[m.group(1)] = [int(w) for _, w in entries]
    packs, unresolved_loot = {}, []
    for m in re.finditer(r"static\s+readonly\s+LootPack\s+(\w+)\s*=\s*new\s+LootPack\s*\(", lp):
        name = m.group(1)
        block = lp[m.end() - 1:matching(lp, m.end() - 1, "(", ")")]
        prefix = re.match(r"^(Ml|Se|Aos|Old)(?=[A-Z])", name)
        era = {"Ml": "ML", "Se": "SE", "Aos": "AOS", "Old": "Old"}[prefix.group(1)] if prefix else "Any"
        entries = []
        for e in re.finditer(r"new\s+LootPackEntry\s*\(([^()]*)\)", block):
            args = split_top(e.group(1))
            if len(args) < 4:
                unresolved_loot.append(name + ": " + e.group(0))
                continue
            q_ok, q = literal(args[3])
            entry = {"Chance": float(args[2]), "Items": args[1], "Quantity": q if q_ok else args[3]}
            if args[0] == "true":
                entry["AtSpawn"] = True
            if len(args) >= 7:
                entry["MaxProps"], entry["MinIntensity"], entry["MaxIntensity"] = int(args[4]), int(args[5]), int(args[6])
            if args[1] not in groups:
                unresolved_loot.append(name + ": unknown group " + args[1])
            entries.append(entry)
        packs[name] = {era: entries}
    aliases = {}
    for m in re.finditer(r"public\s+static\s+LootPack\s+(\w+)\s*\{\s*get\s*\{\s*return\s+([^;]+);", lp):
        expr, choice = m.group(2).strip(), {}
        while True:
            t = re.match(r"^Core\.(\w+)\s*\?\s*(\w+)\s*:\s*(.+)$", expr)
            if not t:
                break
            choice[t.group(1)] = t.group(2)
            expr = t.group(3).strip()
        choice["Old" if choice else "Any"] = expr
        aliases[m.group(1)] = choice
    for name in packs:
        aliases.setdefault(name, {"Any": name})
    used_tables = sorted({t for s in SPECIAL.values() for ts in s["Tables"].values() for t in ts})
    special = {k: v for k, v in SPECIAL.items() if any(k in g for g in groups.values())}
    loot = {"Source": "ServUO Scripts/Misc/LootPack.cs and Loot.cs (numbers only)",
            "EraRule": "an alias lists eras newest first; use the first one the shard is at or past (Old = pre-AOS, Any = always)",
            "Packs": packs, "Aliases": aliases, "Groups": groups, "GroupWeights": weights,
            "Special": special, "Tables": {k: tables[k] for k in used_tables if k in tables}}
    with open(out_loot, "w", encoding="utf-8") as f:
        json.dump(loot, f, indent=1)

    cats = {}
    for it in items:
        cats[it["Category"]] = cats.get(it["Category"], 0) + 1
    unp = sum(1 for it in items if it.get("Unparsed"))
    print(f"{len(items)} items -> {out_items}")
    print("  categories: " + ", ".join(f"{k} {v}" for k, v in sorted(cats.items(), key=lambda x: -x[1])))
    print("  excluded: " + ", ".join(f"{k} {v}" for k, v in sorted(excluded.items())))
    print(f"  {unp} with unparsed constructor lines, {sum(1 for it in items if 'Graphic' not in it)} without a graphic, "
          f"{sum(1 for it in items if it.get('Constructable') is False)} not [Constructable], {len(item_collisions)} item names defined twice (first kept)"
          f"{'' if tile else ', NO tiledata (weapon/armour layers and default weights missing)'}")
    print(f"{len(packs)} loot packs, {len(groups)} item groups, {len(aliases)} aliases -> {out_loot}")
    print("  group markers resolved through Loot.cs tables: " + ", ".join(sorted(special)))
    missing = [t for t in used_tables if t not in tables]
    if missing:
        print("  Loot.cs tables not found: " + ", ".join(missing))
    for c in item_collisions:
        print("  defined twice: " + c)
    if unresolved_loot:
        print("  unresolved: " + "; ".join(unresolved_loot))


if __name__ == "__main__":
    main()
