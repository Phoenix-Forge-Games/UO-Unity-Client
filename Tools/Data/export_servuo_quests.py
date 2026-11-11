"""One-time migration of ServUO's Mondain's Legacy-style quests (the BaseQuest / MondainQuester framework) into
Content/Quests.json: every quest class (keyed by class name) with its texts, chain, restart delay, offer restrictions,
objectives and rewards; every quest-giver (MondainQuester and its escortable subclasses, plus quest-giving items) with
its name, title, looks, speech and the quests it offers; where each quest-giver stands in the world; and what the quests
use that our own data (Items, Creatures, Spawns, Regions) doesn't have yet. The game owns the file from then on.

  python -I export_servuo_quests.py <ServUO root> <Content/Quests.json>

Read from ServUO and asserted (the script stops if the code doesn't look as expected):
  Scripts/Quests/BaseQuest.cs                          the quest defaults (DoneOnce, AllObjectives, RestartDelay ...)
  Scripts/Services/MondainsLegacyQuests/QuestObjectives.cs  every objective constructor overload; a quest's
                                                       AddObjective(new X(...)) is bound to the overload its arguments
                                                       fit and followed through this(...) to the full constructor, so
                                                       the defaults (escort fame 10, compassion 200 ...) come from code
  Scripts/Services/MondainsLegacyQuests/BaseReward.cs  BaseReward's overloads, the same way
  Scripts/Services/MondainsLegacyQuests/QuestChains.cs the QuestChain enum
  Scripts/Services/MondainsLegacyQuests/TierQuest.cs   the tiered questers (Percolem, Thepem, Zosilem): tier, quests to
                                                       complete first, cooldown (their quests' RestartDelay)
  Data/Mondain's Legacy/Settings.xml                   the MondainsLegacy feature switches CanOffer checks
  Scripts/**                                           every class deriving BaseQuest / MondainQuester / BaseQuestItem,
                                                       custom objective subclasses (nested InternalObjective etc.)
  Spawns/**, RevampedSpawns/**, SpawnsOld/** (XmlSpawner xml), Data/**/*.cfg (decoration), Scripts/** (code that does
  new X().MoveToWorld(new Point3D(..), Map.Y))          where the quest-givers stand
Cliloc numbers stay numbers and literal strings stay strings; where ServUO's source has the English next to a cliloc
number as a comment it is kept under "Texts" (a reading aid only, the cliloc stays the source of truth). The newest era is
taken where code checks Core.XX (CanOffer "return Core.SA;" is true).

Hand-written below (with the code they come from): the output names of each objective constructor's parameters
(OBJECTIVE_FIELDS), the CanOffer patterns understood (CAN_OFFER), the facet numbers (FACETS). Everything else is parsed.

At the end the script prints (and writes under "Missing") the item types, creature types and region names the quests use
that Content/Items.json, Creatures.json and Regions.json don't have, the quest-givers not in Creatures.json, and the
quest-givers that Content/Spawns.json doesn't place.
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

FACETS = {"Felucca": 0, "Trammel": 1, "Ilshenar": 2, "Malas": 3, "Tokuno": 4, "TerMur": 5}

# Output names for the parameters of each objective class's full constructor (QuestObjectives.cs; BaseObjective's
# (maxProgress, seconds) for custom objectives). The parameter names are asserted against the parsed constructors.
OBJECTIVE_FIELDS = {
    "SlayObjective": ("Slay", {"creatures": "Creatures", "name": "Name", "amount": "Amount", "region": "Region",
                               "seconds": "Seconds"}),
    "ObtainObjective": ("Obtain", {"obtain": "Type", "name": "Name", "amount": "Amount", "image": "Image",
                                   "seconds": "Seconds", "hue": "Hue"}),
    "DeliverObjective": ("Deliver", {"delivery": "Type", "deliveryName": "Name", "amount": "Amount",
                                     "destination": "Receiver", "destName": "ReceiverName", "seconds": "Seconds"}),
    "EscortObjective": ("Escort", {"region": "Region", "fame": "Fame", "compassion": "Compassion",
                                   "seconds": "Seconds", "label": "Label"}),
    "ApprenticeObjective": ("Apprentice", {"skill": "Skill", "cap": "Cap", "region": "Region",
                                           "enterRegion": "EnterMessage", "leaveRegion": "LeaveMessage"}),
    "QuestionAndAnswerObjective": ("QuestionAndAnswer", {"count": "Count", "table": "Table"}),
    "BaseObjective": ("Custom", {"maxProgress": "Amount", "seconds": "Seconds"}),
    "SimpleObjective": ("Custom", {"amount": "Amount", "seconds": "Seconds"}),
}
FRAMEWORK_DIR = "Scripts/Services/MondainsLegacyQuests"

# CanOffer bodies understood (whitespace-normalised, comments and #region lines removed). Each is a full-body regex;
# its groups become the restriction. Anything else is kept as {"Note": <code>}.
CAN_OFFER = [
    (r"return MondainsLegacy\.(\w+);", lambda m: [{"Kind": "Feature", "Feature": m.group(1)}]),
    (r"return Core\.(\w+);", lambda m: []),  # newest era: true
    # the apprentice (New Haven skill trainer) quests: not while under a Scroll of Alacrity, and only below the cap
    (r"PlayerMobile pm = (?:this\.)?Owner as PlayerMobile; if \(pm\.AcceleratedStart > DateTime\.UtcNow\) \{ "
     r"(?:this\.)?Owner\.SendLocalizedMessage\((\d+)\); return false; \} else return (?:this\.)?Owner\.Skills\.(\w+)\.Base < (\d+);",
     lambda m: [{"Kind": "SkillBelow", "Skill": m.group(2), "Value": int(m.group(3))},
                {"Kind": "NoAcceleratedSkillGain", "Message": int(m.group(1))}]),
    # bard masteries (SirBerran, SirFelean, SirHareus)
    (r"if \(Owner == null\) return false; if \(Owner\.Skills\[SkillName\.(\w+)\]\.Base < (\d+) \|\| Owner\.Skills\[SkillName\.(\w+)\]\.Base < (\d+)\) "
     r"\{ Owner\.SendLocalizedMessage\((\d+)\); return false; \} foreach \(BaseQuest q in Owner\.Quests\) \{ if \(q is (\w+) \|\| q is (\w+)\) "
     r"\{ Owner\.SendLocalizedMessage\((\d+)\); return false; \} \} return true;",
     lambda m: [{"Kind": "SkillAtLeast", "Skill": m.group(1), "Value": int(m.group(2)), "Message": int(m.group(5))},
                {"Kind": "SkillAtLeast", "Skill": m.group(3), "Value": int(m.group(4)), "Message": int(m.group(5))},
                {"Kind": "NotOnQuest", "Quests": [m.group(6), m.group(7)], "Message": int(m.group(8))}]),
    # Myrmidex invasion (Eodon): the player's allegiance
    (r"if \(!MyrmidexInvasionSystem\.CanRecieveQuest\(Owner, Allegiance\.(\w+)\)\) \{ Owner\.SendLocalizedMessage\((\d+)\); return false; \} return true;",
     lambda m: [{"Kind": "Allegiance", "Allegiance": m.group(1), "Message": int(m.group(2))}]),
    (r"return MyrmidexInvasionSystem\.CanRecieveQuest\(Owner, Allegiance\.(\w+)\);",
     lambda m: [{"Kind": "Allegiance", "Allegiance": m.group(1)}]),
]


# ------------------------------------------------------------------ source reading
def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def mask_source(s):
    """(code, mask): code has comments blanked; mask also blanks string/char literal contents. Both keep every offset
    (newlines too), so a position found in the mask slices the same text out of the code or the raw source."""
    code, mask = list(s), list(s)
    i, n = 0, len(s)

    def blank(arr, a, b):
        for k in range(a, b):
            if arr[k] != "\n":
                arr[k] = " "

    while i < n:
        c = s[i]
        if s.startswith("//", i):
            j = s.find("\n", i)
            j = n if j < 0 else j
            blank(code, i, j); blank(mask, i, j)
            i = j
        elif s.startswith("/*", i):
            j = s.find("*/", i + 2)
            j = n if j < 0 else j + 2
            blank(code, i, j); blank(mask, i, j)
            i = j
        elif c == '"' or s.startswith('@"', i) or s.startswith('$"', i):
            verbatim = c == "@"
            j = i + (1 if c == '"' else 2)
            while j < n:
                if not verbatim and s[j] == "\\":
                    j += 2
                    continue
                if s[j] == '"':
                    if verbatim and j + 1 < n and s[j + 1] == '"':
                        j += 2
                        continue
                    break
                j += 1
            blank(mask, i + 1, j)
            i = j + 1
        elif c == "'":
            j = i + 1
            while j < n and s[j] != "'":
                j += 2 if s[j] == "\\" else 1
            blank(mask, i + 1, j)
            i = j + 1
        elif c == "#" and not s[s.rfind("\n", 0, i) + 1:i].strip():
            j = s.find("\n", i)  # preprocessor line (#region / #endregion / #if)
            j = n if j < 0 else j
            blank(code, i, j); blank(mask, i, j)
            i = j
        else:
            i += 1
    return "".join(code), "".join(mask)


def close_of(mask, i):
    """Index of the bracket closing the one at mask[i]."""
    pairs = {"{": "}", "(": ")", "[": "]"}
    o = mask[i]
    c = pairs[o]
    depth = 0
    for j in range(i, len(mask)):
        if mask[j] == o:
            depth += 1
        elif mask[j] == c:
            depth -= 1
            if depth == 0:
                return j
    raise AssertionError("unbalanced brackets")


def norm(s):
    return re.sub(r"\s+", " ", s).strip()


def split_args(code, mask, generics=False):
    """Split an argument list (text inside the parentheses) at top-level commas (in a parameter list, also not inside
    generic <...>)."""
    out, depth, start = [], 0, 0
    opens, closes = ("([{<", ")]}>") if generics else ("([{", ")]}")
    for i, ch in enumerate(mask):
        if ch in opens:
            depth += 1
        elif ch in closes:
            depth -= 1
        elif ch == "," and depth == 0:
            out.append((code[start:i].strip(), mask[start:i].strip()))
            start = i + 1
    if code[start:].strip():
        out.append((code[start:].strip(), mask[start:].strip()))
    return out


class Src:
    def __init__(self, path, rel):
        self.path, self.rel = path, rel
        self.raw = read(path)
        self.code, self.mask = mask_source(self.raw)


class Cls:
    def __init__(self, name, base, src, start, end, parent):
        self.name, self.base, self.src = name, base, src
        self.start, self.end = start, end  # body span: start = index after '{', end = index of '}'
        self.parent = parent
        self.nested = []

    def find(self, pattern, flags=0):
        """Regex matches in the class's own body (mask), not inside nested classes."""
        rx = re.compile(pattern, flags)
        for m in rx.finditer(self.src.mask, self.start, self.end):
            if not any(n.start - 1 <= m.start() <= n.end for n in self.nested):
                yield m

    def first(self, pattern, flags=0):
        return next(self.find(pattern, flags), None)

    def code(self, a, b):
        return self.src.code[a:b]

    def raw(self, a, b):
        return self.src.raw[a:b]


