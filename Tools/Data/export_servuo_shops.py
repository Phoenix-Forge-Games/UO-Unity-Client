"""Extracts every vendor shop (data only) from ServUO's C# scripts into Content/Shops.json.

  python -I export_servuo_shops.py <ServUO Scripts folder> <Items.json> <Shops.json out>

A ServUO shop is an SBInfo subclass (Scripts/VendorInfo/SB*.cs and a few elsewhere). Its BuyInfo list (usually a nested
InternalBuyInfo : List<GenericBuyInfo>) is what the vendor SELLS to players; its SellInfo (usually a nested
InternalSellInfo : GenericSellInfo) is what the vendor BUYS from players. Names below are from the player's side.

The shop is read by interpreting, in a small C# subset, how ServUO builds it: the SBInfo's field initialisers and
constructor, the list classes' constructors, the helper methods they call, local variables, for/foreach loops, static
tables (Loot.RegularScrollTypes, PresetMapEntry.Table, Tuple arrays...), if/else and switch. Core.<expansion> checks
(and static flags that just return one, like BaseHouse.NewVendorSystem => Core.AOS) are taken as true, Siege as false,
the same as the sibling exporters. Any other condition is kept: both branches are emitted and each entry gets an
"If": "<C# condition>" (conditions of nested blocks joined with &&; a random switch case reads
"Utility.Random(3) == 0"). Nothing of ServUO's code is reused: the game re-implements the rules; these are the numbers.

Output:
{"Source": "...",
 "Notes": {...how ServUO turned these numbers into live prices...},
 "Shops": {
   "<SBInfo class>": {                       nested SBInfo classes are "<Outer>.<Inner>" (e.g. "EllieRafkin.InternalSB")
     "File": "<script path>",
     "Derived": "<text>",                    only when the list is computed rather than written out (SBFurniture)
     "Sells": [                              what the vendor sells to players, in ServUO's order
       {"Type": <Items.json Type, or Creatures.json Type when Creature>,
        "Price": <int> | {"Min": a, "Max": b},
        "Stock": <int> | {"Min": a, "Max": b},   the restock amount the vendor holds (see Notes.Economy)
        "DeclaredStock": <int|range>,            only when Stock differs from what the script wrote (economy items)
        "Stackable": true,                       GenericBuyInfo's 'stacks' flag (economy item)
        "ItemID": <int>,                         the graphic shown in the buy list
        "Hue": <int> | "<rule>",                 omitted when 0; rules: "Dyed", "Neutral", "Blue"... (Utility.Random<X>Hue)
        "Name": <cliloc int> | "<text>",         only when the script names it
        "Args": {"<ctor param>": v} | [v...],    extra constructor arguments for the item (carpet graphic, charges...)
        "Creature": true, "ControlSlots": n,     AnimalBuyInfo: a pet/mount; Type is a creature
        "Content": "<BeverageType>",             BeverageBuyInfo: what the container holds
        "If": "<C# condition>",                  only when conditional (see above)
        "NotInItems": true | "NotInCreatures": true   Type missing from Items.json / Creatures.json (kept, flagged)
       }, ...],
     "Buys": [                               what the vendor buys from players
       {"Type": ..., "Price": <int>, "If"?, "NotInItems"?}, ...]
   }, ...},
 "Vendors": {"<vendor class>": [{"Shop": "<SBInfo key>", "If"?: "<condition>"}, ...]},
     every BaseVendor class with shops, from its (possibly inherited) InitSBInfo; conditions as above
 "Conditions": {"<name>": "<getter>"},       what instance flags used in "If" mean (IsTokunoVendor -> Map == Map.Tokuno)
 "Unparsed": {"<shop or vendor>": ["<statement>", ...]}}

SBFurniture / SBCarpentryBuyback build their lists at runtime from the carpentry craft list
(Scripts/Custom/FurnitureVendor.cs). That computation is reproduced here from Services/Craft/DefCarpentry.cs, the
item graphics in Items.json and the other shops' prices, and the result written out like any other shop.
"""
import json
import os
import re
import sys

ERAS = {"AOS", "SE", "ML", "SA", "HS", "TOL", "EJ", "UOR", "UOTD", "LBR", "T2A", "KR", "UOSA", "Expansion"}
HUE_RULES = {"RandomDyedHue": "Dyed", "RandomNeutralHue": "Neutral", "RandomNondyedHue": "Nondyed",
             "RandomBlueHue": "Blue", "RandomGreenHue": "Green", "RandomRedHue": "Red", "RandomYellowHue": "Yellow",
             "RandomOrangeHue": "Orange", "RandomPinkHue": "Pink", "RandomSnakeHue": "Snake", "RandomBirdHue": "Bird",
             "RandomSlimeHue": "Slime", "RandomAnimalHue": "Animal", "RandomMetalHue": "Metal",
             "RandomBrightHue": "Bright", "RandomSkinHue": "Skin", "RandomHairHue": "Hair"}
MAX_STEPS = 200000


# ----------------------------------------------------------------------------------------------------------- lexing
TOKEN = re.compile(r"""
    (?P<ws>\s+)
  | (?P<lc>//[^\n]*)
  | (?P<bc>/\*.*?\*/)
  | (?P<pp>\#[^\n]*)
  | (?P<vstr>@"(?:[^"]|"")*")
  | (?P<istr>\$"(?:[^"\\]|\\.)*")
  | (?P<str>"(?:[^"\\\n]|\\.)*")
  | (?P<chr>'(?:[^'\\]|\\.)+')
  | (?P<num>0[xX][0-9A-Fa-f]+[uUlL]*|\d+\.\d*(?:[eE][-+]?\d+)?[fFdDmM]?|\.\d+[fFdDmM]?|\d+(?:[eE][-+]?\d+)?[fFdDmMuUlL]*)
  | (?P<id>@?[A-Za-z_]\w*)
  | (?P<op>\?\?=|<<=|>>=|=>|==|!=|<=|>=|&&|\|\||\+\+|--|\+=|-=|\*=|/=|%=|&=|\|=|\^=|\?\?|::|\?\.|[{}()\[\];,.:?<>=+\-*/%!&|^~])
""", re.X | re.S | re.M)


class Tok:
    __slots__ = ("k", "t", "pos", "line")

    def __init__(self, k, t, pos, line):
        self.k, self.t, self.pos, self.line = k, t, pos, line

    def __repr__(self):
        return self.t


def lex(text):
    toks, comments = [], {}
    pos, line = 0, 1
    while pos < len(text):
        m = TOKEN.match(text, pos)
        if not m:
            pos += 1
            continue
        k = m.lastgroup
        s = m.group(0)
        if k == "lc":
            comments[line] = s[2:].strip()
        elif k not in ("ws", "bc", "pp"):
            toks.append(Tok(k, s, pos, line))
        line += s.count("\n")
        pos = m.end()
    return toks, comments


def close(toks, i):
    """Index of the bracket closing toks[i] (one of ( [ {)."""
    pairs = {"(": ")", "[": "]", "{": "}"}
    stack = []
    for j in range(i, len(toks)):
        t = toks[j].t
        if t in pairs and toks[j].k == "op":
            stack.append(pairs[t])
        elif stack and t == stack[-1] and toks[j].k == "op":
            stack.pop()
            if not stack:
                return j
    return len(toks) - 1


def src(toks, a, b):
    """Normalised source text of toks[a:b]."""
    out = ""
    prev = None
    for t in toks[a:b]:
        if prev is not None:
            glue = prev.t in ("(", "[", ".", "!", "~") or t.t in (")", "]", ".", ",", ";", "(", "[") or \
                   (t.t in ("++", "--") and prev.k == "id")
            if t.t == "(" and prev.t in ("if", "for", "foreach", "while", "switch", "&&", "||", "==", "=", ",", "?", ":",
                                         "return", "+", "-", "*", "/", "<", ">", "<=", ">=", "!="):
                glue = False
            if not glue:
                out += " "
        out += t.t
        prev = t
    return out


# ------------------------------------------------------------------------------------------------- class structure
class Member:
    def __init__(self, kind, name, toks, a, b, mods):
        self.kind, self.name, self.toks, self.a, self.b, self.mods = kind, name, toks, a, b, mods
        self.params = []      # method/ctor parameter names
        self.ptypes = []      # parameter types (text)
        self.chain = None     # ctor: ("base"|"this", [arg token ranges])
        self.body = None      # (a, b) token range of the block contents, or ("expr", a, b)
        self.init = None      # field initialiser token range


class ClassInfo:
    def __init__(self, name, full, base, path, toks, a, b, outer, comments, abstract):
        self.name, self.full, self.base, self.path, self.toks = name, full, base, path, toks
        self.a, self.b, self.outer, self.comments, self.abstract = a, b, outer, comments, abstract
        self.members = []
        self.nested = {}

    def find(self, kind, name=None):
        return [m for m in self.members if m.kind == kind and (name is None or m.name == name)]


def split_args(toks, a, b):
    """Top-level comma-separated token ranges in toks[a:b]."""
    out, depth, start = [], 0, a
    i = a
    while i < b:
        t = toks[i].t
        if toks[i].k == "op" and t in "([{":
            i = close(toks, i) + 1
            continue
        if t == "<" and toks[i].k == "op" and generic_end(toks, i, b) is not None:
            i = generic_end(toks, i, b) + 1
            continue
        if t == "," and depth == 0:
            out.append((start, i))
            start = i + 1
        i += 1
    if b > start:
        out.append((start, b))
    return out


def generic_end(toks, i, limit):
    """If toks[i] == '<' opens a generic argument list (List<GenericBuyInfo>), the index of its '>'."""
    depth = 0
    for j in range(i, min(limit, i + 40)):
        t = toks[j]
        if t.t == "<":
            depth += 1
        elif t.t == ">":
            depth -= 1
            if depth == 0:
                return j
        elif t.t in (",", ".", "[", "]", "?") or t.k == "id":
            continue
        else:
            return None
    return None


