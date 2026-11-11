"""Extracts creature and NPC definitions (data only) from ServUO's C# scripts into Content/Creatures.json.

  python -I export_servuo_creatures.py <ServUO Scripts folder> <output json>

Reads each class that derives (directly or through other creatures) from BaseCreature: its [Constructable]
parameterless constructor (base AI arguments, Name, Body, Hue, SetStr/Dex/Int/Hits/Stam/Mana ranges, SetDamage,
SetDamageType, SetResistance, SetSkill, Fame, Karma, VirtualArmor, taming), its loot (AddLoot(LootPack.X, n)), its
CorpseName attribute, and its simple property overrides (Meat, Hides, PoisonImmune, CanFly...). A subclass starts from
its parent's values. Nothing of ServUO's code is reused: the game re-implements the rules (ServerDesign.md, decision 4);
these are the numbers. Anything a constructor does that isn't recognised is listed under "unparsed" for review.

Outfits. Each human-bodied record (and any other that equips items) gets an "Outfit": what it wears and holds. It is read
by interpreting, in a small C# subset, the whole construction of the class: the constructor chain (base/this
initialisers, so BaseVendor/BaseEscortable/... run first) and the virtual methods they call (InitBody, InitOutfit,
GetRandomHue, GetShoeHue, GetHairHue, GetGender, the ShoeType property, base.InitOutfit()...), resolved against the
most-derived class exactly as C# would. Items put in the backpack (PackItem, AddToBackpack, DropItem) are not outfit.

  "Outfit": {
    "Female": true | false | "Random",            sex, when the scripts decide it
    "Skin":   "Random" | <hue>,                    the body hue
    "Hair":   "Random" | {"ItemID": <id or "Random">, "Hue": <hue>} | "None",
    "Beard":  same as Hair (females never get one),
    "Items":  [<entry>, ...]
  }
Female/Skin/Hair/Beard can themselves be wrapped in the IfFemale/OneOf/Chance/If forms below when the scripts choose
them per branch. An item entry is {"Type": <Items.json Type>, "Hue": <hue>} (Hue omitted = the item's default), plus
optional "Args" (other constructor arguments by parameter name, e.g. {"ItemID": 0x204E} for a bare Item), "Resource",
"Layer", "Name", "DropChance" (SetWearable's third argument). Entry forms:
  {"OneOf": [<entry or [entries]>, ...], "Weights": [...]}    a random pick (Weights only when not uniform)
  {"Chance": <percent>, "Then": [entries], "Else": [entries]}   a coin flip / Utility.Random(n) < k / RandomDouble() < p
  {"IfFemale": [entries], "IfMale": [entries]}                 per-sex
  {"If": "<C# condition>", "Then": [...], "Else": [...]}        decided at runtime by something that isn't random or sex
                                                                (map, constructor argument...); also in OutfitUnparsed
Hues: an int; a rule name -- "Neutral", "Dyed", "Nondyed", "Random", "Blue", "Green", "Red", "Yellow", "Orange", "Pink",
"Snake", "Bird", "Slime", "Animal", "Metal", "Bright", "Skin", "Hair" (Utility.Random<Name>Hue); "MatchHair" (a beard
coloured like the hair); {"Min": a, "Max": b} (Utility.Random(a, n) / RandomMinMax); {"OneOf"/"Weights"} or
{"Chance"/"Then"/"Else"} or {"IfFemale"/"IfMale"} of hues; {"Rule": <hue>, "Shared": "<name>"} when one random hue is
drawn once and reused by several items (same Shared name = same colour). Old-style hair items (new ShortHair(),
new Item(0x203B) { Layer = Layer.Hair }) become Hair/Beard. Core.<expansion> checks are taken as true and Siege as false.
Anything outfit-related that can't be read goes in "OutfitUnparsed".

Shared outfits go in Outfits.json next to the output: the BaseVendor default ("Vendor") and any other base class whose
outfit several kinds extend (named after the class without "Base"), plus the town guards (WarriorGuard, ArcherGuard),
which are Mobiles rather than creatures. A record's Outfit can then be {"Base": "<name>", "Items": [its extra entries],
...its own Female/Skin/Hair/Beard replacing the shared ones}; a template may itself have a Base. Item types are checked
against Items.json in the output folder and any missing are printed.
"""
import copy
import json
import os
import re
import sys
from fractions import Fraction
from math import gcd

CLASS = re.compile(r"\b(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)\s*(?::\s*([\w.]+))?")
CORPSE = re.compile(r'\[CorpseName\("([^"]*)"\)\]\s*(?:\[[^\]]*\]\s*)*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)')
NUM = r"(-?\d+(?:\.\d+)?)"
CALLS = {
    "SetStr": "Str", "SetDex": "Dex", "SetInt": "Int", "SetHits": "Hits", "SetStam": "Stam", "SetMana": "Mana",
    "SetDamage": "Damage",
}
OVERRIDE = re.compile(r"public\s+override\s+([\w.<>]+)\s+(\w+)\s*(?:=>\s*([^;{]+);|\{\s*get\s*\{\s*return\s+([^;]+);\s*\}\s*\})")


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


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def class_body(text, name):
    m = re.search(r"class\s+" + name + r"\b[^{]*\{", text)
    if not m:
        return ""
    return text[m.end() - 1:matching(text, m.end() - 1)]


def constructor(body, name):
    """The parameterless constructor: (base or this, its initialiser arguments, body). One that only forwards to
    another constructor (`: this("a horse")`, as mounts do) is followed: the target's base arguments and body, with its
    parameters replaced by the forwarded values."""
    m = re.search(r"public\s+" + name + r"\s*\(\s*\)\s*(?::\s*(base|this)\s*\(([^)]*)\))?\s*\{", body)
    if not m:
        return None, None, ""
    end = matching(body, m.end() - 1)
    kind, args, code = m.group(1), m.group(2), body[m.end():end - 1]
    if kind == "this":
        values = [a.strip() for a in re.findall(r'"[^"]*"|[^,]+', args or "") if a.strip()]
        for t in re.finditer(r"public\s+" + name + r"\s*\(([^)]*)\)\s*:\s*(base|this)\s*\(([^)]*)\)\s*\{", body):
            params = [x.strip().split()[-1] for x in t.group(1).split(",") if x.strip()]
            if len(params) != len(values):
                continue
            target = t.group(3)
            for param, value in zip(params, values):
                target = re.sub(r"\b" + re.escape(param) + r"\b", value, target)
            tend = matching(body, t.end() - 1)
            return t.group(2), target, body[t.end():tend - 1] + ";" + code
    return kind, args, code


def parse_ctor(code, rec, unparsed, covered=None):
    for piece in re.finditer(r"[^;]+", code):
        stmt = piece.group(0).strip()
        if not stmt:
            continue
        s = " ".join(stmt.split())
        s = re.sub(r"^this\.", "", s)  # some scripts write this.SetStr(...)
        m = re.match(r"(\w+)\s*\(\s*([^)]*)\)$", s)
        handled = False
        if m and m.group(1) in CALLS:
            nums = re.findall(NUM, m.group(2))
            if nums:
                lo = float(nums[0])
                hi = float(nums[1]) if len(nums) > 1 else lo
                rec[CALLS[m.group(1)]] = [lo, hi]
                handled = True
        elif m and m.group(1) == "SetDamageType":
            t = re.match(r"ResistanceType\.(\w+)\s*,\s*" + NUM, m.group(2))
            if t:
                rec.setdefault("DamageTypes", {})[t.group(1)] = float(t.group(2))
                handled = True
        elif m and m.group(1) == "SetResistance":
            t = re.match(r"ResistanceType\.(\w+)\s*,\s*" + NUM + r"(?:\s*,\s*" + NUM + r")?", m.group(2))
            if t:
                lo = float(t.group(2))
                rec.setdefault("Resists", {})[t.group(1)] = [lo, float(t.group(3)) if t.group(3) else lo]
                handled = True
        elif m and m.group(1) == "SetSkill":
            t = re.match(r"SkillName\.(\w+)\s*,\s*" + NUM + r"(?:\s*,\s*" + NUM + r")?", m.group(2))
            if t:
                lo = float(t.group(2))
                rec.setdefault("Skills", {})[t.group(1)] = [lo, float(t.group(3)) if t.group(3) else lo]
                handled = True
        elif m and m.group(1) in ("SetSpecialAbility", "SetWeaponAbility", "SetAreaEffect", "SetMagicalAbility"):
            rec.setdefault("Abilities", []).append(m.group(2))
            handled = True
        elif m and m.group(1) in ("PackItem", "PackGold", "PackReg", "PackGem", "PackBodyPart", "PackPotion", "PackScroll",
                                  "PackMagicItems", "PackArmor", "PackWeapon", "PackSlayer", "PackNecroReg", "AddItem",
                                  "PackInstrument", "PackItems"):
            rec.setdefault("Pack", []).append(s)
            handled = True
        else:
            a = re.match(r"(\w+)\s*=\s*(.+)$", s)
            if a:
                key, value = a.group(1), a.group(2)
                if re.fullmatch(NUM, value):
                    rec[key] = float(value) if "." in value else int(value)
                elif re.fullmatch(r'"[^"]*"', value):
                    rec[key] = value.strip('"')
                elif value in ("true", "false"):
                    rec[key] = value == "true"
                elif re.fullmatch(r"0x[0-9A-Fa-f]+", value):
                    rec[key] = int(value, 16)
                else:
                    rec[key] = value  # an expression (random hue, name list...): kept as written
                handled = True
        if not handled and not (covered and covered(piece.start(), piece.end())):
            unparsed.append(s[:160])


# ---------------------------------------------------------------------------------------------------------------------
# Outfits: what a human-bodied NPC wears and holds, read from its constructor chain and InitBody/InitOutfit methods.
# ---------------------------------------------------------------------------------------------------------------------

HUMAN_BODIES = {183, 184, 185, 186, 400, 401, 402, 403, 605, 606, 607, 608, 666, 667, 694, 695, 744, 745, 750, 751, 987,
                990, 991, 994}
