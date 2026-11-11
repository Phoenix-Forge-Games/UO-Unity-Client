"""Extracts every crafting system (data and rules) from ServUO's C# scripts into Content/Crafting.json.

  python -I export_servuo_crafting.py <ServUO Scripts folder> <Items.json> <Crafting.json out>

ServUO's crafting lives in Scripts/Services/Craft: the engine (Core/CraftSystem.cs, CraftItem.cs, CraftRes.cs,
CraftSubRes.cs, CraftGroup.cs...) and one CraftSystem subclass per trade (Def*.cs). Each Def*'s InitCraftList builds
the menu by calling AddCraft / AddRes / AddSkill / SetNeedHeat ... in order. That method (and the private helpers it
calls, like DefInscription.AddSpell or DefTinkering.AddJewelrySet) is read by interpreting, in a small C# subset, what
it does: locals and fields, arithmetic, enum casts, params arrays, switch, for loops, if/else. Core.<expansion> checks
are taken as true and Siege as false, the same as the sibling exporters; the dropped side of such a check is listed
under the system's "Skipped". Any other condition is kept: both branches run and each item made under it gets
"If": "<C# condition>". The engine's rules (success/exceptional chance, failure loss, station tile lists, maker's
marks, colour inheritance) are read from CraftItem.cs / CraftSystem.cs / DefBlacksmithy.cs and written out as data plus
plain-language text. Nothing of ServUO's code is reused: the game re-implements the rules; these are the numbers.

Output:
{"Source": "...",
 "Rules": {
   "SuccessChance": {"Text": ..., "Formula": ..., ...},       how a craft's success chance is computed
   "ExceptionalChance": {"Text": ..., "Modes": {...}, ...},   per-system "ECA" mode -> formula
   "FailLosesResources": {"Text": ..., "Normal": "All", "UseAllRes": "Half", "NeverConsumedOnFailure": [types]},
   "MakersMark": {...}, "Quality": {...}, "Timing": {...}, "SkillGain": {...}, "Hue": {...},
   "ResourceEquivalents": {"<required Type>": [Types accepted for it]},
   "Stations": {"<Anvil|Forge|Heat|Oven|Mill|Water|BeverageMaker>":
                 {"Static": [tile ids], "ItemIDs": [world item graphics, if different], "Items": [Types]}},
   "StationRange": 2, "StationZ": 16, ...},
 "Materials": {"<CraftResource>": {"Name", "Cliloc", "Hue", "Types": [...], "Attributes": {...}}},
 "Systems": {
   "<Blacksmithy|...>": {
     "Class": "DefBlacksmithy", "Skill": "Blacksmith", "GumpTitle": <cliloc>,
     "Tools": [Types], "ToolAddons": [Types],
     "MinCraftEffect": 1, "MaxCraftEffect": 1, "Delay": 1.25, "ChanceAtMin": 0.0, "ExceptionalMode": "...",
     "NeedsAnvilAndForge": bool, "NeedsForge": bool, "Requires": [...], "CanCraftRules": [...],
     "Resmelt"/"Repair"/"MarkOption"/"CanEnhance"/"CanAlter": bool,
     "Materials": {"Primary": {"Type": <menu's default resource>, "Name": <cliloc>, "Options": [
                      {"Name": "DullCopper", "Type": "DullCopperIngot", "ReqSkill": 65.0, "Hue": 2419,
                       "MenuName": {"Cliloc": n}, "GenericName": {"Cliloc": n}, "LackSkillMessage": {...}}, ...]},
                   "Secondary": {...} | null},
     "Groups": [ {"Name": "<text>" | {"Cliloc": n}, "Items": [
        {"Type": <Items.json Type>, "Name": "<text>" | {"Cliloc": n}, "MinSkill": a, "MaxSkill": b,
         "Resources": [{"Type", "Amount", "Name", "Message", "Material": bool, "Accepts": [...]?,
                        "KeptOnFailure": true?}],
         "Skills": [{"Skill", "Min", "Max"}],      every skill checked, the system's main one first
         "NeedHeat", "NeedOven", "NeedMill", "NeedWater", "UseAllRes": bool, "Recipe": null | {"Id", "Name"},
         "Requires": null | [..],                   "BasketWeaving", "MechanicalLife", "Faction"
         optional: "NeedMaker", "UseSubRes2", "ForceTypeRes", "ForceNonExceptional", "ForceExceptional",
         "ChanceAtMin", "ForceSuccessChance", "MinSkillOffset", "ItemHue", "Mana", "Hits", "Stam", "DisplayID",
         "Data", "ThemePack", "Beverage", "ChooseResource", "Markable", "Custom": {...}, "Expansion": [..],
         "If": "<C# condition>", "NotInItems": true, "CustomCraft": {...} (an action, e.g. a tinker trap)},
        ...]}, ...],
     "Skipped": [...], "Unparsed": [...]}}}
Item types are checked against Items.json; any missing are flagged "NotInItems" (kept) and printed.
"""
import json
import os
import re
import sys

# ----------------------------------------------------------------------------------------------------------------
# Tokenizer and a small C# parser
# ----------------------------------------------------------------------------------------------------------------

TOKEN = re.compile(r"""
 (?P<ws>\s+)
|(?P<lc>//[^\n]*)
|(?P<bc>/\*.*?\*/)
|(?P<pp>\#[^\n]*)
|(?P<vstr>@"(?:[^"]|"")*")
|(?P<str>"(?:\\.|[^"\\\n])*")
|(?P<chr>'(?:\\.|[^'\\])+')
|(?P<num>0[xX][0-9a-fA-F]+[uUlL]*|(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?[fFdDmMuUlL]*)
|(?P<id>@?[A-Za-z_]\w*)
|(?P<op>=>|\+\+|--|&&|\|\||==|!=|<=|>=|\+=|-=|\*=|/=|%=|&=|\|=|\^=|\?\?|::|[{}()\[\];,.<>=!+\-*/%&|^~?:])
""", re.S | re.X)


class Tok:
    __slots__ = ("kind", "val", "start", "end")

    def __init__(self, kind, val, start, end):
        self.kind, self.val, self.start, self.end = kind, val, start, end

    def __repr__(self):
        return f"{self.kind}:{self.val}"


def tokenize(text):
    toks, pos, n = [], 0, len(text)
    while pos < n:
        m = TOKEN.match(text, pos)
        if not m:
            toks.append(Tok("op", text[pos], pos, pos + 1))
            pos += 1
            continue
        kind = m.lastgroup
        if kind not in ("ws", "lc", "bc", "pp"):
            toks.append(Tok(kind, m.group(0), pos, m.end()))
        pos = m.end()
    toks.append(Tok("eof", "", n, n))
    return toks


def strip_comments(text):
    """Text with comments and preprocessor lines blanked (strings kept)."""
    out, pos = [], 0
    for m in TOKEN.finditer(text):
        if m.lastgroup in ("lc", "bc", "pp"):
            out.append(text[pos:m.start()])
            out.append(" " if m.lastgroup != "bc" else re.sub(r"[^\n]", " ", m.group(0)))
            pos = m.end()
    out.append(text[pos:])
    return "".join(out)


class ParseError(Exception):
    pass


class Node:
    __slots__ = ("kind", "a", "text")

    def __init__(self, kind, a, text):
        self.kind, self.a, self.text = kind, a, text

    def __repr__(self):
        return f"<{self.kind} {self.text}>"


PRIMITIVES = {"int", "double", "float", "bool", "string", "long", "short", "byte", "uint", "ulong", "ushort",
              "sbyte", "decimal", "object", "char", "var"}
KEYWORD_STMTS = {"if", "else", "switch", "case", "default", "for", "foreach", "while", "do", "return", "break",
                 "continue", "new", "typeof", "true", "false", "null", "this", "base", "throw", "try", "catch",
                 "finally", "using", "is", "as", "in"}
ASSIGN_OPS = {"=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^="}


