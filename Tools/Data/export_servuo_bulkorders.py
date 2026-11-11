"""One-time migration of UO's bulk order deeds (BODs) into Content/BulkOrders.json: for each of the eight crafts (Smith,
Tailor, Alchemy, Inscription, Tinkering, Fletching, Carpentry, Cooking) the vendors that hand them out, the small-BOD
item lists and the large-BOD sets (from Data/Bulk Orders/<Craft>/*.cfg), how a deed is rolled (list, amount, material,
exceptional), the materials and the skill each needs, the reward points, fame and gold a filled deed is worth, and the
points-bought reward catalogue with the item each reward constructs. The game owns the file from then on.

  python -I export_servuo_bulkorders.py <ServUO root> <Content/BulkOrders.json>

Read from ServUO and asserted (the script stops if the code doesn't look as expected):
  Scripts/Services/BulkOrders/BulkOrderSystem.cs   delay, cache, offer chance, large chance, skill and hue per craft,
                                                     banked-point share, the vendor-price gold of small deeds
  Scripts/Services/BulkOrders/BulkMaterialType.cs  material names (index order)
  Scripts/Services/BulkOrders/SmallBODs/*.cs       which cfg each list property loads, CreateRandomFor (amounts,
                                                     material chances, the skill gates), GetRequiredSkill
  Scripts/Services/BulkOrders/LargeBODs/*.cs       each large BOD's set switch, materials, exceptional chance
  Scripts/Services/BulkOrders/Rewards/Rewards.cs   RewardTypes, gold tables, the reward collections and the delegate
                                                     methods they call (resolved to the item class and its arguments)
  Scripts/Mobiles/**                               vendors overriding SupportsBulkOrders / BODType
  Scripts/VendorInfo, Scripts/**                   GenericBuyInfo prices (BulkOrderSystem.ComputeGold) and the class
                                                     index (does a cfg type exist, what does it derive from)
  Scripts/Misc/ResourceInfo.cs                     CraftResource enum and hues
The newest era is taken wherever the code checks Core.XX, and BulkOrderSystem.NewSystemEnabled is taken as true (the
points / banking reward system). Hand-written below (with the method they come from): which list a small BOD draws from
(SMALL_LISTS), the points formulas (POINTS); each asserts the code fragments it was written from.

At the end the script prints the reward item classes and BOD item types that are missing from Content/Items.json.
"""
import json
import os
import re
import sys

CRAFTS = [  # BODType, small BOD class, large BOD class, reward calculator class
    ("Smith", "SmallSmithBOD", "LargeSmithBOD", "SmithRewardCalculator"),
    ("Tailor", "SmallTailorBOD", "LargeTailorBOD", "TailorRewardCalculator"),
    ("Alchemy", "SmallAlchemyBOD", "LargeAlchemyBOD", "AlchemyRewardCalculator"),
    ("Inscription", "SmallInscriptionBOD", "LargeInscriptionBOD", "InscriptionRewardCalculator"),
    ("Tinkering", "SmallTinkerBOD", "LargeTinkerBOD", "TinkeringRewardCalculator"),
    ("Fletching", "SmallFletchingBOD", "LargeFletchingBOD", "FletchingRewardCalculator"),
    ("Carpentry", "SmallCarpentryBOD", "LargeCarpentryBOD", "CarpentryRewardCalculator"),
    ("Cooking", "SmallCookingBOD", "LargeCookingBOD", "CookingRewardCalculator"),
]

# Which list a small BOD draws from: hand-written from each Small*BOD.CreateRandomFor (the branch on Utility.RandomBool /
# RandomDouble before the entries are chosen). "Code" is the fragment (whitespace-normalised) asserted in the method;
# per list: the SmallBulkEntry property, the chance it is the list used, whether a coloured material is rolled for it
# (MaterialRoll: the chance that roll happens) and whether it can ask for exceptional.
SMALL_LISTS = {
    "Smith": {"Code": ["if (useMaterials = Utility.RandomBool()) entries = SmallBulkEntry.BlacksmithArmor; else entries = SmallBulkEntry.BlacksmithWeapons;"],
              "Lists": [("BlacksmithArmor", "0.5", 1, True), ("BlacksmithWeapons", "0.5", 0, True)]},
    "Tailor": {"Code": ["bool useMaterials = Utility.RandomBool();",
                        "if (useMaterials && theirSkill >= 6.2) // Ugly, but the easiest leather BOD is Leather Cap which requires at least 6.2 skill. entries = SmallBulkEntry.TailorLeather; else entries = SmallBulkEntry.TailorCloth;"],
               "Lists": [("TailorLeather", "Skill >= 6.2 ? 0.5 : 0", 1, True), ("TailorCloth", "Skill >= 6.2 ? 0.5 : 1", 0, True)],
               "Note": "a leather order only from 6.2 skill (the easiest leather item, a leather cap); below that every order is cloth"},
    "Alchemy": {"Code": ["entries = SmallBulkEntry.AlchemySmalls;"], "Lists": [("AlchemySmalls", "1", 0, False)]},
    "Inscription": {"Code": ["entries = SmallBulkEntry.InscriptionSmalls;"], "Lists": [("InscriptionSmalls", "1", 0, False)]},
    "Tinkering": {"Code": ["if (useMaterials = 0.75 > Utility.RandomDouble()) entries = SmallBulkEntry.TinkeringSmalls; else entries = SmallBulkEntry.TinkeringSmallsRegular;",
                           "if (useMaterials && theirSkill >= 70.1) excChance = (theirSkill + 80.0) / 200.0;"],
                  "Lists": [("TinkeringSmalls", "0.75", 1, True), ("TinkeringSmallsRegular", "0.25", 0, False)],
                  "Note": "only the 'smalls' list asks for metal or exceptional; tools, smithy hammers and jewellery never take a metal (NoMaterial), and jewellery asks for a random gem (Gem)"},
    "Fletching": {"Code": ["if (theirSkill < 30.0 || .20 > Utility.RandomDouble()) { entries = SmallBulkEntry.FletchingSmallsRegular; } else { useMaterials = true; entries = SmallBulkEntry.FletchingSmalls; }",
                           "bool reqExceptional = useMaterials && excChance > Utility.RandomDouble();"],
                  "Lists": [("FletchingSmalls", "Skill < 30 ? 0 : 0.8", 1, True), ("FletchingSmallsRegular", "Skill < 30 ? 1 : 0.2", 0, False)],
                  "Note": "below 30 skill every order is from the regular list (plain wood, never exceptional)"},
    "Carpentry": {"Code": ["bool useMaterials = Utility.RandomBool();", "entries = SmallBulkEntry.CarpentrySmalls;"],
                  "Lists": [("CarpentrySmalls", "1", 0.5, True)],
                  "Note": "one list; a coloured wood is rolled for half the orders"},
    "Cooking": {"Code": ["if (0.20 > Utility.RandomDouble()) { nonexceptional = true; entries = SmallBulkEntry.CookingSmallsRegular; } else { entries = SmallBulkEntry.CookingSmalls; }",
                         "bool reqExceptional = !nonexceptional && excChance > Utility.RandomDouble();"],
                "Lists": [("CookingSmalls", "0.8", 0, True), ("CookingSmallsRegular", "0.2", 0, False)],
                "Note": "the regular list never asks for exceptional; cooking doesn't drop items the player can't make exceptional"},
}