def parse_members(ci):
    toks = ci.toks
    i = ci.a + 1
    end = ci.b
    while i < end:
        start = i
        # attributes
        while i < end and toks[i].t == "[":
            i = close(toks, i) + 1
        j = i
        names = []
        kind = None
        while j < end:
            t = toks[j]
            if t.t in ("class", "struct", "interface", "enum", "delegate", "event"):
                kind = "type"
                break
            if t.t == "<" and generic_end(toks, j, end) is not None:
                j = generic_end(toks, j, end) + 1
                continue
            if t.t == "[" and j + 1 < end and toks[j + 1].t in ("]", ","):
                j = close(toks, j) + 1
                continue
            if t.t in ("(", "{", "=", ";", "=>"):
                break
            names.append(t.t)
            j += 1
        if kind == "type":
            # nested type: skip its block (handled by the class scan)
            k = j
            while k < end and toks[k].t not in ("{", ";"):
                k += 1
            i = close(toks, k) + 1 if k < end and toks[k].t == "{" else k + 1
            continue
        if j >= end:
            break
        t = toks[j].t
        name = names[-1] if names else ""
        mods = names[:-1]
        if t == "(":
            m = Member("method", name, toks, start, None, mods)
            if name == ci.name and (len(mods) == 0 or mods[-1] in ("public", "private", "protected", "internal", "static")):
                m.kind = "ctor"
            pc = close(toks, j)
            for a, b in split_args(toks, j + 1, pc):
                parts = [x for x in toks[a:b] if x.t not in ("ref", "out", "params", "this")]
                if "=" in [x.t for x in parts]:
                    parts = parts[:[x.t for x in parts].index("=")]
                if parts:
                    m.params.append(parts[-1].t)
                    m.ptypes.append(src(parts, 0, len(parts) - 1))
            k = pc + 1
            if k < end and toks[k].t == ":" and m.kind == "ctor":
                kw = toks[k + 1].t
                ac = close(toks, k + 2)
                m.chain = (kw, split_args(toks, k + 3, ac))
                k = ac + 1
            while k < end and toks[k].t == "where":
                while toks[k].t not in ("{", "=>", ";"):
                    k += 1
            if toks[k].t == "{":
                e = close(toks, k)
                m.body = (k + 1, e)
                i = e + 1
            elif toks[k].t == "=>":
                e = k
                while toks[e].t != ";":
                    e = close(toks, e) + 1 if toks[e].t in ("(", "{", "[") else e + 1
                m.body = ("expr", k + 1, e)
                i = e + 1
            else:
                i = k + 1
            m.b = i
            ci.members.append(m)
        elif t == "=" or t == ";":
            # field (possibly several declarators: a = 1, b = 2;)
            e = j
            while e < end and toks[e].t != ";":
                e = close(toks, e) + 1 if toks[e].t in ("(", "{", "[") else e + 1
            for a, b in split_args(toks, j - 1, e) if t == "=" else []:
                eq = next((x for x in range(a, b) if toks[x].t == "="), None)
                m = Member("field", toks[a].t if eq is None else toks[eq - 1].t, toks, start, e + 1, mods)
                m.init = (eq + 1, b) if eq is not None else None
                ci.members.append(m)
            if t == ";":
                m = Member("field", name, toks, start, e + 1, mods)
                ci.members.append(m)
            i = e + 1
        elif t == "{":
            e = close(toks, j)
            m = Member("prop", name, toks, start, e + 1, mods)
            # getter
            for g in range(j + 1, e):
                if toks[g].t == "get" and toks[g - 1].t in ("{", "}", ";", "]", "public", "private", "protected", "internal"):
                    if toks[g + 1].t == "{":
                        m.body = (g + 2, close(toks, g + 1))
                    elif toks[g + 1].t == "=>":
                        f = g + 2
                        while toks[f].t != ";":
                            f = close(toks, f) + 1 if toks[f].t in ("(", "{", "[") else f + 1
                        m.body = ("expr", g + 2, f)
                    break
            i = e + 1
            if i < end and toks[i].t == "=":
                f = i
                while toks[f].t != ";":
                    f = close(toks, f) + 1 if toks[f].t in ("(", "{", "[") else f + 1
                m.init = (i + 1, f)
                i = f + 1
            ci.members.append(m)
        elif t == "=>":
            e = j
            while e < end and toks[e].t != ";":
                e = close(toks, e) + 1 if toks[e].t in ("(", "{", "[") else e + 1
            m = Member("prop", name, toks, start, e + 1, mods)
            m.body = ("expr", j + 1, e)
            ci.members.append(m)
            i = e + 1
        else:
            i = j + 1


def scan_classes(root):
    classes = {}     # full name -> ClassInfo
    by_name = {}     # simple name -> [ClassInfo]
    for folder, _, files in os.walk(root):
        for f in sorted(files):
            if not f.endswith(".cs"):
                continue
            path = os.path.join(folder, f)
            text = open(path, encoding="utf-8", errors="replace").read()
            toks, comments = lex(text)
            rel = os.path.relpath(path, root).replace("\\", "/")
            stack = []  # (ClassInfo, end index)
            i = 0
            while i < len(toks):
                while stack and i > stack[-1][1]:
                    stack.pop()
                t = toks[i]
                if t.t in ("class", "struct") and t.k == "id" and i + 1 < len(toks) and toks[i + 1].k == "id":
                    name = toks[i + 1].t
                    j = i + 2
                    if j < len(toks) and toks[j].t == "<":
                        j = generic_end(toks, j, len(toks)) + 1
                    base = ""
                    if toks[j].t == ":":
                        k = j + 1
                        parts = []
                        while toks[k].t not in ("{", ",", "where"):
                            if toks[k].t == "<":
                                g = generic_end(toks, k, len(toks))
                                parts.append(src(toks, k, g + 1).replace(" ", ""))
                                k = g + 1
                                continue
                            parts.append(toks[k].t)
                            k += 1
                        base = "".join(parts)
                    while toks[j].t != "{":
                        j += 1
                    e = close(toks, j)
                    outer = stack[-1][0] if stack else None
                    full = (outer.full + "." + name) if outer else name
                    abstract = any(toks[x].t == "abstract" for x in range(max(0, i - 4), i))
                    ci = ClassInfo(name, full, base, rel, toks, j, e, outer, comments, abstract)
                    parse_members(ci)
                    if full not in classes:
                        classes[full] = ci
                        by_name.setdefault(name, []).append(ci)
                        if outer:
                            outer.nested[name] = ci
                    stack.append((ci, e))
                    i = j + 1
                    continue
                i += 1
    return classes, by_name


# --------------------------------------------------------------------------------------------------------- values
class Sym:
    """A value only known at runtime; carries its C# text."""
    def __init__(self, text):
        self.text = text

    def __repr__(self):
        return "Sym(%s)" % self.text


class OwnerSym(Sym):
    """The vendor an SBInfo was built for (new SBAlchemist(this)): never null; its members are runtime values."""
    pass


class Rng:
    def __init__(self, lo, hi):
        self.lo, self.hi = lo, hi

    def json(self):
        return {"Min": self.lo, "Max": self.hi}


class TypeV:
    def __init__(self, name):
        self.name = name


class ClassRef:
    def __init__(self, ci):
        self.ci = ci


class NsRef:
    def __init__(self, name):
        self.name = name


class Obj:
    def __init__(self, cls, fields):
        self.cls, self.fields = cls, fields


class Sink:
    """A buy list, sell table or SBInfo list being filled."""
    def __init__(self, kind):
        self.kind = kind
        self.items = []


class Hue:
    def __init__(self, rule):
        self.rule = rule


class SBRef:
    def __init__(self, key):
        self.key = key


class Return(Exception):
    def __init__(self, value):
        self.value = value


class Break(Exception):
    pass


class Continue(Exception):
    pass


def num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


# ---------------------------------------------------------------------------------------------------- interpreter
class Ctx:
    def __init__(self, ci, inst=None, conds=(), unparsed=None):
        self.ci = ci            # class whose code runs
        self.inst = inst        # instance fields dict (or None for static code)
        self.locals = {}
        self.conds = list(conds)
        self.self_sink = None   # 'this' when the class is itself a list (InternalBuyInfo)
        self.unparsed = unparsed if unparsed is not None else []