class Parser:
    def __init__(self, toks, text, lo=0, hi=None):
        self.t, self.text, self.p = toks, text, lo
        self.hi = len(toks) - 1 if hi is None else hi

    # -- helpers
    def peek(self, k=0):
        i = self.p + k
        return self.t[i] if i < self.hi else self.t[-1]

    def at(self, val, k=0):
        tk = self.peek(k)
        return tk.kind in ("op", "id") and tk.val == val

    def next(self):
        tk = self.peek()
        if tk.kind == "eof":
            raise ParseError("unexpected end")
        self.p += 1
        return tk

    def expect(self, val):
        tk = self.next()
        if tk.val != val:
            raise ParseError(f"expected {val!r}, got {tk.val!r} at {tk.start}")
        return tk

    def src(self, i0, i1):
        if i1 <= i0:
            return ""
        return " ".join(self.text[self.t[i0].start:self.t[i1 - 1].end].split())

    def match_close(self, i, open_, close):
        depth = 0
        while i < self.hi:
            v = self.t[i].val if self.t[i].kind == "op" else None
            if v == open_:
                depth += 1
            elif v == close:
                depth -= 1
                if depth == 0:
                    return i
            i += 1
        raise ParseError("unbalanced " + open_)

    # -- types
    def try_type(self):
        """Parse a type at the cursor; returns its text or None (cursor restored)."""
        save = self.p
        try:
            tk = self.peek()
            if tk.kind != "id" or (tk.val in KEYWORD_STMTS and tk.val not in ("object",)):
                return None
            parts = [self.next().val]
            while self.at(".") and self.peek(1).kind == "id":
                self.next()
                parts.append(self.next().val)
            name = ".".join(parts)
            if self.at("<"):
                self.next()
                args = [self.try_type()]
                if args[0] is None:
                    raise ParseError("generic")
                while self.at(","):
                    self.next()
                    a = self.try_type()
                    if a is None:
                        raise ParseError("generic")
                    args.append(a)
                self.expect(">")
                name += "<" + ",".join(args) + ">"
            if self.at("?"):
                # nullable only when followed by an identifier (declaration), not a conditional
                if self.peek(1).kind == "id" and self.peek(2).val in ("=", ";", ","):
                    self.next()
            while self.at("[") and (self.at("]", 1) or self.at(",", 1)):
                self.next()
                while self.at(","):
                    self.next()
                self.expect("]")
                name += "[]"
            return name
        except ParseError:
            self.p = save
            return None

    # -- statements
    def block(self):
        self.expect("{")
        out = []
        while not self.at("}"):
            if self.peek().kind == "eof":
                raise ParseError("eof in block")
            out.append(self.statement())
        self.expect("}")
        return out

    def skip_statement(self, i0):
        """Recover: skip to the end of the statement starting at i0 (a ';' at depth 0 or a closed block)."""
        self.p = i0
        depth = 0
        while self.peek().kind != "eof":
            v = self.peek().val if self.peek().kind == "op" else None
            if v in ("(", "[", "{"):
                depth += 1
            elif v in (")", "]", "}"):
                if depth == 0:
                    break
                depth -= 1
                if depth == 0 and v == "}":
                    self.next()
                    if self.at(";") or self.at(")") or self.at(","):
                        continue
                    break
            elif v == ";" and depth == 0:
                self.next()
                break
            self.next()
        return Node("unknown", None, self.src(i0, self.p))

    def statement(self):
        i0 = self.p
        try:
            return self._statement()
        except ParseError:
            return self.skip_statement(i0)

    def _statement(self):
        i0 = self.p
        tk = self.peek()
        if tk.val == "{" and tk.kind == "op":
            return Node("block", self.block(), "{...}")
        if tk.val == ";" and tk.kind == "op":
            self.next()
            return Node("block", [], ";")
        if tk.kind == "id":
            v = tk.val
            if v == "if":
                self.next()
                self.expect("(")
                c0 = self.p
                cond = self.expr()
                ctext = self.src(c0, self.p)
                self.expect(")")
                then = self.statement()
                other = None
                if self.at("else"):
                    self.next()
                    other = self.statement()
                return Node("if", (cond, then, other), ctext)
            if v == "switch":
                self.next()
                self.expect("(")
                c0 = self.p
                subject = self.expr()
                stext = self.src(c0, self.p)
                self.expect(")")
                self.expect("{")
                sections = []
                while not self.at("}"):
                    labels = []
                    while self.at("case") or self.at("default"):
                        if self.next().val == "case":
                            labels.append(self.expr())
                        else:
                            labels.append("default")
                        self.expect(":")
                    if not labels:
                        raise ParseError("switch section")
                    body = []
                    while not (self.at("case") or self.at("default") or self.at("}")):
                        body.append(self.statement())
                    sections.append((labels, body))
                self.expect("}")
                return Node("switch", (subject, sections), stext)
            if v == "for":
                self.next()
                self.expect("(")
                init = None if self.at(";") else self.simple_statement()
                self.expect(";")
                cond = None if self.at(";") else self.expr()
                self.expect(";")
                steps = []
                while not self.at(")"):
                    steps.append(self.expr())
                    if self.at(","):
                        self.next()
                self.expect(")")
                body = self.statement()
                return Node("for", (init, cond, steps, body), self.src(i0, self.p))
            if v == "foreach":
                self.next()
                self.expect("(")
                self.try_type()
                name = self.next().val
                self.expect("in")
                coll = self.expr()
                self.expect(")")
                body = self.statement()
                return Node("foreach", (name, coll, body), self.src(i0, self.p))
            if v in ("while", "do", "try", "throw", "using", "lock"):
                raise ParseError("unsupported " + v)
            if v == "return":
                self.next()
                e = None if self.at(";") else self.expr()
                self.expect(";")
                return Node("return", e, self.src(i0, self.p))
            if v in ("break", "continue"):
                self.next()
                self.expect(";")
                return Node(v, None, v)
        st = self.simple_statement()
        self.expect(";")
        return st

    def simple_statement(self):
        """A declaration or an expression (no trailing ';')."""
        i0 = self.p
        save = self.p
        ty = self.try_type()
        if ty is not None and self.peek().kind == "id" and self.peek(1).val in ("=", ";", ",", "in"):
            decls = []
            while True:
                name = self.next().val
                init = None
                if self.at("="):
                    self.next()
                    init = self.array_init() if self.at("{") else self.expr()
                decls.append((name, init))
                if self.at(","):
                    self.next()
                    continue
                break
            return Node("decl", (ty, decls), self.src(i0, self.p))
        self.p = save
        e = self.expr()
        return Node("expr", e, self.src(i0, self.p))

    def array_init(self):
        i0 = self.p
        self.expect("{")
        elems = []
        while not self.at("}"):
            elems.append(self.array_init() if self.at("{") else self.expr())
            if self.at(","):
                self.next()
        self.expect("}")
        return Node("array", elems, self.src(i0, self.p))

    # -- expressions
    def expr(self):
        i0 = self.p
        left = self.conditional()
        if self.peek().kind == "op" and self.peek().val in ASSIGN_OPS:
            op = self.next().val
            right = self.array_init() if self.at("{") else self.expr()
            return Node("assign", (op, left, right), self.src(i0, self.p))
        return left

    def conditional(self):
        i0 = self.p
        c = self.binary(0)
        if self.at("?"):
            self.next()
            a = self.expr()
            self.expect(":")
            b = self.expr()
            return Node("cond", (c, a, b), self.src(i0, self.p))
        return c

    LEVELS = [["??"], ["||"], ["&&"], ["|"], ["^"], ["&"], ["==", "!="], ["<", ">", "<=", ">=", "is", "as"],
              ["+", "-"], ["*", "/", "%"]]

    def binary(self, level):
        if level == len(self.LEVELS):
            return self.unary()
        i0 = self.p
        left = self.binary(level + 1)
        ops = self.LEVELS[level]
        while self.peek().kind in ("op", "id") and self.peek().val in ops:
            op = self.next().val
            if op in ("is", "as"):
                ty = self.try_type()
                if ty is None:
                    raise ParseError("is/as")
                if op == "is" and self.peek().kind == "id" and self.peek().val not in KEYWORD_STMTS:
                    self.next()  # pattern variable
                left = Node("is" if op == "is" else "as", (left, ty), self.src(i0, self.p))
                continue
            right = self.binary(level + 1)
            left = Node("bin", (op, left, right), self.src(i0, self.p))
        return left

    def unary(self):
        i0 = self.p
        tk = self.peek()
        if tk.kind == "op" and tk.val in ("!", "-", "+", "~", "++", "--"):
            self.next()
            e = self.unary()
            return Node("unary", (tk.val, e), self.src(i0, self.p))
        if tk.kind == "op" and tk.val == "(":
            # lambda?
            close = self.match_close(self.p, "(", ")")
            if self.t[close + 1].val == "=>":
                return self.lambda_(i0, close + 1)
            # cast?
            save = self.p
            self.next()
            ty = self.try_type()
            if ty is not None and self.at(")"):
                nxt = self.peek(1)
                if ty in PRIMITIVES or nxt.kind in ("id", "num", "str", "vstr", "chr") or (
                        nxt.kind == "op" and nxt.val in ("(",)):
                    if not (nxt.kind == "id" and nxt.val in ("is", "as")):
                        self.next()
                        e = self.unary()
                        return Node("cast", (ty, e), self.src(i0, self.p))
            self.p = save
        return self.postfix()

    def lambda_(self, i0, arrow):
        self.p = arrow + 1
        if self.at("{"):
            self.p = self.match_close(self.p, "{", "}") + 1
        else:
            self.expr()
        return Node("opaque", None, self.src(i0, self.p))

    def postfix(self):
        i0 = self.p
        e = self.primary()
        while True:
            if self.at("."):
                self.next()
                name = self.next()
                if name.kind != "id":
                    raise ParseError("member")
                e = Node("member", (e, name.val), self.src(i0, self.p))
                if self.at("<"):
                    save = self.p
                    self.next()
                    ok = self.try_type() is not None
                    while ok and self.at(","):
                        self.next()
                        ok = self.try_type() is not None
                    if ok and self.at(">") and self.at("(", 1):
                        self.next()
                    else:
                        self.p = save
            elif self.at("("):
                self.next()
                args = []
                while not self.at(")"):
                    if self.peek().kind == "id" and self.peek().val in ("ref", "out"):
                        self.next()
                    args.append(self.expr())
                    if self.at(","):
                        self.next()
                self.expect(")")
                e = Node("call", (e, args), self.src(i0, self.p))
            elif self.at("["):
                self.next()
                idx = self.expr()
                self.expect("]")
                e = Node("index", (e, idx), self.src(i0, self.p))
            elif self.at("++") or self.at("--"):
                op = self.next().val
                e = Node("postfix", (op, e), self.src(i0, self.p))
            else:
                return e

    def primary(self):
        i0 = self.p
        tk = self.next()
        if tk.kind == "num":
            v = tk.val.rstrip("fFdDmMuUlL") if not tk.val.lower().startswith("0x") else tk.val.rstrip("uUlL")
            if v.lower().startswith("0x"):
                val = int(v, 16)
            elif re.fullmatch(r"\d+", v) and not re.search(r"[fFdDmM]$", tk.val):
                val = int(v)
            else:
                val = float(v)
            return Node("lit", val, tk.val)
        if tk.kind == "str":
            return Node("lit", bytes(tk.val[1:-1], "utf-8").decode("unicode_escape"), tk.val)
        if tk.kind == "vstr":
            return Node("lit", tk.val[2:-1].replace('""', '"'), tk.val)
        if tk.kind == "chr":
            return Node("lit", tk.val[1:-1], tk.val)
        if tk.kind == "op" and tk.val == "(":
            e = self.expr()
            self.expect(")")
            return Node("paren", e, self.src(i0, self.p))
        if tk.kind == "id":
            v = tk.val
            if v in ("true", "false"):
                return Node("lit", v == "true", v)
            if v == "null":
                return Node("lit", None, v)
            if v == "typeof":
                self.expect("(")
                ty = self.try_type()
                if ty is None:
                    raise ParseError("typeof")
                self.expect(")")
                return Node("typeof", ty, self.src(i0, self.p))
            if v == "new":
                ty = self.try_type()
                if self.at("["):  # new T[n] / new T[] {..} / new[] {..}
                    self.next()
                    if not self.at("]"):
                        self.expr()
                    self.expect("]")
                    init = self.array_init() if self.at("{") else Node("array", [], "")
                    return Node("newarr", (ty, init), self.src(i0, self.p))
                if ty is not None and ty.endswith("[]") and self.at("{"):
                    init = self.array_init()
                    return Node("newarr", (ty[:-2], init), self.src(i0, self.p))
                args = []
                if self.at("("):
                    self.next()
                    while not self.at(")"):
                        args.append(self.expr())
                        if self.at(","):
                            self.next()
                    self.expect(")")
                if self.at("{"):
                    self.p = self.match_close(self.p, "{", "}") + 1
                return Node("new", (ty, args), self.src(i0, self.p))
            if self.at("=>"):
                return self.lambda_(i0, self.p)
            return Node("name", v, v)
        raise ParseError(f"unexpected {tk.val!r}")


# ----------------------------------------------------------------------------------------------------------------
# Class and member scanning
# ----------------------------------------------------------------------------------------------------------------

CLASS = re.compile(r"\b(?:public|internal|private|protected)?\s*(?:static\s+|sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)\s*(?::\s*([\w.]+))?")
ENUM = re.compile(r"\benum\s+(\w+)\s*(?::\s*\w+)?\s*\{([^{}]*)\}")
MODIFIERS = {"public", "private", "protected", "internal", "static", "readonly", "override", "virtual", "abstract",
             "sealed", "new", "const", "extern", "unsafe", "volatile", "async", "partial"}


def matching(text, start):
    """Index just past the brace block opening at text[start] == '{' (text without comments)."""
    depth = 0
    i, n = start, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            i += 1
            while i < n and text[i] != '"':
                i += 2 if text[i] == "\\" else 1
        elif c == "'":
            i += 1
            while i < n and text[i] != "'":
                i += 2 if text[i] == "\\" else 1
        elif c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return n


def class_body(text, name):
    m = re.search(r"\bclass\s+" + name + r"\b[^{]*\{", text)
    if not m:
        return None, None
    return m.end() - 1, matching(text, m.end() - 1)