QTY = "(Quantity == 10 ? 10 : Quantity == 15 ? 25 : Quantity == 20 ? 50 : 0)"
# ComputePoints of each <Craft>RewardCalculator, hand-written; "Code" fragments are asserted in that method.
POINTS = {
    "Smith": {"Expr": QTY + " + Exceptional * 200 + (ItemCount > 1 ? TypePoints : 0) + (Material >= 1 && Material <= 8 ? 200 + 50 * (Material - 1) : 0)",
              "Code": ["if (exceptional) points += 200;", "if (itemCount > 1) points += this.LookupTypePoints(this.m_Types, type);",
                       "if (material >= BulkMaterialType.DullCopper && material <= BulkMaterialType.Valorite) points += 200 + (50 * (material - BulkMaterialType.DullCopper));"]},
    "Tailor": {"Expr": QTY + " + Exceptional * 100 + (ItemCount == 4 ? 300 : ItemCount == 5 ? 400 : ItemCount == 6 ? 500 : 0) + (Material == 1 ? 50 : Material == 2 ? 100 : Material == 3 ? 150 : 0)",
               "Code": ["if (exceptional) points += 100;", "if (itemCount == 4) points += 300; else if (itemCount == 5) points += 400; else if (itemCount == 6) points += 500;",
                        "if (material == BulkMaterialType.Spined) points += 50; else if (material == BulkMaterialType.Horned) points += 100; else if (material == BulkMaterialType.Barbed) points += 150;"]},
    "Tinkering": {"Expr": QTY + " + (ItemCount == 3 ? 200 : ItemCount == 4 ? 300 : ItemCount == 5 ? 350 : ItemCount == 6 ? 400 : 0) + (Material >= 1 && Material <= 8 ? 200 + 50 * (Material - 1) : 0) + Exceptional * 200",
                  "Code": ["case 3: points += 200; break; case 4: points += 300; break; case 5: points += 350; break; case 6: points += 400; break;",
                           "if (material >= BulkMaterialType.DullCopper && material <= BulkMaterialType.Valorite) points += 200 + (50 * (material - BulkMaterialType.DullCopper));",
                           "if (exceptional) points += 200;"]},
    "Carpentry": {"Expr": QTY + " + Exceptional * 200 + (Material >= 1 ? 250 + 50 * Material : 0) + (ItemCount > 1 ? TypePoints : 0)",
                  "Code": ["if (exceptional) points += 200;", "case BulkMaterialType.OakWood: points += 300; break;", "case BulkMaterialType.AshWood: points += 350; break;",
                           "case BulkMaterialType.YewWood: points += 400; break;", "case BulkMaterialType.Heartwood: points += 450; break;",
                           "case BulkMaterialType.Bloodwood: points += 500; break;", "case BulkMaterialType.Frostwood: points += 550; break;",
                           "if (itemCount > 1) points += this.LookupTypePoints(m_Types, type);"]},
    "Fletching": {"Expr": QTY + " + Exceptional * 200 + (Material >= 1 ? 250 + 50 * Material : 0) + (ItemCount > 1 ? TypePoints : 0)",
                  "Code": ["if (exceptional) points += 200;", "case BulkMaterialType.OakWood: points += 300; break;", "case BulkMaterialType.AshWood: points += 350; break;",
                           "case BulkMaterialType.YewWood: points += 400; break;", "case BulkMaterialType.Heartwood: points += 450; break;",
                           "case BulkMaterialType.Bloodwood: points += 500; break;", "case BulkMaterialType.Frostwood: points += 550; break;",
                           "if (itemCount > 1) points += this.LookupTypePoints(m_Types, type);"]},
    "Inscription": {"Expr": QTY + " + (ItemCount > 1 ? TypePoints : 0)",
                    "Code": ["if (itemCount > 1) points += this.LookupTypePoints(m_Types, type); return points;"]},
    "Cooking": {"Expr": QTY + " + Exceptional * 200 + (ItemCount > 1 ? TypePoints : 0)",
                "Code": ["if (exceptional) points += 200;", "if (itemCount > 1) points += this.LookupTypePoints(m_Types, type);"]},
    "Alchemy": {"Expr": QTY + " + (ItemCount == 3 ? (TypePoints > 0 ? TypePoints : 300) : ItemCount == 4 ? 200 : ItemCount == 5 ? 400 : ItemCount == 6 ? 350 : 0)",
                "Code": ["if (itemCount == 3) { if(type == typeof(RefreshPotion) || type == typeof(HealPotion) || type == typeof(CurePotion)) points += 250; else points += 300; }",
                         "else if (itemCount == 4) points += 200; else if (itemCount == 5) points += 400; else if (itemCount == 6) points += 350;"],
                # AlchemyRewardCalculator has no m_Types; its ComputePoints type test, written as a RewardTypes entry
                "RewardTypes": [{"Points": 250, "Types": ["RefreshPotion", "HealPotion", "CurePotion"],
                                 "Note": "not a ServUO RewardType: AlchemyRewardCalculator.ComputePoints gives a 3-part large BOD whose first item is one of these 250 points, any other 3-part 300"}]},
}

VARS = {"Skill", "Quantity", "Exceptional", "Material", "ItemCount", "TypePoints", "Gold", "BuyPrice"}


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def norm(s):
    s = re.sub(r"/\*.*?\*/", "", s, flags=re.S)
    return re.sub(r"\s+", " ", s).strip()


def strip_comments(s):
    s = re.sub(r"/\*.*?\*/", "", s, flags=re.S)
    return re.sub(r"//[^\n]*", "", s)


def block(src, start):
    """The text inside the braces that open at or after src[start]."""
    i = src.index("{", start)
    depth = 0
    for j in range(i, len(src)):
        if src[j] == "{":
            depth += 1
        elif src[j] == "}":
            depth -= 1
            if depth == 0:
                return src[i + 1:j]
    raise AssertionError("unbalanced braces")


def method(src, pattern, what):
    m = re.search(pattern, src)
    assert m, f"method not found: {what}"
    return block(src, m.end() - 1 if src[m.end() - 1] == "{" else m.end())


def num(s):
    s = s.strip()
    return int(s, 16) if s.lower().startswith("0x") else (float(s) if "." in s else int(s))


def expect(text, fragments, what):
    t = norm(text)
    for frag in fragments:
        assert norm(frag) in t, f"{what}: ServUO code changed, expected: {frag}"


# ------------------------------------------------------------------ the evaluator's grammar, to check every Expr
TOK = re.compile(r"\s*(?:(\d+\.\d*|\.\d+|\d+)|([A-Za-z_]\w*)|(&&|\|\||==|!=|<=|>=|[-+*/%<>!?:(),]))")
FUNCS = {"trunc": 1, "floor": 1, "ceil": 1, "round": 1, "abs": 1, "sqrt": 1, "min": 2, "max": 2, "clamp": 3, "rand": 2, "randf": 0}