FEMALE_BODIES = {184, 186, 401, 403, 606, 608, 667, 695, 745, 751}
RACE_BODIES = {"Human": (400, 401), "Elf": (605, 606), "Gargoyle": (666, 667)}
HUE_RULES = {
    "RandomNeutralHue": "Neutral", "RandomDyedHue": "Dyed", "RandomNondyedHue": "Nondyed", "RandomHue": "Random",
    "RandomBlueHue": "Blue", "RandomGreenHue": "Green", "RandomRedHue": "Red", "RandomYellowHue": "Yellow",
    "RandomOrangeHue": "Orange", "RandomPinkHue": "Pink", "RandomSnakeHue": "Snake", "RandomBirdHue": "Bird",
    "RandomSlimeHue": "Slime", "RandomAnimalHue": "Animal", "RandomMetalHue": "Metal", "RandomBrightHue": "Bright",
    "RandomSkinHue": "Skin", "RandomHairHue": "Hair",
}
SLOTS = ("Female", "Skin", "HairItemID", "HairHue", "FacialHairItemID", "FacialHairHue")
OUTFIT_WORDS = re.compile(r"\b(AddItem|EquipItem|SetWearable|AssignRandomHair|AssignRandomFacialHair|HairItemID|HairHue|"
                          r"FacialHairItemID|FacialHairHue|Female)\b")
NOTE_WORDS = re.compile(r"\b(AddItem|EquipItem|SetWearable|AssignRandomHair|AssignRandomFacialHair|HairItemID|HairHue|"
                        r"FacialHairItemID|FacialHairHue)\b")
PACK_CALL = re.compile(r"^(PackItem|AddToBackpack|Pack\w+|[\w.]*DropItem|[\w.]*Backpack\.\w+)\s*\(")
NOT_INLINED = {"SetStr", "SetDex", "SetInt", "SetHits", "SetStam", "SetMana", "SetDamage", "SetDamageType", "SetResistance",
               "SetSkill", "SetSpecialAbility", "SetWeaponAbility", "SetAreaEffect", "SetMagicalAbility", "InitStats",
               "AddLoot", "GenerateLoot", "PackItem", "AddToBackpack", "Say", "Delete", "LoadSBInfo", "InitSBInfo"}


class Dist:
    """A random choice: [(probability, value)]."""
    def __init__(self, opts):
        self.opts = opts


class Rng:
    def __init__(self, lo, hi):
        self.lo, self.hi = lo, hi


class Sex:
    def __init__(self, f, m):
        self.f, self.m = f, m


class Shared:
    """A random hue drawn once into a local variable and reused by several items."""
    def __init__(self, name, v):
        self.name, self.v = name, v


class Tok:
    def __init__(self, name):
        self.name = name


class Unk:
    def __init__(self, text):
        self.text = text


UNIFORM01 = Tok("RandomDouble")
RANDOM_HAIR = Tok("RandomHair")


def canon(v):
    if isinstance(v, Dist):
        return ("D", tuple((str(p), canon(x)) for p, x in v.opts))
    if isinstance(v, Rng):
        return ("R", v.lo, v.hi)
    if isinstance(v, Sex):
        return ("S", canon(v.f), canon(v.m))
    if isinstance(v, Shared):
        return ("H", v.name, canon(v.v))
    if isinstance(v, Tok):
        return ("T", v.name)
    if isinstance(v, Unk):
        return ("U", v.text)
    if isinstance(v, dict):
        return ("I", json.dumps(v, sort_keys=True, default=lambda o: repr(canon(o))))
    if isinstance(v, tuple):
        return ("C",) + tuple(canon(x) for x in v)
    if isinstance(v, list):
        return ("L",) + tuple(canon(x) for x in v)
    return ("V", repr(v))


def mk_dist(pairs):
    merged = []
    for p, v in pairs:
        if p == 0:
            continue
        for i, (q, w) in enumerate(merged):
            if canon(w) == canon(v):
                merged[i] = (q + p, w)
                break
        else:
            merged.append((p, v))
    if len(merged) == 1:
        return merged[0][1]
    return Dist(merged)


# ---- a small C# statement parser -------------------------------------------------------------------------------------

def skip_string(t, i, end):
    q = t[i]
    verbatim = i > 0 and t[i - 1] == "@"
    i += 1
    while i < end:
        if t[i] == "\\" and not verbatim:
            i += 2
            continue
        if t[i] == q:
            if verbatim and i + 1 < end and t[i + 1] == q:
                i += 2
                continue
            return i + 1
        i += 1
    return end


def close_bracket(t, i, end):
    """t[i] is an opening bracket; index just past its partner."""
    depth = 0
    while i < end:
        c = t[i]
        if c in "\"'":
            i = skip_string(t, i, end)
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return end


def scan_to(t, i, end, stop):
    depth = 0
    while i < end:
        c = t[i]
        if c in "\"'":
            i = skip_string(t, i, end)
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            if depth == 0:
                return i
            depth -= 1
        elif depth == 0 and c == stop:
            return i
        i += 1
    return end


def skip_ws(t, i, end):
    while i < end and t[i].isspace():
        i += 1
    return i


def keyword_at(t, i, word):
    j = i + len(word)
    return t.startswith(word, i) and (j >= len(t) or not (t[j].isalnum() or t[j] == "_"))


HEAD = re.compile(r"(if|switch|for|foreach|while|using|lock|fixed)\s*\(")


def parse_stmt(t, i, end):
    """One statement starting at t[i]: (node, index after it)."""
    if t[i] == "{":
        j = close_bracket(t, i, end)
        return ("block", parse_list(t, i + 1, j - 1)), j
    m = HEAD.match(t, i)
    if m and m.end() <= end:
        p = m.end() - 1
        q = close_bracket(t, p, end)
        cond = t[p + 1:q - 1]
        k = skip_ws(t, q, end)
        if m.group(1) == "switch":
            if k < end and t[k] == "{":
                j = close_bracket(t, k, end)
                return ("switch", cond, parse_cases(t, k + 1, j - 1), (i, q)), j
            return ("stmt", t[i:q], (i, q)), q
        body, k2 = parse_stmt(t, k, end) if k < end else (("block", []), k)
        if m.group(1) == "if":
            k3 = skip_ws(t, k2, end)
            if k3 < end and keyword_at(t, k3, "else"):
                k4 = skip_ws(t, k3 + 4, end)
                els, k5 = parse_stmt(t, k4, end)
                return ("if", cond, body, els, (i, q), (k3, k3 + 4)), k5
            return ("if", cond, body, None, (i, q), None), k2
        return ("loop", m.group(1), cond, body, (i, q)), k2
    for word in ("try", "finally", "do", "else", "unchecked", "checked", "unsafe"):
        if keyword_at(t, i, word):
            k = skip_ws(t, i + len(word), end)
            body, k2 = parse_stmt(t, k, end) if k < end else (("block", []), k)
            if word == "try":
                k3 = skip_ws(t, k2, end)
                while k3 < end and (keyword_at(t, k3, "catch") or keyword_at(t, k3, "finally")):
                    k4 = skip_ws(t, k3 + 5 + (2 if t.startswith("finally", k3) else 0), end)
                    if k4 < end and t[k4] == "(":
                        k4 = skip_ws(t, close_bracket(t, k4, end), end)
                    _, k3 = parse_stmt(t, k4, end)
                    k2 = k3
                    k3 = skip_ws(t, k3, end)
                return ("block", [body]), k2
            if word == "do":
                k3 = skip_ws(t, k2, end)
                if keyword_at(t, k3, "while"):
                    k4 = scan_to(t, k3, end, ";")
                    return ("loop", "do", "", body, (i, i + 2)), k4 + 1
            return ("loop", word, "", body, (i, i + len(word))), k2
    if t[i] == ";":
        return None, i + 1
    j = scan_to(t, i, end, ";")
    return ("stmt", t[i:j], (i, j)), j + 1


def parse_list(t, i, end):
    nodes = []
    while True:
        i = skip_ws(t, i, end)
        if i >= end:
            return nodes
        node, i = parse_stmt(t, i, end)
        if node:
            nodes.append(node)


def parse_cases(t, i, end):
    groups = []  # [labels, nodes, label spans]
    while True:
        i = skip_ws(t, i, end)
        if i >= end:
            return groups
        if keyword_at(t, i, "case") or (keyword_at(t, i, "default") and t[skip_ws(t, i + 7, end):].startswith(":")):
            j = scan_to(t, i, end, ":")
            label = "default" if t.startswith("default", i) else t[i + 4:j].strip()
            if not groups or groups[-1][1]:
                groups.append([[], [], []])
            groups[-1][0].append(label)
            groups[-1][2].append((i, j + 1))
            i = j + 1
            continue
        node, i = parse_stmt(t, i, end)
        if node:
            if not groups:
                groups.append([[], [], []])
            groups[-1][1].append(node)


def split_args(s):
    out, i, start, end = [], 0, 0, len(s)
    while i < end:
        c = s[i]
        if c in "\"'":
            i = skip_string(s, i, end)
            continue
        if c in "([{":
            i = close_bracket(s, i, end)
            continue
        if c == ",":
            out.append(s[start:i].strip())
            start = i + 1
        i += 1
    if s[start:].strip():
        out.append(s[start:].strip())
    return out


def find_top(s, chars):
    """Index of the first top-level occurrence of any of the strings in chars (outside brackets and strings)."""
    i, end = 0, len(s)
    while i < end:
        c = s[i]
        if c in "\"'":
            i = skip_string(s, i, end)
            continue
        if c in "([{":
            i = close_bracket(s, i, end)
            continue
        for ch in chars:
            if s.startswith(ch, i):
                return i, ch
        i += 1
    return -1, None


def strip_parens(e):
    e = e.strip()
    while e.startswith("(") and close_bracket(e, 0, len(e)) == len(e):
        e = e[1:-1].strip()
    return e


def norm(s):
    s = " ".join(s.split())
    s = re.sub(r"\bthis\.", "", s)
    return re.sub(r"\b(?:global::)?Server\.(?:Items|Mobiles|Engines\.\w+)\.", "", s)