CLASS_RX = re.compile(r"\b(?:class|struct|interface)\s+(\w+)\s*(?:<[^>{]*>)?\s*(?::\s*([\w.<>, ]+?))?\s*(?:where\b[^{]*)?\{")


def index_classes(root):
    classes = {}
    scripts = os.path.join(root, "Scripts")
    for dp, dn, fn in os.walk(scripts):
        for f in fn:
            if not f.endswith(".cs"):
                continue
            p = os.path.join(dp, f)
            src = Src(p, os.path.relpath(p, root).replace("\\", "/"))
            stack = []
            for m in CLASS_RX.finditer(src.mask):
                ob = m.end() - 1
                cb = close_of(src.mask, ob)
                while stack and stack[-1].end < m.start():
                    stack.pop()
                parent = stack[-1] if stack else None
                bases = [b.strip().split(".")[-1] for b in (m.group(2) or "").split(",") if b.strip()]
                c = Cls(m.group(1), bases[0] if bases else None, src, ob + 1, cb, parent)
                c.bases = bases
                if parent:
                    parent.nested.append(c)
                classes.setdefault(c.name, []).append(c)
                stack.append(c)
    return classes


def descendants(classes, root_name):
    out = {root_name}
    changed = True
    while changed:
        changed = False
        for name, defs in classes.items():
            if name not in out and any(d.base in out for d in defs):
                out.add(name)
                changed = True
    out.discard(root_name)
    return out


def the_def(classes, name, base_set):
    """The definition of `name` that derives from one of base_set (names can repeat across namespaces)."""
    defs = [d for d in classes.get(name, []) if d.base in base_set]
    assert len(defs) == 1, f"{name}: {len(defs)} definitions deriving from {sorted(base_set)[:5]}"
    return defs[0]


# ------------------------------------------------------------------ values
def type_name(s):
    return s.strip().split(".")[-1]


def parse_int(s):
    s = s.strip()
    neg = s.startswith("-")
    s = s.lstrip("-").strip()
    if re.fullmatch(r"0[xX][0-9A-Fa-f]+", s):
        v = int(s, 16)
    elif re.fullmatch(r"\d+", s):
        v = int(s)
    else:
        return None
    return -v if neg else v


def unescape(lit):
    if lit.startswith("@"):
        return lit[2:-1].replace('""', '"')
    body = lit[1:-1]
    return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t", "r": "\r"}.get(m.group(1), m.group(1)), body)


class Expr(dict):
    """An argument the script couldn't reduce to a value: kept as code text."""


def evaluate(code, mask, env=None, cls=None):
    """C# expression -> value: int, float, str, bool, None, ("type", X), [values], ("enum", "A.B"), ("timespan", s),
    or Expr({"Expr": code}). env maps identifiers (constructor parameters, locals) to values; cls resolves static fields."""
    env = env or {}
    c, k = code.strip(), mask.strip()
    while c.startswith("(") and close_of(k, 0) == len(k) - 1 and not re.match(r"\(\s*\w+\s*\)\s*\S", c):
        c, k = c[1:-1].strip(), k[1:-1].strip()
    v = parse_int(c)
    if v is not None:
        return v
    if re.fullmatch(r"-?\d+\.\d*|-?\.\d+", c):
        return float(c)
    if c in ("null",):
        return None
    if c in ("true", "false"):
        return c == "true"
    if re.fullmatch(r'@?"(?:[^"\\]|\\.|"")*"', c):
        return unescape(c)
    m = re.fullmatch(r"typeof\s*\(\s*([\w.]+)\s*\)", c)
    if m:
        return ("type", type_name(m.group(1)))
    # string concatenation of literals
    parts = re.split(r"\+", k)
    if len(parts) > 1 and all(p.strip().startswith('"') for p in parts):
        vals, pos = [], 0
        for p in parts:
            seg = c[pos:pos + len(p)]
            pos += len(p) + 1
            vals.append(evaluate(seg, p, env, cls))
        if all(isinstance(v, str) for v in vals):
            return "".join(vals)
    m = re.fullmatch(r"new\s*(?:[\w.]+\s*)?\[\s*\]\s*\{", k[:k.find("{") + 1] if "{" in k else "") if k.startswith("new") else None
    if m and k.endswith("}"):
        ob = k.find("{")
        inner_c, inner_k = c[ob + 1:-1], k[ob + 1:-1]
        return [evaluate(a, b, env, cls) for a, b in split_args(inner_c, inner_k)]
    m = re.fullmatch(r"TimeSpan\.From(Seconds|Minutes|Hours|Days)\s*\(\s*([\d.]+)\s*\)", c)
    if m:
        return ("timespan", float(m.group(2)) * {"Seconds": 1, "Minutes": 60, "Hours": 3600, "Days": 86400}[m.group(1)])
    m = re.fullmatch(r"(SkillName|Race|QuestChain|Allegiance|Direction)\s*\.\s*(\w+)", c)
    if m:
        return ("enum", f"{m.group(1)}.{m.group(2)}")
    if re.fullmatch(r"[A-Za-z_]\w*", c):
        if c in env:
            return env[c]
        if cls is not None:
            f = static_field(cls, c)
            if f is not None:
                return f
    m = re.fullmatch(r"([A-Za-z_]\w*)\s*\[\s*(\w+)\s*\]", c)
    if m and m.group(2) in env:
        arr = env.get(m.group(1))
        if arr is None and cls is not None:
            arr = static_field(cls, m.group(1))
        if isinstance(arr, list):
            return arr[env[m.group(2)]]
    return Expr({"Expr": norm(c)})


def static_field(cls, name):
    """The initialiser of a field declared in cls (or its enclosing classes): `... name = <expr>;`."""
    c = cls
    while c is not None:
        m = c.first(r"(?:static|readonly|private|public|protected)[\w\s\[\]<>.,]*?\b" + name + r"\s*=\s*")
        if m:
            end = statement_end(c.src.mask, m.end())
            return evaluate(c.code(m.end(), end), c.src.mask[m.end():end], {}, c)
        c = c.parent
    return None