class Interp:
    def __init__(self, classes, by_name, items, creatures):
        self.classes, self.by_name, self.items, self.creatures = classes, by_name, items, creatures
        self.steps = 0
        self.static_cache = {}
        self.glossary = {}
        self.hooks = {}          # simple call name -> fn(ctx, args, node) for special contexts
        self.special_methods = {}  # (class full, method) -> fn(args)
        self.owner_names = set()   # parameter names bound to the shop's vendor

    # ---- class lookup
    def lookup_class(self, name, ci=None):
        name = name.split("<")[0]
        if "." in name:
            parts = name.split(".")
            for k in range(len(parts)):
                cand = ".".join(parts[k:])
                if cand in self.classes:
                    return self.classes[cand]
            name = parts[-1]
        c = ci
        while c is not None:
            if name in c.nested:
                return c.nested[name]
            if c.name == name:
                return c
            c = c.outer
        # nested classes of the base chain
        c = ci
        while c is not None:
            b = self.base_of(c)
            while b is not None:
                if name in b.nested:
                    return b.nested[name]
                b = self.base_of(b)
            c = c.outer
        cands = [x for x in self.by_name.get(name, []) if x.outer is None] or self.by_name.get(name, [])
        return cands[0] if cands else None

    def base_of(self, ci):
        if not ci.base:
            return None
        b = ci.base.split("<")[0]
        if b in ("List", "SBInfo", "GenericSellInfo", "Object"):
            return None
        return self.lookup_class(b, ci.outer)

    def derives(self, ci, root):
        seen = 0
        while ci is not None and seen < 50:
            if ci.base.split("<")[0].split(".")[-1] == root or ci.name == root:
                return True
            ci = self.base_of(ci)
            seen += 1
        return False

    def sink_kind(self, ci):
        c, seen = ci, 0
        while c is not None and seen < 50:
            b = c.base.replace(" ", "")
            if b.startswith("List<GenericBuyInfo>") or b.startswith("List<IBuyItemInfo>"):
                return "buy"
            if b.split(".")[-1] == "GenericSellInfo":
                return "sell"
            c = self.base_of(c)
            seen += 1
        return None

    # ---- parsing statements
    def run_block(self, ctx, toks, a, b):
        i = a
        while i < b:
            i = self.run_stmt(ctx, toks, i, b)

    def stmt_end(self, toks, i, b):
        j = i
        while j < b and toks[j].t != ";":
            j = close(toks, j) + 1 if toks[j].t in ("(", "{", "[") else j + 1
        return j

    def tick(self):
        self.steps += 1
        if self.steps > MAX_STEPS:
            raise RuntimeError("step limit")

    def run_stmt(self, ctx, toks, i, b):
        self.tick()
        t = toks[i].t
        if t == ";":
            return i + 1
        if t == "{":
            e = close(toks, i)
            self.run_block(ctx, toks, i + 1, e)
            return e + 1
        if t == "if":
            pc = close(toks, i + 1)
            cond = self.cond(ctx, toks, i + 2, pc)
            then_end = self.stmt_span(toks, pc + 1, b)
            else_a = else_end = None
            if then_end < b and toks[then_end].t == "else":
                else_a = then_end + 1
                else_end = self.stmt_span(toks, else_a, b)
            if cond is True:
                self.run_stmt(ctx, toks, pc + 1, then_end)
            elif cond is False:
                if else_a is not None:
                    self.run_stmt(ctx, toks, else_a, else_end)
            else:
                self.branch(ctx, cond.text, lambda c: self.run_stmt(c, toks, pc + 1, then_end))
                if else_a is not None:
                    self.branch(ctx, negate(cond.text), lambda c: self.run_stmt(c, toks, else_a, else_end))
            return else_end if else_a is not None else then_end
        if t == "for":
            pc = close(toks, i + 1)
            parts = []
            s = i + 2
            for _ in range(2):
                e = s
                while toks[e].t != ";":
                    e = close(toks, e) + 1 if toks[e].t in ("(", "[", "{") else e + 1
                parts.append((s, e))
                s = e + 1
            parts.append((s, pc))
            body_end = self.stmt_span(toks, pc + 1, b)
            if parts[0][1] > parts[0][0]:
                self.run_simple(ctx, toks, parts[0][0], parts[0][1])
            n = 0
            while True:
                c = self.cond(ctx, toks, parts[1][0], parts[1][1]) if parts[1][1] > parts[1][0] else True
                if c is False:
                    break
                if c is not True:
                    ctx.unparsed.append("loop condition not known: " + src(toks, i, pc + 1))
                    break
                try:
                    self.run_stmt(ctx, toks, pc + 1, body_end)
                except Break:
                    break
                except Continue:
                    pass
                for a2, b2 in split_args(toks, parts[2][0], parts[2][1]):
                    self.eval(ctx, toks, a2, b2)
                n += 1
                if n > 5000:
                    ctx.unparsed.append("loop too long: " + src(toks, i, pc + 1))
                    break
            return body_end
        if t == "foreach":
            pc = close(toks, i + 1)
            k = next(x for x in range(i + 2, pc) if toks[x].t == "in")
            var = toks[k - 1].t
            seq = self.eval(ctx, toks, k + 1, pc)
            body_end = self.stmt_span(toks, pc + 1, b)
            if isinstance(seq, list):
                for v in seq:
                    ctx.locals[var] = v
                    try:
                        self.run_stmt(ctx, toks, pc + 1, body_end)
                    except Break:
                        break
                    except Continue:
                        pass
            else:
                ctx.unparsed.append("foreach over unknown: " + src(toks, i, pc + 1))
            return body_end
        if t == "while" or t == "do":
            e = self.stmt_span(toks, i, b)
            ctx.unparsed.append(src(toks, i, e))
            return e
        if t == "switch":
            pc = close(toks, i + 1)
            subj = self.eval(ctx, toks, i + 2, pc)
            subj_text = src(toks, i + 2, pc)
            bc = close(toks, pc + 1)
            sections = []   # (labels, a, b)
            j = pc + 2
            labels, body_a = [], None
            while j < bc:
                if toks[j].t in ("case", "default"):
                    if body_a is not None and body_a < j:
                        sections.append((labels, body_a, j))
                        labels = []
                    if toks[j].t == "default":
                        labels.append(None)
                        j += 2
                    else:
                        c = j + 1
                        while toks[c].t != ":":
                            c += 1
                        labels.append((j + 1, c))
                        j = c + 1
                    body_a = j
                    continue
                j = self.stmt_span(toks, j, bc)
            if body_a is not None:
                sections.append((labels, body_a, bc))
            matched = False
            for labels_, a2, b2 in sections:
                vals = [None if l is None else self.eval(ctx, toks, l[0], l[1]) for l in labels_]
                if isinstance(subj, Sym) or isinstance(subj, Rng):
                    texts = ["default" if l is None else src(toks, l[0], l[1]) for l in labels_]
                    if None in labels_:
                        cond = "!(" + " || ".join("%s == %s" % (subj_text, src(toks, l[0], l[1])) for s in sections for l in s[0] if l is not None) + ")"
                    else:
                        cond = " || ".join("%s == %s" % (subj_text, x) for x in texts)
                    try:
                        self.branch(ctx, cond, lambda c, a2=a2, b2=b2: self.run_block(c, toks, a2, b2))
                    except Break:
                        pass
                elif not matched and (any(v == subj for v in vals if v is not None) or None in labels_):
                    matched = True
                    try:
                        self.run_block(ctx, toks, a2, b2)
                    except Break:
                        pass
            return bc + 1
        if t == "return":
            e = self.stmt_end(toks, i, b)
            raise Return(self.eval(ctx, toks, i + 1, e) if e > i + 1 else None)
        if t == "break":
            raise Break()
        if t == "continue":
            raise Continue()
        if t == "try":
            e = close(toks, i + 1)
            self.run_block(ctx, toks, i + 2, e)
            j = e + 1
            while j < b and toks[j].t in ("catch", "finally"):
                k = j + 1
                if toks[k].t == "(":
                    k = close(toks, k) + 1
                j = close(toks, k) + 1
            return j
        e = self.stmt_end(toks, i, b)
        self.run_simple(ctx, toks, i, e)
        return e + 1

    def stmt_span(self, toks, i, b):
        """End index (exclusive) of the statement starting at i."""
        t = toks[i].t
        if t == "{":
            return close(toks, i) + 1
        if t == "if":
            pc = close(toks, i + 1)
            e = self.stmt_span(toks, pc + 1, b)
            if e < b and toks[e].t == "else":
                e = self.stmt_span(toks, e + 1, b)
            return e
        if t in ("for", "foreach", "while", "using", "lock", "fixed"):
            pc = close(toks, i + 1)
            return self.stmt_span(toks, pc + 1, b)
        if t == "switch":
            pc = close(toks, i + 1)
            return close(toks, pc + 1) + 1
        if t == "do":
            e = self.stmt_span(toks, i + 1, b)
            return self.stmt_end(toks, e, b) + 1
        if t == "try":
            j = close(toks, i + 1) + 1
            while j < b and toks[j].t in ("catch", "finally"):
                k = j + 1
                if toks[k].t == "(":
                    k = close(toks, k) + 1
                j = close(toks, k) + 1
            return j
        return self.stmt_end(toks, i, b) + 1

    def branch(self, ctx, cond, fn):
        saved = ctx.conds
        ctx.conds = saved + [cond]
        try:
            fn(ctx)
        finally:
            ctx.conds = saved

    def run_simple(self, ctx, toks, a, b):
        """A declaration or expression statement."""
        # local declaration: Type name = expr  |  Type name  |  var name = expr
        j = a
        while j < b and toks[j].k == "id" or (j < b and toks[j].t in (".", "[", "]")) or \
                (j < b and toks[j].t == "<" and generic_end(toks, j, b) is not None):
            if toks[j].t == "<":
                j = generic_end(toks, j, b) + 1
                continue
            j += 1
        decl = j > a + 1 and toks[j - 1].k == "id" and toks[j - 2].k in ("id",) or \
            (j > a + 1 and toks[j - 1].k == "id" and toks[j - 2].t in ("]", ">"))
        if decl and toks[a].t not in ("return", "new", "throw") and (j == b or toks[j].t in ("=", ",")):
            for a2, b2 in split_args(toks, j - 1, b):
                eq = next((x for x in range(a2, b2) if toks[x].t == "="), None)
                name = toks[eq - 1].t if eq is not None else toks[b2 - 1].t
                ctx.locals[name] = self.eval(ctx, toks, eq + 1, b2) if eq is not None else None
            return
        if toks[a].t == "throw":
            return
        v = self.eval(ctx, toks, a, b)
        if isinstance(v, Sym) and v.text.startswith("?call "):
            ctx.unparsed.append(src(toks, a, b))

    # ---- conditions
    def cond(self, ctx, toks, a, b):
        v = self.eval(ctx, toks, a, b)
        if isinstance(v, bool):
            return v
        if isinstance(v, Sym):
            return v
        return Sym(src(toks, a, b))

    # ---- expressions
    def eval(self, ctx, toks, a, b):
        self.tick()
        p = ExprParser(toks, a, b)
        node = p.parse()
        return self.ev(ctx, node)

    def text(self, node):
        return src(node[-1][0], node[-1][1], node[-1][2])

    def ev(self, ctx, node):
        k = node[0]
        if k == "num":
            return node[1]
        if k == "str":
            return node[1]
        if k == "lit":
            return node[1]
        if k == "name":
            return self.name(ctx, node[1], node)
        if k == "member":
            obj = self.ev(ctx, node[1])
            return self.member(ctx, obj, node[2], node)
        if k == "typeof":
            return TypeV(node[1].split(".")[-1])
        if k == "paren":
            return self.ev(ctx, node[1])
        if k == "cast":
            v = self.ev(ctx, node[2])
            if node[1] in ("int", "long", "short", "byte") and isinstance(v, float):
                return int(v)
            if node[1] in ("double", "float") and num(v):
                return float(v)
            return v
        if k == "un":
            op = node[1]
            if op in ("++", "--"):
                return self.assign(ctx, node[2], "+=" if op == "++" else "-=", 1, node)
            v = self.ev(ctx, node[2])
            if op == "!":
                if isinstance(v, bool):
                    return not v
                return Sym(negate(self.text(node[2]) if not isinstance(v, Sym) else v.text))
            if op == "-":
                if num(v):
                    return -v
                return Sym(self.text(node))
            if op == "+":
                return v
            return Sym(self.text(node))
        if k == "post":
            old = self.ev(ctx, node[2])
            self.assign(ctx, node[2], "+=" if node[1] == "++" else "-=", 1, node)
            return old
        if k == "bin":
            return self.binop(ctx, node)
        if k == "ternary":
            c = self.ev(ctx, node[1])
            if c is True:
                return self.ev(ctx, node[2])
            if c is False:
                return self.ev(ctx, node[3])
            return Sym(self.text(node))
        if k == "assign":
            v = self.ev(ctx, node[3])
            return self.assign(ctx, node[2], node[1], v, node)
        if k == "index":
            obj = self.ev(ctx, node[1])
            idx = self.ev(ctx, node[2][0]) if node[2] else None
            if isinstance(obj, list) and isinstance(idx, int) and 0 <= idx < len(obj):
                return obj[idx]
            return Sym(self.text(node))
        if k == "call":
            return self.call(ctx, node)
        if k == "new":
            return self.new(ctx, node)
        if k == "array":
            return [self.ev(ctx, e) for e in node[2]]
        if k == "is":
            return Sym(self.text(node))
        if k == "lambda":
            return Sym(self.text(node))
        return Sym(self.text(node))

    def binop(self, ctx, node):
        op = node[1]
        if op in ("&&", "||"):
            x = self.ev(ctx, node[2])
            if op == "&&" and x is False or op == "||" and x is True:
                return x
            y = self.ev(ctx, node[3])
            if isinstance(x, bool) and isinstance(y, bool):
                return (x and y) if op == "&&" else (x or y)
            if isinstance(x, bool):
                return y if isinstance(y, Sym) else Sym(self.text(node[3]))
            if isinstance(y, bool):
                if op == "&&" and y is False or op == "||" and y is True:
                    return y
                return x if isinstance(x, Sym) else Sym(self.text(node[2]))
            xt = x.text if isinstance(x, Sym) else self.text(node[2])
            yt = y.text if isinstance(y, Sym) else self.text(node[3])
            return Sym("%s %s %s" % (wrap_for(xt, op), op, wrap_for(yt, op)))
        x = self.ev(ctx, node[2])
        y = self.ev(ctx, node[3])
        if op in ("==", "!=") and (isinstance(x, OwnerSym) and y is None or isinstance(y, OwnerSym) and x is None):
            return op == "!="
        # era comparisons: Core.Expansion >= Expansion.X
        if isinstance(x, NsRef) and x.name == "Core.Expansion" or isinstance(y, NsRef) and y.name == "Core.Expansion":
            if op in ("==", "!="):
                return op == "!="
            core_left = isinstance(x, NsRef) and x.name == "Core.Expansion"
            return op in ((">=", ">") if core_left else ("<=", "<"))
        if num(x) and num(y):
            if op == "+": return x + y
            if op == "-": return x - y
            if op == "*": return x * y
            if op == "/":
                if y == 0:
                    return Sym(self.text(node))
                return int(x / y) if isinstance(x, int) and isinstance(y, int) else x / y
            if op == "%": return x % y if y else Sym(self.text(node))
            if op == "<": return x < y
            if op == ">": return x > y
            if op == "<=": return x <= y
            if op == ">=": return x >= y
            if op == "==": return x == y
            if op == "!=": return x != y
            if op == "&": return x & y
            if op == "|": return x | y
            if op == "^": return x ^ y
            if op == "<<": return x << y
            if op == ">>": return x >> y
        if isinstance(x, Rng) and num(y) and op in ("+", "-", "*"):
            f = {"+": lambda v: v + y, "-": lambda v: v - y, "*": lambda v: v * y}[op]
            return Rng(f(x.lo), f(x.hi))
        if isinstance(x, str) and op == "+":
            return x + (str(y) if not isinstance(y, Sym) else "{" + y.text + "}")
        if isinstance(x, bool) and isinstance(y, bool) and op in ("==", "!="):
            return (x == y) == (op == "==")
        if op in ("==", "!=") and x is None and y is None:
            return op == "=="
        if op in ("==", "!=") and isinstance(x, str) and isinstance(y, str):
            return (x == y) == (op == "==")
        return Sym(self.text(node))

    def assign(self, ctx, target, op, value, node):
        if op != "=":
            cur = self.ev(ctx, target)
            base = op[:-1]
            if num(cur) and num(value):
                value = {"+": cur + value, "-": cur - value, "*": cur * value,
                         "/": int(cur / value) if value and isinstance(cur, int) and isinstance(value, int) else (cur / value if value else 0)}.get(base, cur)
            else:
                value = Sym(self.text(node))
        if target[0] == "name":
            n = target[1]
            if n in ctx.locals:
                ctx.locals[n] = value
            elif ctx.inst is not None:
                ctx.inst[n] = value
            else:
                ctx.locals[n] = value
            return value
        if target[0] == "member" and target[1][0] == "name" and target[1][1] == "this" and ctx.inst is not None:
            ctx.inst[target[2]] = value
            return value
        ctx.unparsed.append("assignment to " + self.text(target))
        return value

    def name(self, ctx, n, node):
        if n in ctx.locals:
            return ctx.locals[n]
        if n == "this":
            return ctx.self_sink if ctx.self_sink is not None else NsRef("this")
        if n == "true":
            return True
        if n == "false":
            return False
        if n == "null":
            return None
        if ctx.inst is not None and n in ctx.inst:
            return ctx.inst[n]
        # a member of this class (or its bases / outer classes)
        r = self.member_of_class(ctx, ctx.ci, n, node)
        if r is not NotFound:
            return r
        ci = self.lookup_class(n, ctx.ci)
        if ci is not None:
            return ClassRef(ci)
        return NsRef(n)

    def member_of_class(self, ctx, ci, n, node):
        c = ci
        while c is not None:
            b = c
            depth = 0
            while b is not None and depth < 30:
                for m in b.members:
                    if m.name == n and m.kind in ("field", "prop"):
                        static = "static" in m.mods or "const" in m.mods
                        if static:
                            return self.static_value(b, m)
                        if ctx.inst is not None and c is ci:
                            # an instance member without a value yet: a runtime flag
                            g = self.getter_text(b, m)
                            if g is not None:
                                self.glossary[n] = g
                            return Sym(n)
                        return Sym(n)
                b = self.base_of(b)
                depth += 1
            c = c.outer
        return NotFound

    def getter_text(self, ci, m):
        if m.body is None:
            return None
        toks = ci.toks
        if m.body[0] == "expr":
            return src(toks, m.body[1], m.body[2])
        a, b = m.body
        if b - a >= 2 and toks[a].t == "return" and toks[b - 1].t == ";":
            t = src(toks, a + 1, b - 1)
            if t.startswith("(") and t.endswith(")"):
                t = t[1:-1]
            return t
        return None

    def static_value(self, ci, m):
        key = (ci.full, m.name)
        if key in self.static_cache:
            return self.static_cache[key]
        self.static_cache[key] = Sym(ci.name + "." + m.name)  # recursion guard
        ctx = Ctx(ci)
        v = Sym(ci.name + "." + m.name)
        try:
            if m.kind == "field" and m.init:
                v = self.eval(ctx, ci.toks, m.init[0], m.init[1])
            elif m.kind == "prop" and m.body is not None:
                if m.body[0] == "expr":
                    v = self.eval(ctx, ci.toks, m.body[1], m.body[2])
                else:
                    try:
                        self.run_block(ctx, ci.toks, m.body[0], m.body[1])
                    except Return as r:
                        v = r.value
            elif m.kind == "prop" and m.init:
                v = self.eval(ctx, ci.toks, m.init[0], m.init[1])
        except (Break, Continue, RecursionError, StopIteration, IndexError):
            pass
        if v is None and m.kind == "prop":
            v = Sym(ci.name + "." + m.name)
        self.static_cache[key] = v
        return v

    def member(self, ctx, obj, n, node):
        if isinstance(obj, NsRef):
            full = obj.name + "." + n
            if obj.name == "Core":
                if n in ERAS:
                    return True if n != "Expansion" else NsRef("Core.Expansion")
                if n == "Siege":
                    return False
            if obj.name == "Siege" and n == "SiegeShard":
                return False
            if obj.name == "this":
                return self.name(ctx, n, node)
            ci = self.lookup_class(full)
            if ci is not None and (full in self.classes or n in [c.name for c in self.by_name.get(n, [])]):
                return ClassRef(ci)
            return NsRef(full) if not obj.name[:1].islower() else Sym(self.text(node))
        if isinstance(obj, ClassRef):
            if obj.ci.name == "Siege" and n == "SiegeShard":
                return False
            if obj.ci.name == "Core" and n in ERAS:
                return True if n != "Expansion" else NsRef("Core.Expansion")
            if n in obj.ci.nested:
                return ClassRef(obj.ci.nested[n])
            b, depth = obj.ci, 0
            while b is not None and depth < 30:
                for m in b.members:
                    if m.name == n and m.kind in ("field", "prop"):
                        return self.static_value(b, m)
                b = self.base_of(b)
                depth += 1
            return NsRef(obj.ci.name + "." + n)
        if isinstance(obj, list) and n in ("Length", "Count"):
            return len(obj)
        if isinstance(obj, Obj):
            if n in obj.fields:
                return obj.fields[n]
            low = n[:1].lower() + n[1:]
            if low in obj.fields:
                return obj.fields[low]
        if isinstance(obj, Sink) and n == "Count":
            return Sym(self.text(node))
        return Sym(self.text(node))

    def args(self, ctx, nodes):
        return [self.ev(ctx, a) for a in nodes]

    def call(self, ctx, node):
        fn, argn = node[1], node[2]
        # x.Method(args)
        if fn[0] == "member":
            mname = fn[2]
            target = fn[1]
            if target[0] == "name" and target[1] == "Utility":
                return self.utility(ctx, mname, self.args(ctx, argn), node)
            if target[0] == "name" and target[1] == "Tuple" and mname == "Create":
                vals = self.args(ctx, argn)
                return Obj("Tuple", {"Item%d" % (i + 1): v for i, v in enumerate(vals)})
            obj = self.ev(ctx, target)
            if isinstance(obj, NsRef) and obj.name == "this":
                return self.call_simple(ctx, mname, argn, node)
            if isinstance(obj, Sink):
                return self.sink_call(ctx, obj, mname, self.args(ctx, argn), node)
            if mname == "ToString" and not argn and num(obj):
                return str(obj)
            if mname == "ToString" and not argn and isinstance(obj, str):
                return obj
            if isinstance(obj, ClassRef):
                key = (obj.ci.full, mname)
                if key in self.special_methods:
                    return self.special_methods[key](self.args(ctx, argn))
                ms = obj.ci.find("method", mname)
                if ms:
                    return self.invoke(ctx, obj.ci, ms, argn, None, node)
            return Sym("?call " + self.text(node))
        if fn[0] == "name":
            return self.call_simple(ctx, fn[1], argn, node)
        return Sym("?call " + self.text(node))

    def call_simple(self, ctx, n, argn, node):
        if n in self.hooks:
            return self.hooks[n](ctx, self.args(ctx, argn), node)
        if n == "Add" and ctx.self_sink is not None:
            return self.sink_call(ctx, ctx.self_sink, "Add", self.args(ctx, argn), node)
        if n == "typeof":
            return TypeV(self.text(argn[0]))
        key = (ctx.ci.full, n)
        if key in self.special_methods:
            return self.special_methods[key](self.args(ctx, argn))
        c = ctx.ci
        while c is not None:
            ms = c.find("method", n)
            if ms:
                return self.invoke(ctx, c, ms, argn, ctx if c is ctx.ci else None, node)
            c = c.outer
        return Sym("?call " + self.text(node))

    def invoke(self, ctx, ci, methods, argn, caller, node):
        args = self.args(ctx, argn)
        cand = [m for m in methods if len(m.params) == len(args) and m.body is not None] or \
               [m for m in methods if m.body is not None]
        if not cand:
            return Sym("?call " + self.text(node))
        m = cand[0]
        key = (ci.full, m.name)
        if key in self.special_methods:
            return self.special_methods[key](args)
        sub = Ctx(ci, ctx.inst if caller is not None else None, ctx.conds, ctx.unparsed)
        sub.self_sink = ctx.self_sink if caller is not None else None
        for p, v in zip(m.params, args):
            sub.locals[p] = v
        try:
            if m.body[0] == "expr":
                return self.eval(sub, ci.toks, m.body[1], m.body[2])
            self.run_block(sub, ci.toks, m.body[0], m.body[1])
        except Return as r:
            return r.value
        return None

    def utility(self, ctx, n, args, node):
        if n in HUE_RULES:
            return Hue(HUE_RULES[n])
        if n == "Random" and len(args) == 2 and all(num(a) for a in args):
            return Rng(args[0], args[0] + args[1] - 1)
        if n == "Random" and len(args) == 1 and num(args[0]):
            return Rng(0, args[0] - 1)
        if n == "RandomMinMax" and len(args) == 2 and all(num(a) for a in args):
            return Rng(min(args), max(args))
        if n == "RandomList":
            return {"OneOf": [jsonable(a) for a in args]}
        return Sym(self.text(node))

    def sink_call(self, ctx, sink, n, args, node):
        if n == "Add":
            if sink.kind == "sell":
                if len(args) == 2:
                    sink.items.append(("sell", args, list(ctx.conds), self.text(node)))
                    return None
            elif sink.kind == "buy":
                if len(args) == 1 and isinstance(args[0], dict):
                    e = dict(args[0])
                    if ctx.conds:
                        e["If"] = join_conds(ctx.conds)
                    sink.items.append(e)
                    return None
            elif sink.kind == "sb":
                if len(args) == 1 and isinstance(args[0], SBRef):
                    e = {"Shop": args[0].key}
                    if ctx.conds:
                        e["If"] = join_conds(ctx.conds)
                    sink.items.append(e)
                    return None
            ctx.unparsed.append(self.text(node))
            return None
        if n == "Clear":
            if ctx.conds:
                ctx.unparsed.append("conditional Clear(): " + self.text(node) + " if " + " && ".join(ctx.conds))
            else:
                sink.items.clear()
            return None
        if n in ("Contains", "Any", "Exists"):
            return Sym(self.text(node))
        return Sym("?call " + self.text(node))

    # ---- object creation
    def new(self, ctx, node):
        tname, argn, init = node[1], node[2], node[3]
        if tname.endswith("[]") or tname == "[]":
            return [self.ev(ctx, e) for e in (init or [])]
        base = tname.split("<")[0].split(".")[-1]
        flat = tname.replace(" ", "")
        if base in ("GenericBuyInfo", "AnimalBuyInfo", "BeverageBuyInfo", "PresetMapBuyInfo"):
            return self.buy_info(ctx, base, self.args(ctx, argn), node)
        if flat.startswith("List<GenericBuyInfo>") or flat.startswith("List<IBuyItemInfo>"):
            return Sink("buy")
        if base == "GenericSellInfo":
            return Sink("sell")
        if flat.startswith("List<SBInfo>"):
            return Sink("sb")
        if base in ("List", "HashSet", "Dictionary"):
            if init:
                vals = [self.ev(ctx, e) for e in init]
                return vals
            return []
        ci = self.lookup_class(tname, ctx.ci)
        if ci is not None:
            if self.derives(ci, "SBInfo") or ci.base == "SBInfo":
                return SBRef(ci.full)
            kind = self.sink_kind(ci)
            if kind:
                sink = Sink(kind)
                self.construct(ctx, ci, self.args(ctx, argn), sink, None)
                return sink
            # a plain data object (PresetMapEntry, Entry...): bind constructor parameters by name
            args = self.args(ctx, argn)
            ctors = [m for m in ci.find("ctor") if len(m.params) == len(args)]
            fields = {}
            if ctors:
                for p, v in zip(ctors[0].params, args):
                    fields[p] = v
            else:
                for i, v in enumerate(args):
                    fields["arg%d" % i] = v
            if init:
                for e in init:
                    if e[0] == "assign" and e[2][0] == "name":
                        fields[e[2][1]] = self.ev(ctx, e[3])
            o = Obj(ci.name, fields)
            o.ci = ci
            o.src = self.text(node)
            return o
        return Sym(self.text(node))

    def construct(self, ctx, ci, args, sink, inst):
        """Runs a list class's constructor (and its field initialisers) with Add going into sink."""
        sub = Ctx(ci, {} if inst is None else inst, ctx.conds, ctx.unparsed)
        sub.self_sink = sink
        for m in ci.members:
            if m.kind == "field" and m.init and "static" not in m.mods and "const" not in m.mods:
                sub.inst[m.name] = self.eval(sub, ci.toks, m.init[0], m.init[1])
        ctors = [m for m in ci.find("ctor") if len(m.params) == len(args) and "static" not in m.mods]
        if not ctors:
            return sub
        m = ctors[0]
        for p, v in zip(m.params, args):
            sub.locals[p] = v
            if isinstance(v, OwnerSym):
                self.owner_names.add(p)
        if m.chain and m.chain[0] == "this":
            cargs = [self.eval(sub, ci.toks, a, b) for a, b in m.chain[1]]
            self.construct_into(sub, ci, cargs)
        if m.body and m.body[0] != "expr":
            try:
                self.run_block(sub, ci.toks, m.body[0], m.body[1])
            except Return:
                pass
        return sub

    def construct_into(self, sub, ci, args):
        ctors = [m for m in ci.find("ctor") if len(m.params) == len(args)]
        if not ctors:
            return
        m = ctors[0]
        saved = dict(sub.locals)
        for p, v in zip(m.params, args):
            sub.locals[p] = v
        if m.chain and m.chain[0] == "this":
            self.construct_into(sub, ci, [self.eval(sub, ci.toks, a, b) for a, b in m.chain[1]])
        if m.body and m.body[0] != "expr":
            try:
                self.run_block(sub, ci.toks, m.body[0], m.body[1])
            except Return:
                pass
        sub.locals = saved

    def buy_info(self, ctx, kind, a, node):
        e = {}
        try:
            if kind == "PresetMapBuyInfo":
                entry, price, amount = a
                e["Type"] = "PresetMap"
                if isinstance(entry, Obj):
                    nm = entry.fields.get("name")
                    if nm:
                        e["Name"] = nm
                    e["Args"] = {k[:1].upper() + k[1:]: jsonable(v) for k, v in entry.fields.items() if k != "name"}
                    note = getattr(entry, "comment", None)
                    if note:
                        e["Args"]["Label"] = note
                e["Price"], e["Stock"], e["ItemID"], e["Hue"] = price, amount, 0x14EC, 0
                return finish_buy(e, False)
            stacks = False
            if kind == "AnimalBuyInfo":
                e["ControlSlots"] = a[0]
                a = a[1:]
                e["Creature"] = True
            name = None
            if a and (isinstance(a[0], str) or isinstance(a[0], Sym) and not isinstance(a[1], (int, Rng))) and len(a) >= 2 and isinstance(a[1], TypeV):
                name, a = a[0], a[1:]
            elif a and a[0] is None and len(a) > 1 and isinstance(a[1], TypeV):
                a = a[1:]
            t = a[0]
            e["Type"] = t.name if isinstance(t, TypeV) else (t.fields if isinstance(t, Obj) else t)
            rest = a[1:]
            if kind == "BeverageBuyInfo":
                content = rest[0]
                e["Content"] = content.name.split(".")[-1] if isinstance(content, NsRef) else jsonable(content)
                rest = rest[1:]
            price, amount, item_id, hue = rest[:4]
            more = rest[4:]
            args = None
            if more and isinstance(more[-1], bool):
                stacks = more[-1]
                more = more[:-1]
            if more:
                args = more[0]
            if name is not None:
                if isinstance(name, str) and re.fullmatch(r"\d+", name):
                    name = int(name)
                e["Name"] = name
            e["Price"], e["Stock"], e["ItemID"], e["Hue"] = price, amount, item_id, hue
            if args is not None:
                args = jsonable(args)
                ci = self.lookup_class(e["Type"]) if isinstance(e["Type"], str) else None
                if isinstance(args, list) and ci is not None:
                    ctors = [m for m in ci.find("ctor") if len(m.params) == len(args)]
                    if ctors:
                        args = dict(zip(ctors[0].params, args))
                e["Args"] = args
            return finish_buy(e, stacks)
        except (ValueError, IndexError, AttributeError, TypeError):
            ctx.unparsed.append(self.text(node))
            return Sym(self.text(node))