def check_expr(expr):
    toks, pos = [], 0
    expr = expr.strip()
    while pos < len(expr):
        m = TOK.match(expr, pos)
        assert m and m.end() > pos, f"bad token in {expr!r} at {pos}"
        toks.append(m.group(1) or m.group(2) or m.group(3))
        pos = m.end()
    toks.append(None)
    i = [0]

    def peek():
        return toks[i[0]]

    def take(t=None):
        tok = toks[i[0]]
        assert t is None or tok == t, f"expected {t} in {expr!r}, got {tok}"
        i[0] += 1
        return tok

    def ternary():
        orr()
        if peek() == "?":
            take()
            ternary()
            take(":")
            ternary()

    def binary(sub, ops):
        def f():
            sub()
            while peek() in ops:
                take()
                sub()
        return f

    def unary():
        if peek() in ("!", "-"):
            take()
            unary()
        else:
            primary()

    def primary():
        t = take()
        if t == "(":
            ternary()
            take(")")
        elif re.match(r"[\d.]", t):
            pass
        elif t in FUNCS:
            take("(")
            n = 0
            if peek() != ")":
                ternary()
                n = 1
                while peek() == ",":
                    take()
                    ternary()
                    n += 1
            take(")")
            assert n == FUNCS[t], f"{t} takes {FUNCS[t]} arguments in {expr!r}"
        else:
            assert t in VARS, f"unknown variable {t} in {expr!r}"

    mul = binary(unary, ("*", "/", "%"))
    add = binary(mul, ("+", "-"))
    rel = binary(add, ("<", "<=", ">", ">="))
    eq = binary(rel, ("==", "!="))
    andd = binary(eq, ("&&",))
    orr = binary(andd, ("||",))
    ternary()
    assert peek() is None, f"trailing tokens in {expr!r}"


def check_all(node):
    n = 0
    if isinstance(node, dict):
        for k, v in node.items():
            if k == "Expr":
                check_expr(v)
                n += 1
            else:
                n += check_all(v)
    elif isinstance(node, list):
        for v in node:
            n += check_all(v)
    return n


def X(expr, note=None):
    d = {"Expr": expr}
    if note:
        d["Note"] = note
    return d


# ------------------------------------------------------------------ ServUO-wide indexes
def class_index(scripts):
    """class name -> (base, file); a cfg type ServUO can't find is dropped by SmallBulkEntry.LoadEntries."""
    idx = {}
    for dp, _, fns in os.walk(scripts):
        for fn in fns:
            if not fn.endswith(".cs"):
                continue
            p = os.path.join(dp, fn)
            src = strip_comments(read(p))
            for m in re.finditer(r"\b(?:class|struct)\s+(\w+)(?:\s*<[^>{]*>)?\s*(?::\s*([\w.]+))?", src):
                idx.setdefault(m.group(1), (m.group(2).split(".")[-1] if m.group(2) else None, p))
    return idx


def derives(idx, cls, bases):
    seen = set()
    while cls and cls not in seen:
        seen.add(cls)
        if cls in bases:
            return True
        cls = idx.get(cls, (None,))[0]
    return False


def buy_prices(scripts):
    """GenericBuyInfo(typeof(T), price, ...) across the scripts: what BulkOrderSystem.ComputeGold reads from
    GenericBuyInfo.BuyPrices (the price the last-built shop sells it for). Conflicting prices: the commonest (then the
    highest) is taken."""
    seen = {}
    pat = re.compile(r"\bnew\s+\w*BuyInfo\(\s*(?:(?:\"[^\"]*\"|\d+|null)\s*,\s*)?typeof\((\w+)\)\s*,\s*(\d+)\s*,")
    for dp, _, fns in os.walk(scripts):
        for fn in fns:
            if fn.endswith(".cs"):
                for m in pat.finditer(read(os.path.join(dp, fn))):
                    seen.setdefault(m.group(1), []).append(int(m.group(2)))
    out, conflicts = {}, {}
    for t, prices in seen.items():
        best = max(set(prices), key=lambda p: (prices.count(p), p))
        out[t] = best
        if len(set(prices)) > 1:
            conflicts[t] = sorted(set(prices))
    return out, conflicts


def resources(scripts):
    src = read(os.path.join(scripts, "Misc/ResourceInfo.cs"))
    body = block(src, src.index("public enum CraftResource\n") if "public enum CraftResource\n" in src else src.index("enum CraftResource"))
    names, value = {}, -1
    for part in strip_comments(body).split(","):
        part = part.strip()
        if not part:
            continue
        m = re.fullmatch(r"(\w+)(?:\s*=\s*(\d+))?", part)
        assert m, part
        value = int(m.group(2)) if m.group(2) else value + 1
        names[m.group(1)] = value
    assert names["Iron"] == 1 and names["DullCopper"] == 2 and names["OakWood"] == 302, "CraftResource enum"
    assert src.index("m_AOSLeatherInfo =") > src.index("m_LeatherInfo ="), "leather tables order"
    hues = {}
    for m in re.finditer(r"new CraftResourceInfo\((0x[0-9A-Fa-f]+|\d+),\s*\d+,\s*\"[^\"]*\",\s*CraftAttributeInfo\.\w+,\s*CraftResource\.(\w+)", src):
        hues[m.group(2)] = num(m.group(1))  # later (AOS) leather table wins: Core.AOS
    assert hues["DullCopper"] == 0x973 and hues["Frostwood"] == 0x47F and hues["SpinedLeather"] == 0x8AC, "resource hues"
    return names, hues


def vendors(scripts):
    base = read(os.path.join(scripts, "Mobiles/NPCs/BaseVendor.cs"))
    dm = re.search(r"public virtual BODType BODType\s*\{\s*get\s*\{\s*return BODType\.(\w+);", base)
    assert dm, "BaseVendor.BODType"
    out = {}
    for dp, _, fns in os.walk(os.path.join(scripts, "Mobiles")):
        for fn in fns:
            if not fn.endswith(".cs"):
                continue
            src = read(os.path.join(dp, fn))
            for cm in re.finditer(r"class\s+(\w+)\s*:\s*\w+", src):
                body = block(src, cm.end())
                sm = re.search(r"override bool SupportsBulkOrders\(Mobile from\)\s*\{([^}]*)\}", body)
                if not sm:
                    continue
                cond = norm(sm.group(1))
                km = re.fullmatch(r"return \(?(?:BulkOrderSystem\.NewSystemEnabled && |Core\.\w+ && )*from is PlayerMobile (?:&& Core\.\w+ )?&& from\.Skills\[SkillName\.(\w+)\]\.Base > 0\)?;", cond)
                assert km, f"{fn}: SupportsBulkOrders reads {cond}"
                tm = re.search(r"override BODType BODType\s*\{\s*get\s*\{\s*return BODType\.(\w+);", body)
                out[cm.group(1)] = (tm.group(1) if tm else dm.group(1), km.group(1))
    return out


# ------------------------------------------------------------------ bulk order data
def list_properties(src):
    """SmallBulkEntry / LargeBulkEntry property -> (cfg folder, cfg name)."""
    props = {}
    for m in re.finditer(r"public static SmallBulkEntry\[\]\s+(\w+)\s*\{\s*get\s*\{\s*return GetEntries\(\"([^\"]+)\",\s*\"([^\"]+)\"\);", src):
        props[m.group(1)] = (m.group(2), m.group(3))
    return props