def ternary(e):
    """(cond, a, b) when e is a top-level conditional expression."""
    i, end, depth = 0, len(e), 0
    q = -1
    while i < end:
        c = e[i]
        if c in "\"'":
            i = skip_string(e, i, end)
            continue
        if c in "([{":
            i = close_bracket(e, i, end)
            continue
        if c == "?":
            if e.startswith("??", i) or e.startswith("?.", i) or e.startswith("?[", i):
                i += 2
                continue
            if q < 0:
                q = i
            depth += 1
        elif c == ":" and q >= 0:
            depth -= 1
            if depth == 0:
                return e[:q].strip(), e[q + 1:i].strip(), e[i + 1:].strip()
        i += 1
    return None


# ---- the interpreter ---------------------------------------------------------------------------------------------------

class Binding:
    def __init__(self, val, spans=()):
        self.val, self.spans = val, list(spans)


class Ctx:
    def __init__(self, engine, cls):
        self.engine, self.cls, self.defcls = engine, cls, cls
        self.locals = {}
        self.female = False
        self.track = False
        self.handled = set()
        self.unparsed = []
        self.used = set()
        self.depth = 0
        self.hstate = [False]  # shared by all branches: did the code give it a human body?
        self.uses = {}  # local name -> how often the current code mentions it
        self.cond_effects = []
        self.in_sex = False
        self.skin = None

    @property
    def human(self):
        return self.hstate[0]

    @human.setter
    def human(self, v):
        self.hstate[0] = v

    def branch(self, female=None):
        c = Ctx.__new__(Ctx)
        c.__dict__.update(self.__dict__)
        c.locals = {k: Binding(b.val, b.spans) for k, b in self.locals.items()}
        if female is not None:
            c.female = female
            c.in_sex = True
        return c

    def note(self, text):
        text = text[:160]
        if text not in self.unparsed:
            self.unparsed.append(text)