class _NF:
    pass


NotFound = _NF()


def wrap(t):
    return t if re.fullmatch(r"[!\w.()]+", t) else "(" + t + ")"


def wrap_for(t, op):
    """Parenthesises t as an operand of && / || only when it holds the other one at top level."""
    other = "||" if op == "&&" else "&&"
    depth, i = 0, 0
    while i < len(t):
        c = t[i]
        depth += c == "("
        depth -= c == ")"
        if depth == 0 and t.startswith(other, i):
            return "(" + t + ")"
        i += 1
    return t


def join_conds(conds):
    return conds[0] if len(conds) == 1 else " && ".join(wrap_for(c, "&&") for c in conds)


def negate(t):
    m = re.fullmatch(r"!\s*(\w[\w.]*)", t)
    if m:
        return m.group(1)
    m = re.fullmatch(r"!\((.*)\)", t)
    if m and balanced(m.group(1)):
        return m.group(1)
    for a, b in (("!=", "=="), ("==", "!=")):
        if t.count(a) == 1 and "&&" not in t and "||" not in t and "?" not in t:
            return t.replace(a, b)
    return "!" + wrap(t)


def balanced(s):
    d = 0
    for c in s:
        d += c == "("
        d -= c == ")"
        if d < 0:
            return False
    return d == 0