def statement_end(mask, i):
    depth = 0
    for j in range(i, len(mask)):
        ch = mask[j]
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == ";" and depth == 0:
            return j
    raise AssertionError("no statement end")


def jsonable(v):
    if isinstance(v, tuple):
        if v[0] == "type":
            return v[1]
        if v[0] == "enum":
            return v[1].split(".")[-1]
        if v[0] == "timespan":
            return v[1]
    if isinstance(v, list):
        return [jsonable(x) for x in v]
    return v


# ------------------------------------------------------------------ statements
def parse_statements(code, mask, a, b):
    """Statements in code[a:b]: ("if", [(cond_code, cond_mask, stmts) ..., (None, None, stmts)]), ("for", header, stmts),
    ("stmt", code, mask)."""
    out, i = [], a
    while True:
        while i < b and mask[i].isspace():
            i += 1
        if i >= b:
            return out
        if mask[i] == "{":
            j = close_of(mask, i)
            out.extend(parse_statements(code, mask, i + 1, j))
            i = j + 1
            continue
        if mask[i] == ";":
            i += 1
            continue
        kw = re.match(r"(if|for|foreach|while)\b\s*\(", mask[i:b])
        if kw:
            branches = []
            while True:
                po = i + kw.end() - 1
                pc = close_of(mask, po)
                cond = (code[po + 1:pc], mask[po + 1:pc])
                body, i = _block(code, mask, pc + 1, b)
                if kw.group(1) != "if":
                    out.append((kw.group(1), cond, body))
                    break
                branches.append((cond[0], cond[1], body))
                m = re.match(r"\s*else\b\s*", mask[i:b])
                if not m:
                    out.append(("if", branches))
                    break
                i += m.end()
                kw = re.match(r"(if)\b\s*\(", mask[i:b])
                if kw:
                    continue
                body, i = _block(code, mask, i, b)
                branches.append((None, None, body))
                out.append(("if", branches))
                break
            continue
        e = statement_end(mask, i)
        out.append(("stmt", code[i:e], mask[i:e]))
        i = e + 1


def _block(code, mask, i, b):
    while mask[i].isspace():
        i += 1
    if mask[i] == "{":
        j = close_of(mask, i)
        return parse_statements(code, mask, i + 1, j), j + 1
    e = statement_end(mask, i)
    return [("stmt", code[i:e], mask[i:e])], e + 1


def chance_of(cond):
    """`0.30 > Utility.RandomDouble()` -> 0.3; `Utility.RandomBool()` -> 0.5; else None."""
    c = norm(cond)
    m = re.fullmatch(r"([\d.]+) > Utility\.RandomDouble\(\)", c) or re.fullmatch(r"Utility\.RandomDouble\(\) < ([\d.]+)", c)
    if m:
        return float(m.group(1))
    if c == "Utility.RandomBool()":
        return 0.5
    return None


# ------------------------------------------------------------------ constructors and binding
class Ctor:
    def __init__(self, cls, params, chain, chain_args):
        self.cls, self.params, self.chain, self.chain_args = cls, params, chain, chain_args


def ctors_of(cls):
    out = []
    for m in cls.find(r"\b(?:public|protected|private|internal)\s+" + cls.name + r"\s*\("):
        po = m.end() - 1
        pc = close_of(cls.src.mask, po)
        params = []
        for pcode, pmask in split_args(cls.code(po + 1, pc), cls.src.mask[po + 1:pc], generics=True):
            pm = re.fullmatch(r"(params\s+)?([\w.\[\]<>, ]+?)\s+(\w+)(?:\s*=\s*(.+))?", norm(pcode))
            assert pm, f"{cls.name}: parameter {pcode!r}"
            params.append({"params": bool(pm.group(1)), "type": pm.group(2), "name": pm.group(3), "default": pm.group(4)})
        rest = cls.src.mask[pc + 1:pc + 400]
        cm = re.match(r"\s*:\s*(this|base)\s*\(", rest)
        chain, chain_args = None, []
        if cm:
            co = pc + 1 + cm.end() - 1
            cc = close_of(cls.src.mask, co)
            chain = cm.group(1)
            chain_args = split_args(cls.code(co + 1, cc), cls.src.mask[co + 1:cc])
        if params and params[0]["type"] == "Serial":
            continue
        out.append(Ctor(cls, params, chain, chain_args))
    return out


def fits(param, value):
    t = param["type"]
    if param["params"]:
        return False
    if t == "Type":
        return isinstance(value, tuple) and value[0] == "type" or value is None
    if t == "Type[]":
        return isinstance(value, list) or value is None
    if t == "string":
        return isinstance(value, str) or value is None or isinstance(value, Expr)
    if t in ("int", "double"):
        return isinstance(value, (int, float)) and not isinstance(value, bool) or isinstance(value, Expr)
    if t == "bool":
        return isinstance(value, bool)
    if t == "SkillName":
        return isinstance(value, tuple) and value[0] == "enum" and value[1].startswith("SkillName.")
    if t == "TimeSpan":
        return isinstance(value, tuple) and value[0] == "timespan"
    return True  # object, arrays of entries, interfaces: anything


def bind(ctor, values):
    params = ctor.params
    env = {}
    if params and params[-1]["params"]:
        fixed = params[:-1]
        if len(values) < len(fixed):
            return None
        rest = values[len(fixed):]
        if not all(isinstance(v, tuple) and v[0] == "type" for v in rest):
            if not (len(rest) == 1 and isinstance(rest[0], list)):
                return None
            rest = rest[0]
        env[params[-1]["name"]] = list(rest)
    else:
        fixed = params
        if len(values) > len(fixed):
            return None
        for p in fixed[len(values):]:
            if p["default"] is None:
                return None
    for p, v in zip(fixed, values):
        if not fits(p, v):
            return None
        env[p["name"]] = v
    for p in fixed[len(values):]:
        env[p["name"]] = evaluate(p["default"], p["default"])
    return env


class Resolver:
    def __init__(self, classes):
        self.classes = classes
        self.framework = {}
        for name in list(OBJECTIVE_FIELDS) + ["BaseReward"]:
            defs = [d for d in classes.get(name, []) if d.src.rel.startswith(FRAMEWORK_DIR)]
            assert len(defs) == 1, f"framework class {name}: {len(defs)}"
            self.framework[name] = defs[0]

    def find_class(self, name, near):
        """A class named `name`: nested in `near` or its parents first, then in near's file, then anywhere unique."""
        if name in self.framework:
            return self.framework[name]
        c = near
        while c is not None:
            for n in c.nested:
                if n.name == name:
                    return n
            c = c.parent
        same = [d for d in self.classes.get(name, []) if near is not None and d.src is near.src]
        if len(same) == 1:
            return same[0]
        defs = self.classes.get(name, [])
        assert len(defs) == 1, f"class {name}: {len(defs)} definitions (near {near.name if near else None})"
        return defs[0]

    def construct(self, cls, values, trail=None):
        """Follow `new cls(values)` through this(...)/base(...) to a framework class's full constructor.
        Returns (framework class name, env, [custom classes passed through])."""
        trail = trail or []
        matches = [(c, env) for c in ctors_of(cls) for env in [bind(c, values)] if env is not None]
        sigs = {tuple(p["type"] for p in c.params) for c, _ in matches}
        assert len(sigs) == 1, f"{cls.name}({', '.join(map(str, values))}): {len(sigs)} overloads fit"
        ctor, env = matches[0]
        if ctor.chain == "this":
            vals = [evaluate(c, m, env, cls) for c, m in ctor.chain_args]
            return self.construct(cls, vals, trail)
        if self.framework.get(cls.name) is cls:
            # a framework class's full constructor (an objective's hands (amount, seconds) on to BaseObjective)
            return cls.name, env, trail
        assert ctor.chain in ("base", None), cls.name  # no initializer: the implicit base()
        vals =[evaluate(c, m, env, cls) for c, m in ctor.chain_args]
        base = self.find_class(cls.base, cls)
        return self.construct(base, vals, trail + [cls])


# ------------------------------------------------------------------ quests
TEXT_FIELDS = ["Title", "Description", "Refuse", "Uncomplete", "Complete", "FailedMsg", "ObjectiveDescription",
               "CompleteMessage"]