def load_cfg(root, folder, name, idx, problems):
    """SmallBulkEntry.LoadEntries: Type,graphic[,cliloc[,hue]]; the name is 1020000 + graphic (1078872 + graphic past
    0x4000) when no cliloc is given; a type ServUO can't find is skipped."""
    path = os.path.join(root, "Data", "Bulk Orders", folder, name + ".cfg")
    assert os.path.exists(path), path
    items = []
    lower = {k.lower(): k for k in idx}
    for line in read(path).splitlines():
        if not line or line.startswith("#"):
            continue
        parts = line.split(",")
        assert len(parts) >= 2, f"{path}: {line}"
        tname, graphic = parts[0].strip(), num(parts[1])
        cls = tname if tname in idx else lower.get(tname.lower())
        if cls is None:
            problems.append(f"{folder}/{name}.cfg: type {tname} doesn't exist in ServUO (ServUO skips the line)")
            continue
        if cls != tname:
            problems.append(f"{folder}/{name}.cfg: {tname} matched {cls} ignoring case")
        if len(parts) <= 2:
            cliloc, hue = (1020000 + graphic if graphic < 0x4000 else 1078872 + graphic), 0
        else:
            cliloc = num(parts[2])
            hue = num(parts[3]) if len(parts) >= 4 and parts[3].strip() else 0
        assert graphic > 0, line
        items.append({"Type": cls, "Graphic": graphic, "Name": cliloc, "Hue": hue})
    return items


def parse_chances(src, field):
    m = re.search(rf"double\[\]\s+{field}\s*=\s*new double\[\]\s*\{{([^}}]*)\}}", src)
    assert m, field
    return [float(v) for v in re.findall(r"\d*\.\d+", strip_comments(m.group(1)))]


def materials_for(src, file_src, mats, what):
    m = re.search(r"GetRandomMaterial\(BulkMaterialType\.(\w+),\s*(\w+)\)", src)
    if not m:
        return ["None"], None
    chances = parse_chances(file_src, m.group(2))
    start = mats.index(m.group(1))
    out = ["None"] + mats[start:start + len(chances) - 1]
    assert len(out) == len(chances), what
    assert abs(sum(chances) - 1) < 0.002, f"{what}: material chances sum {sum(chances)}"
    return out, chances


def gold_tables(region, what):
    """The gold table ComputeGold uses (newest era branch of 'goldTable = ...'), labelled by its comments."""
    cg = method(region, r"override int ComputeGold\(int quantity, bool exceptional, BulkMaterialType material, int itemCount, Type type\)\s*\{", what + ".ComputeGold")
    m = re.search(r"goldTable\s*=\s*\(?\s*(?:Core\.\w+\s*\?\s*(\w+)\s*:\s*\w+|(\w+))\s*\)?\s*;", cg)
    assert m, what + " goldTable"
    name = m.group(1) or m.group(2)
    dm = re.search(rf"int\[\](\[\])?(\[\])?\s+{name}\s*=\s*new int", region)
    assert dm, name
    body = block(region, dm.end())
    tables = []
    if dm.group(2):  # int[][][]
        for chunk in re.split(r"new int\[\]\[\]", body)[1:]:
            lm = re.match(r"\s*//([^\n]*)", chunk)
            rows = [[int(v) for v in re.findall(r"\d+", r)] for r in re.findall(r"new int\[\]\s*\{([^}]*)\}", chunk)]
            assert len(rows) == 3, f"{what}: gold rows"
            tables.append({"Label": lm.group(1).strip() if lm else "", "Values": rows})
    else:  # int[][]: one value per quantity
        for lab, vals in re.findall(r"new int\[\]\s*(?://([^\n]*))?\s*\{([^}]*)\}", body):
            v = [int(x) for x in re.findall(r"\d+", strip_comments(vals))]
            assert len(v) in (0, 3), f"{what}: gold row {v}"
            tables.append({"Label": lab.strip(), "Values": [[x] for x in v]})
    return cg, tables


def resolve_material_column(cg, materials, what):
    m = re.search(r"int mtrlIndex = (.+?);\n", cg)
    assert m, what + " mtrlIndex"
    t = norm(m.group(1))
    col = {}
    rm = re.fullmatch(r"\(material >= BulkMaterialType\.(\w+) && material <= BulkMaterialType\.(\w+)\) \? 1 \+ \(int\)\(material - BulkMaterialType\.\1\) : 0", t)
    if rm:
        lo, hi = materials.index(rm.group(1)), materials.index(rm.group(2))
        for i in range(lo, hi + 1):
            col[i] = 1 + i - lo
    else:
        pairs = re.findall(r"material == BulkMaterialType\.(\w+) \? (\d+)", t)
        assert pairs and t.endswith(": 0)") or t.endswith(": 0"), f"{what}: mtrlIndex {t}"
        for name, v in pairs:
            col[materials.index(name)] = int(v)
    if all(col.get(i, 0) == i for i in range(len(materials))):
        return "Material"
    expr = "0"
    for i in sorted(col, reverse=True):
        expr = f"Material == {i} ? {col[i]} : " + expr
    return expr


def gold_table_for(craft, cg, region, count, first_type, reward_types, what):
    """Which gold table a deed uses before the exceptional offset, from <Craft>RewardCalculator.ComputeGold."""
    t = norm(cg)
    if "goldTable[itemCount - 1][quanIndex]" in t:
        return count - 1, False
    if craft == "Smith":  # SmithRewardCalculator.ComputeType
        expect(strip_comments(region), ["if (itemCount == 1) return 0;", "for (typeIdx = 0; typeIdx < 7; ++typeIdx) { if (this.m_Types[typeIdx].Contains(type)) break; }",
                   "if (typeIdx > 5) typeIdx = 5;", "return (typeIdx + 1) * 2;", "if (exceptional) typeIndex++;"], what)
        if count == 1:
            return 0, True
        ti = next((i for i in range(7) if first_type in reward_types[i]["Types"]), 7)
        return (min(ti, 5) + 1) * 2, True
    m = re.search(r"int typeIndex = \(\(([^;]*?)\) \* 2\) \+ \(exceptional \? 1 : 0\);", t)
    assert m, f"{what}: typeIndex"
    pairs = dict((int(a), int(b)) for a, b in re.findall(r"itemCount == (\d+) \? (\d+)", m.group(1)))
    return pairs.get(count, 0) * 2, True


def calc_region(rewards, cls):
    m = re.search(rf"class {cls} : RewardCalculator", rewards)
    assert m, cls
    return block(rewards, m.end())


def reward_types(region, what):
    out = []
    m = re.search(r"RewardType\[\]\s+m_Types\s*=\s*(?:new RewardType\[\]\s*)?\{", region)
    if not m:
        return out
    body = strip_comments(block(region, m.end() - 1))
    for rm in re.finditer(r"new RewardType\((\d+)\s*,", body):
        depth, j = 1, rm.end()
        while depth:
            depth += {"(": 1, ")": -1}.get(body[j], 0)
            j += 1
        out.append({"Points": int(rm.group(1)), "Types": re.findall(r"typeof\((\w+)\)", body[rm.end():j])})
    assert out, what
    return out


def int_arrays(region):
    """static int[] / int[][] fields of a calculator (cloth and sandal hues)."""
    arrs = {}
    for m in re.finditer(r"int\[\](\[\])?\s+(\w+)\s*=\s*new int\[\]", region):
        body = block(region, m.end())
        if m.group(1):
            arrs[m.group(2)] = [[num(v) for v in re.findall(r"0x[0-9A-Fa-f]+|\d+", r)] for r in re.findall(r"new int\[\]\s*\{([^}]*)\}", body)]
        else:
            arrs[m.group(2)] = [num(v) for v in re.findall(r"0x[0-9A-Fa-f]+|\d+", strip_comments(body))]
    return arrs


def split_args(s):
    out, depth, cur = [], 0, ""
    for ch in s:
        if ch in "([":
            depth += 1
        elif ch in ")]":
            depth -= 1
        if ch == "," and depth == 0:
            out.append(cur.strip())
            cur = ""
        else:
            cur += ch
    if cur.strip():
        out.append(cur.strip())
    return out