def jsonable(v):
    if isinstance(v, bool) or v is None or isinstance(v, (int, float, str)):
        return v
    if isinstance(v, Rng):
        return v.json()
    if isinstance(v, Hue):
        return v.rule
    if isinstance(v, TypeV):
        return v.name
    if isinstance(v, NsRef):
        return v.name
    if isinstance(v, Sym):
        return {"Expr": v.text}
    if isinstance(v, list):
        return [jsonable(x) for x in v]
    if isinstance(v, dict):
        return {k: jsonable(x) for k, x in v.items()}
    if isinstance(v, Obj):
        return {k: jsonable(x) for k, x in v.fields.items()}
    return {"Expr": repr(v)}


def finish_buy(e, stacks):
    """Normalises a buy entry into JSON-ready values."""
    out = {}
    out["Type"] = e["Type"]
    for k in ("Creature", "ControlSlots", "Name", "Content"):
        if k in e:
            out[k] = jsonable(e[k])
    out["Price"] = jsonable(e["Price"])
    stock = jsonable(e["Stock"])
    if stacks:
        # GenericBuyInfo: EconomyItem = BaseVendor.UseVendorEconomy (Core.AOS && !Siege) && stacks
        out["Stock"] = ECONOMY_STOCK
        if stock != ECONOMY_STOCK:
            out["DeclaredStock"] = stock
        out["Stackable"] = True
    else:
        out["Stock"] = stock
    out["ItemID"] = jsonable(e["ItemID"])
    hue = jsonable(e["Hue"])
    if hue not in (0, None):
        out["Hue"] = hue
    if "Args" in e:
        out["Args"] = e["Args"]
    return out