def prop(cls, name):
    """(value code, value mask, span) of an overridden property's getter `return X;`, or the getter body if it's more."""
    m = cls.first(r"\boverride\s+[\w\[\]<>.?]+\s+" + name + r"\s*(=>|\{)")
    if not m:
        return None
    mask = cls.src.mask
    if m.group(1) == "=>":
        e = statement_end(mask, m.end())
        return cls.code(m.end(), e), mask[m.end():e], (m.start(), e)
    ob = m.end() - 1
    cb = close_of(mask, ob)
    g = re.match(r"\s*get\s*(=>|\{)", mask[ob + 1:cb])
    assert g, f"{cls.name}.{name}: no getter"
    gs = ob + 1 + g.end()
    if g.group(1) == "=>":
        e = statement_end(mask, gs)
        return cls.code(gs, e), mask[gs:e], (m.start(), cb)
    gc = close_of(mask, gs - 1)
    body = mask[gs:gc]
    r = re.fullmatch(r"\s*return\s+([\s\S]+?);\s*", body)
    if r:
        return cls.code(gs + r.start(1), gs + r.end(1)), mask[gs + r.start(1):gs + r.end(1)], (m.start(), cb)
    return None, cls.code(gs, gc), (m.start(), cb)


def method(cls, name):
    m = cls.first(r"\boverride\s+[\w\[\]<>.]+\s+" + name + r"\s*\(")
    if not m:
        return None
    pc = close_of(cls.src.mask, m.end() - 1)
    ob = cls.src.mask.index("{", pc)
    cb = close_of(cls.src.mask, ob)
    return ob + 1, cb


def comment_text(cls, span):
    """English next to a cliloc: the comment right before the member, or one inside it."""
    a, b = span
    raw, mask = cls.src.raw, cls.src.mask

    def comments(text):
        return [re.sub(r"\n\s*\*(?!/)", "\n", m.group(1) or m.group(2) or "")
                for m in re.finditer(r"/\*([\s\S]*?)\*/|//([^\n]*)", text)]

    inside = comments(raw[a:b + 1])
    # a comment after the member on its own last line: `... { return 1150929; } // Journey to the Athenaeum Isle`
    eol = raw.find("\n", b + 1)
    tail = raw[b + 1:eol if eol >= 0 else len(raw)]
    trailing = []
    tm = re.match(r"[ \t]*(//[^\n]*|/\*)", tail)
    if tm:
        end = raw.find("*/", b + 1) + 2 if tm.group(1) == "/*" else eol
        trailing = comments(raw[b + 1:end])
    # a comment between the previous statement and the member, unless it trails that statement on its line
    j = a - 1
    while j > cls.start and mask[j] not in ";{}":
        j -= 1
    nl = raw.find("\n", j + 1)
    before = comments(raw[nl if 0 <= nl < a else a:a])
    for texts in (inside, trailing, before):
        t = norm(" ".join(texts))
        if t:
            return t
    return None


def text_value(cls, name, texts):
    p = prop(cls, name)
    if p is None:
        return "absent"
    code, mask, span = p
    if code is None:
        return {"Code": norm(mask)}
    v = evaluate(code, mask, {}, cls)
    if isinstance(v, int) and not isinstance(v, bool):
        t = comment_text(cls, span)
        if t:
            texts[name] = t
    return jsonable(v) if not isinstance(v, Expr) else v


def item_types(classes):
    return descendants(classes, "Item")