class Rewards:
    def __init__(self, rewards_src, scripts, idx, res_names, res_hues):
        self.base = calc_region_base(rewards_src)
        self.scripts, self.idx, self.res, self.hues = scripts, idx, res_names, res_hues
        self.res_by_value = {v: k for k, v in res_names.items()}

    def value(self, s, arrays):
        """Simplify one constructor argument (newest era branch, resources by name, arithmetic)."""
        s = s.strip()
        m = re.fullmatch(r"Core\.\w+\s*\?\s*(.+?)\s*:\s*[^:]+", s)
        if m:
            s = m.group(1).strip()
        while re.fullmatch(r"\((.*)\)", s) and s.count("(") == 1:
            s = s[1:-1].strip()
        m = re.fullmatch(r"\(CraftResource\)\s*(\d+)", s)
        if m:
            return self.res_by_value[int(m.group(1))]
        m = re.fullmatch(r"CraftResource\.(\w+)(?:\s*\+\s*(\d+))?", s)
        if m:
            return self.res_by_value[self.res[m.group(1)] + int(m.group(2) or 0)]
        m = re.fullmatch(r"(\w+)\[Utility\.Random\(\1\.Length\)\]", s)
        if m:
            return {"RandomOf": arrays[m.group(1)]}
        m = re.fullmatch(r"(\w+)\[(\d+)\]\[Utility\.Random\(\1\[\2\]\.Length\)\]", s)
        if m:
            return {"RandomOf": arrays[m.group(1)][int(m.group(2))]}
        if re.fullmatch(r"[\d\s+\-*/()x0-9A-Fa-f]+", s) and not re.search(r"[A-Fa-f]", re.sub(r"0x[0-9A-Fa-f]+", "", s)):
            return int(eval(re.sub(r"0x[0-9A-Fa-f]+", lambda h: str(int(h.group(0), 16)), s).replace("/", "//"), {"__builtins__": {}}))
        if s in ("true", "false"):
            return s == "true"
        m = re.fullmatch(r"\w+\.(\w+)", s)
        return m.group(1) if m else s

    def ctor_params(self, cls, args):
        """Parameter names of the constructor the arguments fit best (int for numbers, bool for flags)."""
        info = self.idx.get(cls)
        assert info, f"reward class {cls} not in ServUO"
        src = strip_comments(read(info[1]))
        best = None
        for m in re.finditer(rf"public\s+{cls}\s*\(([^)]*)\)", src):
            ps = [re.sub(r"\s*=.*$", "", p).split() for p in split_args(m.group(1)) if p]
            if len(ps) != len(args) or (len(ps) == 1 and ps[0][0] == "Serial"):
                continue
            score = sum((t == "int") == (isinstance(a, int) and not isinstance(a, bool)) and (t == "bool") == isinstance(a, bool)
                        for (t, _), a in zip(ps, args))
            if best is None or score > best[0]:
                best = (score, [p[-1] for p in ps])
        return best[1] if best else [f"arg{i}" for i in range(len(args))]

    def construct(self, cls, args_src, param, arg, arrays):
        args = [self.value(re.sub(rf"\b{param}\b", str(arg), a), arrays) for a in split_args(args_src)]
        names = self.ctor_params(cls, args)
        return {"Item": cls, "Args": dict(zip(names, args))}

    def resolve(self, region, delegate, arg, what):
        for src in (region, self.base):
            m = re.search(rf"ConstructCallback\s+{delegate}\s*=\s*new\s+ConstructCallback\((\w+)\)", src)
            meth = m.group(1) if m else delegate
            mm = re.search(rf"static\s+Item\s+{meth}\s*\(\s*int\s+(\w+)\s*\)\s*\{{", src)
            if mm:
                break
        assert mm, f"{what}: delegate {delegate}"
        param, body = mm.group(1), strip_comments(block(src, mm.end() - 1))
        arrays = int_arrays(region)
        body = re.sub(r"throw new InvalidOperationException\(\);", "", body)
        # switch (type): the arg's case (or default)
        if re.search(rf"switch\s*\(\s*{param}\s*\)", body):
            default = None
            for labels, cls, a in re.findall(r"((?:(?:case\s+\d+|default)\s*:\s*)+)return\s+new\s+(\w+)\(([^;]*)\)\s*;", body):
                cases = [int(c) for c in re.findall(r"case\s+(\d+)", labels)]
                if arg in cases:
                    return self.construct(cls, a, param, arg, arrays), meth
                if "default" in labels:
                    default = (cls, a)
            if default:
                return self.construct(default[0], default[1], param, arg, arrays), meth
            raise AssertionError(f"{what}: {meth}({arg}) has no case")
        # switch (Utility.Random(n)): one of the cases at random
        if re.search(r"switch\s*\(\s*Utility\.Random\(\d+\)\s*\)", body):
            choices = [c for c in dict.fromkeys(re.findall(r"return\s+new\s+(\w+)\(", body))]
            r = {"Item": choices[0], "Choices": choices}
            return r, meth
        # if (cond) return new X(...);
        ifs = re.findall(r"if\s*\(([^;{]*?)\)\s*return\s+new\s+(\w+)\(([^;]*)\)\s*;", body)
        if ifs:
            for cond, cls, a in ifs:
                c = re.sub(rf"\b{param}\b", str(arg), cond)
                c = re.sub(r"\w+\.Length", "999", c).replace("&&", " and ").replace("||", " or ")
                if eval(c, {"__builtins__": {}}):
                    return self.construct(cls, a, param, arg, arrays), meth
            raise AssertionError(f"{what}: {meth}({arg}) matches no branch")
        m = re.search(r"new\s+(\w+)\(([^;]*?)\)\s*;", body)
        assert m, f"{what}: {meth} builds nothing"
        r = self.construct(m.group(1), m.group(2), param, arg, arrays)
        for prop, v in re.findall(r"\b\w+\.(\w+)\s*=\s*([^;]+);", body):
            r.setdefault("Set", {})[prop] = self.value(re.sub(rf"\b{param}\b", str(arg), v), arrays)
        return r, meth

    def collection(self, region, what):
        out = []
        for m in re.finditer(r"RewardCollection\.Add\(new BODCollectionItem\(", region):
            depth, j = 1, m.end()
            while depth:
                depth += {"(": 1, ")": -1}.get(region[j], 0)
                j += 1
            a = split_args(region[m.end():j - 1])
            assert len(a) in (5, 6), f"{what}: {a}"
            hm = re.fullmatch(r"CraftResources\.GetHue\(CraftResource\.(\w+)\)", a[2])
            hue = self.hues[hm.group(1)] if hm else num(a[2])
            arg = 0
            if len(a) == 6:
                am = re.fullmatch(r"\(int\)CraftResource\.(\w+)", a[5])
                arg = self.res[am.group(1)] if am else num(a[5])
            made, meth = self.resolve(region, a[4], arg, what)
            rec = {"Points": num(a[3]), "Name": num(a[1]), "Graphic": num(a[0]), "Hue": hue, "Item": made["Item"], "Arg": arg}
            if "Choices" in made:
                rec["Choices"] = made["Choices"]
            if made.get("Args"):
                rec["Args"] = made["Args"]
            if made.get("Set"):
                rec["Set"] = made["Set"]
            desc = made["Item"] + "(" + ", ".join(f"{k}=" + (f"one of {v['RandomOf']} at random" if isinstance(v, dict) else str(v)) for k, v in made.get("Args", {}).items()) + ")"
            if "Choices" in made:
                desc = "one of " + ", ".join(made["Choices"]) + " at random"
            for k, v in made.get("Set", {}).items():
                desc += f", {k} = " + (f"one of {v['RandomOf']} at random" if isinstance(v, dict) else str(v))
            rec["Note"] = f"{meth}({arg}) makes {desc}"
            out.append(rec)
        assert out, what
        return out