class Outfits:
    def __init__(self, classes, items):
        self.classes = classes
        self.items = items  # Type -> Items.json record
        self.bodies = {}
        self.missing = {}

    # -- source lookup --
    def body(self, cls):
        if cls not in self.bodies:
            self.bodies[cls] = class_body(self.classes[cls][2], cls) if cls in self.classes else ""
        return self.bodies[cls]

    def parent(self, cls):
        return self.classes[cls][0] if cls in self.classes else None

    def chain(self, cls):
        seen = []
        while cls in self.classes and cls not in seen and cls not in ("BaseCreature", "Mobile"):
            seen.append(cls)
            cls = self.parent(cls)
        return seen

    def ctors(self, cls):
        body = self.body(cls)
        out = []
        for m in re.finditer(r"(?:public|protected|internal|private)\s+" + cls + r"\s*\(", body):
            p = m.end() - 1
            q = close_bracket(body, p, len(body))
            params = [a for a in split_args(body[p + 1:q - 1])]
            if len(params) == 1 and params[0].split()[0] == "Serial":
                continue
            k = skip_ws(body, q, len(body))
            init = None
            if k < len(body) and body[k] == ":":
                im = re.compile(r":\s*(base|this)\s*\(").match(body, k)
                if not im:
                    continue
                a = im.end() - 1
                b = close_bracket(body, a, len(body))
                init = (im.group(1), split_args(body[a + 1:b - 1]))
                k = skip_ws(body, b, len(body))
            if k >= len(body) or body[k] != "{":
                continue
            e = close_bracket(body, k, len(body))
            out.append((params, init, body[k + 1:e - 1]))
        return out

    def method(self, cls, name, nargs, defcls=None):
        """(params, code, owner) of the method name resolved from cls upwards (virtual dispatch)."""
        pat = re.compile(r"((?:(?:public|protected|private|internal|static|override|virtual|new|sealed)\s+)+)[\w.<>\[\]]+\s+"
                         + name + r"\s*\(")
        for start in ([cls] + ([defcls] if defcls and defcls != cls else [])):
            for c in self.chain(start):
                body = self.body(c)
                for m in pat.finditer(body):
                    p = m.end() - 1
                    q = close_bracket(body, p, len(body))
                    params = split_args(body[p + 1:q - 1])
                    required = [a for a in params if "=" not in a]
                    if not (len(required) <= nargs <= len(params)):
                        continue
                    k = skip_ws(body, q, len(body))
                    if body.startswith("=>", k):
                        e = scan_to(body, k + 2, len(body), ";")
                        code = "return " + body[k + 2:e] + ";"
                    elif k < len(body) and body[k] == "{":
                        code = body[k + 1:close_bracket(body, k, len(body)) - 1]
                    else:
                        continue
                    private = "private" in m.group(1) or ("static" in m.group(1) and "public" not in m.group(1))
                    if private and start == cls and defcls and c not in self.chain(defcls):
                        continue
                    return params, code, c
        return None

    def prop(self, cls, name):
        """Getter code of property name, resolved from cls upwards."""
        pat = re.compile(r"(?:(?:public|protected|internal|override|virtual|static|new|sealed)\s+)+[\w.<>\[\]]+\s+" + name
                         + r"\s*(=>|\{)")
        for c in self.chain(cls):
            body = self.body(c)
            m = pat.search(body)
            if not m:
                continue
            if m.group(1) == "=>":
                e = scan_to(body, m.end(), len(body), ";")
                return "return " + body[m.end():e] + ";", c
            blk = body[m.end() - 1:close_bracket(body, m.end() - 1, len(body))]
            g = re.search(r"\bget\s*(=>|\{)", blk)
            if not g:
                return None
            if g.group(1) == "=>":
                e = scan_to(blk, g.end(), len(blk), ";")
                return "return " + blk[g.end():e] + ";", c
            return blk[g.end():close_bracket(blk, g.end() - 1, len(blk)) - 1], c
        return None

    def item_params(self, t, n):
        for params, _, _ in self.ctors(t) if t in self.classes else []:
            required = [a for a in params if "=" not in a]
            if len(required) <= n <= len(params):
                return [a.split("=")[0].split()[-1] for a in params]
        return None

    # -- values --
    def value(self, e, ctx):
        e = strip_parens(norm(e))
        cast = re.match(r"^\(\s*([A-Za-z_][\w.<>\[\]]*)\s*\)\s*(?=[\w(@\"])", e)
        if cast and cast.group(1) not in ("Female", "Body"):
            return self.value(e[cast.end():], ctx)
        tern = ternary(e)
        if tern:
            c = self.cond(tern[0], ctx)
            a, b = (lambda: self.value(tern[1], ctx)), (lambda: self.value(tern[2], ctx))
            if c is True:
                return a()
            if c is False:
                return b()
            if isinstance(c, Fraction):
                return mk_dist([(c, a()), (1 - c, b())])
            if isinstance(c, tuple):
                return Sex(a(), b()) if c[1] else Sex(b(), a())
            return Unk(e)
        if e == "null":
            return None
        i, op = find_top(e, ["==", "!=", "<=", ">=", "<", ">"])
        if i > 0 and not e.startswith("new ") and "=>" not in e and "<" not in e[:i].replace("<=", ""):
            c = self.cond(e, ctx)
            if isinstance(c, Fraction):
                return mk_dist([(c, True), (1 - c, False)])
            if isinstance(c, bool):
                return c
        if e in ("true", "false"):
            return e == "true"
        if re.fullmatch(r"-?\d+", e):
            return int(e)
        if re.fullmatch(r"0x[0-9A-Fa-f]+", e):
            return int(e, 16)
        if re.fullmatch(r"-?(?:\d+\.\d*|\.\d+)[fFdDmM]?", e):
            return Fraction(e.rstrip("fFdDmM"))
        if e.startswith('"'):
            return e.strip('"')
        m = re.match(r"^new\s+([\w.]+)\s*", e)
        if m:
            return self.new_item(e, m, ctx)
        m = re.fullmatch(r"(?:Utility\.)?(Immovable|Newbied|Rehued)\s*\((.*)\)", e)
        if m:
            args = split_args(m.group(2))
            v = self.value(args[0], ctx)
            if m.group(1) == "Rehued" and isinstance(v, dict) and len(args) > 1:
                v["Hue"] = self.value(args[1], ctx)
            return v
        m = re.fullmatch(r"Utility\.(\w+)\s*(?:<[\w.]+>)?\s*\((.*)\)", e)
        if m:
            fn, args = m.group(1), split_args(m.group(2))
            if fn in HUE_RULES:
                return HUE_RULES[fn]
            if fn == "RandomBool":
                return mk_dist([(Fraction(1, 2), True), (Fraction(1, 2), False)])
            if fn == "RandomDouble":
                return UNIFORM01
            vals = [self.value(a, ctx) for a in args]
            if fn == "Random" and len(vals) == 1 and isinstance(vals[0], int):
                return Rng(0, vals[0] - 1)
            if fn == "Random" and len(vals) == 2 and all(isinstance(v, int) for v in vals):
                return Rng(vals[0], vals[0] + vals[1] - 1)
            if fn == "RandomMinMax" and len(vals) == 2 and all(isinstance(v, int) for v in vals):
                return Rng(vals[0], vals[1])
            if fn == "RandomList" and vals:
                return mk_dist([(Fraction(1, len(vals)), v) for v in vals])
            return Unk(e)
        m = re.fullmatch(r"(?:Race\.\w+|Race|\w+\.Race)\.(\w+)\s*\((.*)\)", e)
        if m:
            if m.group(1) == "RandomSkinHue":
                return "Skin"
            if m.group(1) == "RandomHairHue":
                return "Hair"
            if m.group(1) in ("RandomHair", "RandomFacialHair"):
                return RANDOM_HAIR
            return Unk(e)
        m = re.fullmatch(r"Race\.(Human|Elf|Gargoyle)\.(Male|Female)Body", e)
        if m:
            return RACE_BODIES[m.group(1)][m.group(2) == "Female"]
        if e == "HairHue":
            return Tok("HairHue")
        if e == "Hue":
            return ctx.skin if ctx.skin is not None else Unk(e)
        if e == "this":
            return Tok("Self")
        m = re.fullmatch(r"(.+?)\s*[|&]\s*0x[0-9A-Fa-f]+", e)
        if m and "||" not in e and "&&" not in e:
            return self.value(m.group(1), ctx)  # flag bits on a hue
        m = re.fullmatch(r"Loot\.Construct\s*\((.*)\)", e)
        if m:
            types = re.findall(r"typeof\s*\(\s*([\w.]+)\s*\)", m.group(1))
            if types:
                return mk_dist([(Fraction(1, len(types)), {"Type": t.split(".")[-1]}) for t in types])
        if e == "Female":
            return ctx.female if isinstance(ctx.female, bool) else Unk(e)
        m = re.fullmatch(r"(base\.)?(\w+)\s*\((.*)\)", e)
        if m and m.group(2) not in NOT_INLINED:
            args = split_args(m.group(3))
            start = self.parent(ctx.defcls) if m.group(1) else ctx.cls
            found = self.method(start, m.group(2), len(args), None if m.group(1) else ctx.defcls)
            if found:
                return self.call_value(found, args, ctx)
            if m.group(2) == "GetGender":
                return mk_dist([(Fraction(1, 2), True), (Fraction(1, 2), False)])
            return Unk(e)
        if re.fullmatch(r"\w+", e):
            if e in ctx.locals:
                ctx.used.add(e)
                v = ctx.locals[e].val
                if ctx.uses.get(e, 0) > 2 and isinstance(v, (Dist, Rng, str)) and not iter_items(v):
                    return Shared(e, v)
                return v
            found = self.prop(ctx.cls, e)
            if found:
                return self.call_value((["x"] * 0, found[0], found[1]), [], ctx)
            return Unk(e)
        if re.fullmatch(r"[A-Z]\w*(\.\w+)+", e):
            return Tok(e)
        return Unk(e)

    def new_item(self, e, m, ctx):
        t = m.group(1).split(".")[-1]
        i = m.end()
        args, init = [], []
        if i < len(e) and e[i] == "(":
            j = close_bracket(e, i, len(e))
            args = split_args(e[i + 1:j - 1])
            i = skip_ws(e, j, len(e))
        if i < len(e) and e[i] == "{":
            j = close_bracket(e, i, len(e))
            init = split_args(e[i + 1:j - 1])
            i = skip_ws(e, j, len(e))
        if i != len(e):
            return Unk(e)
        entry = {"Type": t}
        if args:
            names = (["ItemID"] if t == "Item" else self.item_params(t, len(args))) or ["arg%d" % n for n in range(len(args))]
            for name, a in zip(names, args):
                v = self.value(a, ctx)
                if "hue" in name.lower():
                    entry["Hue"] = v
                else:
                    entry.setdefault("Args", {})[name] = v
        for a in init:
            kv = a.split("=", 1)
            if len(kv) == 2:
                self.set_prop(entry, kv[0].strip(), kv[1].strip(), ctx)
        return entry

    def set_prop(self, entry, key, expr, ctx):
        if key == "Hue":
            entry["Hue"] = self.value(expr, ctx)
        elif key == "Resource":
            entry["Resource"] = self.value(expr, ctx)
        elif key == "Name":
            entry["Name"] = self.value(expr, ctx)
        elif key == "Layer":
            entry["Layer"] = self.value(expr, ctx)

    def call_value(self, found, args, ctx):
        params, code, owner = found
        if ctx.depth > 12:
            return Unk("recursion")
        sub = ctx.branch()
        sub.defcls, sub.depth, sub.track, sub.used = owner, ctx.depth + 1, False, set()
        sub.locals = {}
        for p, a in zip(params, args):
            sub.locals[p.split("=")[0].split()[-1]] = Binding(self.value(a, ctx))
        nodes = parse_list(code, 0, len(code))
        sub.uses = self.count_uses(code)
        v = self.val_nodes(nodes, sub)
        return Unk("no return") if v is NORET else v

    def val_nodes(self, nodes, ctx):
        for i, n in enumerate(nodes):
            if n[0] == "block":
                v = self.val_nodes(n[1], ctx)
                if v is not NORET:
                    return v
            elif n[0] == "stmt":
                s = norm(n[1])
                if s.startswith("return"):
                    return self.value(s[6:], ctx)
                d = re.fullmatch(r"(?:[\w.<>\[\]]+\s+)?(\w+)\s*=\s*(.+)", s)
                if d and not d.group(2).startswith("="):
                    ctx.locals[d.group(1)] = Binding(self.value(d.group(2), ctx))
            elif n[0] == "if":
                c = self.cond(n[1], ctx)
                rest = nodes[i + 1:]
                then = lambda: self.val_nodes([n[2]], ctx.branch())
                other = lambda: self.val_nodes(([n[3]] if n[3] else []) + rest, ctx.branch())
                if c is True:
                    v = self.val_nodes([n[2]], ctx)
                    if v is not NORET:
                        return v
                    continue
                if c is False:
                    if n[3]:
                        v = self.val_nodes([n[3]], ctx)
                        if v is not NORET:
                            return v
                    continue
                a, b = then(), other()
                if a is NORET or b is NORET:
                    return Unk("if " + n[1])
                if isinstance(c, Fraction):
                    return mk_dist([(c, a), (1 - c, b)])
                if isinstance(c, tuple):
                    return Sex(a, b) if c[1] else Sex(b, a)
                return Unk("if " + n[1])
            elif n[0] == "switch":
                outs = self.outcomes(self.value(n[1], ctx))
                if outs is None:
                    return Unk("switch " + n[1])
                pairs = []
                for p, val in outs:
                    g = self.pick_case(n[2], val, ctx)
                    v = self.val_nodes(n[2][g][1], ctx.branch()) if g is not None else NORET
                    if v is NORET:
                        v = self.val_nodes(nodes[i + 1:], ctx.branch())
                    pairs.append((p, v))
                return mk_dist(pairs)
        return NORET

    # -- conditions: True / False / Fraction (chance of true) / ("sex", female polarity) / Unk --
    def cond(self, e, ctx):
        e = strip_parens(norm(e))
        i, op = find_top(e, ["||"])
        if i >= 0:
            a, b = self.cond(e[:i], ctx), self.cond(e[i + 2:], ctx)
            if a is True or b is True:
                return True
            if a is False:
                return b
            if b is False:
                return a
            if isinstance(a, Fraction) and isinstance(b, Fraction):
                return 1 - (1 - a) * (1 - b)
            return Unk(e)
        i, op = find_top(e, ["&&"])
        if i >= 0:
            a = self.cond(e[:i], ctx)
            if a is False:
                return False
            b = self.cond(e[i + 2:], ctx)
            if a is True:
                return b
            if b is True:
                return a
            if b is False:
                return False
            if isinstance(a, Fraction) and isinstance(b, Fraction):
                return a * b
            return Unk(e)
        if e.startswith("!") and not e.startswith("!="):
            v = self.cond(e[1:], ctx)
            if isinstance(v, bool):
                return not v
            if isinstance(v, Fraction):
                return 1 - v
            if isinstance(v, tuple):
                return ("sex", not v[1])
            return v
        m = re.fullmatch(r"Female\s*=\s*([^=].*)", e)
        if m:
            v = self.value(m.group(1), ctx)
            self.set_female(v, ctx)
            ctx.cond_effects.append(("Female", v))
            return self.cond("Female", ctx)
        if e in ("Female", "Body.IsFemale", "Female == true") or re.fullmatch(r"Body\s*==\s*(0x191|401|Race\.\w+\.FemaleBody)", e):
            return self.sexcond(True, ctx)
        if e in ("Female == false", "Body.IsMale") or re.fullmatch(r"Body\s*==\s*(0x190|400|Race\.\w+\.MaleBody)", e):
            return self.sexcond(False, ctx)
        m = re.fullmatch(r"Body\s*!=\s*(0x191|401|0x190|400)", e)
        if m:
            return self.sexcond(m.group(1) in ("0x190", "400"), ctx)
        if re.fullmatch(r"Core\.(AOS|SE|ML|SA|HS|TOL|EJ|UOR|UOTD|LBR|T2A|KR|UOSA)", e):
            return True
        m = re.fullmatch(r"Core\.Expansion\s*(>=|>|<=|<|==|!=)\s*Expansion\.\w+", e)
        if m:
            return m.group(1) in (">=", ">", "!=")
        if e in ("Siege.SiegeShard", "Core.Siege"):
            return False
        if e in ("Race != null", "Race == null"):
            return e == "Race != null"
        m = re.fullmatch(r"(?:\w+\.)?EquipItem\s*\((.*)\)", e)
        if m:
            v = self.value(m.group(1), ctx)
            ctx.cond_effects.append(("Equip", v))
            return True
        m = re.fullmatch(r"(\w+)\s*(==|!=)\s*null", e)
        if m and m.group(1) in ctx.locals:
            ctx.used.add(m.group(1))
            v = ctx.locals[m.group(1)].val
            if v is None or isinstance(v, dict):
                return (v is None) == (m.group(2) == "==")
            return Unk(e)
        i, op = find_top(e, ["==", "!=", "<=", ">=", "<", ">"])
        if i >= 0:
            a, b = self.value(e[:i], ctx), self.value(e[i + len(op):], ctx)
            p = self.compare(a, op, b)
            if p is not None:
                return p
            return Unk(e)
        v = self.value(e, ctx)
        if isinstance(v, bool):
            return v
        if isinstance(v, Dist) and all(isinstance(x, bool) for _, x in v.opts):
            return sum((p for p, x in v.opts if x), Fraction(0))
        return Unk(e)

    def sexcond(self, polarity, ctx):
        if ctx.female in (True, False):
            return ctx.female == polarity
        return ("sex", polarity)

    def compare(self, a, op, b):
        flip = {"<": ">", ">": "<", "<=": ">=", ">=": "<=", "==": "==", "!=": "!="}
        a = a.v if isinstance(a, Shared) else a
        b = b.v if isinstance(b, Shared) else b
        if isinstance(a, (int, Fraction)) and not isinstance(a, bool) and not isinstance(b, (int, Fraction)):
            a, b, op = b, a, flip[op]
        if not isinstance(b, (int, Fraction)) or isinstance(b, bool):
            if isinstance(a, Tok) and isinstance(b, Tok) and op in ("==", "!="):
                return (a.name == b.name) == (op == "==")
            return None
        test = {"<": lambda x: x < b, ">": lambda x: x > b, "<=": lambda x: x <= b, ">=": lambda x: x >= b,
                "==": lambda x: x == b, "!=": lambda x: x != b}[op]
        if a is UNIFORM01:
            c = Fraction(b)
            p = {"<": c, "<=": c, ">": 1 - c, ">=": 1 - c}.get(op)
            return None if p is None else max(Fraction(0), min(Fraction(1), p))
        if isinstance(a, Rng):
            n = a.hi - a.lo + 1
            return Fraction(sum(1 for x in range(a.lo, a.hi + 1) if test(x)), n) if n <= 100000 else None
        if isinstance(a, Dist) and all(isinstance(x, (int, Fraction)) for _, x in a.opts):
            return sum((p for p, x in a.opts if test(x)), Fraction(0))
        if isinstance(a, (int, Fraction)):
            return test(a)
        return None

    def outcomes(self, v):
        if isinstance(v, Rng) and v.hi - v.lo < 200:
            n = v.hi - v.lo + 1
            return [(Fraction(1, n), x) for x in range(v.lo, v.hi + 1)]
        if isinstance(v, Dist):
            out = []
            for p, x in v.opts:
                sub = self.outcomes(x)
                if sub is None:
                    return None
                out += [(p * q, y) for q, y in sub]
            return out
        if isinstance(v, (int, Tok, str)) and not isinstance(v, bool):
            return [(Fraction(1), v)]
        return None

    def pick_case(self, groups, val, ctx):
        default = None
        for gi, (labels, _, _) in enumerate(groups):
            for lab in labels:
                if lab == "default":
                    default = gi
                    continue
                lv = self.value(lab, ctx)
                if isinstance(lv, Tok) and isinstance(val, Tok):
                    if lv.name.split(".")[-1] == val.name.split(".")[-1]:
                        return gi
                elif canon(lv) == canon(val):
                    return gi
        return default

    def hair_items(self, entries):
        """Old-style hair/beard items (new ShortHair(hue), new Item(0x203B) { Layer = Layer.Hair }) become Hair/Beard."""
        out = []
        for e in entries:
            if "Type" in e:
                chain = self.chain(e["Type"]) if e["Type"] in self.classes else []
                layer = e.get("Layer").name.split(".")[-1] if isinstance(e.get("Layer"), Tok) else None
                pre = None
                if "Hair" in chain or layer == "Hair":
                    pre = "Hair"
                elif "Beard" in chain or layer == "FacialHair":
                    pre = "FacialHair"
                if pre:
                    gid = e.get("Args", {}).get("ItemID")
                    if gid is None:
                        g = re.search(r"base\(\s*(0x[0-9A-Fa-f]+|\d+)", self.body(e["Type"]))
                        gid = int(g.group(1), 0) if g else Unk(e["Type"])
                    out.append({"_slot": pre + "ItemID", "_v": gid})
                    out.append({"_slot": pre + "Hue", "_v": e.get("Hue", 0)})
                    continue
                out.append(e)
            elif "OneOf" in e:
                out.append({"OneOf": [self.hair_items(o) for o in e["OneOf"]], "_w": e["_w"]})
            elif "Chance" in e or "If" in e:
                k = "Chance" if "Chance" in e else "If"
                out.append({k: e[k], "Then": self.hair_items(e["Then"]), "Else": self.hair_items(e["Else"])})
            elif "IfFemale" in e:
                out.append({"IfFemale": self.hair_items(e["IfFemale"]), "IfMale": self.hair_items(e["IfMale"])})
            else:
                out.append(e)
        return out

    def set_female(self, v, ctx):
        if isinstance(v, bool):
            ctx.female = v
        else:
            ctx.female = "Random"

    # -- statements: returns (entries, signal) --
    def count_uses(self, code):
        return {w: n for w, n in __import__("collections").Counter(re.findall(r"\b[a-z_]\w*\b", code)).items()}

    def run(self, nodes, ctx):
        out = []
        for idx, n in enumerate(nodes):
            kind = n[0]
            if kind == "block":
                e, sig = self.run(n[1], ctx)
                out += e
                if sig:
                    return out, sig
            elif kind == "stmt":
                e, sig, fork = self.stmt(n, ctx, nodes[idx + 1:])
                out += e
                if fork is not None:
                    return out + fork, None
                if sig:
                    return out, sig
            elif kind == "if":
                rest = nodes[idx + 1:]
                then_returns = self.ends_in_return(n[2]) and n[3] is None and rest
                e, sig = self.run_if(n, ctx, rest if then_returns else None)
                out += e
                if then_returns:
                    return out, None
                if sig:
                    return out, sig
            elif kind == "switch":
                e, sig = self.run_switch(n, ctx)
                out += e
                if sig == "return":
                    return out, sig
            elif kind == "loop":
                probe = ctx.branch()
                probe.track, probe.unparsed = False, []
                e, _ = self.run([n[3]], probe)
                if has_effect(e) or any(OUTFIT_WORDS.search(u) for u in probe.unparsed):
                    ctx.note("%s (%s) {...}" % (n[1], norm(n[2])))
        return out, None

    def ends_in_return(self, node):
        if node[0] == "stmt":
            return norm(node[1]).startswith("return")
        if node[0] == "block" and node[1]:
            return self.ends_in_return(node[1][-1])
        return False

    def mark(self, span, ctx):
        if ctx.track and span:
            ctx.handled.add(span)

    def mark_used(self, ctx):
        for name in ctx.used:
            b = ctx.locals.get(name)
            if b:
                for sp in b.spans:
                    self.mark(sp, ctx)

    def merge_locals(self, ctx, branches, combine):
        names = set()
        for b in branches:
            names |= set(b.locals)
        for name in names:
            vals = [b.locals[name].val if name in b.locals else None for b in branches]
            spans = [sp for b in branches if name in b.locals for sp in b.locals[name].spans]
            if len({canon(v) for v in vals}) > 1:
                ctx.locals[name] = Binding(combine(vals), spans)
            elif name in ctx.locals or name in branches[0].locals:
                ctx.locals[name] = Binding(vals[0], spans)

    def run_branch(self, node, ctx, female=None):
        b = ctx.branch(female)
        b.cond_effects = []
        e, sig = self.run([node] if node else [], b)
        return e, sig, b

    def run_if(self, n, ctx, rest):
        _, cond_text, then, els, head, else_span = n
        ctx.cond_effects = []
        c = self.cond(cond_text, ctx)
        effects = ctx.cond_effects
        pre = []
        for kind, v in effects:
            if kind == "Female":
                pre.append({"_slot": "Female", "_v": v})
            elif kind == "Equip":
                pre += item_entries(v)
        other = ("block", ([els] if els else []) + (rest or []))
        if c is True or c is False:
            e, sig = self.run([then] if c else ([els] if els else []), ctx)
            if c is False and rest:
                e2, sig = self.run(rest, ctx)
                e += e2
            if has_effect(pre + e):
                self.mark(head, ctx)
                self.mark(else_span, ctx)
            return pre + e, sig
        if isinstance(c, Unk):
            track, ctx.track = ctx.track, False
            e1, _, b1 = self.run_branch(then, ctx)
            e2, _, b2 = self.run_branch(other, ctx)
            ctx.track = track
            if has_effect(e1) or has_effect(e2):
                ctx.note("if (%s)" % norm(cond_text))
                return pre + [{"If": norm(cond_text), "Then": e1, "Else": e2}], None
            return pre, None
        if isinstance(c, tuple):
            ft = c[1]
            e1, s1, b1 = self.run_branch(then, ctx, ft)
            e2, s2, b2 = self.run_branch(other, ctx, not ft)
            fem, male = (e1, e2) if ft else (e2, e1)
            self.merge_locals(ctx, [b1, b2] if ft else [b2, b1], lambda vs: Sex(vs[0], vs[1]))
            res = pre + [{"IfFemale": fem, "IfMale": male}]
        else:
            e1, s1, b1 = self.run_branch(then, ctx)
            e2, s2, b2 = self.run_branch(other, ctx)
            self.merge_locals(ctx, [b1, b2], lambda vs: mk_dist([(c, vs[0]), (1 - c, vs[1])]))
            res = pre + [{"Chance": c, "Then": e1, "Else": e2}]
        if has_effect(res):
            self.mark(head, ctx)
            self.mark(else_span, ctx)
        return res, ("return" if s1 == "return" and s2 == "return" else None)

    def run_switch(self, n, ctx):
        _, subj, groups, head = n
        sv = self.value(subj, ctx)
        if isinstance(sv, Sex):
            bf, bm = ctx.branch(True), ctx.branch(False)
            ef, sf = self.run_switch(n, bf)
            em_, sm = self.run_switch(n, bm)
            self.merge_locals(ctx, [bf, bm], lambda vs: Sex(vs[0], vs[1]))
            return [{"IfFemale": ef, "IfMale": em_}], ("return" if sf == sm == "return" else None)
        outs = self.outcomes(sv)
        if outs is None:
            # a switch on something only known at runtime (a constructor argument, a map...): one "If" per case
            track, ctx.track = ctx.track, False
            chain, default = [], []
            for labels, nodes_, _ in groups:
                e, _, _ = self.run_branch(("block", nodes_), ctx)
                named = [lab for lab in labels if lab != "default"]
                if len(named) < len(labels):
                    default = e
                if named:
                    chain.append((" || ".join("%s == %s" % (norm(subj), norm(lab)) for lab in named), e))
            ctx.track = track
            res = default
            for text, e in reversed(chain):
                res = [{"If": text, "Then": e, "Else": res}]
            if has_effect(res):
                ctx.note("switch (%s)" % norm(subj))
                return res, None
            return [], None
        weights = {}
        for p, v in outs:
            g = self.pick_case(groups, v, ctx)
            weights[g] = weights.get(g, Fraction(0)) + p
        options, ws, branches, sigs = [], [], [], []
        for g, p in weights.items():
            if g is None:
                options.append([])
                b = ctx.branch()
                sig = None
            else:
                e, sig, b = self.run_branch(("block", groups[g][1]), ctx)
                options.append(e)
            ws.append(p)
            branches.append(b)
            sigs.append(sig)
        self.merge_locals(ctx, branches, lambda vs: mk_dist(list(zip(ws, vs))))
        res = options[0] if len(options) == 1 else [{"OneOf": options, "_w": ws}]
        if has_effect(res):
            self.mark(head, ctx)
            for g in weights:
                if g is not None:
                    for sp in groups[g][2]:
                        self.mark(sp, ctx)
                    for node in groups[g][1]:
                        if node[0] == "stmt" and norm(node[1]) == "break":
                            self.mark(node[2], ctx)
        return res, ("return" if sigs and all(s == "return" for s in sigs) else None)

    def stmt(self, n, ctx, rest):
        """(entries, signal, forked continuation or None)"""
        raw, span = n[1], n[2]
        s = norm(raw)
        ctx.used = set()
        if s in ("break", "continue") or s.startswith("goto"):
            return [], "break", None
        if s.startswith("return"):
            return [], "return", None
        if s.startswith("base.") is False and PACK_CALL.match(s):
            return [], None, None
        m = re.fullmatch(r"(?:\w+\.)?(AddItem|EquipItem|SetWearable)\s*\((.*)\)", s)
        if m:
            args = split_args(m.group(2))
            v = self.value(args[0], ctx) if args else Unk(s)
            if m.group(1) == "SetWearable" and len(args) > 1:
                hue = self.value(args[1], ctx)
                for it in iter_items(v):
                    if hue != -1:
                        it["Hue"] = hue
                if len(args) > 2:
                    for it in iter_items(v):
                        it["DropChance"] = self.value(args[2], ctx)
            if is_container(v, self.items):
                return [], None, None
            ent = item_entries(v)
            if ent is None:
                ctx.note(s)
                return [], None, None
            ent = self.hair_items(ent)
            self.mark(span, ctx)
            self.mark_used(ctx)
            return ent, None, None
        m = re.fullmatch(r"Utility\.AssignRandom(Facial)?Hair\s*\((.*)\)", s)
        if m:
            args = split_args(m.group(2))
            pre = "FacialHair" if m.group(1) else "Hair"
            out = [{"_slot": pre + "ItemID", "_v": RANDOM_HAIR}]
            if len(args) < 2 or args[1] == "true":
                out.append({"_slot": pre + "Hue", "_v": "Hair"})
            elif args[1] != "false":
                out.append({"_slot": pre + "Hue", "_v": self.value(args[1], ctx)})
            ctx.human = True
            self.mark(span, ctx)
            self.mark_used(ctx)
            return out, None, None
        m = re.fullmatch(r"((?:[\w.<>\[\],]+\s+)?[\w.]+)\s*=\s*([^=].*)", s)
        if m:
            return self.assign(m.group(1), m.group(2), s, span, ctx, rest)
        m = re.fullmatch(r"[\w.<>\[\]]+\s+(\w+)", s)
        if m and not s.startswith(("new ", "return ")):
            ctx.locals[m.group(1)] = Binding(None, [span])
            return [], None, None
        m = re.fullmatch(r"(base\.)?(\w+)\s*\((.*)\)", s)
        if m and m.group(2) not in NOT_INLINED:
            args = split_args(m.group(3))
            start = self.parent(ctx.defcls) if m.group(1) else ctx.cls
            found = self.method(start, m.group(2), len(args), None if m.group(1) else ctx.defcls)
            if found and ctx.depth < 12:
                params, code, owner = found
                sub = ctx.branch()
                sub.defcls, sub.depth, sub.track = owner, ctx.depth + 1, False
                sub.locals = {}
                for p, a in zip(params, args):
                    sub.locals[p.split("=")[0].split()[-1]] = Binding(self.value(a, ctx))
                sub.uses = self.count_uses(code)
                e, _ = self.run(parse_list(code, 0, len(code)), sub)
                ctx.female, ctx.human = sub.female, sub.human or ctx.human
                if has_effect(e):
                    self.mark(span, ctx)
                return e, None, None
        if NOTE_WORDS.search(s):
            ctx.note(s)
        return [], None, None

    def assign(self, lhs, rhs, s, span, ctx, rest):
        decl = re.fullmatch(r"([\w.<>\[\]]+)\s+(\w+)", lhs)
        if decl or lhs in ctx.locals:
            name = decl.group(2) if decl else lhs
            v = self.value(rhs, ctx)
            old = ctx.locals.get(name)
            ctx.locals[name] = Binding(v, ([span] if decl or not old else old.spans + [span]))
            if isinstance(v, (Dist, Sex)) and any(x is None or isinstance(x, dict) for x in leaves(v)) and rest:
                return [], None, self.fork(name, v, ctx, rest)
            return [], None, None
        if lhs in ("Female",):
            v = self.value(rhs, ctx)
            self.set_female(v, ctx)
            self.mark(span, ctx)
            return [{"_slot": "Female", "_v": v}], None, None
        if lhs in ("Body", "BodyValue"):
            v = self.value(rhs, ctx)
            if isinstance(v, int):
                if v not in HUMAN_BODIES:
                    ctx.human = False
                    return [], None, None
                ctx.human = True
                self.mark(span, ctx)
                fem = v in FEMALE_BODIES
                if not ctx.in_sex and ctx.female != "Random":
                    ctx.female = fem
                    return [{"_slot": "Female", "_v": fem}], None, None
                return [], None, None
            if isinstance(v, Sex) and v.f in HUMAN_BODIES:
                ctx.human = True
                self.mark(span, ctx)
            return [], None, None
        if lhs == "Hue":
            v = self.value(rhs, ctx)
            ctx.skin = v
            self.mark(span, ctx)
            self.mark_used(ctx)
            return [{"_slot": "Skin", "_v": v}], None, None
        if lhs in ("HairItemID", "HairHue", "FacialHairItemID", "FacialHairHue"):
            v = self.value(rhs, ctx)
            if lhs.endswith("ItemID") and v not in (0, None):
                ctx.human = True
            self.mark(span, ctx)
            self.mark_used(ctx)
            return [{"_slot": lhs, "_v": v}], None, None
        m = re.fullmatch(r"(\w+)\.(\w+)", lhs)
        if m and m.group(1) in ctx.locals:
            b = ctx.locals[m.group(1)]
            b.spans.append(span)
            for it in iter_items(b.val):
                self.set_prop(it, m.group(2), rhs, ctx)
            if m.group(2) in ("Hue", "Resource", "Name"):
                self.mark_used(ctx)
            return [], None, None
        if NOTE_WORDS.search(s):
            ctx.note(s)
        return [], None, None

    def fork(self, name, v, ctx, rest):
        """A local item that is one of several things (or nothing): run the rest of the block once per choice."""
        def go(x):
            b = ctx.branch()
            b.locals[name] = Binding(x, ctx.locals[name].spans)
            e, _ = self.run(rest, b)
            ctx.female, ctx.human = b.female, b.human or ctx.human
            return e
        if isinstance(v, Sex):
            return [{"IfFemale": go(v.f), "IfMale": go(v.m)}]
        return [{"OneOf": [go(x) for _, x in v.opts], "_w": [p for p, _ in v.opts]}]

    # -- whole classes --
    def simulate(self, cls, own_code=None):
        """(entries, ctx) for constructing cls with its parameterless (or simplest) constructor."""
        ctx = Ctx(self, cls)
        ctx.cond_effects = []
        ctors = self.ctors(cls)
        if not ctors:
            return [], ctx
        ctor = next((c for c in ctors if not c[0]), None) or min(ctors, key=lambda c: len(c[0]))
        e = self.run_ctor(cls, ctor, [], ctx, own_code)
        return e, ctx

    def run_ctor(self, cls, ctor, argvals, ctx, own_code=None):
        params, init, code = ctor
        if ctx.depth > 12:
            return []
        out = []
        locals_ = {p.split("=")[0].split()[-1]: Binding(v) for p, v in zip(params, argvals)}
        save = ctx.locals
        ctx.locals = locals_
        if init:
            target = cls if init[0] == "this" else self.parent(cls)
            vals = [self.value(a, ctx) for a in init[1]]
            out += self.ctor_of(target, vals, ctx)
        elif self.parent(cls) in self.classes:
            out += self.ctor_of(self.parent(cls), [], ctx)
        ctx.locals = locals_
        ctx.defcls = cls
        ctx.uses = self.count_uses(code)
        ctx.track = own_code is not None and code == own_code
        e, _ = self.run(parse_list(code, 0, len(code)), ctx)
        ctx.track = False
        ctx.locals = save
        return out + e

    def ctor_of(self, cls, vals, ctx):
        if cls not in self.classes or cls in ("BaseCreature", "Mobile"):
            return []
        for c in self.ctors(cls):
            required = [a for a in c[0] if "=" not in a]
            if len(required) <= len(vals) <= len(c[0]):
                ctx.depth += 1
                e = self.run_ctor(cls, c, vals, ctx)
                ctx.depth -= 1
                return e
        return []