ECONOMY_STOCK = 500
VENDOR_TYPES = {"Mobile", "BaseVendor", "BaseCreature"}


# ------------------------------------------------------------------------------------------------ expression parser
PREC = [("||",), ("&&",), ("|",), ("^",), ("&",), ("==", "!="), ("<", ">", "<=", ">=", "is", "as"), ("<<", ">>"),
        ("+", "-"), ("*", "/", "%")]
TYPE_KW = {"int", "double", "float", "long", "short", "byte", "uint", "ushort", "bool", "string", "object", "char",
           "decimal", "sbyte", "ulong"}


class ExprParser:
    def __init__(self, toks, a, b):
        self.toks, self.i, self.b, self.a = toks, a, b, a

    def peek(self, k=0):
        j = self.i + k
        return self.toks[j].t if j < self.b else None

    def node(self, *parts, start):
        return parts + ((self.toks, start, self.i),)

    def parse(self):
        if self.i >= self.b:
            return ("lit", None, (self.toks, self.i, self.i))
        start = self.i
        try:
            n = self.assignment()
        except (IndexError, ValueError, StopIteration):
            return ("raw", (self.toks, start, self.b))
        if self.i < self.b:
            return ("raw", (self.toks, start, self.b))
        return n

    def assignment(self):
        start = self.i
        left = self.ternary()
        op = self.peek()
        if op in ("=", "+=", "-=", "*=", "/=", "%=", "|=", "&=", "??="):
            self.i += 1
            right = self.assignment()
            return self.node("assign", op, left, right, start=start)
        if op == "=>":
            self.i = self.b
            return self.node("lambda", start=start)
        return left

    def ternary(self):
        start = self.i
        c = self.binary(0)
        if self.peek() == "?":
            self.i += 1
            a = self.assignment()
            if self.peek() != ":":
                raise ValueError
            self.i += 1
            b = self.assignment()
            return self.node("ternary", c, a, b, start=start)
        if self.peek() == "??":
            self.i += 1
            b = self.ternary()
            return self.node("bin", "??", c, b, start=start)
        return c

    def binary(self, level):
        if level >= len(PREC):
            return self.unary()
        start = self.i
        left = self.binary(level + 1)
        while self.peek() in PREC[level]:
            op = self.peek()
            self.i += 1
            if op in ("is", "as"):
                self.type_name()
                left = self.node("is", left, start=start)
                continue
            right = self.binary(level + 1)
            left = self.node("bin", op, left, right, start=start)
        return left

    def unary(self):
        start = self.i
        t = self.peek()
        if t in ("!", "-", "+", "~", "++", "--"):
            self.i += 1
            e = self.unary()
            return self.node("un", t, e, start=start)
        if t == "(":
            # cast?
            c = close(self.toks, self.i)
            inner = self.toks[self.i + 1:c]
            nxt = self.toks[c + 1] if c + 1 < self.b else None
            if inner and all(x.k == "id" or x.t in (".", "[", "]", "<", ">", ",") for x in inner) and nxt is not None and \
                    (inner[0].t in TYPE_KW or (nxt.k in ("id", "num", "str") or nxt.t == "(") and nxt.t not in ("is", "as")):
                tname = "".join(x.t for x in inner)
                self.i = c + 1
                e = self.unary()
                return self.node("cast", tname, e, start=start)
        return self.postfix()

    def type_name(self):
        parts = []
        while self.i < self.b:
            t = self.toks[self.i]
            if t.k == "id" or t.t == ".":
                parts.append(t.t)
                self.i += 1
            elif t.t == "<" and generic_end(self.toks, self.i, self.b) is not None:
                g = generic_end(self.toks, self.i, self.b)
                parts.append("".join(x.t for x in self.toks[self.i:g + 1]))
                self.i = g + 1
            elif t.t == "[" and self.peek(1) in ("]", ","):
                c = close(self.toks, self.i)
                parts.append("[]")
                self.i = c + 1
            elif t.t == "?":
                self.i += 1
            else:
                break
        return "".join(parts)

    def arglist(self):
        """At '(' or '{' or '['; returns the argument nodes and moves past the bracket."""
        c = close(self.toks, self.i)
        out = []
        for a, b in split_args(self.toks, self.i + 1, c):
            # named / out / ref arguments
            while a < b and self.toks[a].t in ("out", "ref", "params"):
                a += 1
            if b - a >= 2 and self.toks[a + 1].t == ":" and self.toks[a].k == "id":
                a += 2
            p = ExprParser(self.toks, a, b)
            out.append(p.parse())
        self.i = c + 1
        return out

    def postfix(self):
        start = self.i
        e = self.primary()
        while self.i < self.b:
            t = self.peek()
            if t in (".", "?."):
                self.i += 1
                n = self.toks[self.i].t
                self.i += 1
                if self.peek() == "<" and generic_end(self.toks, self.i, self.b) is not None and \
                        self.toks[generic_end(self.toks, self.i, self.b) + 1].t == "(":
                    self.i = generic_end(self.toks, self.i, self.b) + 1
                e = self.node("member", e, n, start=start)
            elif t == "(":
                args = self.arglist()
                e = self.node("call", e, args, start=start)
            elif t == "[":
                args = self.arglist()
                e = self.node("index", e, args, start=start)
            elif t in ("++", "--"):
                self.i += 1
                e = self.node("post", t, e, start=start)
            else:
                break
        return e

    def primary(self):
        start = self.i
        t = self.toks[self.i]
        if t.k == "num":
            self.i += 1
            s = t.t.rstrip("uUlLfFdDmM") if not t.t.lower().startswith("0x") else t.t.rstrip("uUlL")
            if s.lower().startswith("0x"):
                v = int(s, 16)
            elif "." in s or "e" in s.lower() or t.t[-1:] in "fFdDmM":
                v = float(s)
            else:
                v = int(s)
            return self.node("num", v, start=start)
        if t.k == "str":
            self.i += 1
            return self.node("str", bytes(t.t[1:-1], "utf-8").decode("unicode_escape", errors="replace"), start=start)
        if t.k == "vstr":
            self.i += 1
            return self.node("str", t.t[2:-1].replace('""', '"'), start=start)
        if t.k == "istr":
            self.i += 1
            return self.node("raw2", start=start)
        if t.k == "chr":
            self.i += 1
            return self.node("str", t.t[1:-1], start=start)
        if t.t == "(":
            c = close(self.toks, self.i)
            p = ExprParser(self.toks, self.i + 1, c)
            inner = p.parse()
            self.i = c + 1
            return self.node("paren", inner, start=start)
        if t.t == "typeof":
            c = close(self.toks, self.i + 1)
            name = "".join(x.t for x in self.toks[self.i + 2:c])
            self.i = c + 1
            return self.node("typeof", name, start=start)
        if t.t == "new":
            self.i += 1
            if self.peek() == "[":
                self.i = close(self.toks, self.i) + 1
                init = self.arglist()
                return self.node("new", "[]", [], init, start=start)
            if self.peek() == "{":
                init = self.arglist()
                return self.node("new", "anon", [], init, start=start)
            tname = self.type_name()
            args = []
            init = None
            if self.peek() == "[":   # new Type[n]
                self.i = close(self.toks, self.i) + 1
                tname += "[]"
            if self.peek() == "(":
                args = self.arglist()
            if self.peek() == "{":
                init = self.arglist()
            return self.node("new", tname, args, init, start=start)
        if t.t == "{":
            init = self.arglist()
            return self.node("new", "[]", [], init, start=start)
        if t.t in ("default",) and self.peek(1) == "(":
            self.i = close(self.toks, self.i + 1) + 1
            return self.node("lit", None, start=start)
        if t.k == "id":
            self.i += 1
            if self.peek() == "<" and generic_end(self.toks, self.i, self.b) is not None:
                g = generic_end(self.toks, self.i, self.b)
                if g + 1 < self.b and self.toks[g + 1].t in ("(", "."):
                    self.i = g + 1
            return self.node("name", t.t, start=start)
        raise ValueError("unexpected " + t.t)