def calc_region_base(rewards):
    m = re.search(r"public abstract class RewardCalculator", rewards)
    assert m
    return block(rewards, m.end())


def main():
    root, out_path = sys.argv[1], sys.argv[2]
    scripts = os.path.join(root, "Scripts")
    bo = os.path.join(scripts, "Services/BulkOrders")
    system = read(os.path.join(bo, "BulkOrderSystem.cs"))
    rewards_src = read(os.path.join(bo, "Rewards/Rewards.cs"))
    small_base = read(os.path.join(bo, "SmallBODs/SmallBOD.cs"))
    problems = []

    mats_src = read(os.path.join(bo, "BulkMaterialType.cs"))
    mats = re.findall(r"\w+", block(mats_src, mats_src.index("enum BulkMaterialType")))
    assert mats[0] == "None" and "Valorite" in mats and "Frostwood" in mats, "BulkMaterialType"

    # ---- rules (BulkOrderSystem)
    delay = re.search(r"public static readonly int Delay = (\d+);", system)
    cached = re.search(r"public static readonly int MaxCachedDeeds = (\d+);", system)
    assert delay and cached, "Delay / MaxCachedDeeds"
    cb = norm(method(system, r"public static Item CreateBulkOrder\(Mobile m, BODType type, bool fromContextMenu\)\s*\{", "CreateBulkOrder"))
    om = re.search(r"fromContextMenu \|\| ([\d.]+) > Utility\.RandomDouble\(\)", cb)
    lm = re.search(r"bool doLarge = theirSkill >= ([\d.]+) && \(\(theirSkill - ([\d.]+)\) / ([\d.]+)\) > Utility\.RandomDouble\(\);", cb)
    assert om and lm, "CreateBulkOrder"
    banked = re.findall(r"banked = \(double\)points \* (\d*\.\d+);", system)
    assert len(banked) == 2, "banked shares"
    gm = norm(method(system, r"public static bool ComputeGold\(Type type, int quantity, out int gold\)\s*\{", "BulkOrderSystem.ComputeGold"))
    assert "gold = (quantity * GenericBuyInfo.BuyPrices[type]) / 2;" in gm, "BulkOrderSystem.ComputeGold"
    expect(system, ["if (entry.LastBulkOrder + TimeSpan.FromHours(Delay) < DateTime.UtcNow) { entry.LastBulkOrder = DateTime.UtcNow; return true; } else if (entry.CachedDeeds > 0) { entry.CachedDeeds--; return true; }",
                    "else if (LastBulkOrder + TimeSpan.FromHours(BulkOrderSystem.Delay) < DateTime.UtcNow) { CachedDeeds++; LastBulkOrder = LastBulkOrder + TimeSpan.FromHours(BulkOrderSystem.Delay); }",
                    "return Math.Max(m.Skills[skill].Base, m.GetRacialSkillBonus(skill));"], "BulkOrderSystem")
    skills = dict(re.findall(r"case BODType\.(\w+):\s*return SkillName\.(\w+);", system))
    hues = {k: num(v) for k, v in re.findall(r"case BODType\.(\w+):\s*return\s+(0x[0-9A-Fa-f]+|\d+);", system)}
    assert len(skills) == 8 and len(hues) == 8, "GetSkillForBOD / GetBodHue"

    rs = method(small_base, r"public static double GetRequiredSkill\(BulkMaterialType type\)\s*\{", "GetRequiredSkill")
    req = {}
    for labels, v in re.findall(r"((?:case BulkMaterialType\.\w+:\s*)+)skillReq = ([\d.]+);", rs):
        for name in re.findall(r"BulkMaterialType\.(\w+)", labels):
            req[name] = float(v)
    gr = norm(method(small_base, r"public static BulkMaterialType GetRandomMaterial\(BulkMaterialType start, double\[\] chances\)\s*\{", "GetRandomMaterial"))
    assert "if (random < chances[i]) return (i == 0 ? BulkMaterialType.None : start + (i - 1)); random -= chances[i];" in gr, "GetRandomMaterial"

    small_props = list_properties(read(os.path.join(bo, "SmallBODs/SmallBulkEntry.cs")))
    large_props = list_properties(read(os.path.join(bo, "LargeBODs/LargeBulkEntry.cs")))
    assert len(small_props) >= 13 and len(large_props) >= 40, "list properties"

    print("indexing ServUO classes and shop prices...")
    idx = class_index(scripts)
    prices, price_conflicts = buy_prices(scripts)
    res_names, res_hues = resources(scripts)
    vend = vendors(scripts)
    rw = Rewards(rewards_src, scripts, idx, res_names, res_hues)
    title = re.search(r"new TextDefinition\((\d+) \+ title\)", read(os.path.join(bo, "Items/BODRewardTitleDeed.cs")))
    assert title, "BODRewardTitleDeed"

    tinker_src = read(os.path.join(bo, "SmallBODs/SmallTinkerBOD.cs"))
    nm = re.search(r"_NonMaterials\s*=\s*\{([^}]*)\}", tinker_src)
    assert nm and "return _NonMaterials.Any(x => x == t || t.IsSubclassOf(x));" in tinker_src, "CannotAssignMaterial"
    non_materials = set(re.findall(r"typeof\((\w+)\)", nm.group(1)))
    assert "if (entry.Type.IsSubclassOf(typeof(BaseJewel)))" in tinker_src, "tinker gems"

    crafts, bod_types, reward_items = {}, set(), set()
    counts = {}
    for craft, small_cls, large_cls, calc in CRAFTS:
        what = craft
        ssrc = read(os.path.join(bo, "SmallBODs", small_cls + ".cs"))
        lsrc = read(os.path.join(bo, "LargeBODs", large_cls + ".cs"))
        region = calc_region(rewards_src, calc)

        # ---- small: CreateRandomFor
        cr = method(ssrc, rf"public static {small_cls} CreateRandomFor\(Mobile m\)\s*\{{", small_cls + ".CreateRandomFor")
        crn = norm(cr)
        rule = SMALL_LISTS[craft]
        expect(cr, rule["Code"], small_cls + ".CreateRandomFor")
        assert f"BulkOrderSystem.GetBODSkill(m, SkillName.{skills[craft]})" in crn, what + " skill"
        amounts = []
        for cond, vals in re.findall(r"(if \(theirSkill >= [\d.]+\)|else if \(theirSkill >= [\d.]+\)|else) amountMax = Utility\.RandomList\(([\d, ]+)\);", crn):
            t = re.search(r"[\d.]+", cond)
            amounts.append({"MinSkill": float(t.group(0)) if t else 0, "Values": [int(v) for v in vals.split(",")]})
        assert len(amounts) == 3 and amounts[-1]["MinSkill"] == 0, what + " amounts"
        materials, chances = materials_for(cr, ssrc, mats, what)
        if chances:
            mm = re.search(r"if \(useMaterials && theirSkill >= ([\d.]+)\) \{ for \(int i = 0; i < (\d+); \+\+i\) \{ BulkMaterialType check = GetRandomMaterial\(BulkMaterialType\.\w+, \w+\); double skillReq = GetRequiredSkill\(check\); if \(theirSkill >= skillReq\) \{ material = check; break; \} \} \}", crn)
            assert mm, what + " material roll"
            mat_min, tries = float(mm.group(1)), int(mm.group(2))
        em = re.search(r"if \((?:useMaterials && )?theirSkill >= ([\d.]+)\) excChance = \(theirSkill \+ ([\d.]+)\) / ([\d.]+);", crn)
        if em:
            exc = f"Skill >= {em.group(1)} ? (Skill + {num(em.group(2)):g}) / {num(em.group(3)):g} : 0"
        else:
            assert "reqExceptional" not in crn, what + " exceptional"
            exc = "0"
        filt = "if (reqExceptional) chance = item.GetExceptionalChance(system, chance, m);" in crn
        assert "if (chance > 0.0) validEntries.Add(entries[i]);" in crn and "if (allRequiredSkills && chance >= 0.0)" in crn, what + " valid entries"
        lists = []
        for prop, chance, roll, exc_ok in rule["Lists"]:
            folder, name = small_props[prop]
            items = load_cfg(root, folder, name, idx, problems)
            for it in items:
                bod_types.add(it["Type"])
                if it["Type"] in prices:
                    it["BuyPrice"] = prices[it["Type"]]
                if craft == "Tinkering":
                    if roll and derives(idx, it["Type"], non_materials):
                        it["NoMaterial"] = True
                    if derives(idx, it["Type"], {"BaseJewel"}):
                        it["Gem"] = True
            rec = {"Name": name, "Chance": X(chance), "UsesMaterial": bool(roll), "Exceptional": exc_ok, "Items": items}
            if roll and roll != 1:
                rec["MaterialRoll"] = roll
            lists.append(rec)
        small = {"Lists": lists, "Amounts": amounts, "ExceptionalChance": X(exc)}
        if chances:
            small["MaterialChances"] = chances
            small["MaterialMinSkill"] = mat_min
            small["MaterialTries"] = tries
        note = ("A list is picked by its Chance (Skill = the player's craft skill, the higher of base skill and racial bonus); "
                "then the amount from Amounts (the first row whose MinSkill the player has; one of Values at random). ")
        if chances:
            note += (f"On a list that UsesMaterial (MaterialRoll: the chance it's rolled at all) and from {mat_min:g} skill, a material is drawn "
                     f"by MaterialChances up to {tries} times until one is drawn whose MaterialSkill the player has (None needs nothing); otherwise none. ")
        note += "Exceptional is asked with ExceptionalChance on lists marked Exceptional. "
        note += ("The item is one at random of the list's items the player can craft (has every required skill and a success chance above 0)"
                 + (", and, if exceptional is asked, can make exceptional (exceptional chance above 0)." if filt else "; whether they could make it exceptional isn't checked."))
        if rule.get("Note"):
            note += " " + rule["Note"][0].upper() + rule["Note"][1:] + "."
        small["Note"] = note

        # ---- large: the parameterless constructor's set switch
        lc = strip_comments(method(lsrc, rf"public {large_cls}\(\)\s*\{{", large_cls + "()"))
        lcn = norm(lc)
        dflt = re.search(r"bool useMaterials = (true|false);", lcn)
        use_default = dflt.group(1) == "true" if dflt else False
        sm = re.search(r"switch\s*\(\s*(?:rand|Utility\.Random\((\d+)\))\s*\)", lc)
        n = int(sm.group(1)) if sm.group(1) else int(re.search(r"int rand = Utility\.Random\((\d+)\);", lc).group(1))
        sets = []
        lmaterials, lchances = materials_for(lc, lsrc, mats, large_cls)
        if lchances:
            assert lmaterials == materials, f"{what}: large and small materials differ"
        sw = block(lc, sm.end())
        cases = re.split(r"(?:default\s*:\s*)?case\s+(\d+)\s*:", sw)[1:]
        override = re.search(r"if \(rand > (\d+) && rand < (\d+)\) useMaterials = false;", lcn)
        xm = re.search(r"(?:reqExceptional|RequireExceptional) = \(?((?:!?\w+ && )?)([\d.]+) > Utility\.RandomDouble\(\)\)?;", lcn)
        if not xm:
            assert "Exceptional" not in lcn, f"{large_cls}: exceptional rule"
        for k in range(0, len(cases), 2):
            case, seg = int(cases[k]), norm(cases[k + 1])
            pm = re.search(r"LargeBulkEntry\.ConvertEntries\(this, LargeBulkEntry\.(\w+)\);", seg)
            assert pm, f"{large_cls} case {case}"
            um = re.search(r"useMaterials = (true|false|Core\.\w+);", seg)
            uses = (um.group(1) != "false") if um else use_default
            if override and int(override.group(1)) < case < int(override.group(2)):
                uses = False
            nonexc = "nonexceptional = true;" in seg
            exc_ok = bool(xm) and not (xm.group(1) == "useMaterials && " and not uses) and not (xm.group(1) == "!nonexceptional && " and nonexc)
            if xm and xm.group(1) not in ("", "useMaterials && ", "!nonexceptional && "):
                raise AssertionError(f"{large_cls}: exceptional condition {xm.group(1)}")
            folder, name = large_props[pm.group(1)]
            items = load_cfg(root, folder, name, idx, problems)
            for it in items:
                bod_types.add(it["Type"])
            rec = {"Name": name, "UsesMaterial": uses and bool(lchances), "Exceptional": exc_ok, "Items": items}
            if "jewelry = true;" in seg:
                assert "if (jewelry) { AssignGemType(); }" in lcn
                rec["Gem"] = True
            sets.append(rec)
        assert len(sets) == n, f"{large_cls}: {len(sets)} cases for Random({n})"
        lam = re.search(r"[Aa]mountMax = Utility\.RandomList\(([\d, ]+)\);", lcn)
        assert lam, large_cls + " amounts"
        large = {"Sets": sets, "Amounts": [{"MinSkill": 0, "Values": [int(v) for v in lam.group(1).split(",")]}],
                 "ExceptionalChance": X(f"{num(xm.group(2)):g}" if xm else "0")}
        if lchances:
            large["MaterialChances"] = lchances
        large["Note"] = ("One of the Sets at random (equal odds; no skill check on the items), the amount from Amounts; a set that UsesMaterial draws one "
                         "material by MaterialChances (no skill gate); a set marked Exceptional asks for exceptional with ExceptionalChance. "
                         "The player needs every item of the set at that amount (and quality, material) to fill it."
                         + (" Gem: the deed asks for a random gem (1 of 9) in its jewellery." if any(s.get("Gem") for s in sets) else ""))

        # ---- rewards, points, gold
        rtypes = reward_types(region, calc)
        if craft == "Alchemy":
            assert not rtypes
            rtypes = POINTS["Alchemy"]["RewardTypes"]
        cp = method(region, r"override int ComputePoints\(int quantity, bool exceptional, BulkMaterialType material, int itemCount, Type type\)\s*\{", calc + ".ComputePoints")
        expect(cp, ["if (quantity == 10) points += 10; else if (quantity == 15) points += 25; else if (quantity == 20) points += 50;"] + POINTS[craft]["Code"], calc + ".ComputePoints")
        pexpr = POINTS[craft]["Expr"]
        cg, tables = gold_tables(region, calc)
        expect(cg, ["if (itemCount == 1 && BulkOrderSystem.NewSystemEnabled && BulkOrderSystem.ComputeGold(type, quantity, out gold)) { return gold; }",
                    "int quanIndex = (quantity == 20 ? 2 : quantity == 15 ? 1 : 0);", "int min = (gold * 9) / 10; int max = (gold * 10) / 9; return Utility.RandomMinMax(min, max);"], calc + ".ComputeGold")
        column = resolve_material_column(cg, materials, calc) if "mtrlIndex" in cg else "0"
        exc_offset = None
        for s in sets:
            gt, exc_offset = gold_table_for(craft, cg, region, len(s["Items"]), s["Items"][0]["Type"], rtypes, calc)
            s["GoldTable"] = gt
            top = gt + (1 if exc_offset and s["Exceptional"] else 0)
            if top >= len(tables) or not tables[top]["Values"][0]:
                problems.append(f"{craft} large '{s['Name']}' ({len(s['Items'])} items) indexes gold table {top}, which ServUO doesn't have (ComputeGold would throw)")
            tp = next((r["Points"] for r in rtypes if s["Items"][0]["Type"] in r["Types"]), 0)
            if tp:
                s["TypePoints"] = tp
        small_table, _ = gold_table_for(craft, cg, region, 1, None, rtypes, calc)
        gold = {
            "SmallBuyPrice": X("trunc(Quantity * BuyPrice / 2)"),
            "Tables": tables,
            "SmallTable": small_table,
            "ExceptionalOffset": bool(exc_offset),
            "Row": X("Quantity == 20 ? 2 : Quantity == 15 ? 1 : 0"),
            "Column": X(column),
            "Spread": X("rand(trunc(Gold * 9 / 10), trunc(Gold * 10 / 9))"),
            "Note": ("Gold paid into the bank for a filled deed. A small deed whose item a vendor sells (its BuyPrice, the shop price) pays "
                     "SmallBuyPrice exactly. Otherwise Gold = Tables[t].Values[Row][Column], with t = SmallTable for a small deed or the set's "
                     "GoldTable for a large one" + (", plus 1 when the deed is exceptional" if exc_offset else "")
                     + "; the payout is Spread (rand inclusive, like Utility.RandomMinMax: 90% to 111% of Gold). The table labels are ServUO's comments "
                     "and don't always match how the code indexes them (the code is what's exported)."),
        }
        coll = rw.collection(region, calc)
        for r in coll:
            reward_items.add(r["Item"])
            for c in r.get("Choices", []):
                reward_items.add(c)
            if r["Item"] == "BODRewardTitleDeed":
                r["Title"] = int(title.group(1)) + r["Arg"]
                if r["Title"] != r["Name"]:
                    problems.append(f"{craft} reward {r['Points']} pts: title deed shows cliloc {r['Name']} but grants title {r['Title']} (arg {r['Arg']})")

        vlist = sorted(v for v, (t, _) in vend.items() if t == craft)
        assert vlist, what + " vendors"
        for v in vlist:
            assert vend[v][1] == skills[craft], f"{v}: needs {vend[v][1]}"
        crafts[craft] = {
            "Skill": skills[craft], "Hue": hues[craft], "Vendors": vlist,
            "Materials": materials,
            "MaterialSkill": {m: req.get(m, 0.0) for m in materials[1:]},
            "Small": small, "Large": large,
            "RewardTypes": rtypes,
            "Points": X(pexpr, "ComputePoints: reward points for a filled deed (TypePoints: the RewardTypes entry holding the large deed's first item)"),
            "Fame": X(f"trunc(({pexpr}) / 50) * trunc(({pexpr}) / 50)", "ComputeFame: (points / 50, whole) squared"),
            "Gold": gold,
            "Rewards": coll,
            "Note": f"Vendors give and take these orders to players with any {skills[craft]} skill (above 0).",
        }
        counts[craft] = (len(lists), sum(len(l["Items"]) for l in lists), len(sets), len(coll))

    data = {
        "Source": "ServUO Scripts/Services/BulkOrders (BulkOrderSystem, SmallBODs, LargeBODs, Rewards), Data/Bulk Orders/*.cfg, the vendors in Scripts/Mobiles, shop prices (GenericBuyInfo); newest era, the points reward system",
        "Rules": {
            "DelayHours": int(delay.group(1)), "MaxCached": int(cached.group(1)), "OfferChance": float(om.group(1)),
            "LargeChance": X(f"Skill >= {lm.group(1)} ? (Skill - {num(lm.group(2)):g}) / {num(lm.group(3)):g} : 0",
                             "the chance an offered deed is large (Skill here is the base skill, without racial bonus)"),
            "BankedShare": {"Small": float(banked[0]), "Large": float(banked[1])},
            "Note": (f"Per craft a player holds up to MaxCached waiting offers (they start full); one more is earned every DelayHours. Asking a vendor "
                     f"(its 'bulk order info' entry) gives an offer at once if DelayHours have passed since the last, else spends a cached one, else "
                     f"says how long to wait. Selling items to the vendor offers one with OfferChance. A declined offer is given back (SeriousFun: "
                     f"RefundBulkOrder). Handing in a filled deed pays its Gold into the bank and its Fame, and its Points either go to one reward "
                     f"now (spend them on a Rewards entry costing no more) or are banked at BankedShare of their value (small x{float(banked[0]):g}, "
                     f"large x{float(banked[1]):g}) to buy rewards from the bank later; a pending turn-in must be claimed before the next one. "
                     f"A vendor only deals in its own craft's deeds."),
            "VariablesNote": ("Formula variables: Skill (craft skill 0-120), Quantity (10/15/20), Exceptional (0/1), Material (index into the craft's "
                              "Materials, 0 = none), ItemCount (1 small, else the large set's item count), TypePoints (RewardTypes points of the deed's "
                              "first item type, 0 if none). Gold formulas also use Gold (the table value) and BuyPrice (the item's shop price)."),
        },
        "Crafts": crafts,
    }
    nexpr = check_all(data)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write("\n")

    for craft, (nl, ni, ns, nr) in counts.items():
        print(f"{craft:12} small lists {nl} ({ni} items), large sets {ns}, rewards {nr}")
    print(f"{nexpr} formulas checked")
    no_price = sorted({it["Type"] for c in crafts.values() for l in c["Small"]["Lists"] for it in l["Items"] if "BuyPrice" not in it})
    print(f"small items with no shop price (table gold): {len(no_price)}")
    conflicted = sorted(t for t in price_conflicts if t in bod_types)
    if conflicted:
        print("small items sold at different prices (commonest taken): " + ", ".join(f"{t} {price_conflicts[t]}" for t in conflicted))
    for p in problems:
        print("NOTE:", p)

    items_path = os.path.join(os.path.dirname(os.path.abspath(out_path)), "Items.json")
    have = {i["Type"] for i in json.load(open(items_path, encoding="utf-8"))["Items"]}
    missing_bod = sorted(bod_types - have)
    missing_rew = sorted(reward_items - have)
    print(f"\nBOD item types not in Items.json ({len(missing_bod)}): " + ", ".join(missing_bod))
    print(f"Reward item types not in Items.json ({len(missing_rew)}): " + ", ".join(missing_rew))


main()