NORET = Tok("no return")


def leaves(v):
    if isinstance(v, Dist):
        return [y for _, x in v.opts for y in leaves(x)]
    if isinstance(v, Sex):
        return leaves(v.f) + leaves(v.m)
    return [v]


def iter_items(v):
    return [x for x in leaves(v) if isinstance(x, dict)]


def is_container(v, items):
    its = iter_items(v)
    return bool(its) and all(items.get(i["Type"], {}).get("Layer") == "Backpack" or
                             items.get(i["Type"], {}).get("Category") == "Container" or i["Type"] in ("Backpack", "Bag")
                             for i in its)


def item_entries(v):
    if v is None:
        return []
    if isinstance(v, dict):
        return [v]
    if isinstance(v, Dist):
        opts = [item_entries(x) for _, x in v.opts]
        return None if any(o is None for o in opts) else [{"OneOf": opts, "_w": [p for p, _ in v.opts]}]
    if isinstance(v, Sex):
        f, m = item_entries(v.f), item_entries(v.m)
        return None if f is None or m is None else [{"IfFemale": f, "IfMale": m}]
    return None


def has_effect(entries):
    for e in entries:
        if "Type" in e or "_slot" in e:
            return True
        if "OneOf" in e and any(has_effect(o) for o in e["OneOf"]):
            return True
        if ("Chance" in e or "If" in e) and (has_effect(e["Then"]) or has_effect(e["Else"])):
            return True
        if "IfFemale" in e and (has_effect(e["IfFemale"]) or has_effect(e["IfMale"])):
            return True
    return False