class QuestExporter:
    def __init__(self, root, classes):
        self.root, self.classes = root, classes
        self.res = Resolver(classes)
        self.items = item_types(classes)
        self.mobiles = descendants(classes, "Mobile")
        settings = ET.parse(os.path.join(root, "Data/Mondain's Legacy/Settings.xml")).getroot()
        self.features = {e.tag: e.get("active") == "true" for e in settings}
        self.tiers = self.parse_tiers()
        self.unparsed = []

    def parse_tiers(self):
        cls = self.res.find_class("TierQuestInfo", None)
        m = cls.first(r"static\s+TierQuestInfo\s*\(\s*\)\s*\{")
        assert m, "TierQuestInfo static constructor"
        body = cls.code(m.end(), close_of(cls.src.mask, m.end() - 1))
        tiers = {}
        for qm in re.finditer(r"(\w+)\s*=\s*new\s+TierQuestInfo\s*\(\s*typeof\((\w+)\)\s*,([\s\S]*?)\)\s*;", body):
            assert qm.group(1) == qm.group(2)
            lst = []
            for chunk in re.split(r"new\s+TierInfo\s*\(", qm.group(3))[1:]:
                tm = re.match(r"\s*(\d+)\s*,\s*TimeSpan\.FromMinutes\((\d+)\)\s*,\s*((?:typeof\(\w+\)\s*,?\s*)+)\)", chunk)
                assert tm, chunk
                qs = re.findall(r"typeof\((\w+)\)", tm.group(3))
                assert qs
                lst.append({"Tier": len(lst), "ToComplete": int(tm.group(1)), "CooldownSeconds": int(tm.group(2)) * 60, "Quests": qs})
            assert lst, qm.group(1)
            tiers[qm.group(1)] = lst
        assert set(tiers) == {"Percolem", "Thepem", "Zosilem"}, tiers.keys()
        return tiers

    def tier_of(self, quest):
        for quester, lst in self.tiers.items():
            for t in lst:
                if quest in t["Quests"]:
                    return quester, t
        return None, None

    # ---- objectives and rewards
    def objective(self, quest_cls, ctor_name, values):
        cls = self.res.find_class(ctor_name, quest_cls)
        fw, env, trail = self.res.construct(cls, values)
        kind, fields = OBJECTIVE_FIELDS[fw]
        rec = {"Kind": kind}
        assert set(env) <= set(fields), f"{fw}: parameters {sorted(env)} vs {sorted(fields)}"
        for pname, out in fields.items():
            v = env.get(pname)
            if kind == "QuestionAndAnswer" and pname == "table":
                v = self.qa_table(quest_cls, v)
            v = jsonable(v)
            if v in (None, 0) and out in ("Seconds", "Image", "Hue", "Label", "Region", "EnterMessage", "LeaveMessage"):
                continue
            if out == "Seconds" and v == -1:
                continue
            rec[out] = v
        if trail:
            custom = trail[0]
            rec["Class"] = custom.name
            if values:
                rec["Args"] = [jsonable(v) for v in values]
            overrides = sorted({m.group(1) for m in custom.find(r"\boverride\s+[\w\[\]<>.]+\s+(\w+)\s*[({=]")} -
                               {"Serialize", "Deserialize"})
            if overrides:
                rec["Overrides"] = overrides
            d = prop(custom, "ObjectiveDescription")
            if d and d[0] is not None:
                v = evaluate(d[0], d[1], {}, custom)
                qm = re.fullmatch(r"Quest\.(\w+)", v.get("Expr", "")) if isinstance(v, Expr) else None
                if qm:  # the quest's own text, e.g. ObjectiveDescription => Quest.Uncomplete
                    p = prop(quest_cls, qm.group(1))
                    v = evaluate(p[0], p[1], {}, quest_cls) if p and p[0] else v
                rec["Description"] = jsonable(v)
            descr = [unescape(m.group(1)) for m in re.finditer(r'(?:m_Descr|Descriptions)\.Add\(\s*("(?:[^"\\]|\\.)*")\s*\)', custom.code(custom.start, custom.end))]
            if descr:
                rec["Description"] = descr if len(descr) > 1 else descr[0]
            if kind == "Custom":
                tm = re.findall(r"typeof\s*\(\s*(\w+)\s*\)", custom.code(custom.start, custom.end))
                if tm:
                    rec["Types"] = sorted(set(tm))
                rec["Note"] = f"custom objective {custom.name} ({custom.src.rel}); progress is counted by its own code"
        return rec

    def qa_table(self, quest_cls, v):
        """A QuestionAndAnswerEntry[]: either a literal array, or `new QuestionAndAnswerEntry[n]` filled by
        `table[i] = new QuestionAndAnswerEntry(question, new object[] { answers }, new object[] { wrong answers });`."""
        assert isinstance(v, Expr) and re.fullmatch(r"new QuestionAndAnswerEntry\[(\d+)\]", v["Expr"]), v
        size = int(re.search(r"\d+", v["Expr"]).group(0))
        entries = {}
        code = norm(quest_cls.code(quest_cls.start, quest_cls.end))
        for m in re.finditer(r"\w+\[(\d+)\] = new QuestionAndAnswerEntry\((\d+), new object\[\] \{([^}]*)\}, new object\[\] \{([^}]*)\}\);", code):
            entries[int(m.group(1))] = {"Question": int(m.group(2)),
                                        "Answers": [int(x) for x in re.findall(r"\d+", m.group(3))],
                                        "WrongAnswers": [int(x) for x in re.findall(r"\d+", m.group(4))]}
        assert sorted(entries) == list(range(size)), f"{quest_cls.name}: question table {sorted(entries)} of {size}"
        return [entries[i] for i in range(size)]

    def reward(self, quest_cls, values):
        fw, env, trail = self.res.construct(self.res.framework["BaseReward"], values)
        assert fw == "BaseReward" and set(env) == {"type", "amount", "name"}, env
        t = jsonable(env["type"])
        rec = {"Kind": "Gold" if t == "Gold" else "Item" if t else "Text"}
        if t:
            rec["Type"] = t
            rec["Amount"] = env["amount"]
        rec["Name"] = jsonable(env["name"])
        return rec

    def new_args(self, cls, code, mask, env):
        m = re.fullmatch(r"new\s+([\w.]+)\s*\(", mask[:mask.find("(") + 1].strip()) if mask.strip().startswith("new") else None
        if not m or not mask.strip().endswith(")"):
            return None, None
        po = mask.find("(")
        pc = close_of(mask, po)
        assert pc == len(mask.rstrip()) - 1, code
        vals = [evaluate(a, b, env, cls) for a, b in split_args(code[po + 1:pc], mask[po + 1:pc])]
        return type_name(m.group(1)), vals

    def run_ctor(self, cls, stmts, env, objectives, rewards, notes):
        for st in stmts:
            if st[0] == "stmt":
                code, mask = st[1], st[2]
                c = norm(code)
                m = re.fullmatch(r"(?:this\s*\.\s*)?Add(Objective|Reward)\s*\(", norm(mask)[:norm(mask).find("(") + 1]) if "(" in mask else None
                if m:
                    po = mask.find("(")
                    pc = close_of(mask, po)
                    inner_c, inner_m = code[po + 1:pc], mask[po + 1:pc]
                    if re.fullmatch(r"\s*\w+\s*", inner_m):
                        name, vals = env[inner_m.strip()]
                    else:
                        name, vals = self.new_args(cls, inner_c, inner_m, env)
                    assert name, f"{cls.name}: {c}"
                    if m.group(1) == "Objective":
                        objectives.append(self.objective(cls, name, vals))
                    else:
                        assert name == "BaseReward", f"{cls.name}: reward {name}"
                        rewards.append(self.reward(cls, vals))
                    continue
                dm = re.fullmatch(r"([\w.<>\[\]]+)\s+(\w+)\s*=\s*(new[\s\S]*)", code.strip())
                if dm:
                    off = code.index(dm.group(3))
                    env[dm.group(2)] = self.new_args(cls, dm.group(3), mask[off:off + len(dm.group(3))], env)
                    continue
                if c in ("base()",):
                    continue
                notes.append(c)
            elif st[0] == "if":
                opts, any_random = [], False
                remaining = 1.0
                for cond_c, cond_m, body in st[1]:
                    o, r = [], []
                    self.run_ctor(cls, body, dict(env), o, r, notes)
                    ch = chance_of(cond_c) if cond_c is not None else 1.0
                    any_random |= cond_c is not None and ch is not None
                    p = None if ch is None else round(remaining * ch, 4)
                    if ch is not None:
                        remaining *= 1 - ch
                    opts.append((cond_c, p, o, r))
                if any_random:
                    if any(o for _, _, o, _ in opts):
                        objectives.append({"Kind": "OneOf", "Note": "one branch is rolled when the quest is offered",
                                           "Options": [{"Chance": p, "Objectives": o} for _, p, o, _ in opts]})
                    if any(r for _, _, _, r in opts):
                        rewards.append({"Kind": "OneOf", "Options": [{"Chance": p, "Rewards": r} for _, p, _, r in opts]})
                else:
                    for cond_c, p, o, r in opts:
                        for x in o:
                            x["Condition"] = norm(cond_c) if cond_c else "else"
                        objectives.extend(o)
                        rewards.extend(r)
                        if cond_c and not o and not r:
                            notes.append("if (" + norm(cond_c) + ") ...")
            elif st[0] == "for":
                header = norm(st[1][0])
                fm = re.fullmatch(r"int (\w+) = 0; \1 < (\w+)\.Length; \1\+\+", header)
                assert fm, f"{cls.name}: for ({header})"
                arr = evaluate(fm.group(2), fm.group(2), env, cls)
                assert isinstance(arr, list), f"{cls.name}: {fm.group(2)}"
                for i in range(len(arr)):
                    e2 = dict(env)
                    e2[fm.group(1)] = i
                    self.run_ctor(cls, st[2], e2, objectives, rewards, notes)
            else:
                notes.append(f"{st[0]} ({norm(st[1][0])}) ...")

    def can_offer(self, cls):
        r = method(cls, "CanOffer")
        if r is None:
            return []
        body = norm(cls.code(*r))
        for rx, f in CAN_OFFER:
            m = re.fullmatch(rx, body)
            if m:
                out = f(m)
                for x in out:
                    if x["Kind"] == "Feature":
                        x["Active"] = self.features.get(x["Feature"])
                return out
        return [{"Kind": "Code", "Note": body}]

    def extras(self, cls, name):
        """What GiveRewards / OnAccept / OnCompleted do beyond the base: items handed over, player flags set, masteries,
        recipes, messages; the rest is kept as a note."""
        r = method(cls, name)
        if r is None:
            return None
        code, mask = cls.code(*r), cls.src.mask[r[0]:r[1]]
        out = {}
        items = []
        for m in re.finditer(r"\bnew\s+(\w+)\s*\(", mask):
            if m.group(1) in self.items:
                items.append(m.group(1))
        if items:
            out["Items"] = items
        flags = re.findall(r"Owner\.(\w+)\s*=\s*true", code)
        if flags:
            out["PlayerFlags"] = flags
        mast = re.findall(r"MasteryInfo\.LearnMastery\(Owner,\s*SkillName\.(\w+),\s*(\d+)\)", code)
        if mast:
            out["Masteries"] = [{"Skill": s, "Volume": int(v)} for s, v in mast]
        rec = re.findall(r"AcquireRecipe\(\(int\)[\w.]+\.(\w+)\)", code)
        if rec:
            out["Recipes"] = rec
        msgs = [int(x) for x in re.findall(r"SendLocalizedMessage\(\s*(\d+)", code)]
        if msgs:
            out["Messages"] = msgs
        rest = norm(mask)
        for pat in [r"(?:this\.)?Owner\.SendLocalizedMessage\([^;]*\);", r"(?:this\.)?Owner\.PlaySound\([^;]*\);",
                    r"base\.\w+\(\);", r"(?:this\.)?Owner\.\w+ = true;",
                    r"(?:this\.)?Owner\.AddToBackpack\(new \w+\(\)\);"]:
            rest = re.sub(pat, "", rest)
        if rest.strip():
            out["Note"] = norm(code)[:600]
        return out or None

    def quest(self, cls):
        texts = {}
        rec = {"Type": cls.name, "File": cls.src.rel}
        for f in TEXT_FIELDS:
            v = text_value(cls, f, texts)
            if v != "absent":
                rec[f] = v
        if texts:
            rec["Texts"] = texts
        for f, default in (("DoneOnce", False), ("AllObjectives", True), ("ForceRemember", False),
                           ("ShowDescription", True), ("ShowRewards", True), ("CanRefuseReward", False)):
            p = prop(cls, f)
            if p is not None:
                v = evaluate(p[0], p[1], {}, cls) if p[0] is not None else Expr({"Expr": norm(p[1])})
                assert isinstance(v, bool) or isinstance(v, Expr), f"{cls.name}.{f}: {p}"
                rec[f] = v if isinstance(v, bool) else v["Expr"]
            else:
                rec[f] = default
        p = prop(cls, "ChainID")
        if p is not None:
            v = evaluate(p[0], p[1])
            assert isinstance(v, tuple) and v[1].startswith("QuestChain."), (cls.name, p)
            rec["ChainID"] = v[1].split(".")[1]
        p = prop(cls, "NextQuest")
        if p is not None:
            v = evaluate(p[0], p[1])
            if v is not None:
                assert isinstance(v, tuple) and v[0] == "type", (cls.name, p)
                rec["NextQuest"] = v[1]
        p = prop(cls, "RestartDelay")
        rec["RestartDelay"] = 0
        if p is not None:
            if norm(p[0] or "") == "TierQuestInfo.GetCooldown(TierInfo, GetType())":
                q, t = self.tier_of(cls.name)
                assert t, cls.name
                rec["RestartDelay"] = t["CooldownSeconds"]
            else:
                v = evaluate(p[0], p[1]) if p[0] else None
                assert isinstance(v, tuple) and v[0] == "timespan", (cls.name, p)
                rec["RestartDelay"] = v[1]
        q, t = self.tier_of(cls.name)
        if t:
            rec["Tier"] = {"Quester": q, "Tier": t["Tier"], "ToComplete": t["ToComplete"],
                           "Note": "TierQuest.cs: the quester offers this tier once the player has done ToComplete of its quests"}
        rec["CanOffer"] = self.can_offer(cls)
        # constructor
        m = cls.first(r"\bpublic\s+" + cls.name + r"\s*\(\s*\)\s*(?::\s*base\s*\(\s*\)\s*)?\{")
        if not m:
            m = cls.first(r"\bpublic\s+" + cls.name + r"\s*\([^)]+\)\s*(?::\s*(?:base|this)\s*\([^)]*\)\s*)?\{")
            rec["Note"] = "no parameterless constructor; read from " + norm(cls.src.mask[m.start():m.end() - 1])
        assert m, f"{cls.name}: constructor"
        ob = m.end() - 1
        cb = close_of(cls.src.mask, ob)
        stmts = parse_statements(cls.src.code, cls.src.mask, ob + 1, cb)
        objectives, rewards, notes = [], [], []
        self.run_ctor(cls, stmts, {}, objectives, rewards, notes)
        rec["Objectives"] = objectives
        rec["Rewards"] = rewards
        if notes:
            rec["ConstructorNotes"] = notes
        for name, key in (("GiveRewards", "OnReward"), ("OnAccept", "OnAccept"), ("OnCompleted", "OnCompleted")):
            e = self.extras(cls, name)
            if e:
                rec[key] = e
                # items GiveRewards hands over (not Gold: the bounty quest computes its amount; see OnReward.Note)
                for it in [i for i in e.get("Items", []) if i != "Gold"] if name == "GiveRewards" else []:
                    rewards.append({"Kind": "Item", "Type": it, "Amount": 1, "Source": "GiveRewards"})
                for fl in e.get("PlayerFlags", []) if name == "GiveRewards" else []:
                    rewards.append({"Kind": "Flag", "Flag": fl, "Source": "GiveRewards"})
                for ms in e.get("Masteries", []) if name == "GiveRewards" else []:
                    rewards.append({"Kind": "Mastery", **ms, "Source": "GiveRewards"})
                for rc in e.get("Recipes", []):
                    rewards.append({"Kind": "Recipe", "Recipe": rc, "Source": name})
        return rec

    # ---- quest-givers
    def quester_quests(self, cls):
        """Types the quester offers (Quests getter), following base classes; ("OneOf", [...]) for escortables that
        pick one destination quest per spawn."""
        c = cls
        while c is not None and c.name not in ("MondainQuester", "BaseQuestItem"):
            p = prop(c, "Quests")
            if p is not None:
                code, mask, _ = p
                if code is None:
                    return {"Code": norm(mask)}, c
                v = evaluate(code, mask, {}, c)
                if v is None:
                    return [], c
                m = re.fullmatch(r"(\w+)\[(\w+)\]", v[0].get("Expr", "")) if isinstance(v, list) and len(v) == 1 and isinstance(v[0], Expr) else None
                if m:  # new Type[] { m_Quests[m_Quest] }: the escortable picks its destination when spawned
                    arr = static_field(c, m.group(1))
                    assert isinstance(arr, list)
                    return {"OneOf": [jsonable(x) for x in arr], "Note": f"{c.name} picks one of these per spawn ({m.group(2)})"}, c
                assert isinstance(v, list) and not any(isinstance(x, Expr) for x in v), f"{c.name}.Quests: {v}"
                return [jsonable(x) for x in v], c
            c = self.res.find_class(c.base, c) if c.base else None
        return [], None

    def say(self, cls, name):
        r = method(cls, name)
        if r is None:
            return None
        code = norm(cls.code(*r))
        out = []
        for m in re.finditer(r"Say(?:To)?\((?:\w+,\s*)?(Utility\.RandomMinMax\((\d+),\s*(\d+)\)|Utility\.RandomBool\(\)\s*\?\s*(\d+)\s*:\s*(\d+)|(\d+)|(\"(?:[^\"\\]|\\.)*\"))\)", code):
            if m.group(2):
                out.extend(range(int(m.group(2)), int(m.group(3)) + 1))
            elif m.group(4):
                out += [int(m.group(4)), int(m.group(5))]
            elif m.group(6):
                out.append(int(m.group(6)))
            else:
                out.append(unescape(m.group(7)))
        if not code:
            return []  # overridden to say nothing
        return {"Lines": out, "Code": code} if not out or re.sub(r"(this\.)?Say(To)?\([^;]*\);", "", code).strip() else out

    def quester(self, cls, kind):
        rec = {"Type": cls.name, "Kind": kind, "File": cls.src.rel}
        chain = []
        c = cls
        while c is not None and c.name not in ("MondainQuester", "BaseVendor", "BaseQuestItem", "Item"):
            chain.append(c)
            c = self.res.find_class(c.base, c) if c.base and c.base in self.classes else None
        rec["Base"] = cls.base
        # name / title from the parameterless constructor
        for c in chain:
            m = c.first(r"\bpublic\s+" + c.name + r"\s*\(\s*\)\s*(?::\s*(base|this)\s*\()?")
            if m:
                pc = m.end() - 1
                args = []
                if m.group(1):
                    po = m.end() - 1
                    pc = close_of(c.src.mask, po)
                    args = [evaluate(a, b, {}, c) for a, b in split_args(c.code(po + 1, pc), c.src.mask[po + 1:pc])]
                # MondainQuester(string name, string title): asserted in main
                if m.group(1) == "base" and c.base == "MondainQuester" and args and "Name" not in rec and kind == "Mobile":
                    names = ["Name", "Title"]
                    for n, v in zip(names, args):
                        rec[n] = jsonable(v) if not isinstance(v, Expr) else v["Expr"]
                elif kind == "Item" and args and "ItemID" not in rec:
                    rec["ItemID"] = jsonable(args[0]) if not isinstance(args[0], Expr) else args[0]["Expr"]
                ob = c.src.mask.index("{", pc)
                body = norm(c.code(ob + 1, close_of(c.src.mask, ob)))
                self.assignments(body, rec)
                break
        for c in chain:
            r = method(c, "InitBody")
            if r:
                self.assignments(norm(c.code(*r)), rec)
                break
        for c in reversed(chain):
            p = prop(c, "LabelNumber")
            if p and p[0]:
                rec["LabelNumber"] = jsonable(evaluate(p[0], p[1], {}, c))
        quests, owner = self.quester_quests(cls)
        rec["Quests"] = quests
        if owner is not None and owner is not cls:
            rec["QuestsFrom"] = owner.name
        for name, key in (("Advertise", "Advertise"), ("OnOfferFailed", "OfferFailed")):
            for c in chain:
                s = self.say(c, name)
                if s is not None:
                    rec[key] = s
                    break
        for c in chain:
            if method(c, "OnTalk"):
                rec["OnTalk"] = {"Class": c.name, "Code": norm(c.code(*method(c, "OnTalk")))[:600]}
                break
        for c in chain:
            r = method(c, "InitOutfit")
            if r:
                rec["Outfit"] = [m.group(1) for m in re.finditer(r"\bnew\s+(\w+)\s*\(", c.src.mask[r[0]:r[1]]) if m.group(1) in self.items]
                break
        for c in chain:
            p = prop(c, "IsActiveVendor")
            if p and p[0]:
                rec["Vendor"] = evaluate(p[0], p[1]) is True
                r = method(c, "InitSBInfo")
                if r and rec["Vendor"]:
                    rec["SBInfos"] = re.findall(r"new\s+(\w+)\s*\(", c.src.mask[r[0]:r[1]])
                break
        if cls.name in self.tiers:
            # Quests is empty: TierQuestInfo offers the quests of the tiers the player has reached
            rec["Tiers"] = self.tiers[cls.name]
            assert rec["Quests"] == [], cls.name
            rec["Quests"] = [q for t in self.tiers[cls.name] for q in t["Quests"]]
        return rec

    def assignments(self, body, rec):
        for m in re.finditer(r"(?:this\.)?(Female|Race|Body|Hue|CantWalk|HairItemID|HairHue|FacialHairItemID|FacialHairHue|Name|Title|Blessed|Direction)\s*=\s*([^;]+);", body):
            v = evaluate(m.group(2), m.group(2))
            v = jsonable(v) if not isinstance(v, Expr) else v["Expr"]
            rec.setdefault(m.group(1), v)