class ClassInfo:
    """Members of one class: methods (params, body token range) and fields (initialiser token range)."""

    def __init__(self, text, name):
        self.text = text
        self.name = name
        self.toks = tokenize(text)
        self.methods, self.fields = {}, {}
        b0, b1 = class_body(text, name)
        if b0 is None:
            raise ValueError("class not found: " + name)
        i = next(k for k, t in enumerate(self.toks) if t.start == b0)
        j = next(k for k, t in enumerate(self.toks) if t.start == b1 - 1)
        self._scan(i + 1, j)

    def _scan(self, i, j):
        t = self.toks
        p = Parser(t, self.text)
        while i < j:
            tk = t[i]
            if tk.val == "[" and tk.kind == "op":  # attribute
                i = p.match_close(i, "[", "]") + 1
                continue
            if tk.val in (";", "}") and tk.kind == "op":
                i += 1
                continue
            start = i
            while t[i].kind == "id" and t[i].val in MODIFIERS:
                i += 1
            if t[i].val in ("class", "struct", "interface", "enum", "delegate"):
                while i < j and t[i].val not in ("{", ";"):
                    i += 1
                if t[i].val == "{":
                    i = p.match_close(i, "{", "}") + 1
                else:
                    i += 1
                continue
            p.p = i
            ty = p.try_type()
            i = p.p
            if ty is None:
                i += 1
                continue
            if t[i].val == "(":  # constructor
                name, ty = ty, None
            else:
                name = t[i].val
                i += 1
                if t[i].val == ".":  # explicit interface member
                    while t[i].val == "." or t[i].kind == "id":
                        name = t[i].val if t[i].kind == "id" else name
                        i += 1
            if t[i].val == "(" and t[i].kind == "op":
                close = p.match_close(i, "(", ")")
                params = self._params(i + 1, close)
                i = close + 1
                if t[i].val == ":":  # base/this call
                    i += 1
                    while t[i].val != "(":
                        i += 1
                    i = p.match_close(i, "(", ")") + 1
                if t[i].val == "{":
                    end = p.match_close(i, "{", "}")
                    self.methods.setdefault(name, (params, i, end + 1, "block"))
                    i = end + 1
                elif t[i].val == "=>":
                    k = i
                    while t[k].val != ";":
                        k += 1
                    self.methods.setdefault(name, (params, i + 1, k, "expr"))
                    i = k + 1
                else:
                    i += 1
                continue
            if t[i].val == "{" or t[i].val == "=>":  # property
                if t[i].val == "{":
                    i = p.match_close(i, "{", "}") + 1
                    if t[i].val == "=":
                        while t[i].val != ";":
                            i += 1
                else:
                    while t[i].val != ";":
                        i += 1
                continue
            # fields: name [= init] {, name [= init]} ;
            while True:
                if t[i].val == "=":
                    k = i + 1
                    depth = 0
                    while not (depth == 0 and t[k].val in (",", ";")):
                        if t[k].val in ("(", "[", "{"):
                            depth += 1
                        elif t[k].val in (")", "]", "}"):
                            depth -= 1
                        k += 1
                    self.fields[name] = (ty, i + 1, k)
                    i = k
                else:
                    self.fields[name] = (ty, None, None)
                if t[i].val == ",":
                    i += 1
                    name = t[i].val
                    i += 1
                    continue
                i += 1
                break

    def _params(self, i, j):
        out = []
        t = self.toks
        p = Parser(t, self.text)
        while i < j:
            is_params = False
            while t[i].kind == "id" and t[i].val in ("params", "ref", "out", "this", "in"):
                is_params = is_params or t[i].val == "params"
                i += 1
            p.p = i
            ty = p.try_type()
            i = p.p
            name = t[i].val
            i += 1
            default = None
            if t[i].val == "=":
                k = i + 1
                while k < j and t[k].val != ",":
                    k += 1
                default = (i + 1, k)
                i = k
            out.append((name, ty, is_params, default))
            if i < j and t[i].val == ",":
                i += 1
        return out

    def method_src(self, name):
        params, a, b, kind = self.methods[name]
        return " ".join(self.text[self.toks[a].start:self.toks[b - 1].end].split())


# ----------------------------------------------------------------------------------------------------------------
# Values and the interpreter
# ----------------------------------------------------------------------------------------------------------------

class TypeRef:
    __slots__ = ("name",)

    def __init__(self, name):
        self.name = name.split(".")[-1].split("<")[0]

    def __eq__(self, o):
        return isinstance(o, TypeRef) and o.name == self.name

    def __hash__(self):
        return hash(("T", self.name))

    def __repr__(self):
        return f"typeof({self.name})"


class EnumVal:
    __slots__ = ("enum", "member", "value")

    def __init__(self, enum, member, value):
        self.enum, self.member, self.value = enum, member, value

    def __eq__(self, o):
        if isinstance(o, EnumVal):
            return o.enum == self.enum and o.member == self.member
        return isinstance(o, int) and self.value == o

    def __hash__(self):
        return hash((self.enum, self.member))

    def __repr__(self):
        return f"{self.enum}.{self.member}"


class Opaque:
    """A value the subset can't compute (a callback, an object); kept as its C# text."""
    __slots__ = ("text",)

    def __init__(self, text):
        self.text = text

    def __repr__(self):
        return self.text


class Unknown:
    """A condition decided at runtime; kept as C# text."""
    __slots__ = ("text",)

    def __init__(self, text):
        self.text = text


class Break(Exception):
    pass


class Continue(Exception):
    pass


class Return(Exception):
    def __init__(self, value):
        self.value = value


def num(v):
    if isinstance(v, EnumVal):
        if v.value is None:
            raise TypeError("enum without value " + repr(v))
        return v.value
    if isinstance(v, bool) or not isinstance(v, (int, float)):
        raise TypeError("not a number: " + repr(v))
    return v


def tv(v):
    """A value as JSON."""
    if isinstance(v, TypeRef):
        return v.name
    if isinstance(v, EnumVal):
        return f"{v.enum}.{v.member}"
    if isinstance(v, (Opaque, Unknown)):
        return {"Expr": v.text}
    if isinstance(v, list):
        return [tv(x) for x in v]
    return v


def text_def(v):
    """A ServUO TextDefinition (cliloc number or literal text) as JSON."""
    if isinstance(v, bool):
        return v
    if isinstance(v, int):
        return {"Cliloc": v} if v else None
    if isinstance(v, str):
        return v if v else None
    if isinstance(v, EnumVal) and v.value is not None:
        return {"Cliloc": v.value}
    return tv(v)


class CraftItemRec:
    def __init__(self, index, type_, group, name, cond, expansions):
        self.index = index
        self.type = type_
        self.group = group
        self.name = name
        self.resources = []  # (type, name, amount, message)
        self.skills = []  # (skill, min, max)
        self.props = {}
        self.cond = cond
        self.expansions = expansions


class SystemState:
    def __init__(self):
        self.items = []
        self.groups = []  # [key, name, [item indices]]
        self.subres = {"Init": False, "Type": None, "Name": None, "Options": []}
        self.subres2 = {"Init": False, "Type": None, "Name": None, "Options": []}
        self.flags = {}
        self.unparsed = []
        self.skipped = []


BOOL_SETTERS = {
    "SetUseAllRes": "UseAllRes", "SetForceTypeRes": "ForceTypeRes", "SetNeedHeat": "NeedHeat",
    "SetNeedOven": "NeedOven", "SetNeedMaker": "NeedMaker", "SetNeedWater": "NeedWater", "SetNeedMill": "NeedMill",
    "SetUseSubRes2": "UseSubRes2",
}
VALUE_SETTERS = {
    "SetItemHue": "ItemHue", "SetManaReq": "Mana", "SetStamReq": "Stam", "SetHitsReq": "Hits",
    "SetBeverageType": "Beverage", "SetNeededThemePack": "ThemePack", "SetData": "Data", "SetDisplayID": "DisplayID",
    "SetForceSuccess": "ForceSuccessChance", "SetMinSkillOffset": "MinSkillOffset",
}
FLAG_SETTERS = {
    "SetRequiresBasketWeaving": ("Requires", "BasketWeaving"), "SetRequiresMechanicalLife": ("Requires", "MechanicalLife"),
    "SetRequireResTarget": ("ChooseResource", True), "ForceNonExceptional": ("ForceNonExceptional", True),
    "ForceExceptional": ("ForceExceptional", True),
}
CALLBACK_SETTERS = {"AddCraftAction": "CraftAction", "AddCreateItem": "CreateItem", "AddResCallback": "ResourceCheck",
                    "SetMutateAction": "MutateAction"}
SYSTEM_PROPS = {"Resmelt", "Repair", "MarkOption", "CanEnhance", "QuestOption", "CanAlter"}