# --------------------------------------------------------------------------------------------------- SBFurniture
def carpentry_entries(interp, classes, by_name, items, other_prices, notes):
    """Reproduces SBFurniture.BuildEntries (Scripts/Custom/FurnitureVendor.cs)."""
    sbf = classes.get("SBFurniture")
    defc = (by_name.get("DefCarpentry") or [None])[0]
    if sbf is None or defc is None:
        notes.append("SBFurniture or DefCarpentry not found")
        return []
    material = interp.static_value(sbf, sbf.find("field", "_MaterialCost")[0])
    mat = {}
    for o in material if isinstance(material, list) else []:
        if isinstance(o, list) and len(o) == 2 and isinstance(o[0], TypeV):
            mat[o[0].name] = o[1]
    max_entries = interp.static_value(sbf, sbf.find("field", "MaxEntries")[0])
    crafts = []  # [type, min skill, [(res type, amount)]]

    def add_craft(ctx, a, node):
        skill_variant = len(a) >= 4 and isinstance(a[3], NsRef) and a[3].name.startswith("SkillName.")
        t = a[0].name if isinstance(a[0], TypeV) else None
        minskill = a[4] if skill_variant else a[3]
        res = a[6] if skill_variant else a[5]
        amount = a[8] if skill_variant else a[7]
        if ctx.conds:
            notes.append("carpentry craft under a runtime condition: %s if %s" % (t, " && ".join(ctx.conds)))
        crafts.append([t, minskill, [(res.name if isinstance(res, TypeV) else None, amount)]])
        return len(crafts) - 1

    def add_res(ctx, a, node):
        idx = a[0]
        if isinstance(idx, int) and 0 <= idx < len(crafts):
            crafts[idx][2].append((a[1].name if isinstance(a[1], TypeV) else None, a[3]))
        return None

    interp.hooks = {"AddCraft": add_craft, "AddRes": add_res}
    init = defc.find("method", "InitCraftList")[0]
    ctx = Ctx(defc, {})
    try:
        interp.run_block(ctx, defc.toks, init.body[0], init.body[1])
    finally:
        interp.hooks = {}
    # every other call there (AddSkill, AddRecipe, ForceNonExceptional, SetData...) doesn't change the price
    ctx.unparsed[:] = [u for u in ctx.unparsed if "AddCraft" in u or "AddRes(" in u]
    for u in ctx.unparsed:
        notes.append("DefCarpentry: " + u)

    def search_for(t):
        for c in crafts:
            if c[0] == t:
                return c
        return None

    cache = {}

    def craft_cost(c, visiting):
        if c[0] in cache:
            return cache[c[0]]
        if c[0] in visiting:
            return -1
        visiting.add(c[0])
        total = 0
        for rt, amt in c[2]:
            if total < 0:
                break
            unit = res_cost(rt, visiting)
            total = -1 if unit < 0 else total + unit * amt
        visiting.discard(c[0])
        cache[c[0]] = total
        return total

    def res_cost(t, visiting):
        if t in mat:
            return mat[t]
        part = search_for(t)
        return craft_cost(part, visiting) if part is not None else -1

    def instantiable(t):
        ci = interp.lookup_class(t)
        if ci is None or ci.abstract:
            return False
        ctors = ci.find("ctor")
        if not ctors:
            return True
        return any(not m.params and "public" in m.mods and "static" not in m.mods for m in ctors)

    entries, seen = [], set()
    for c in crafts:
        t = c[0]
        if t is None or t in seen:
            continue
        seen.add(t)
        materials = craft_cost(c, set())
        if materials < 0:
            continue
        if not instantiable(t):
            continue
        it = items.get(t)
        item_id = it.get("Graphic") if it else None
        hue = it.get("Hue", 0) if it else 0
        if it is None:
            notes.append("furniture item %s not in Items.json: graphic unknown" % t)
        elif "HueExpr" in it:
            notes.append("furniture item %s has a computed hue (%s); 0 used" % (t, it["HueExpr"]))
            hue = 0
        skill = int(max(0.0, c[1])) if num(c[1]) else 0
        e = {"Type": t, "BuyPrice": materials * 2 + skill * 2 + 10, "SellPrice": max(1, materials + skill),
             "ItemID": item_id, "Hue": hue}
        if t in other_prices:
            e["SellPrice"] = max(1, min(e["SellPrice"], other_prices[t] // 2))
        entries.append(e)
    if isinstance(max_entries, int) and len(entries) > max_entries:
        notes.append("furniture: %d craftable items, keeping the %d cheapest" % (len(entries), max_entries))
        entries = sorted(entries, key=lambda x: x["BuyPrice"])[:max_entries]
    return entries


# ------------------------------------------------------------------------------------------------------------- main
def main():
    root, items_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
    items = {i["Type"]: i for i in json.load(open(items_path, encoding="utf-8"))["Items"]}
    cpath = os.path.join(os.path.dirname(items_path), "Creatures.json")
    creatures = {c["Type"]: c for c in json.load(open(cpath, encoding="utf-8"))["Creatures"]} if os.path.exists(cpath) else {}
    classes, by_name = scan_classes(root)
    interp = Interp(classes, by_name, items, creatures)

    # trailing comments on PresetMapEntry table rows ("// Royal City") become a Label for unnamed maps
    pme = classes.get("PresetMapEntry")
    if pme is not None:
        orig_new = interp.new

        def new_with_comment(ctx, node, orig_new=orig_new):
            v = orig_new(ctx, node)
            if isinstance(v, Obj) and v.cls == "PresetMapEntry":
                line = node[-1][0][node[-1][1]].line
                c = v.ci.comments.get(line)
                if c and v.fields.get("name") in (0, None):
                    v.comment = c
            return v
        interp.new = new_with_comment

    sbs = sorted((ci for ci in classes.values() if ci.name != "SBInfo" and not ci.abstract and interp.derives(ci, "SBInfo")),
                 key=lambda c: c.full)
    shops, unparsed, notes = {}, {}, []
    furniture_cache = {}

    def get_entries(args):
        if "e" not in furniture_cache:
            other = {}
            for k, s in shops.items():
                if k in ("SBFurniture", "SBCarpentryBuyback") or not s.get("_Default"):
                    continue
                for e in s["Sells"]:
                    p = e["Price"]
                    p = p["Min"] if isinstance(p, dict) and "Min" in p else p
                    if isinstance(e["Type"], str) and isinstance(p, int) and e["Type"] != "PresetMap":
                        if e["Type"] not in other or p < other[e["Type"]]:
                            other[e["Type"]] = p
            fnotes = []
            ents = carpentry_entries(interp, classes, by_name, items, other, fnotes)
            notes.extend(fnotes)
            furniture_cache["e"] = [Obj("Entry", {"Type": TypeV(e["Type"]), "BuyPrice": e["BuyPrice"],
                                                  "SellPrice": e["SellPrice"], "ItemID": e["ItemID"], "Hue": e["Hue"]})
                                    for e in ents]
        return furniture_cache["e"]

    if "SBFurniture" in classes:
        interp.special_methods[("SBFurniture", "GetEntries")] = get_entries

    def imbuing_item_id(args):
        t = args[0]
        it = items.get(t.name) if isinstance(t, TypeV) else None
        return it.get("Graphic") if it and "Graphic" in it else Sym("ItemID(%s)" % getattr(t, "name", t))

    def run_shop(ci):
        u = []
        owners = []
        interp.owner_names = set()
        ctx = Ctx(ci, {}, (), u)
        # helpers that sample a new item for its graphic (Activator.CreateInstance(type).ItemID)
        for m in ci.find("method"):
            if m.body and m.body[0] != "expr":
                body = src(ci.toks, m.body[0], m.body[1])
                if "Activator.CreateInstance" in body and ".ItemID" in body and len(m.params) == 1:
                    interp.special_methods[(ci.full, m.name)] = imbuing_item_id
        for m in ci.members:
            if m.kind == "field" and m.init and "static" not in m.mods and "const" not in m.mods:
                ctx.inst[m.name] = interp.eval(ctx, ci.toks, m.init[0], m.init[1])
        ctors = [m for m in ci.find("ctor") if "static" not in m.mods]
        default = any(not m.params for m in ctors) or not ctors
        if ctors:
            # the parameterless constructor if there is one (following : this(...)), else the first
            m = next((x for x in ctors if not x.params), ctors[0])
            for p, pt in zip(m.params, m.ptypes):
                ctx.locals[p] = OwnerSym(p) if pt.split(".")[-1] in VENDOR_TYPES else Sym(p)
                if pt.split(".")[-1] in VENDOR_TYPES:
                    owners.append(p)
            if m.chain and m.chain[0] == "this":
                cargs = [interp.eval(ctx, ci.toks, a, b) for a, b in m.chain[1]]
                tgt = [x for x in ctors if len(x.params) == len(cargs) and x is not m]
                if tgt:
                    for p, v in zip(tgt[0].params, cargs):
                        # a constructor argument of null (owner) stays a runtime value
                        ctx.locals[p] = v if v is not None else Sym(p)
                    for p, pt in zip(tgt[0].params, tgt[0].ptypes):
                        if pt.split(".")[-1] in VENDOR_TYPES and isinstance(ctx.locals[p], OwnerSym):
                            owners.append(p)
                    if tgt[0].body and tgt[0].body[0] != "expr":
                        interp.run_block(ctx, ci.toks, tgt[0].body[0], tgt[0].body[1])
            if m.body and m.body[0] != "expr":
                interp.run_block(ctx, ci.toks, m.body[0], m.body[1])
        sells = buys = None
        for pname, kind in (("BuyInfo", "buy"), ("SellInfo", "sell")):
            props = [p for p in ci.members if p.kind == "prop" and p.name == pname]
            v = None
            if props and props[0].body:
                p = props[0]
                try:
                    if p.body[0] == "expr":
                        v = interp.eval(ctx, ci.toks, p.body[1], p.body[2])
                    else:
                        interp.run_block(ctx, ci.toks, p.body[0], p.body[1])
                except Return as r:
                    v = r.value
            if not isinstance(v, Sink):
                if props:
                    u.append("%s: not a list (%s)" % (pname, v))
                v = Sink(kind)
            if kind == "buy":
                sells = v
            else:
                buys = v
        rec = {"File": ci.path}
        rec["Sells"] = []
        for e in sells.items:
            if isinstance(e, dict):
                rec["Sells"].append(e)
        rec["Buys"] = []
        index = {}
        for it in buys.items:
            _, (t, price), conds, text = it
            if not isinstance(t, TypeV):
                u.append(text)
                continue
            e = {"Type": t.name, "Price": jsonable(price)}
            if conds:
                e["If"] = join_conds(conds)
            key = (t.name, e.get("If"))
            if key in index:   # GenericSellInfo is a table: a later Add replaces the price
                index[key]["Price"] = e["Price"]
            else:
                index[key] = e
                rec["Buys"].append(e)
        # the owner parameter (m, owner) is the vendor itself: write its members as Vendor.X
        for e in rec["Sells"] + rec["Buys"]:
            if "If" in e:
                for p in sorted(set(owners) | interp.owner_names):
                    e["If"] = re.sub(r"(?<![\w.\"])" + re.escape(p) + r"(?!\w)", "Vendor", e["If"])
        rec["_Default"] = default
        return rec, u

    # every shop except the two that are computed from the others, then those
    order = [c for c in sbs if c.full not in ("SBFurniture", "SBCarpentryBuyback")] + \
            [c for c in sbs if c.full in ("SBFurniture", "SBCarpentryBuyback")]
    for ci in order:
        interp.steps = 0
        try:
            rec, u = run_shop(ci)
        except RuntimeError as ex:
            rec, u = {"File": ci.path, "Sells": [], "Buys": []}, ["interpreter: %s" % ex]
        if ci.full in ("SBFurniture", "SBCarpentryBuyback"):
            rec["Derived"] = ("Computed by ServUO at startup from the carpentry craft list (DefCarpentry): every "
                              "craftable item whose materials are all basic (Board 3, Log 2, Cloth 2, IronIngot 6, "
                              "Feather 1, BlankScroll 5, Nails 3, or carpentry-made parts of those) and that has a "
                              "parameterless constructor; sells for materials*2 + skill*2 + 10, buys for "
                              "max(1, materials + skill) capped at half of any other shop's price for it; "
                              "MinSkill truncated; at most the 250 cheapest.")
        shops[ci.full] = rec
        if u:
            unparsed[ci.full] = sorted(set(u), key=u.index)

    # flag types not in Items.json / Creatures.json
    missing_items, missing_creatures = {}, {}
    for key, s in shops.items():
        for e in s["Sells"] + s["Buys"]:
            t = e["Type"]
            if not isinstance(t, str):
                continue
            if e.get("Creature"):
                if creatures and t not in creatures:
                    e["NotInCreatures"] = True
                    missing_creatures.setdefault(t, set()).add(key)
            elif t not in items:
                e["NotInItems"] = True
                missing_items.setdefault(t, set()).add(key)

    # vendors: which shops each BaseVendor class sets up in InitSBInfo (inherited when not overridden)
    vendors, vend_unparsed = {}, {}

    def vendor_shops(ci):
        c = ci
        while c is not None:
            ms = [m for m in c.find("method", "InitSBInfo") if m.body and m.body[0] != "expr"]
            if ms:
                u = []
                ctx = Ctx(ci, {}, (), u)
                sink = Sink("sb")
                for nm in ("m_SBInfos", "SBInfos", "_SBInfos", "m_SBInfo", "sbInfos"):
                    ctx.inst[nm] = sink
                ctx.inst["Title"] = Sym("Title")
                ctx.ci = c
                try:
                    interp.run_block(ctx, c.toks, ms[0].body[0], ms[0].body[1])
                except (RuntimeError, Return):
                    pass
                return sink.items, u
            c = interp.base_of(c)
        return [], []

    for ci in sorted(classes.values(), key=lambda c: c.full):
        if not interp.derives(ci, "BaseVendor") or ci.abstract:
            continue
        interp.steps = 0
        lst, u = vendor_shops(ci)
        if lst:
            vendors[ci.full] = lst
        if u:
            vend_unparsed[ci.full] = u
    for k, u in vend_unparsed.items():
        unparsed["vendor " + k] = u

    for s in shops.values():
        s.pop("_Default", None)
    result = {
        "Source": "ServUO scripts: SBInfo shops (numbers only). Sells = vendor sells to players (GenericBuyInfo), "
                  "Buys = vendor buys from players (GenericSellInfo). Core.* era checks taken as true, Siege as false.",
        "Notes": {
            "Economy": "ServUO's vendor economy (BaseVendor.UseVendorEconomy = Core.AOS && !Siege) treats Stackable "
                       "entries as economy items: the vendor always restocks %d of them, and the price moves +1 per "
                       "1000 bought by players and -1 per 1000 sold to the vendor (never below 2); when a vendor also "
                       "sells a Stackable type, it buys it back for max(1, int(current sell price * 0.75)) instead of "
                       "the Buys price." % ECONOMY_STOCK,
            "PriceScalar": "Sells prices are multiplied by the vendor's price scalar: 100 + the faction town tax when "
                           "the vendor stands in a faction town, else 100 (price*scalar+50)/100.",
            "BuysAdjustments": "Buys prices are the base: armour and weapons pay 60%% for low quality, 125%% for "
                               "exceptional, +100 per durability and per protection/damage level; Pitcher pays 3 "
                               "empty or milk / 5 full, BeverageBottle 3, Jug 6 regardless of the table.",
            "Resale": "Items a player sells to a vendor are resold at 1.9 x the Buys price.",
            "Restock": "Stock is the amount on hand after a restock (Utility.Random ranges are drawn per restock).",
            "Hue": "Hue rules: Utility.Random<Rule>Hue() drawn when the vendor builds its list.",
        },
        "Shops": shops,
        "Vendors": vendors,
        "Conditions": dict(sorted(interp.glossary.items())),
        "Unparsed": unparsed,
    }
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=1)
        f.write("\n")

    n_sell = sum(len(s["Sells"]) for s in shops.values())
    n_buy = sum(len(s["Buys"]) for s in shops.values())
    n_if = sum(1 for s in shops.values() for e in s["Sells"] + s["Buys"] if "If" in e)
    print(f"{len(shops)} shops, {n_sell} Sells entries, {n_buy} Buys entries ({n_if} conditional), "
          f"{len(vendors)} vendor classes with shops -> {out}")
    if missing_items:
        print("types not in Items.json: " + ", ".join("%s (%s)" % (t, ", ".join(sorted(s))) for t, s in sorted(missing_items.items())))
    if missing_creatures:
        print("creatures not in Creatures.json: " + ", ".join("%s (%s)" % (t, ", ".join(sorted(s))) for t, s in sorted(missing_creatures.items())))
    empty = [k for k, s in shops.items() if not s["Sells"] and not s["Buys"]]
    if empty:
        print("shops with no entries: " + ", ".join(empty))
    if creatures:
        refs = {}
        for c in creatures.values():
            for s in c.get("Shops", []):
                refs.setdefault(s, []).append(c["Type"])
        bad = {s: v for s, v in refs.items() if s not in shops}
        if bad:
            print("Creatures.json Shops naming no shop here: " + "; ".join("%s (%s)" % (s, ", ".join(v)) for s, v in sorted(bad.items())))
        diffs = []
        for v, lst in vendors.items():
            name = v.split(".")[-1]
            if name in creatures:
                mine = [e["Shop"] for e in lst]
                theirs = creatures[name].get("Shops", [])
                if sorted(set(mine)) != sorted(set(theirs)):
                    diffs.append("%s: Creatures.json %s, scripts %s" % (name, theirs, mine))
        if diffs:
            print("vendors whose Creatures.json Shops differ from InitSBInfo:\n  " + "\n  ".join(diffs))
    for n in notes:
        print("note: " + n)
    if unparsed:
        print("unparsed: " + "; ".join("%s: %s" % (k, " | ".join(v)) for k, v in sorted(unparsed.items())))


if __name__ == "__main__":
    main()