# ------------------------------------------------------------------ locations
def spawner_points(root):
    """(type lowercase, facet, x, y, z, source) for every XmlSpawner entry."""
    out = []
    for folder in ("Spawns", "RevampedSpawns", "SpawnsOld"):
        base = os.path.join(root, folder)
        for dp, dn, fn in os.walk(base):
            for f in sorted(fn):
                if not f.endswith(".xml"):
                    continue
                p = os.path.join(dp, f)
                rel = os.path.relpath(p, root).replace("\\", "/")
                tree = ET.parse(p)
                for pt in tree.getroot().iter("Points"):
                    facet = FACETS.get((pt.findtext("Map") or "").strip())
                    spec = pt.findtext("Objects2") or ""
                    x = int(pt.findtext("CentreX") or pt.findtext("X"))
                    y = int(pt.findtext("CentreY") or pt.findtext("Y"))
                    z = int(pt.findtext("CentreZ") or 0)
                    rng = int(pt.findtext("Range") or 0)
                    for part in spec.split(":OBJ="):
                        kind = part.split(":")[0].strip()
                        if kind.startswith("OBJ="):
                            kind = kind[4:]
                        base_t = kind.split("/")[0].split(",")[0].strip()
                        if base_t:
                            out.append((base_t.lower(), facet, x, y, z, rel, rng, kind))
    return out