class Interp:
    def __init__(self, cls, enums, state):
        self.cls = cls
        self.enums = enums  # name -> {member: value}
        self.st = state
        self.fields = {}
        self.scopes = []
        self.cond_stack = []  # runtime conditions (C# text)
        self.exp_stack = []  # Core.<expansion> gates passed
        for fname, (ty, a, b) in sorted(cls.fields.items(), key=lambda kv: kv[1][1] or 0):
            if a is None:
                self.fields[fname] = 0 if ty in ("int", "double", "float", "long") else None
                continue
            p = Parser(cls.toks, cls.text, a, b)
            try:
                node = p.array_init() if p.at("{") else p.expr()
                self.fields[fname] = self.eval(node)
            except Exception:
                self.fields[fname] = Opaque(p.src(a, b))

    # -- names
    def lookup(self, name):
        for s in reversed(self.scopes):
            if name in s:
                return True, s[name]
        if name in self.fields:
            return True, self.fields[name]
        return False, None

    def store(self, name, value):
        for s in reversed(self.scopes):
            if name in s:
                s[name] = value
                return
        if name in self.fields:
            self.fields[name] = value
            return
        if name in SYSTEM_PROPS:
            self.st.flags[name] = value
            return
        raise TypeError("assign to unknown " + name)

    def enum_val(self, enum, member):
        values = self.enums.get(enum)
        return EnumVal(enum, member, values.get(member) if values else None)

    # -- expressions
    def eval(self, n):
        k = n.kind
        if k == "lit":
            return n.a
        if k == "paren":
            return self.eval(n.a)
        if k == "typeof":
            return TypeRef(n.a)
        if k == "name":
            found, v = self.lookup(n.a)
            if found:
                return v
            if n.a in self.cls.methods:
                return Opaque(n.a)
            return Opaque(n.a)
        if k == "member":
            obj, name = n.a
            if obj.kind == "name":
                o = obj.a
                found, _ = self.lookup(o)
                if not found:
                    if o == "Core":
                        return True  # Core.<expansion> (and Core.Expansion comparisons below)
                    if o == "Siege" and name == "SiegeShard":
                        return False
                    if o in self.enums or o[0].isupper():
                        return self.enum_val(o, name)
            if obj.kind == "name" and obj.a == "this":
                found, v = self.lookup(name)
                return v if found else Opaque(name)
            if obj.kind == "member" and obj.a[0].kind == "name" and obj.a[0].a in ("Server", "Items"):
                return self.enum_val(obj.a[1], name)
            base = self.eval(obj)
            if isinstance(base, EnumVal):  # Outer.NestedEnum.Member
                return self.enum_val(base.member, name)
            if isinstance(base, list) and name in ("Length", "Count"):
                return len(base)
            return Opaque(n.text)
        if k == "cast":
            ty, e = n.a
            v = self.eval(e)
            if ty in ("int", "long", "short", "byte", "uint") and isinstance(v, float):
                return int(v)
            if ty in ("double", "float") and isinstance(v, int) and not isinstance(v, bool):
                return float(v)
            return v
        if k == "unary":
            op, e = n.a
            if op in ("++", "--"):
                v = num(self.eval(e)) + (1 if op == "++" else -1)
                self.assign_to(e, v)
                return v
            v = self.eval(e)
            if op == "!":
                if isinstance(v, Unknown):
                    return Unknown("!(" + v.text + ")")
                if isinstance(v, bool):
                    return not v
                return Unknown(n.text)
            if op == "-":
                return -num(v)
            if op == "+":
                return num(v)
            return Opaque(n.text)
        if k == "postfix":
            op, e = n.a
            v = num(self.eval(e))
            self.assign_to(e, v + (1 if op == "++" else -1))
            return v
        if k == "bin":
            return self.binop(n)
        if k == "cond":
            c = self.truth(self.eval(n.a[0]))
            if isinstance(c, Unknown):
                return Opaque(n.text)
            return self.eval(n.a[1] if c else n.a[2])
        if k == "index":
            base = self.eval(n.a[0])
            idx = self.eval(n.a[1])
            if isinstance(base, list):
                return base[num(idx)]
            return Opaque(n.text)
        if k == "newarr":
            return [self.eval(e) for e in n.a[1].a]
        if k == "array":
            return [self.eval(e) for e in n.a]
        if k == "assign":
            op, target, value = n.a
            v = self.eval(value)
            if op != "=":
                cur = self.eval(target)
                v = self.arith(op[0], cur, v)
            self.assign_to(target, v)
            return v
        if k == "call":
            return self.call(n)
        if k == "is":
            return Unknown(n.text)
        return Opaque(n.text)

    def assign_to(self, target, v):
        if target.kind == "name":
            self.store(target.a, v)
        elif target.kind == "member" and target.a[0].kind == "name" and target.a[0].a == "this":
            self.store(target.a[1], v)
        elif target.kind == "index":
            base = self.eval(target.a[0])
            if not isinstance(base, list):
                raise TypeError("index assign")
            base[num(self.eval(target.a[1]))] = v
        else:
            raise TypeError("assign target " + target.text)

    def truth(self, v):
        if isinstance(v, (bool, Unknown)):
            return v
        return Unknown(repr(v))

    def binop(self, n):
        op, l, r = n.a
        if op in ("&&", "||"):
            a = self.truth(self.eval(l))
            if op == "&&" and a is False:
                return False
            if op == "||" and a is True:
                return True
            b = self.truth(self.eval(r))
            if isinstance(a, Unknown) or isinstance(b, Unknown):
                if op == "&&":
                    if b is False:
                        return False
                    if b is True:
                        return a
                    if a is True:
                        return b
                else:
                    if b is True:
                        return True
                    if b is False:
                        return a
                    if a is False:
                        return b
                return Unknown(n.text)
            return (a and b) if op == "&&" else (a or b)
        a = self.eval(l)
        b = self.eval(r)
        if op in ("==", "!="):
            if isinstance(a, (Opaque, Unknown)) or isinstance(b, (Opaque, Unknown)):
                return Unknown(n.text)
            if a is True and isinstance(b, EnumVal) or b is True and isinstance(a, EnumVal):
                return Unknown(n.text)
            eq = a == b
            return eq if op == "==" else not eq
        if op in ("<", ">", "<=", ">="):
            if a is True or b is True:  # Core.Expansion >= Expansion.X
                return True
            try:
                x, y = num(a), num(b)
            except TypeError:
                return Unknown(n.text)
            return {"<": x < y, ">": x > y, "<=": x <= y, ">=": x >= y}[op]
        if op == "??":
            return b if a is None else a
        return self.arith(op, a, b)

    def arith(self, op, a, b):
        if op == "+" and (isinstance(a, str) or isinstance(b, str)):
            return f"{a}{b}"
        x, y = num(a), num(b)
        if op == "+":
            return x + y
        if op == "-":
            return x - y
        if op == "*":
            return x * y
        if op == "/":
            return x // y if isinstance(x, int) and isinstance(y, int) else x / y
        if op == "%":
            return x % y
        if op == "|":
            return x | y
        if op == "&":
            return x & y
        if op == "^":
            return x ^ y
        raise TypeError(op)

    # -- calls
    def call(self, n):
        fn, args = n.a
        if fn.kind == "member" and fn.a[0].kind == "name" and fn.a[0].a == "this":
            name = fn.a[1]
        elif fn.kind == "name":
            name = fn.a
        else:
            return Opaque(n.text)
        handler = getattr(self, "api_" + name, None)
        if name in BOOL_SETTERS or name in VALUE_SETTERS or name in FLAG_SETTERS or name in CALLBACK_SETTERS:
            return self.api_setter(name, args)
        if handler:
            return handler([self.eval(a) for a in args], args)
        if name in self.cls.methods:
            return self.invoke(name, [self.eval(a) for a in args])
        return Opaque(n.text)

    def invoke(self, name, values):
        params, a, b, kind = self.cls.methods[name]
        scope = {}
        vi = 0
        for pname, pty, is_params, default in params:
            if is_params:
                scope[pname] = list(values[vi:])
                vi = len(values)
            elif vi < len(values):
                scope[pname] = values[vi]
                vi += 1
            elif default:
                p = Parser(self.cls.toks, self.cls.text, default[0], default[1])
                scope[pname] = self.eval(p.expr())
            else:
                scope[pname] = None
        p = Parser(self.cls.toks, self.cls.text, a, b)
        self.scopes.append(scope)
        try:
            if kind == "block":
                body = p.block()
                self.run_block(body)
            else:
                return self.eval(p.expr())
        except Return as r:
            return r.value
        finally:
            self.scopes.pop()
        return None

    def item(self, index):
        i = num(index)
        return self.st.items[i]

    def api_AddCraft(self, v, nodes):
        if len(v) >= 4 and isinstance(v[3], EnumVal) and v[3].enum == "SkillName":
            type_, group, name, skill, mn, mx, res, resname, amount = v[:9]
            message = v[9] if len(v) > 9 else ""
            skill = skill.member
        else:
            type_, group, name, mn, mx, res, resname, amount = v[:8]
            message = v[8] if len(v) > 8 else ""
            skill = None
        index = len(self.st.items)
        rec = CraftItemRec(index, type_, group, name, " && ".join(self.cond_stack) or None,
                           list(dict.fromkeys(self.exp_stack)))
        rec.resources.append((res, resname, amount, message))
        rec.skills.append((skill, mn, mx))  # None = the system's main skill
        self.st.items.append(rec)
        key = ("n", group) if isinstance(group, int) and not isinstance(group, bool) else ("s", group)
        for g in self.st.groups:
            if g[0] == key:
                g[2].append(index)
                break
        else:
            self.st.groups.append([key, group, [index]])
        return index

    def api_AddRes(self, v, nodes):
        index, type_, name, amount = v[:4]
        message = v[4] if len(v) > 4 else ""
        self.item(index).resources.append((type_, name, amount, message))

    def api_AddSkill(self, v, nodes):
        index, skill, mn, mx = v
        self.item(index).skills.append((skill.member if isinstance(skill, EnumVal) else tv(skill), mn, mx))

    def api_AddRecipe(self, v, nodes):
        index, rid = v
        rec = self.item(index)
        if "Recipe" in rec.props:  # ServUO warns and ignores a second recipe
            return
        rec.props["Recipe"] = {"Id": num(rid), "Name": rid.member} if isinstance(rid, EnumVal) else {"Id": num(rid)}

    def api_setter(self, name, args):
        index = self.eval(args[0])
        rec = self.item(index)
        if name in CALLBACK_SETTERS:
            rec.props.setdefault("Custom", {})[CALLBACK_SETTERS[name]] = args[1].text
            return
        if name in FLAG_SETTERS:
            key, val = FLAG_SETTERS[name]
            if key == "Requires":
                rec.props.setdefault("Requires", [])
                if val not in rec.props["Requires"]:
                    rec.props["Requires"].append(val)
            else:
                rec.props[key] = val
            return
        val = self.eval(args[1])
        if isinstance(val, Unknown):
            val = {"If": val.text}
        if name in BOOL_SETTERS:
            rec.props[BOOL_SETTERS[name]] = val
        else:
            if name == "SetMinSkillOffset" or name == "SetForceSuccess":
                rec.props[VALUE_SETTERS[name]] = val
            else:
                rec.props[VALUE_SETTERS[name]] = tv(val)

    def _subres(self, col, v):
        type_, name, req = v[0], v[1], v[2]
        if len(v) == 5:
            generic, message = v[3], v[4]
        else:
            generic, message = 0, v[3]
        col["Options"].append({"Type": type_, "Name": name, "ReqSkill": float(num(req)), "Generic": generic,
                               "Message": message})

    def api_SetSubRes(self, v, nodes):
        self.st.subres.update(Init=True, Type=v[0], Name=v[1])

    def api_SetSubRes2(self, v, nodes):
        self.st.subres2.update(Init=True, Type=v[0], Name=v[1])

    def api_AddSubRes(self, v, nodes):
        self._subres(self.st.subres, v)

    def api_AddSubRes2(self, v, nodes):
        self._subres(self.st.subres2, v)

    # -- statements
    def run_block(self, stmts):
        self.scopes.append({})
        try:
            for s in stmts:
                self.run(s)
        finally:
            self.scopes.pop()

    def core_refs(self, node):
        return re.findall(r"\bCore\.(\w+)", node.text)

    def run(self, s):
        try:
            self._run(s)
        except (TypeError, IndexError, KeyError, ValueError, AttributeError) as e:
            self.st.unparsed.append(f"{s.text} ({type(e).__name__}: {e})")

    def _run(self, s):
        k = s.kind
        if k == "block":
            self.run_block(s.a)
        elif k == "expr":
            v = self.eval(s.a)
            if isinstance(v, Opaque) and s.a.kind == "call":
                self.st.unparsed.append(s.text)
        elif k == "decl":
            ty, decls = s.a
            for name, init in decls:
                self.scopes[-1][name] = self.eval(init) if init is not None else None
        elif k == "if":
            cond, then, other = s.a
            c = self.truth(self.eval(cond))
            cores = self.core_refs(cond)
            if c is True:
                self.exp_stack.extend(cores)
                self.run(then)
                del self.exp_stack[len(self.exp_stack) - len(cores):]
                if other is not None:
                    self.skip(s.text, other, negated=True)
            elif c is False:
                self.skip(s.text, then, negated=False)
                if other is not None:
                    self.run(other)
            else:
                self.cond_stack.append(c.text)
                self.run(then)
                self.cond_stack.pop()
                if other is not None:
                    self.cond_stack.append("!(" + c.text + ")")
                    self.run(other)
                    self.cond_stack.pop()
        elif k == "switch":
            subject, sections = s.a
            v = self.eval(subject)
            chosen = None
            for labels, body in sections:
                for lab in labels:
                    if lab != "default" and self.eval(lab) == v:
                        chosen = body
                if chosen:
                    break
            if chosen is None:
                for labels, body in sections:
                    if "default" in labels:
                        chosen = body
            if chosen is not None:
                try:
                    self.run_block(chosen)
                except Break:
                    pass
        elif k == "for":
            init, cond, steps, body = s.a
            self.scopes.append({})
            try:
                if init is not None:
                    self.run(init)
                guard = 0
                while cond is None or self.truth(self.eval(cond)) is True:
                    guard += 1
                    if guard > 10000:
                        raise RuntimeError("loop")
                    try:
                        self.run(body)
                    except Break:
                        break
                    except Continue:
                        pass
                    for e in steps:
                        self.eval(e)
            finally:
                self.scopes.pop()
        elif k == "foreach":
            name, coll, body = s.a
            values = self.eval(coll)
            if not isinstance(values, list):
                self.st.unparsed.append(s.text)
                return
            for v in values:
                self.scopes.append({name: v})
                try:
                    self.run(body)
                except Break:
                    break
                except Continue:
                    pass
                finally:
                    self.scopes.pop()
        elif k == "return":
            raise Return(self.eval(s.a) if s.a is not None else None)
        elif k == "break":
            raise Break()
        elif k == "continue":
            raise Continue()
        else:
            self.st.unparsed.append(s.text)

    def skip(self, cond_text, node, negated):
        if not re.search(r"\b(?:Core|Siege)\.", cond_text):
            return  # an ordinary false branch (a helper's local test), not an expansion gate
        crafts = len(re.findall(r"\bAddCraft\s*\(", node_text(self.cls, node)))
        types = re.findall(r"\bAddCraft\s*\(\s*typeof\s*\(\s*([\w.]+)", node_text(self.cls, node))
        entry = {"Condition": ("!(" + cond_text + ")") if negated else cond_text, "Statements": count_stmts(node)}
        if crafts:
            entry["AddCraft"] = crafts
            entry["Types"] = [t.split(".")[-1] for t in types]
        self.st.skipped.append(entry)