# ---- turning the interpreted result into JSON ---------------------------------------------------------------------------

def pct(p):
    v = p * 100
    return int(v) if v.denominator == 1 else round(float(v), 2)


def weights(ps):
    if len(set(ps)) <= 1:
        return None
    den = 1
    for p in ps:
        den = den * p.denominator // gcd(den, p.denominator)
    ints = [int(p * den) for p in ps]
    g = 0
    for i in ints:
        g = gcd(g, i)
    return [i // g for i in ints]


class Emitter:
    def __init__(self, notes):
        self.notes = notes

    def hue(self, v):
        if v is None or isinstance(v, (bool, str)):
            return v
        if isinstance(v, int):
            return v
        if isinstance(v, Fraction):
            return float(v)
        if isinstance(v, Dist):
            vals, ps = [], []
            for p, x in v.opts:
                vals.append(self.hue(x))
                ps.append(p)
            out = {"OneOf": vals}
            w = weights(ps)
            if w:
                out["Weights"] = w
            return out
        if isinstance(v, Rng):
            return {"Min": v.lo, "Max": v.hi}
        if isinstance(v, Sex):
            return {"IfFemale": self.hue(v.f), "IfMale": self.hue(v.m)}
        if isinstance(v, Shared):
            return {"Rule": self.hue(v.v), "Shared": v.name}
        if isinstance(v, Tok):
            if v.name == "HairHue":
                return "MatchHair"
            if v.name == "RandomHair":
                return "Random"
            return v.name.split(".")[-1]
        if isinstance(v, Unk):
            self.notes.append("value: " + v.text[:150])
            return None
        if isinstance(v, dict):
            return self.item(v)
        return None

    def item(self, e):
        out = {"Type": e["Type"]}
        if "Hue" in e:
            out["Hue"] = self.hue(e["Hue"])
        for k in ("Layer", "Resource", "Name", "DropChance"):
            if k in e:
                out[k] = self.hue(e[k])
        if e.get("Args"):
            out["Args"] = {k: self.hue(v) for k, v in e["Args"].items()}
        return out

    def items(self, entries):
        out = []
        for e in entries:
            if "_slot" in e:
                continue
            if "Type" in e:
                out.append(self.item(e))
                continue
            if "OneOf" in e:
                opts, ps = [], []
                for o, p in zip(e["OneOf"], e["_w"]):
                    o = self.items(o)
                    for i, q in enumerate(opts):
                        if q == o:
                            ps[i] += p
                            break
                    else:
                        opts.append(o)
                        ps.append(p)
                kind = ("OneOf", opts, ps)
            elif "Chance" in e:
                kind = ("Chance", [self.items(e["Then"]), self.items(e["Else"])], e["Chance"])
            elif "If" in e:
                kind = ("If", [self.items(e["Then"]), self.items(e["Else"])], e["If"])
            else:
                kind = ("Sex", [self.items(e["IfFemale"]), self.items(e["IfMale"])], None)
            opts = kind[1]
            if all(o == opts[0] for o in opts):
                out += opts[0]
                continue
            pre = 0
            while all(len(o) > pre for o in opts) and all(o[pre] == opts[0][pre] for o in opts):
                pre += 1
            out += opts[0][:pre]
            opts = [o[pre:] for o in opts]
            suf = 0
            while all(len(o) > suf for o in opts) and all(o[len(o) - 1 - suf] == opts[0][len(opts[0]) - 1 - suf] for o in opts):
                suf += 1
            tail = opts[0][len(opts[0]) - suf:] if suf else []
            opts = [o[:len(o) - suf] for o in opts]
            if kind[0] == "OneOf":
                node = {"OneOf": [o[0] if len(o) == 1 else o for o in opts]}
                w = weights(kind[2])
                if w:
                    node["Weights"] = w
            elif kind[0] in ("Chance", "If"):
                node = {"Chance": pct(kind[2])} if kind[0] == "Chance" else {"If": kind[2]}
                node["Then"] = opts[0]
                if opts[1]:
                    node["Else"] = opts[1]
            else:
                node = {"IfFemale": opts[0], "IfMale": opts[1]}
            out.append(node)
            out += tail
        return out


def slot_value(entries, slot, prior):
    v = prior
    for e in entries:
        if e.get("_slot") == slot:
            v = e["_v"]
        elif "OneOf" in e:
            vals = [slot_value(o, slot, v) for o in e["OneOf"]]
            v = ("oneof", e["_w"], vals) if len({canon(x) for x in vals}) > 1 else vals[0]
        elif "Chance" in e:
            a, b = slot_value(e["Then"], slot, v), slot_value(e["Else"], slot, v)
            v = ("chance", e["Chance"], a, b) if canon(a) != canon(b) else a
        elif "IfFemale" in e:
            a, b = slot_value(e["IfFemale"], slot, v), slot_value(e["IfMale"], slot, v)
            v = ("sex", None, a, b) if canon(a) != canon(b) else a
        elif "If" in e:
            a, b = slot_value(e["Then"], slot, v), slot_value(e["Else"], slot, v)
            v = ("if", e["If"], a, b) if canon(a) != canon(b) else a
    return v


def cond_json(v, leaf):
    if isinstance(v, tuple):
        kind = v[0]
        if kind == "oneof":
            out = {"OneOf": [cond_json(x, leaf) for x in v[2]]}
            w = weights(v[1])
            if w:
                out["Weights"] = w
            return out
        if kind == "chance":
            return {"Chance": pct(v[1]), "Then": cond_json(v[2], leaf), "Else": cond_json(v[3], leaf)}
        if kind == "if":
            return {"If": v[1], "Then": cond_json(v[2], leaf), "Else": cond_json(v[3], leaf)}
        return {"IfFemale": cond_json(v[2], leaf), "IfMale": cond_json(v[3], leaf)}
    return leaf(v)


def zip_cond(a, b, leaf):
    """Combine two slot values (item id and hue) that may each be conditional."""
    if isinstance(a, tuple) and isinstance(b, tuple) and a[0] == b[0] and canon(a[1]) == canon(b[1]) and len(a) == len(b):
        if a[0] == "oneof" and len(a[2]) == len(b[2]):
            out = {"OneOf": [zip_cond(x, y, leaf) for x, y in zip(a[2], b[2])]}
            w = weights(a[1])
            if w:
                out["Weights"] = w
            return out
        if a[0] != "oneof":
            key = ("Then", "Else") if a[0] in ("chance", "if") else ("IfFemale", "IfMale")
            out = {"Chance": pct(a[1])} if a[0] == "chance" else ({"If": a[1]} if a[0] == "if" else {})
            out[key[0]] = zip_cond(a[2], b[2], leaf)
            out[key[1]] = zip_cond(a[3], b[3], leaf)
            return out
    if isinstance(a, tuple):
        return cond_json(a, lambda x: leaf(x, b) if not isinstance(b, tuple) else zip_cond(x, b, leaf))
    if isinstance(b, tuple):
        return cond_json(b, lambda y: leaf(a, y))
    return leaf(a, b)


def finish(entries, notes):
    """The Outfit object (or None) for interpreted entries."""
    em = Emitter(notes)
    out = {}
    female = slot_value(entries, "Female", None)
    if female is not None:
        def fleaf(v):
            if isinstance(v, Dist) and all(isinstance(x, bool) for _, x in v.opts):
                p = sum((p for p, x in v.opts if x), Fraction(0))
                return "Random" if p == Fraction(1, 2) else {"Chance": pct(p), "Then": True, "Else": False}
            return em.hue(v)
        out["Female"] = cond_json(female, fleaf)
    skin = slot_value(entries, "Skin", None)
    if skin is not None:
        out["Skin"] = cond_json(skin, lambda v: "Random" if v == "Skin" else em.hue(v))
    hair_hue = slot_value(entries, "HairHue", None)
    for key, pre in (("Hair", "Hair"), ("Beard", "FacialHair")):
        iid, hue = slot_value(entries, pre + "ItemID", None), slot_value(entries, pre + "Hue", None)
        if iid is None and hue is None:
            continue

        def leaf(i, h, key=key):
            if i is None or i == 0:
                return "None"
            if key == "Beard" and (isinstance(h, Tok) and h.name == "HairHue" or
                                   isinstance(h, Shared) and isinstance(hair_hue, Shared) and h.name == hair_hue.name):
                hj = "MatchHair"
            else:
                hj = em.hue(h)
            if i is RANDOM_HAIR and hj != "MatchHair" and (h == "Hair" or isinstance(h, Shared) and h.v == "Hair"):
                return "Random"
            r = {"ItemID": em.hue(i)}
            if h is not None:
                r["Hue"] = hj
            return r
        out[key] = zip_cond(iid, hue, leaf)
        if key == "Beard" and isinstance(out[key], dict) and out[key].get("IfFemale") == "None":
            out[key] = out[key]["IfMale"]  # females never get a beard anyway
    if out.get("Female") == {"Chance": 50, "Then": True, "Else": False}:
        out["Female"] = "Random"
    for k in ("Female", "Skin", "Hair", "Beard"):
        v = out.get(k)
        if v is None and k in out or isinstance(v, dict) and all(x is None for x in v.values()):
            del out[k]  # copied from someone else at runtime (noted in OutfitUnparsed)
    items = em.items(entries)
    if items:
        out["Items"] = items
    return out or None


def outfit_types(o, acc):
    if isinstance(o, dict):
        if "Type" in o and isinstance(o["Type"], str):
            acc.add(o["Type"])
        for k, v in o.items():
            if k not in ("Args",):
                outfit_types(v, acc)
    elif isinstance(o, list):
        for v in o:
            outfit_types(v, acc)
    return acc


def is_subsequence(small, big):
    """Indexes of big that match small in order, or None."""
    idx, j = [], 0
    for s in small:
        while j < len(big) and big[j] != s:
            j += 1
        if j == len(big):
            return None
        idx.append(j)
        j += 1
    return idx


def express(full, template, name):
    """full as {"Base": name, "Items": extras, overrides...}, or None when it can't be said that way."""
    idx = is_subsequence(template.get("Items", []), full.get("Items", []))
    if idx is None:
        return None
    out = {"Base": name}
    for k in ("Female", "Skin", "Hair", "Beard"):
        if full.get(k) == template.get(k):
            continue
        if k in full:
            out[k] = full[k]
        elif k in ("Hair", "Beard"):
            out[k] = "None"
        else:
            return None
    extras = [e for i, e in enumerate(full.get("Items", [])) if i not in set(idx)]
    if extras:
        out["Items"] = extras
    return out


def template_name(cls):
    return cls[4:] if cls.startswith("Base") and len(cls) > 4 and cls[4].isupper() else cls


def assign_outfits(engine, classes, usable, outfits):
    """Shared outfits (Outfits.json) and each record's Outfit, written against the nearest shared one it extends."""
    def ancestors(cls):
        return engine.chain(cls)[1:]

    counts = {}
    for c in usable:
        if outfits.get(c["Type"], (None,))[0]:
            for a in ancestors(c["Type"]):
                counts[a] = counts.get(a, 0) + 1
    full = {}
    for a, n in counts.items():
        if n >= 3 or a == "BaseVendor":
            e, ctx = engine.simulate(a)
            notes = list(ctx.unparsed)
            o = finish(e, notes)
            if o and ctx.human:
                full[a] = (o, notes)
    active = set(full)

    def express_best(cls, outfit):
        for a in ancestors(cls):
            if a in active:
                x = express(outfit, full[a][0], template_name(a))
                if x is not None:
                    return x, a
        return outfit, None

    while True:
        refs = {a: 0 for a in active}
        redundant = set()
        for a in active:
            x, parent = express_best(a, full[a][0])
            if parent:
                refs[parent] += 1
                if set(x) == {"Base"}:
                    redundant.add(a)
        for c in usable:
            o = outfits.get(c["Type"], (None,))[0]
            if o:
                _, parent = express_best(c["Type"], o)
                if parent:
                    refs[parent] += 1
        drop = {a for a in active if a != "BaseVendor" and (refs[a] < 2 or a in redundant)}
        if not drop:
            break
        active -= drop
    templates = {}
    for a in sorted(active, key=template_name):
        x, _ = express_best(a, full[a][0])
        if full[a][1]:
            x = dict(x, OutfitUnparsed=full[a][1])
        templates[template_name(a)] = x
    records = {}
    for c in usable:
        o, notes, _ = outfits.get(c["Type"], (None, [], False))
        records[c["Type"]] = (express_best(c["Type"], o)[0] if o else None, notes)
    # Human NPCs that aren't creatures (town guards derive from Mobile): their outfits are kept here by class name.
    for name in sorted(classes):
        if name != "BaseGuard" and "BaseGuard" in engine.chain(name) and not classes[name][4]:
            e, ctx = engine.simulate(name)
            notes = list(ctx.unparsed)
            o = finish(e, notes)
            if o:
                templates[name] = dict(o, OutfitUnparsed=notes) if notes else o
    return templates, records


def main():
    root, out = sys.argv[1], sys.argv[2]
    classes = {}  # name -> (base, file, text)
    for folder, _, files in os.walk(root):
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(folder, f)
            text = strip_comments(open(path, encoding="utf-8", errors="replace").read())
            corpses = {m.group(2): m.group(1) for m in CORPSE.finditer(text)}
            for m in CLASS.finditer(text):
                name, base = m.group(1), (m.group(2) or "").split(".")[-1]
                if name not in classes:
                    classes[name] = (base, os.path.relpath(path, root), text, corpses.get(name), "abstract" in text[max(0, m.start() - 40):m.end()])

    def is_creature(name, seen=()):
        if name == "BaseCreature":
            return True
        if name not in classes or name in seen:
            return False
        return is_creature(classes[name][0], seen + (name,))

    def is_vendor(name, seen=()):
        if name == "BaseVendor":
            return True
        if name not in classes or name in seen:
            return False
        return is_vendor(classes[name][0], seen + (name,))

    items_path = os.path.join(os.path.dirname(out), "Items.json")
    items = {i["Type"]: i for i in json.load(open(items_path, encoding="utf-8"))["Items"]} if os.path.exists(items_path) else {}
    engine = Outfits(classes, items)
    outfits = {}  # name -> (outfit or None, notes, human)
    done = {}

    def build(name):
        if name in done:
            return done[name]
        base, path, text, corpse, abstract = classes[name]
        rec = dict(build(base)) if base in classes and base != "BaseCreature" and is_creature(base) else {}
        rec = json.loads(json.dumps(rec))  # deep copy
        rec.pop("Abstract", None)
        rec["Type"], rec["Base"], rec["File"] = name, base, path.replace("\\", "/")
        if corpse:
            rec["CorpseName"] = corpse
        body = class_body(text, name)
        kind, args, code = constructor(body, name)
        unparsed = []
        if kind == "base" and base == "BaseCreature" or (kind == "base" and args and "AIType" in args):
            parts = [p.strip() for p in re.findall(r'"[^"]*"|[^,]+', args)]
            ai = next((i for i, p in enumerate(parts) if "AIType" in p), 0)
            for p in parts[:ai]:  # mounts: (name, body, mount item id, AI...)
                if re.fullmatch(r'"[^"]*"', p):
                    rec["Name"] = p.strip('"')
                elif re.fullmatch(r"0x[0-9A-Fa-f]+|\d+", p):
                    rec["MountItem" if "Body" in rec else "Body"] = int(p, 16) if p.startswith("0x") else int(p)
            parts = parts[ai:]
            keys = ["AI", "FightMode", "RangePerception", "RangeFight", "ActiveSpeed", "PassiveSpeed"]
            for k, v in zip(keys, parts):
                v = v.split(".")[-1]
                rec[k] = float(v) if re.fullmatch(NUM, v) and "." in v else (int(v) if re.fullmatch(NUM, v) else v)
        if is_vendor(name):
            rec["Vendor"] = True
            rec.setdefault("AI", "AI_Vendor")
            rec.setdefault("Body", "Human")  # BaseVendor picks a male or female human body
            if kind == "base" and args and args.strip().startswith('"'):
                rec["Title"] = args.strip().strip('"')
            shops = re.findall(r"SBInfos\.Add\(\s*new\s+(\w+)\s*\(", body) or re.findall(r"m_SBInfos\.Add\(\s*new\s+(\w+)\s*\(", body)
            if shops:
                rec["Shops"] = shops
        entries, octx = engine.simulate(name, code or None)
        if code:
            mask = set()
            for a, b in octx.handled:
                mask.update(range(a, b))

            def covered(a, b):
                chars = [i for i in range(a, b) if not code[i].isspace() and code[i] not in "{}"]
                return bool(chars) and all(i in mask for i in chars)
            before = []
            parse_ctor(code, copy.deepcopy(rec), before)
            parse_ctor(code, rec, unparsed, covered)
            if before and not unparsed:
                rec.pop("Unparsed", None)  # its own constructor is now fully read; don't fall back to the parent's list
        notes = list(octx.unparsed)
        outfit = finish(entries, notes)
        body_field = rec.get("Body")
        human = body_field == "Human" or isinstance(body_field, int) and body_field in HUMAN_BODIES or octx.human
        if isinstance(body_field, int) and body_field not in HUMAN_BODIES and not octx.human:
            human = False
        if outfit and not human:  # a non-human body: only what it holds/wears, if anything
            outfit = {"Items": outfit["Items"]} if "Items" in outfit else None
        outfits[name] = (outfit, notes, human)
        loot = re.search(r"override\s+void\s+GenerateLoot\s*\(\s*\)\s*\{", body)
        if loot:
            lb = body[loot.end():matching(body, loot.end() - 1) - 1]
            rec["Loot"] = [[m.group(1), int(m.group(2) or 1)] for m in re.finditer(r"AddLoot\(\s*LootPack\.(\w+)\s*(?:,\s*(\d+))?", lb)]
        for m in OVERRIDE.finditer(body):
            value = (m.group(3) or m.group(4) or "").strip()
            if len(value) < 60 and m.group(2) not in ("GenerateLoot",):
                rec.setdefault("Overrides", {})[m.group(2)] = value
        if unparsed:
            rec["Unparsed"] = unparsed
        if abstract or code is None and kind is None:
            rec["Abstract"] = True
        done[name] = rec
        return rec

    creatures = [build(n) for n in sorted(classes) if n != "BaseCreature" and is_creature(n)]
    usable = [c for c in creatures if not c.get("Abstract")]
    templates, records = assign_outfits(engine, classes, usable, outfits)
    for c in usable:
        outfit, notes = records.get(c["Type"], (None, []))
        if outfit:
            c["Outfit"] = outfit
        if notes:
            c["OutfitUnparsed"] = notes
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"Source": "ServUO scripts (numbers only)", "Count": len(usable), "Creatures": usable}, f, indent=1)
    outfits_out = os.path.join(os.path.dirname(out), "Outfits.json")
    with open(outfits_out, "w", encoding="utf-8") as f:
        json.dump({"Source": "ServUO scripts: shared NPC outfits (a creature's Outfit {\"Base\": name} means this outfit "
                             "plus its own Items, with its own Female/Skin/Hair/Beard replacing these)",
                   "Outfits": templates}, f, indent=1)
    unparsed = sum(1 for c in usable if c.get("Unparsed"))
    print(f"{len(creatures)} creature classes, {len(usable)} constructable, {unparsed} with unparsed constructor lines -> {out}")
    humans = [c for c in usable if outfits.get(c["Type"], (0, 0, False))[2]]
    dressed = sum(1 for c in usable if c.get("Outfit"))
    print(f"{dressed} with an Outfit ({sum(1 for c in humans if c.get('Outfit'))} of {len(humans)} human-bodied), "
          f"{sum(1 for c in usable if c.get('OutfitUnparsed'))} with OutfitUnparsed, {len(templates)} shared outfits -> {outfits_out}")
    bare = [c["Type"] for c in humans if not c.get("Outfit")]
    if bare:
        print("human-bodied with no Outfit: " + ", ".join(bare))
    used = set()
    for c in usable:
        outfit_types(c.get("Outfit"), used)
    for t in templates.values():
        outfit_types(t, used)
    missing = sorted(t for t in used if t not in engine.items)
    if missing:
        print("item types not in Items.json: " + ", ".join(missing))


if __name__ == "__main__":
    main()