def decoration_points(root):
    """Data/**/*.cfg decoration files: a 'Type gfx (props)' line, then 'x y z' lines; the facet from the folder."""
    out = []
    data = os.path.join(root, "Data")
    for dp, dn, fn in os.walk(data):
        for f in fn:
            if not f.endswith(".cfg"):
                continue
            p = os.path.join(dp, f)
            rel = os.path.relpath(p, root).replace("\\", "/")
            parts = rel.split("/")
            facets = [FACETS[x] for x in parts if x in FACETS] or ([0, 1] if "Britannia" in parts else [])
            if not facets:
                continue
            cur = None
            for line in read(p).splitlines():
                s = line.strip()
                if not s or s.startswith("#"):
                    cur = None if not s else cur
                    continue
                m = re.fullmatch(r"(-?\d+)\s+(-?\d+)\s+(-?\d+)", s)
                if m and cur:
                    for fc in facets:
                        out.append((cur.lower(), fc, int(m.group(1)), int(m.group(2)), int(m.group(3)), rel, 0, cur))
                    continue
                m = re.match(r"([A-Za-z_][\w.]*)", s)
                cur = m.group(1).split(".")[-1] if m else None
    return out


def code_points(classes, names):
    """Scripts that place a quest-giver themselves (the [Generate commands of Eodon, High Seas ...):
    `new X(...)` or `new XmlSpawner("X")` / `new XmlSpawner(someString)`, then the next
    `.MoveToWorld(new Point3D(x, y, z), Map.F)` (F can be a local `map` assigned Map.F earlier in the file)."""
    out = []
    seen = set()
    creation = re.compile(r"\bnew\s+(\w+)\s*\(\s*(\"[^\"]*\"|\w+)?[^;]*?\)")
    for defs in classes.values():
        for d in defs:
            if d.src.rel in seen:
                continue
            seen.add(d.src.rel)
            code = d.src.code
            if "MoveToWorld" not in code:
                continue
            found = []
            for m in creation.finditer(code):
                kind = m.group(1)
                if kind == "XmlSpawner" and m.group(2):
                    arg = m.group(2)
                    if arg.startswith('"'):
                        kind = arg[1:-1]
                    else:  # a string local: the last `arg = "X"` before
                        vs = list(re.finditer(r"\b" + re.escape(arg) + r"\s*=\s*\"([^\"]*)\"", code[:m.start()]))
                        kind = vs[-1].group(1) if vs else ""
                    kind = kind.split("/")[0].split(",")[0].strip()
                if kind in names:
                    found.append((m.start(), m.end(), kind))
            for i, (a, b, kind) in enumerate(found):
                stop = found[i + 1][0] if i + 1 < len(found) else len(code)
                w = re.search(r"MoveToWorld\(\s*(?:new\s+Point3D\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)|(\w+))\s*,\s*([\w.]+)\s*\)",
                              code[b:min(stop, b + 700)])
                if not w:
                    continue
                xyz = w.group(1, 2, 3)
                if w.group(4):  # a Point3D field, e.g. Kodar.HomeLocation
                    pm = re.search(r"\b" + w.group(4) + r"\b\s*(?:=|\{\s*get\s*\{\s*return|=>)\s*new\s+Point3D\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", code)
                    if not pm:
                        continue
                    xyz = pm.group(1, 2, 3)
                facet = w.group(5)
                if not facet.startswith("Map."):
                    vs = list(re.finditer(r"\b" + re.escape(facet) + r"\s*=\s*Map\.(\w+)", code[:b + w.start()]))
                    facet = "Map." + vs[-1].group(1) if vs else facet
                facet = facet[4:] if facet.startswith("Map.") else None
                if facet in FACETS:
                    out.append((kind.lower(), FACETS[facet], int(xyz[0]), int(xyz[1]), int(xyz[2]), d.src.rel, 0, kind))
    return out


# ------------------------------------------------------------------ old engine
def old_engine(classes):
    out = []
    for name in sorted(descendants(classes, "QuestSystem")):
        for d in classes[name]:
            if d.base in descendants(classes, "QuestSystem") | {"QuestSystem"}:
                out.append({"Type": name, "File": d.src.rel})
    return out