def node_text(cls, node):
    if node.kind == "block":
        return " ".join(node_text(cls, x) for x in node.a)
    if node.kind == "if":
        return node.text + " " + node_text(cls, node.a[1]) + (" " + node_text(cls, node.a[2]) if node.a[2] else "")
    if node.kind in ("for", "foreach"):
        return node.text
    return node.text


def count_stmts(node):
    if node.kind == "block":
        return sum(count_stmts(x) for x in node.a)
    if node.kind == "if":
        return 1 + count_stmts(node.a[1]) + (count_stmts(node.a[2]) if node.a[2] else 0)
    return 1


# ----------------------------------------------------------------------------------------------------------------
# Rules read from the engine
# ----------------------------------------------------------------------------------------------------------------

def int_array(text, name):
    m = re.search(r"\b" + name + r"\s*=\s*(?:new\s+int\s*\[\s*\])?\s*\{([^}]*)\}", text)
    if not m:
        raise ValueError("table not found: " + name)
    return [int(x, 0) for x in re.findall(r"0[xX][0-9a-fA-F]+|\d+", m.group(1))]


def type_array(text, name):
    m = re.search(r"\b" + name + r"\s*=\s*(?:new\s*(?:Type|System\.Type)?\s*\[\s*\])?\s*\{(.*?)\};", text, re.S)
    if not m:
        raise ValueError("table not found: " + name)
    return [t.split(".")[-1] for t in re.findall(r"typeof\s*\(\s*([\w.]+)\s*\)", m.group(1))]


def expand_pairs(values):
    """ServUO's Find(itemID, table) reads the table as inclusive [low, high] pairs."""
    ids, pairs, empty = [], [], []
    for i in range(0, len(values) - 1, 2):
        lo, hi = values[i], values[i + 1]
        pairs.append([lo, hi])
        if hi < lo:
            empty.append([lo, hi])
        ids.extend(range(lo, hi + 1))
    return sorted(set(ids)), pairs, empty


def method_body(text, signature_regex):
    m = re.search(signature_regex + r"[^{;]*\{", text)
    if not m:
        return None
    return text[m.end() - 1:matching(text, m.end() - 1)]


def read_engine(craft_dir):
    core = os.path.join(craft_dir, "Core")
    item_text = strip_comments(open(os.path.join(core, "CraftItem.cs"), encoding="utf-8", errors="replace").read())
    sys_text = strip_comments(open(os.path.join(core, "CraftSystem.cs"), encoding="utf-8", errors="replace").read())
    smith = strip_comments(open(os.path.join(craft_dir, "DefBlacksmithy.cs"), encoding="utf-8", errors="replace").read())
    notes = []

    stations = {}
    for key, table in (("Heat", "m_HeatSources"), ("Oven", "m_Ovens"), ("BeverageMaker", "m_Makers"),
                       ("Mill", "m_Mills"), ("Water", "m_WaterSources")):
        ids, pairs, empty = expand_pairs(int_array(item_text, table))
        stations[key] = {"Static": ids, "Ranges": pairs}
        for lo, hi in empty:
            notes.append(f"Stations.{key}: ServUO's {table} pair 0x{lo:X}..0x{hi:X} is reversed (a typo in the "
                         f"script) so it matches nothing; kept as written in Ranges, absent from Static")
    water_addons = re.findall(r"addon\s+is\s+(\w+)", method_body(item_text, r"bool\s+FindWater\s*\(") or "")
    stations["Water"]["Items"] = sorted(set(water_addons))
    stations["Water"]["ItemsNote"] = "any addon component of these addons within range counts as water"

    check = method_body(smith, r"void\s+CheckAnvilAndForge\s*\(")
    loops = check.split("GetStaticTiles")
    item_part, static_part = loops[0], loops[1]

    def ids_in(expr):
        out = set(int(x, 0) for x in re.findall(r"(?:ItemID|\bid)\s*==\s*(0[xX][0-9a-fA-F]+|\d+)", expr))
        for lo, hi in re.findall(r"(?:ItemID|\bid)\s*>=\s*(0[xX][0-9a-fA-F]+|\d+)\s*&&\s*[\w.]*(?:ItemID|\bid)\s*<=\s*"
                                 r"(0[xX][0-9a-fA-F]+|\d+)", expr):
            out.update(range(int(lo, 0), int(hi, 0) + 1))
        return sorted(out)

    def flag_expr(part, name):
        m = re.search(r"bool\s+" + name + r"\s*=\s*(.*?);", part, re.S)
        return m.group(1)

    anvil_items, forge_items = ids_in(flag_expr(item_part, "isAnvil")), ids_in(flag_expr(item_part, "isForge"))
    anvil_static, forge_static = ids_in(flag_expr(static_part, "isAnvil")), ids_in(flag_expr(static_part, "isForge"))
    stations["Anvil"] = {"Static": anvil_static}
    stations["Forge"] = {"Static": forge_static}
    if anvil_items != anvil_static:
        stations["Anvil"]["ItemIDs"] = anvil_items
    if forge_items != forge_static:
        stations["Forge"]["ItemIDs"] = forge_items
    los = "InLOS" in check
    range_m = re.search(r"CheckAnvilAndForge\s*\(\s*from\s*,\s*(\d+)", smith)
    find_range = re.search(r"GetItemsInRange\s*\(\s*from\.Location\s*,\s*(\d+)\s*\)", item_text)

    rules = {
        "ItemTypesTable": [[t.split(".")[-1] for t in re.findall(r"typeof\s*\(\s*([\w.]+)\s*\)", row)]
                           for row in re.findall(r"new\s*\[\s*\]\s*\{([^}]*)\}",
                                                 re.search(r"ItemTypesTable\s*=\s*\{(.*?)\};", item_text, re.S).group(1))],
        "Colored": type_array(item_text, "m_ColoredItemTable"),
        "ClothColored": type_array(item_text, "m_ClothColoredItemTable"),
        "ColoredResources": type_array(item_text, "m_ColoredResourceTable"),
        "Markable": type_array(item_text, "m_MarkableTable"),
        "NeverColor": type_array(item_text, "m_NeverColorTable"),
        "Conversion": {a.split(".")[-1]: b.split(".")[-1] for a, b in re.findall(
            r"\{\s*typeof\s*\(\s*([\w.]+)\s*\)\s*,\s*typeof\s*\(\s*([\w.]+)\s*\)\s*\}",
            re.search(r"m_ResourceConversionTable\s*=.*?\{(.*?)\};", item_text, re.S).group(1))},
        "NoConsume": type_array(sys_text, "_GlobalNoConsume"),
        "RuneNameCliloc": int(re.search(r"NameNumber\s*==\s*(\d+)\s*\)\s*\{[^{}]*?FindItemsByType<RecallRune>",
                                        item_text, re.S).group(1)),
        "Stations": stations,
        "StationRange": int(find_range.group(1)),
        "AnvilForgeRange": int(range_m.group(1)),
        "AnvilForgeLOS": los,
        "Notes": notes,
    }
    # sanity checks: these rules are described in words below; fail loudly if ServUO's engine changed shape
    expect = [
        (item_text, r"consumeType\s*=\s*\(\s*UseAllRes\s*\?\s*ConsumeType\.Half\s*:\s*ConsumeType\.All\s*\)"),
        (item_text, r"amounts\[i\]\s*/=\s*2"),
        (item_text, r"GetChanceAtMin\(this\)\s*\+\s*\(\(valMainSkill\s*-\s*minMainSkill\)\s*/\s*\(maxMainSkill\s*-\s*minMainSkill\)"),
        (item_text, r"valMainSkill\s*==\s*maxMainSkill\)\s*\{\s*chance\s*=\s*1\.0"),
        (item_text, r"case\s+CraftECA\.ChanceMinusSixty\s*:\s*chance\s*-=\s*0\.6"),
        (item_text, r"chance\s*=\s*chance\s*\*\s*0\.5\s*-\s*0\.1"),
        (item_text, r"0\.60\s*-\s*\(\(from\.Skills\[system\.MainSkill\]\.Value\s*-\s*95\.0\)\s*\*\s*0\.03\)"),
        (item_text, r"quality\s*==\s*2\s*&&\s*m_From\.Skills\[m_CraftSystem\.MainSkill\]\.Base\s*>=\s*100\.0"),
        (item_text, r"from\.Skills\[craftSystem\.MainSkill\]\.Base\s*<\s*subResource\.RequiredSkill"),
        (item_text, r"\(item\.Z\s*\+\s*16\)\s*>\s*from\.Z"),
    ]
    for text, pattern in expect:
        if not re.search(pattern, text):
            raise SystemExit("ServUO craft engine no longer matches: " + pattern)
    return rules


def read_resources(scripts):
    path = os.path.join(scripts, "Misc", "ResourceInfo.cs")
    text = strip_comments(open(path, encoding="utf-8", errors="replace").read())
    # attributes (Core.* blocks folded to their true side)
    ctor = method_body(text, r"static\s+CraftAttributeInfo\s*\(\s*\)")
    ctor = fold_core(ctor)
    var_to_static = dict(re.findall(r"CraftAttributeInfo\s+(\w+)\s*=\s*(\w+)\s*=\s*new\s+CraftAttributeInfo", ctor))
    attrs = {}
    for var, field, value in re.findall(r"\b(\w+)\.(\w+)\s*=\s*(-?\d+(?:\.\d+)?)\s*;", ctor):
        if var in var_to_static:
            attrs.setdefault(var_to_static[var], {})[field] = float(value) if "." in value else int(value)
    tables = {}
    for tname, body in re.findall(r"CraftResourceInfo\[\]\s+(\w+)\s*=\s*new\s+CraftResourceInfo\[\]\s*\{(.*?)\};", text, re.S):
        rows = []
        for m in re.finditer(r"new\s+CraftResourceInfo\s*\(\s*(0[xX][0-9a-fA-F]+|\d+)\s*,\s*(\d+)\s*,\s*\"([^\"]*)\"\s*,"
                             r"\s*CraftAttributeInfo\.(\w+)\s*,\s*CraftResource\.(\w+)\s*((?:,\s*typeof\s*\(\s*\w+\s*\)\s*)*)\)", body):
            rows.append({"Resource": m.group(5), "Name": m.group(3), "Cliloc": int(m.group(2)), "Hue": int(m.group(1), 0),
                         "Types": re.findall(r"typeof\s*\(\s*(\w+)\s*\)", m.group(6)), "Attr": m.group(4)})
        tables[tname] = rows
    aos = re.search(r"Core\.AOS\s*\?\s*(\w+)\s*:\s*(\w+)", text)
    if aos:
        tables.pop(aos.group(2), None)  # Core.AOS is taken as true
    materials = {}
    for tname in tables:
        for r in tables[tname]:
            entry = {"Name": r["Name"], "Cliloc": r["Cliloc"], "Hue": r["Hue"], "Types": r["Types"]}
            if attrs.get(r["Attr"]):
                entry["Attributes"] = dict(sorted(attrs[r["Attr"]].items()))
            materials[r["Resource"]] = entry
    return materials


def fold_core(text):
    """Replace 'if (Core.X) { A } else { B }' with A (repeatedly)."""
    while True:
        m = re.search(r"if\s*\(\s*Core\.\w+\s*\)\s*\{", text)
        if not m:
            return text
        end = matching(text, m.end() - 1)
        inner = text[m.end():end - 1]
        rest = text[end:]
        e = re.match(r"\s*else\s*\{", rest)
        if e:
            rest = rest[matching(rest, e.end() - 1):]
        text = text[:m.start()] + inner + rest


def scan_scripts(scripts):
    """All classes (name -> base, file, text, abstract) and enums (name -> [(file, {member: value})])."""
    classes, enums = {}, {}
    for folder, dirs, files in os.walk(scripts):
        dirs.sort()
        for f in sorted(files):
            if not f.endswith(".cs"):
                continue
            path = os.path.join(folder, f)
            text = strip_comments(open(path, encoding="utf-8", errors="replace").read())
            rel = os.path.relpath(path, scripts).replace("\\", "/")
            for m in CLASS.finditer(text):
                name, base = m.group(1), (m.group(2) or "").split(".")[-1]
                if name not in classes:
                    classes[name] = {"Base": base, "File": rel, "Text": text, "Abstract": "abstract" in m.group(0)}
            for m in ENUM.finditer(text):
                values, nxt = {}, 0
                for part in m.group(2).split(","):
                    part = re.sub(r"\[[^\]]*\]", "", part).strip()
                    if not part:
                        continue
                    pm = re.match(r"(\w+)\s*(?:=\s*(.+))?$", part, re.S)
                    if not pm:
                        continue
                    if pm.group(2):
                        expr = pm.group(2).strip()
                        try:
                            nxt = int(expr, 0)
                        except ValueError:
                            ref = values.get(expr)
                            nxt = ref if ref is not None else nxt
                    values[pm.group(1)] = nxt
                    nxt += 1
                enums.setdefault(m.group(1), []).append((rel, values))
    return classes, enums


def is_sub(classes, name, base, seen=()):
    if name == base:
        return True
    c = classes.get(name)
    if not c or name in seen:
        return False
    return is_sub(classes, c["Base"], base, seen + (name,))


# ----------------------------------------------------------------------------------------------------------------
# Systems
# ----------------------------------------------------------------------------------------------------------------

ECA_TEXT = {
    "ChanceMinusSixty": "Exceptional = Success - 0.60",
    "FiftyPercentChanceMinusTenPercent": "Exceptional = Success * 0.5 - 0.10",
    "ChanceMinusSixtyToFourtyFive": "Exceptional = Success - Offset, Offset = clamp(0.60 - (MainSkill - 95) * 0.03, "
                                    "0.45, 0.60) (MainSkill = the crafter's current main-skill value)",
}

CALLBACK_TEXT = {
    "CutUpCloth": "Not a normal craft. Every BoltOfCloth in the backpack is removed and replaced by UncutCloth, 50 per "
                  "bolt, one stack per bolt hue. No skill check; uses one tool charge; takes one craft Delay.",
    "CombineCloth": "Not a normal craft. Every UncutCloth, Cloth and CutUpCloth in the backpack is merged into one "
                    "UncutCloth stack per hue. No skill check; uses one tool charge; takes one craft Delay.",
    "ConsumeTatteredWallMapRes": "The treasure maps must be ones the crafter completed (TreasureMap.CompletedBy): "
                                 "10 of level 1, 5 of level 3, 3 of level 4 and 1 of level 5 (the four resource lines "
                                 "name them). Checked before crafting; on a full consume those maps are used up.",
    "CraftableStoneHouseDoor.Create": "Makes a CraftableStoneHouseDoor of the DoorType in Data (facing from the door "
                                      "type).",
    "CraftableMetalHouseDoor.Create": "Makes a CraftableMetalHouseDoor of the DoorType in Data (facing from the door "
                                      "type).",
}


def system_rules(name, text, classes):
    """Per-system rules outside InitCraftList: main skill, timing, chance at min, ECA, CanCraft gates."""
    out = {}
    m = re.search(r"SkillName\s+MainSkill\s*(?:=>\s*SkillName\.(\w+)|\{\s*get\s*\{\s*return\s+SkillName\.(\w+))", text)
    out["Skill"] = m.group(1) or m.group(2)
    m = re.search(r"GumpTitleNumber\s*(?:=>\s*(\d+)|\{\s*get\s*\{\s*return\s+(\d+))", text)
    if m:
        out["GumpTitle"] = int(m.group(1) or m.group(2))
    m = re.search(r"private\s+" + name + r"\s*\(\s*\)\s*:\s*base\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)", text)
    out["MinCraftEffect"], out["MaxCraftEffect"], out["Delay"] = int(m.group(1)), int(m.group(2)), float(m.group(3))
    m = re.search(r"CraftECA\s+ECA\s*(?:=>\s*CraftECA\.(\w+)|\{\s*get\s*\{\s*return\s+CraftECA\.(\w+))", text)
    out["ExceptionalMode"] = (m.group(1) or m.group(2)) if m else "ChanceMinusSixty"
    body = method_body(text, r"double\s+GetChanceAtMin\s*\(\s*CraftItem\s+item\s*\)")
    overrides = []
    for cond, value in re.findall(r"if\s*\((.*?)\)\s*return\s+([\d.]+)\s*;", body, re.S):
        overrides.append({"NameClilocs": [int(x) for x in re.findall(r"NameNumber\s*==\s*(\d+)", cond)],
                          "Types": re.findall(r"ItemType\s*==\s*typeof\s*\(\s*(\w+)\s*\)", cond),
                          "Value": float(value)})
        leftover = re.sub(r"item\.(NameNumber\s*==\s*\d+|ItemType\s*==\s*typeof\s*\(\s*\w+\s*\))|\|\||\s", "", cond)
        if leftover:
            out.setdefault("Unparsed", []).append("GetChanceAtMin condition: " + " ".join(cond.split()))
    out["ChanceAtMin"] = float(re.findall(r"return\s+([\d.]+)\s*;", body)[-1])
    out["_ChanceOverrides"] = overrides
    # CanCraft gates
    cc = method_body(text, r"int\s+CanCraft\s*\(\s*Mobile\s+from\s*,\s*ITool\s+tool\s*,\s*Type\s+\w+\s*\)") or ""
    rules, requires = [], []
    if "CheckAnvilAndForge" in cc:
        if re.search(r"if\s*\(\s*anvil\s*&&\s*forge\s*\)", cc):
            out["NeedsAnvilAndForge"] = True
            rules.append("Must be within AnvilForgeRange tiles of both an anvil and a forge (Rules.Stations), each "
                         "within 16 Z and in line of sight. A craft-addon tool (smithing press) used within 2 tiles "
                         "skips the check.")
        elif re.search(r"if\s*\(\s*forge\s*\)", cc):
            out["NeedsForge"] = True
            rules.append("Must be within AnvilForgeRange tiles of a forge (Rules.Stations.Forge), within 16 Z and in "
                         "line of sight.")
    for flag, skill, value in re.findall(r"\(\(PlayerMobile\)from\)\.(\w+)\s*&&\s*from\.Skills\[SkillName\.(\w+)\]\.Base\s*>=\s*([\d.]+)", cc):
        requires.append({"Learned": flag, "Skill": skill, "MinBase": float(value)})
        rules.append(f"The crafter must have learned {flag} (a book) and have {skill} base skill >= {value}.")
    if "BaseTool.CheckTool" in cc:
        rules.append("If the crafter has a tool of this kind equipped, that equipped tool must be the one used.")
    if "Spellbook.Find" in cc:
        rules.append("Spell scrolls: the crafter must have that spell in a spellbook they carry.")
    if "BaseFactionTrapDeed" in cc:
        rules.append("Faction trap deeds and the faction trap removal kit need the crafter to be in a faction.")
    if re.search(r"ModifiedClockworkAssembly\s*\)\s*&&[^\n]*MechanicalLife", cc):
        rules.append("ModifiedClockworkAssembly needs the Mechanical Life manual read.")
    rules.append("The tool must have uses left and be on the crafter (backpack or equipped).")
    out["CanCraftRules"] = rules
    if requires:
        out["Requires"] = requires
    # colour inheritance override
    rc = method_body(text, r"bool\s+RetainsColorFrom\s*\(\s*CraftItem\s+item\s*,\s*Type\s+type\s*\)")
    if rc is not None:
        if re.fullmatch(r"\{\s*return\s+true\s*;\s*\}", rc):
            out["Hue"] = {"Rule": "Every item takes the hue of the material it was made from."}
        else:
            lists = re.findall(r"(m_\w+Colorables)", rc)
            colorables = type_array(text, lists[0]) if lists else []
            res = re.findall(r"type\s*!=\s*typeof\s*\(\s*(\w+)\s*\)", rc)
            never = re.findall(r"type\s*==\s*typeof\s*\(\s*(\w+)\s*\)\s*\)\s*return\s+false", rc)
            h = {"Items": colorables}
            if res:
                h["Rule"] = "These items also take the hue of the material when it is one of FromResources."
                h["FromResources"] = res
            else:
                h["Rule"] = "These items (and any BaseIngot-derived item) also take the material's hue, except " \
                            "when the material is one of NeverFrom."
                h["NeverFrom"] = never
            out["Hue"] = h
    return out