# ------------------------------------------------------------------ main
def main():
    root, out_path = sys.argv[1], sys.argv[2]
    content = os.path.dirname(os.path.abspath(out_path))
    classes = index_classes(root)
    ex = QuestExporter(root, classes)

    # QuestChains.cs: the QuestChain enum and BaseChain.Chains, each chain's quests in order
    chain_src = read(os.path.join(root, FRAMEWORK_DIR, "QuestChains.cs"))
    em = re.search(r"enum\s+QuestChain\s*\{([^}]*)\}", chain_src)
    assert em, "QuestChain enum"
    chain_names = re.findall(r"(\w+)\s*=\s*\d+", em.group(1))
    assert "None" in chain_names and len(chain_names) > 20
    chain_table = {}
    for m in re.finditer(r"Chains\[\(int\)QuestChain\.(\w+)\]\s*=\s*new\s+Type\[\]\s*\{([^}]*)\};", chain_src):
        chain_table[m.group(1)] = [type_name(t) for t in re.findall(r"typeof\(([\w.]+)\)", m.group(2))]
    assert set(chain_table) == set(chain_names), set(chain_names) ^ set(chain_table)

    base = ex.res.find_class("BaseQuest", None)
    assert base.src.rel == "Scripts/Quests/BaseQuest.cs", base.src.rel
    for f, want in (("AllObjectives", "true"), ("DoneOnce", "false"), ("RestartDelay", "TimeSpan.Zero")):
        m = base.first(r"\bvirtual\s+[\w]+\s+" + f + r"\s*\{")
        assert m and norm(base.code(m.end(), close_of(base.src.mask, m.end() - 1))) == f"get {{ return {want}; }}", f

    quest_names = sorted(descendants(classes, "BaseQuest"))
    quests = {}
    for name in quest_names:
        cls = the_def(classes, name, {"BaseQuest"} | set(quest_names))
        quests[name] = ex.quest(cls)
    for q in quests.values():
        if q.get("ChainID"):
            assert q["ChainID"] in chain_names, q["ChainID"]
        if q.get("NextQuest"):
            assert q["NextQuest"] in quests, (q["Type"], q["NextQuest"])
            quests[q["NextQuest"]].setdefault("PrecededBy", []).append(q["Type"])
    for ch, lst in chain_table.items():
        for t in lst:
            assert t in quests, (ch, t)

    mq = ex.res.find_class("MondainQuester", None)
    assert any([p["name"] for p in c.params] == ["name", "title"] for c in ctors_of(mq)), "MondainQuester(name, title)"
    quester_names = descendants(classes, "MondainQuester")
    questers = {}
    for name in sorted(quester_names):
        defs = [d for d in classes[name] if d.base in quester_names | {"MondainQuester"}]
        assert len(defs) == 1, name
        cls = defs[0]
        if re.search(r"\babstract\s+class\s+" + name + r"\b", cls.src.mask[max(0, cls.start - 200):cls.start]):
            continue
        questers[name] = ex.quester(cls, "Mobile")
    item_names = descendants(classes, "BaseQuestItem")
    quest_items = {}
    for name in sorted(item_names):
        cls = the_def(classes, name, item_names | {"BaseQuestItem"})
        quest_items[name] = ex.quester(cls, "Item")

    # which quester offers each quest
    for qn, qr in list(questers.items()) + list(quest_items.items()):
        qs = qr["Quests"]
        lst = qs.get("OneOf", []) if isinstance(qs, dict) else qs
        for t in lst:
            assert isinstance(t, str) and t in quests, (qn, t)
            quests[t].setdefault("OfferedBy", []).append(qn)
    for t, (q, tlist) in {t: ex.tier_of(t) for t in quests}.items():
        if q and q not in quests[t].get("OfferedBy", []):
            quests[t].setdefault("OfferedBy", []).append(q)
    # quests no quest-giver offers and no quest leads to: started by their own code (Town Cryer news, shrines, items,
    # a quester's custom OnTalk ...); where ServUO's scripts name the quest, list the files
    started_elsewhere = {}
    srcs = {d.src.rel: d.src for defs in classes.values() for d in defs}
    for t, q in quests.items():
        if q.get("OfferedBy") or q.get("PrecededBy"):
            continue
        rx = re.compile(r"typeof\s*\(\s*(?:[\w.]+\.)?" + t + r"\s*\)")
        files = sorted(rel for rel, s in srcs.items() if rel != q["File"] and t in s.code and rx.search(s.code))
        started_elsewhere[t] = files
        q["StartedBy"] = files

    # locations
    lower = {n.lower(): n for n in list(questers) + list(quest_items)}
    pts = spawner_points(root) + decoration_points(root) + code_points(classes, set(lower.values()))
    for t, facet, x, y, z, rel, rng, spec in pts:
        n = lower.get(t)
        if not n:
            continue
        rec = questers.get(n) or quest_items.get(n)
        loc = {"Facet": facet, "X": x, "Y": y, "Z": z, "Source": rel}
        if rng:
            loc["Range"] = rng
        if spec.split("/")[0] != spec:
            loc["Spec"] = spec
        if not any((l["Facet"], l["X"], l["Y"], l["Z"]) == (facet, x, y, z) for l in rec.setdefault("Locations", [])):
            rec["Locations"].append(loc)
    for rec in list(questers.values()) + list(quest_items.values()):
        rec.setdefault("Locations", [])
        rec["Locations"].sort(key=lambda l: (l["Facet"] != 1, l["Facet"], l["X"], l["Y"]))

    # cross-check with our data
    def load(name):
        with open(os.path.join(content, name), encoding="utf-8-sig") as f:
            return json.load(f)
    our_items = {i["Type"] for i in load("Items.json")["Items"]}
    our_creatures = {c["Type"] for c in load("Creatures.json")["Creatures"]}
    our_regions = {r["Name"] for r in load("Regions.json")["Regions"] if r.get("Name")}
    our_spawns = {}
    for p in load("Spawns.json")["Points"]:
        for s in p.get("Spawns", []):
            our_spawns.setdefault(s["Type"].lower(), []).append(p)

    used_items, used_creatures, used_regions = {}, {}, {}

    def walk_objs(qname, objs):
        for o in objs:
            if o["Kind"] == "OneOf":
                for opt in o["Options"]:
                    walk_objs(qname, opt["Objectives"])
                continue
            if o["Kind"] == "Slay":
                for c in o["Creatures"]:
                    used_creatures.setdefault(c, set()).add(qname)
            if o["Kind"] in ("Obtain", "Deliver"):
                used_items.setdefault(o["Type"], set()).add(qname)
            if o["Kind"] == "Deliver":  # the receiver is a mobile type too
                used_creatures.setdefault(o["Receiver"], set()).add(qname)
            if o.get("Region") and isinstance(o["Region"], str):
                used_regions.setdefault(o["Region"], set()).add(qname)

    def walk_rewards(qname, rws):
        for r in rws:
            if r["Kind"] == "OneOf":
                for opt in r["Options"]:
                    walk_rewards(qname, opt["Rewards"])
            elif r.get("Type"):
                used_items.setdefault(r["Type"], set()).add(qname)

    for q in quests.values():
        walk_objs(q["Type"], q["Objectives"])
        walk_rewards(q["Type"], q["Rewards"])
        for t in q.get("OnAccept", {}).get("Items", []):
            used_items.setdefault(t, set()).add(q["Type"])
    for n, r in questers.items():
        for t in r.get("Outfit", []):
            used_items.setdefault(t, set()).add(n)

    def missing(used, ours):
        return {k: sorted(v) for k, v in sorted(used.items()) if k not in ours}

    missing_items = missing(used_items, our_items)
    missing_creatures = missing(used_creatures, our_creatures)
    missing_regions = missing(used_regions, our_regions)
    questers_not_in_creatures = sorted(n for n in questers if n not in our_creatures)
    questers_not_in_spawns = sorted(n for n in questers if n.lower() not in our_spawns and questers[n]["Locations"])
    for n, r in questers.items():
        r["InOurSpawns"] = n.lower() in our_spawns
    unplaced = sorted(n for n, r in questers.items() if not r["Locations"])

    objective_kinds, reward_kinds = {}, {}
    for q in quests.values():
        def count(objs):
            for o in objs:
                objective_kinds[o["Kind"]] = objective_kinds.get(o["Kind"], 0) + 1
                if o["Kind"] == "OneOf":
                    for opt in o["Options"]:
                        count(opt["Objectives"])
        count(q["Objectives"])

        def rcount(rws):
            for r in rws:
                reward_kinds[r["Kind"]] = reward_kinds.get(r["Kind"], 0) + 1
                if r["Kind"] == "OneOf":
                    for opt in r["Options"]:
                        rcount(opt["Rewards"])
        rcount(q["Rewards"])

    data = {
        "Source": "ServUO Scripts (BaseQuest / MondainQuester / BaseQuestItem classes, Services/MondainsLegacyQuests), "
                  "Spawns/RevampedSpawns/SpawnsOld xml, Data/**/*.cfg; exported by Tools/Data/export_servuo_quests.py",
        "Note": "Texts: cliloc numbers stay numbers, literal strings stay strings; 'Texts' holds the English ServUO keeps "
                "beside a cliloc as a comment (reading aid). RestartDelay and Seconds are seconds. Locations are UO tiles; "
                "Facet 0 Felucca, 1 Trammel, 2 Ilshenar, 3 Malas, 4 Tokuno, 5 Ter Mur.",
        "Counts": {"Quests": len(quests), "Questers": len(questers), "PlacedQuesters": len(questers) - len(unplaced),
                   "QuestItems": len(quest_items), "Objectives": dict(sorted(objective_kinds.items())),
                   "Rewards": dict(sorted(reward_kinds.items()))},
        "Chains": {k: v for k, v in chain_table.items() if v},
        "Quests": [quests[k] for k in sorted(quests)],
        "Questers": [questers[k] for k in sorted(questers)],
        "QuestItems": [quest_items[k] for k in sorted(quest_items)],
        "Unplaced": unplaced,
        "StartedElsewhere": started_elsewhere,
        "Missing": {
            "Items": missing_items, "Creatures": missing_creatures, "Regions": missing_regions,
            "QuestersNotInCreatures": questers_not_in_creatures,
            "QuestersNotInOurSpawns": questers_not_in_spawns,
        },
        "NotExported": {
            "Note": "The old quest engine (QuestSystem / QuestObjective: Haven, Solen, Ninja, Samurai, Dark Tides ...) is "
                    "story code with hand-built gumps and conversations; not exported.",
            "StoryQuests": old_engine(classes),
        },
    }
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write("\n")

    print(f"{len(quests)} quests, {len(questers)} questers ({len(questers) - len(unplaced)} placed), {len(quest_items)} quest items")
    print("objectives:", data["Counts"]["Objectives"])
    print("rewards:", data["Counts"]["Rewards"])
    print("unplaced questers:", ", ".join(unplaced) or "-")
    print("missing items:", ", ".join(missing_items) or "-")
    print("missing creatures:", ", ".join(missing_creatures) or "-")
    print("missing regions:", ", ".join(missing_regions) or "-")
    print("questers not in Creatures.json:", ", ".join(questers_not_in_creatures) or "-")
    print("placed questers our Spawns.json doesn't place:", ", ".join(questers_not_in_spawns) or "-")


main()