def find_tools(classes):
    """Item classes whose CraftSystem property names a Def* system (directly or inherited)."""
    direct = {}
    for name, c in classes.items():
        m = re.search(r"override\s+CraftSystem\s+CraftSystem\b", c["Text"])
        if not m:
            continue
        b0, b1 = class_body(c["Text"], name)
        if b0 is None:
            continue
        body = c["Text"][b0:b1]
        p = re.search(r"override\s+CraftSystem\s+CraftSystem\s*(?:=>\s*(\w+)\.CraftSystem|\{\s*get\s*\{\s*return\s+(\w+)\.CraftSystem)", body)
        if p:
            direct[name] = p.group(1) or p.group(2)
    out = {}
    for name, c in classes.items():
        if c["Abstract"]:
            continue
        n, seen = name, set()
        while n in classes and n not in seen:
            seen.add(n)
            if n in direct:
                out[name] = direct[n]
                break
            n = classes[n]["Base"]
    return out


def main():
    scripts, items_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
    craft_dir = os.path.join(scripts, "Services", "Craft")
    items = {i["Type"]: i for i in json.load(open(items_path, encoding="utf-8"))["Items"]}
    classes, enum_list = scan_scripts(scripts)
    engine = read_engine(craft_dir)
    materials = read_resources(scripts)
    mat_by_type = {}
    for key, m in materials.items():
        for t in m["Types"]:
            mat_by_type[t] = (key, m)
    tools = find_tools(classes)
    missing = {}

    def check(t, where):
        if t not in items:
            missing.setdefault(t, set()).add(where)
            return False
        return True

    equivalents = {row[0]: row for row in engine["ItemTypesTable"]}
    no_consume = set(engine["NoConsume"])
    systems = {}
    totals = []
    for f in sorted(os.listdir(craft_dir)):
        if not (f.startswith("Def") and f.endswith(".cs")):
            continue
        raw = open(os.path.join(craft_dir, f), encoding="utf-8", errors="replace").read()
        text = strip_comments(raw)
        cname = f[:-3]
        if not re.search(r"class\s+" + cname + r"\s*:\s*CraftSystem\b", text):
            continue
        sysname = cname[3:]
        rules = system_rules(cname, text, classes)
        cls = ClassInfo(text, cname)
        # enums: the ones declared in this file win over same-named ones elsewhere
        rel = os.path.relpath(os.path.join(craft_dir, f), scripts).replace("\\", "/")
        enums = {}
        for ename, defs in enum_list.items():
            local = [v for (path, v) in defs if path == rel]
            enums[ename] = local[0] if local else defs[0][1]
        st = SystemState()
        it = Interp(cls, enums, st)
        it.invoke("InitCraftList", [])
        main_skill = rules["Skill"]

        def material_opts(col, label):
            if not col["Init"]:
                return None
            opts = []
            for o in col["Options"]:
                t = tv(o["Type"])
                key, m = mat_by_type.get(t, (None, None))
                e = {"Name": key or t, "Type": t, "ReqSkill": o["ReqSkill"], "Hue": m["Hue"] if m else 0,
                     "MenuName": text_def(o["Name"])}
                if o["Generic"]:
                    e["GenericName"] = text_def(o["Generic"])
                e["LackSkillMessage"] = text_def(o["Message"])
                if not check(t, f"{sysname} material"):
                    e["NotInItems"] = True
                opts.append(e)
            return {"Type": tv(col["Type"]), "Name": text_def(col["Name"]), "Options": opts,
                    "SkillCheckedAgainst": f"{main_skill} base (unmodified) skill"}

        primary = material_opts(st.subres, "Primary")
        secondary = material_opts(st.subres2, "Secondary")
        groups_out = []
        n_items = 0
        for key, gname, indices in st.groups:
            entries = []
            for i in indices:
                rec = st.items[i]
                e = build_item(rec, rules, st, engine, equivalents, no_consume, classes, check, sysname)
                entries.append(e)
            n_items += len(entries)
            groups_out.append({"Name": text_def(gname), "Items": entries})
        tool_types = sorted(t for t, s in tools.items() if s == cname and not is_addon(classes, t))
        addon_types = sorted(t for t, s in tools.items() if s == cname and is_addon(classes, t))
        for t in tool_types:
            check(t, f"{sysname} tool")
        sysout = {"Class": cname, "Skill": main_skill}
        if "GumpTitle" in rules:
            sysout["GumpTitle"] = {"Cliloc": rules["GumpTitle"]}
        sysout["Tools"] = tool_types
        if addon_types:
            sysout["ToolAddons"] = addon_types
        for k in ("MinCraftEffect", "MaxCraftEffect", "Delay", "ChanceAtMin", "ExceptionalMode"):
            sysout[k] = rules[k]
        sysout["NeedsAnvilAndForge"] = bool(rules.get("NeedsAnvilAndForge"))
        if rules.get("NeedsForge"):
            sysout["NeedsForge"] = True
        if rules.get("Requires"):
            sysout["Requires"] = rules["Requires"]
        sysout["CanCraftRules"] = rules["CanCraftRules"]
        for k in sorted(SYSTEM_PROPS):
            v = st.flags.get(k, False)
            sysout[k] = v if isinstance(v, bool) else tv(v)
        if "Hue" in rules:
            sysout["HueOverride"] = rules["Hue"]
        sysout["Materials"] = {"Primary": primary, "Secondary": secondary}
        sysout["Groups"] = groups_out
        if st.skipped:
            sysout["Skipped"] = st.skipped
        unparsed = list(rules.get("Unparsed", [])) + st.unparsed
        if unparsed:
            sysout["Unparsed"] = unparsed
        systems[sysname] = sysout
        totals.append((sysname, n_items, len(groups_out), primary, secondary, unparsed, st.skipped))

    stations = engine["Stations"]
    for key in ("Anvil", "Forge"):
        found, others = station_classes(classes, key)
        stations[key]["Items"] = found
        stations[key]["ItemsNote"] = "items of these classes count whatever their graphic (nested classes are " \
                                     "<Outer>.<Inner>)"
        if others:
            stations[key]["NotItems"] = others
            stations[key]["NotItemsNote"] = f"these carry ServUO's [{key}] attribute but aren't items; the check " \
                                            "only scans items, so they never count"
    for key in ("Anvil", "Forge", "Water"):
        absent = [t for t in stations[key].get("Items", []) if not check(t, f"station {key}")]
        if absent:
            stations[key]["NotInItems"] = absent
    ordered_stations = {k: stations[k] for k in ("Anvil", "Forge", "Heat", "Oven", "Mill", "Water", "BeverageMaker")}
    for t in no_consume:
        check(t, "never consumed on failure")
    for row in engine["ItemTypesTable"]:
        for t in row:
            check(t, "resource equivalents")

    rules_out = build_rules(engine, ordered_stations)
    for key, m in materials.items():
        for t in m["Types"]:
            check(t, f"material {key}")
    data = {"Source": "ServUO scripts: Services/Craft (engine and Def* systems), Misc/ResourceInfo.cs (materials); "
                      "numbers and rules only",
            "Rules": rules_out,
            "Materials": materials,
            "Systems": systems}
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(data, fh, indent=1)
        fh.write("\n")

    total = sum(t[1] for t in totals)
    print(f"{len(systems)} craft systems, {total} craft entries -> {out}")
    for sysname, n, g, primary, secondary, unparsed, skipped in totals:
        mats = ", ".join(o["Name"] for o in primary["Options"]) if primary else "-"
        if secondary:
            mats += " | " + ", ".join(o["Name"] for o in secondary["Options"])
        dropped = sum(s.get("AddCraft", 0) for s in skipped)
        print(f"  {sysname}: {n} items in {g} groups; materials: {mats}"
              + (f"; {len(unparsed)} unparsed" if unparsed else "")
              + (f"; {dropped} AddCraft dropped by Core/Siege branches" if dropped else ""))
    for note in engine["Notes"]:
        print("note: " + note)
    if missing:
        print(f"{len(missing)} types not in Items.json:")
        for t in sorted(missing):
            print(f"  {t} ({', '.join(sorted(missing[t]))})")


def station_classes(classes, attr):
    """Classes marked [Anvil]/[Forge] (any spelling of the attribute), nested ones as Outer.Inner."""
    files = {}
    for c in classes.values():
        files.setdefault(c["File"], c["Text"])
    items, others = set(), set()
    pattern = re.compile(r"\[[^\[\]]*\b" + attr + r"(?:Attribute)?\b[^\[\]]*\]\s*(?:\[[^\[\]]*\]\s*)*"
                         r"(?:(?:public|private|protected|internal|sealed|abstract|partial|static)\s+)*class\s+(\w+)"
                         r"\s*(?::\s*([\w.]+))?")
    for path in sorted(files):
        text = files[path]
        if attr not in text:
            continue
        spans = []
        for m in re.finditer(r"\bclass\s+(\w+)[^{;]*\{", text):
            spans.append((m.start(), matching(text, m.end() - 1), m.group(1)))
        for m in pattern.finditer(text):
            name, base = m.group(1), (m.group(2) or "").split(".")[-1]
            pos = m.start(1)
            outer = [n for (a, b, n) in spans if a < m.start() and b > pos]
            full = ".".join(outer + [name])
            if is_sub(classes, base, "Item") or base == "Item":
                items.add(full)
            else:
                others.add(full)
    return sorted(items), sorted(others)


def custom_craft(classes, name):
    """A CustomCraft 'item' is an action, not an item; describe the ones ServUO has (tinkering traps)."""
    out = {"Class": name}
    if is_sub(classes, name, "TrapCraft"):
        text = classes[name]["Text"]
        b0, b1 = class_body(text, name)
        body = text[b0:b1]
        trap = re.search(r"TrapType\.(\w+)", body)
        disp = re.search(r"\[CraftItemID\((0[xX][0-9a-fA-F]+|\d+)\)\]\s*public\s+class\s+" + name + r"\b", text)
        base = classes["TrapCraft"]["Text"]
        lvl = re.search(r"trapLevel\s*=\s*\(int\)\(From\.Skills\.Tinkering\.Value\s*/\s*(\d+)\)", base)
        power = re.search(r"TrapPower\s*=\s*trapLevel\s*\*\s*(\d+)", base)
        rng = re.search(r"From\.InRange\(container\.GetWorldLocation\(\),\s*(\d+)\)", base)
        if not (trap and lvl and power and rng):
            out["Text"] = "unparsed TrapCraft"
            return out
        out.update({"Kind": "Trap", "TrapType": trap.group(1), "TrapLevelDivisor": int(lvl.group(1)),
                    "TrapPowerPerLevel": int(power.group(1)), "Range": int(rng.group(1))})
        if disp:
            out["DisplayID"] = int(disp.group(1), 0)
        out["Text"] = (f"Not an item: after the craft succeeds the crafter targets a lockable container within "
                       f"{rng.group(1)} tiles that is unlocked, movable, theirs to use and not already trapped. It "
                       f"gets this trap with level = floor(Tinkering / {lvl.group(1)}) and power = level * "
                       f"{power.group(1)}; the trap is armed once the container is locked (triggers on lockpicking "
                       f"too). Cancelling or a bad target fails the craft.")
    return out


def is_addon(classes, name):
    return any(is_sub(classes, name, b) for b in ("BaseAddon", "BaseAddonDeed", "AddonComponent", "AddonToolComponent",
                                                 "BaseAddonContainer", "BaseAddonContainerDeed", "CraftAddon",
                                                 "CraftAddonDeed"))


def build_item(rec, rules, st, engine, equivalents, no_consume, classes, check, sysname):
    t = tv(rec.type)
    e = {"Type": t, "Name": text_def(rec.name)}
    mn, mx = rec.skills[0][1], rec.skills[0][2]
    e["MinSkill"], e["MaxSkill"] = float(num(mn)), float(num(mx))
    if rec.skills[0][0] is not None and rec.skills[0][0] != rules["Skill"]:
        e["Skill"] = rec.skills[0][0]
    use2 = rec.props.get("UseSubRes2") is True
    col = st.subres2 if use2 else st.subres
    res_out = []
    for (rt, rname, amount, message) in rec.resources:
        rt = tv(rt)
        r = {"Type": rt, "Amount": num(amount), "Name": text_def(rname)}
        if message:
            r["Message"] = text_def(message)
        r["Material"] = bool(col["Init"] and tv(col["Type"]) == rt)
        if rt in equivalents and len(equivalents[rt]) > 1:
            r["Accepts"] = equivalents[rt]
        if rt in no_consume:
            r["KeptOnFailure"] = True
        if not check(rt, f"{sysname} resource"):
            r["NotInItems"] = True
            if rt in classes and classes[rt]["Abstract"]:
                r["AnyDerived"] = True  # an abstract base: any item deriving from it pays
        res_out.append(r)
    if isinstance(rec.name, int) and rec.name == engine["RuneNameCliloc"]:
        # CraftItem.ConsumeRes special case keyed on this name number (the runebook)
        res_out.append({"Type": "RecallRune", "Amount": 1, "Name": None, "Message": {"Cliloc": 1044253},
                        "Material": False, "Condition": "an unmarked rune"})
        check("RecallRune", f"{sysname} resource")
    e["Resources"] = res_out
    skills = []
    for sk, a, b in rec.skills:
        skills.append({"Skill": sk or rules["Skill"], "Min": float(num(a)), "Max": float(num(b))})
    e["Skills"] = skills
    for k in ("NeedHeat", "NeedOven", "NeedMill", "NeedWater", "UseAllRes"):
        e[k] = rec.props.get(k, False)
    e["Recipe"] = rec.props.get("Recipe")
    requires = list(rec.props.get("Requires", []))
    gates = " ".join(rules["CanCraftRules"])
    if "in a faction" in gates and (is_sub(classes, t, "BaseFactionTrapDeed") or t == "FactionTrapRemovalKit"):
        requires.append("Faction")
    if t == "ModifiedClockworkAssembly" and any("ModifiedClockworkAssembly" in r for r in rules["CanCraftRules"]):
        if "MechanicalLife" not in requires:
            requires.append("MechanicalLife")
    e["Requires"] = requires or None
    for k in ("NeedMaker", "UseSubRes2", "ForceTypeRes", "ForceNonExceptional", "ForceExceptional"):
        if rec.props.get(k):
            e[k] = rec.props[k]
    for ov in rules["_ChanceOverrides"]:
        if (isinstance(rec.name, int) and rec.name in ov["NameClilocs"]) or t in ov["Types"]:
            e["ChanceAtMin"] = ov["Value"]
            break
    if "ForceSuccessChance" in rec.props:
        e["ForceSuccessChance"] = num(rec.props["ForceSuccessChance"]) / 100.0
    if "MinSkillOffset" in rec.props:
        e["MinSkillOffset"] = float(num(rec.props["MinSkillOffset"]))
    for k in ("ItemHue", "Mana", "Hits", "Stam", "DisplayID", "Data", "ThemePack", "Beverage"):
        if k in rec.props and rec.props[k] not in (0, None):
            v = rec.props[k]
            if k == "Beverage" and isinstance(v, str):
                v = v.split(".")[-1]
            if k == "ThemePack" and isinstance(v, str):
                v = v.split(".")[-1]
            if k == "ThemePack" and v == "None":
                continue
            e[k] = v
    if rec.props.get("ChooseResource"):
        e["ChooseResource"] = True
    e["Markable"] = (not rec.props.get("ForceNonExceptional")) and any(
        is_sub(classes, t, b) for b in engine["Markable"])
    if "Custom" in rec.props:
        e["Custom"] = {k: {"Callback": v, "Text": CALLBACK_TEXT.get(v, "unparsed callback")}
                       for k, v in sorted(rec.props["Custom"].items())}
    if rec.expansions:
        e["Expansion"] = rec.expansions
    if rec.cond:
        e["If"] = rec.cond
    if not check(t, f"{sysname} item"):
        e["NotInItems"] = True
        if is_sub(classes, t, "CustomCraft"):
            e["CustomCraft"] = custom_craft(classes, t)
    return e


def build_rules(engine, stations):
    return {
        "SuccessChance": {
            "Text": "Every skill the item lists (Skills; the system's main skill first) must be at least its Min "
                    "(the main skill's Min less the item's MinSkillOffset), using the crafter's current skill value, "
                    "or the attempt is refused. Then success rises linearly from ChanceAtMin at the main skill's "
                    "Min to 100% at its Max, and keeps rising past Max (above 1.0 is a sure success). The item's "
                    "ChanceAtMin overrides the system's when present. ForceSuccessChance replaces the whole formula.",
            "Formula": "Success = ChanceAtMin + (MainSkill - Min) / (Max - Min) * (1 - ChanceAtMin); "
                       "Success = 1 when MainSkill == Max; 0 when any required skill < its Min",
            "Fields": {"ChanceAtMin": "Systems.<s>.ChanceAtMin, or the item's ChanceAtMin",
                       "MainSkill": "crafter's current (modified) value of Systems.<s>.Skill",
                       "Min": "item MinSkill - MinSkillOffset", "Max": "item MaxSkill"},
            "Bonuses": ["Crafting talisman for this skill: + its SuccessBonus percent (only when all skills are met)",
                        "Woodworker's bench (Carpentry/Fletching addon) bonus: +0.50"],
            "Roll": "success when Success > random[0,1)",
        },
        "ExceptionalChance": {
            "Text": "Rolled independently of success, from the same Success value (before any clamp), by the "
                    "system's ExceptionalMode. When the result is above 0 the bonuses are added. ForceNonExceptional "
                    "items are never exceptional; ForceExceptional items are always exceptional when all their "
                    "skills are met.",
            "Modes": ECA_TEXT,
            "Bonuses": ["SeriousFun crafting apron: + its ExceptionalBonus percent (AlwaysExceptional apron: 100%)",
                        "Crafting talisman for this skill: its ExceptionalBonus percent REPLACES the apron bonus",
                        "Master chef's apron: + its Bonus percent",
                        "Woodworker's bench bonus: +0.30"],
            "Roll": "exceptional when Exceptional > random[0,1); only matters when the craft also succeeds",
        },
        "FailLosesResources": {
            "Text": "A failed attempt consumes resources: normally the full amount of every resource line (All); "
                    "for UseAllRes items, half of ONE batch's amounts, each at least 1 (Half). Resources listed in "
                    "NeverConsumedOnFailure are kept. An equipped Master Craftsman talisman with charges keeps "
                    "everything (and uses a charge). The tool loses one use either way. SeriousFun crafting aprons: "
                    "Salvager keeps everything on failure, FailureSaving keeps that percent, Efficiency takes that "
                    "percent off every attempt (a success always uses at least 1 of each line).",
            "Normal": "All", "UseAllRes": "Half",
            "NeverConsumedOnFailure": sorted(engine["NoConsume"]),
        },
        "UseAllRes": "Makes as many as the backpack's resources allow in one attempt (every line's amount times the "
                     "count); the result stack (or a non-stackable item's uses) is that count. Skill gain is checked "
                     "once per item made.",
        "Quality": {"Text": "Results are Regular or Exceptional (ServUO never makes low quality from the craft menu).",
                    "MainSkillForMark": 100.0},
        "MakersMark": {
            "Text": "An exceptional item can carry the crafter's mark when the system has MarkOption, the crafter's "
                    "main skill BASE is at least 100, and the item is Markable (its type derives from one of "
                    "MarkableTypes and it isn't ForceNonExceptional). The crafter chooses: always/never/ask.",
            "MarkableTypes": engine["Markable"]},
        "Hue": {
            "Text": "An item takes its material's hue when its type derives from one of ItemTypes AND the resource "
                    "type derives from one of ResourceTypes, or when its system's HueOverride says so. Items that "
                    "don't implement the crafting hook take the hue when they have none. Cloth-coloured items "
                    "(ClothItemTypes) take the hue of the cloth used. NeverColor types never take one. ItemHue sets "
                    "a fixed hue for the result.",
            "ItemTypes": engine["Colored"], "ResourceTypes": engine["ColoredResources"],
            "ClothItemTypes": engine["ClothColored"], "NeverColor": engine["NeverColor"]},
        "Materials": {
            "Text": "A resource line with Material: true is replaced by the material picked in the menu (the "
                    "system's Materials.Primary, or Secondary when the item has UseSubRes2). The crafter's main "
                    "skill BASE must be at least that material's ReqSkill. If the picked material runs out, the "
                    "next one in the menu's order that the crafter has enough of and the skill for is used "
                    "(SeriousFun). ForceTypeRes items convert the picked material through ForceTypeConversion.",
            "ForceTypeConversion": engine["Conversion"],
            "AttributesText": "Top-level Materials.<resource>.Attributes are ServUO's CraftAttributeInfo numbers "
                              "(resists, durability, lower requirements, runic attribute counts and intensities...) "
                              "that the crafted armour/weapon/shield gets from that material; the item classes "
                              "apply them."},
        "ResourceEquivalents": {row[0]: row for row in engine["ItemTypesTable"]},
        "ResourceEquivalentsText": "A resource line of the first type can be paid with any mix of the listed types "
                                   "(boards with logs, leather with hides, cloth with uncut cloth...).",
        "Timing": {"Text": "An attempt plays the craft effect a random MinCraftEffect..MaxCraftEffect times, Delay "
                           "seconds apart, then resolves (SeriousFun SwiftHands apron: no delay). Only one craft at a "
                           "time.",
                   "Fields": ["Systems.<s>.MinCraftEffect", "Systems.<s>.MaxCraftEffect", "Systems.<s>.Delay"]},
        "QuantityResources": "Resources that hold a quantity (pitchers, kegs, bottles of liquid...) are paid from "
                             "that quantity, not the item count. Beverage containers count only when they hold the "
                             "item's Beverage (Water when the item gives none).",
        "ThemePack": "An item's ThemePack is only shown in its craft details; ServUO doesn't enforce it.",
        "SkillGain": "Each attempt makes a skill-gain check for every skill the item lists, over that skill's "
                     "Min..Max range (UseAllRes items: once per item made, on success).",
        "Tools": "Each attempt (success or failure) uses one charge of the tool; a tool at 0 uses breaks.",
        "Stations": stations,
        "StationRange": engine["StationRange"],
        "StationZ": 16,
        "StationText": "NeedHeat/NeedOven/NeedMill/NeedWater/NeedMaker items need a matching world item or static "
                       "tile within StationRange tiles (a square, so diagonals count) whose Z is within StationZ of "
                       "the crafter's. Stations.Water also accepts the listed addons. Anvil/Forge are checked within "
                       "AnvilForgeRange and also need line of sight.",
        "AnvilForgeRange": engine["AnvilForgeRange"],
        "AnvilForgeLineOfSight": engine["AnvilForgeLOS"],
    }


if __name__ == "__main__":
    main()
