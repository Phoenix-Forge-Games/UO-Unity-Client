"""One-time migration of ServUO's housing rules and regions into our content data.

  python -I export_servuo_housing.py <ServUO root> <Content folder>

Writes two files the game owns from then on (ServUO is going away; nothing of its code is reused):

  Regions.json  every region in Data/Regions.xml, per facet, with its rectangles and what the game needs to know
                about it: Guarded (towns and guarded zones), NoHousing (worked out from each region class's
                AllowHousing override in Scripts/, through the class hierarchy and the region's parents), plus the
                areas ServUO ruled out in code (Ilshenar, the Lost Lands, the Valley of Eodon).

  Housing.json  the parts of the housing rules that come from the shard: the account house limit
                (Config/Housing.cfg), road tiles a house can't sit on (HousePlacement.RoadIDs), and the plot tiers'
                prices, storage and lockdowns, taken from the customisable foundation of the same size
                (HousePlacementTool's foundation tables). The classic houses are kept as Blueprints for later.
                Anything already in Housing.json that isn't migrated (Pieces, Styles, our own rules) is kept.
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

FACETS = ["Felucca", "Trammel", "Ilshenar", "Malas", "Tokuno", "TerMur"]


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def region_classes(scripts):
    """class name -> (base, allow) where allow is False/True when the class overrides AllowHousing, else None."""
    classes = {}
    for folder, _, files in os.walk(scripts):
        for name in files:
            if not name.endswith(".cs"):
                continue
            text = strip_comments(read(os.path.join(folder, name)))
            for m in re.finditer(r"class\s+(\w+)\s*:\s*([\w.]+)", text):
                cls, base = m.group(1), m.group(2).split(".")[-1]
                body_start = text.find("{", m.end())
                # The class body: up to the next top-level "class" keyword is close enough for finding an override.
                nxt = re.search(r"\bclass\s+\w+\s*:", text[body_start:])
                body = text[body_start: body_start + nxt.start()] if nxt else text[body_start:]
                allow = None
                o = re.search(r"override\s+bool\s+AllowHousing\s*\([^)]*\)\s*\{(.*?)\n\s*\}", body, re.S)
                if o:
                    # Allowed only when the override can say yes to a player: "return true" or handing back to the
                    # base rule, and never "return false" (GreenAcres refuses players; NoHousingRegion returns its
                    # smart-checking flag, off by default).
                    b = o.group(1)
                    allow = "return false" not in b and ("return true" in b or "base.AllowHousing" in b)
                if cls not in classes or classes[cls][1] is None:
                    classes[cls] = (base, allow)
    return classes


def class_allows(cls, classes, seen=None):
    seen = seen or set()
    while cls in classes and cls not in seen:
        seen.add(cls)
        base, allow = classes[cls]
        if allow is not None:
            return allow
        cls = base
    return True


def class_is(cls, target, classes):
    seen = set()
    while cls and cls not in seen:
        if cls == target:
            return True
        seen.add(cls)
        cls = classes.get(cls, (None, None))[0]
    return False


def rect_of(el):
    a = el.attrib
    if "x1" in a:
        x1, y1, x2, y2 = (int(a[k]) for k in ("x1", "y1", "x2", "y2"))
        r = {"X": x1, "Y": y1, "W": x2 - x1, "H": y2 - y1}
    else:
        r = {"X": int(a["x"]), "Y": int(a["y"]), "W": int(a["width"]), "H": int(a["height"])}
    if "zmin" in a:
        r["ZMin"] = int(a["zmin"])
    if "zmax" in a:
        r["ZMax"] = int(a["zmax"])
    return r


def export_regions(root, classes):
    tree = ET.parse(os.path.join(root, "Data", "Regions.xml"))
    out = []

    def walk(el, facet, parent_no_housing, parent_guarded, parent_name):
        for r in el.findall("region"):
            cls = r.attrib.get("type", "BaseRegion")
            name = r.attrib.get("name")
            guarded = class_is(cls, "GuardedRegion", classes) or parent_guarded
            guards = r.find("guards")
            if guards is not None and guards.attrib.get("disabled", "false").lower() == "true":
                guarded = False
            no_housing = parent_no_housing or not class_allows(cls, classes)
            entry = {"Facet": facet, "Name": name, "Type": cls, "Priority": int(r.attrib.get("priority", "50"))}
            if parent_name:
                entry["Parent"] = parent_name
            if guarded:
                entry["Guarded"] = True
            if no_housing:
                entry["NoHousing"] = True
            entry["Rects"] = [rect_of(x) for x in r.findall("rect")]
            go = r.find("go")
            if go is not None:
                entry["Go"] = {k.upper(): int(v) for k, v in go.attrib.items()}
            out.append(entry)
            walk(r, facet, no_housing, guarded, name)

    for f in tree.getroot().findall("Facet"):
        walk(f, FACETS.index(f.attrib["name"]), False, False, None)

    # Ruled out in code rather than by region (HousePlacement.Check + SpellHelper): the whole of Ilshenar, the Lost
    # Lands (T2A) on Felucca and the Valley of Eodon.
    out.append({"Facet": 2, "Name": "Ilshenar", "Type": "Code", "NoHousing": True, "Rects": [{"X": 0, "Y": 0, "W": 2304, "H": 1600}],
                "Note": "HousePlacement: no houses in Ilshenar."})
    out.append({"Facet": 0, "Name": "Lost Lands", "Type": "Code", "NoHousing": True, "Rects": [{"X": 5120, "Y": 2304, "W": 1024, "H": 1792}],
                "Note": "SpellHelper.IsFeluccaT2A: no houses in the Lost Lands on Felucca."})
    out.append({"Facet": 0, "Name": "Valley of Eodon (Felucca)", "Type": "Code", "NoHousing": True, "Rects": [{"X": 6975, "Y": 2048, "W": 68, "H": 68}],
                "Note": "SpellHelper.IsEodon."})
    out.append({"Facet": 5, "Name": "Valley of Eodon", "Type": "Code", "NoHousing": True, "Rects": [{"X": 0, "Y": 1344, "W": 1088, "H": 1152}],
                "Note": "SpellHelper.IsEodon."})
    return out


def parse_entries(text, table):
    m = re.search(r"HousePlacementEntry\[\]\s+" + table + r"\s*=\s*(?:new\s+HousePlacementEntry\[\]\s*)?\{(.*?)\};", text, re.S)
    entries = []
    for line in (m.group(1) if m else "").splitlines():
        e = re.search(r"new HousePlacementEntry\(typeof\((\w+)\),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(-?\d+),\s*(-?\d+),\s*(-?\d+),\s*(0x[0-9A-Fa-f]+)\)", line)
        if not e:
            continue
        comment = line.split("//", 1)[1].strip() if "//" in line else None
        entries.append({
            "Type": e.group(1), "Cliloc": int(e.group(2)), "Storage": int(e.group(5)), "Lockdowns": int(e.group(6)),
            "Vendors": int(e.group(7)), "Price": int(e.group(8)), "Multi": int(e.group(12), 16),
            **({"Name": comment} if comment else {}),
        })
    return entries


def main():
    root, content = sys.argv[1], sys.argv[2]
    scripts = os.path.join(root, "Scripts")
    classes = region_classes(scripts)
    regions = export_regions(root, classes)
    with open(os.path.join(content, "Regions.json"), "w", encoding="utf-8") as f:
        json.dump({"Source": "ServUO Data/Regions.xml + region classes in Scripts/ (one-time migration; this file is canonical now)",
                   "Facets": FACETS, "Regions": regions}, f, indent=1)

    cfg = read(os.path.join(root, "Config", "Housing.cfg"))
    limit = int(re.search(r"AccountHouseLimit\s*=\s*(\d+)", cfg).group(1))
    placement = strip_comments(read(os.path.join(scripts, "Multis", "HousePlacement.cs")))
    roads = [int(x, 16) for x in re.findall(r"0x[0-9A-Fa-f]+", re.search(r"m_RoadIDs\s*=\s*new int\[\]\s*\{(.*?)\}", placement, re.S).group(1))]
    road_ranges = [[roads[i], roads[i + 1]] for i in range(0, len(roads), 2)]
    yard = int(re.search(r"YardSize\s*=\s*(\d+)", placement).group(1))
    tool = read(os.path.join(scripts, "Multis", "HousePlacementTool.cs"))
    classic = parse_entries(tool, "m_ClassicHouses")
    two = parse_entries(tool, "m_TwoStoryFoundations")
    three = parse_entries(tool, "m_ThreeStoryFoundations")
    contest = parse_entries(tool, "m_CustomHouseContest")
    foundations = two + three + contest

    def foundation(w, h):
        for e in foundations:
            if e.get("Name", "").startswith(f"{w}x{h} "):
                return e
        raise SystemExit(f"no {w}x{h} foundation")

    tiers = []
    for name, size, (w, h), deed in (("Small", 12, (12, 12), "SmallPlotDeed"), ("Medium", 18, (18, 18), "MediumPlotDeed"),
                                      ("Large", 24, (23, 23), "LargePlotDeed"), ("Keep", 32, (32, 32), "KeepPlotDeed")):
        e = foundation(w, h)
        tiers.append({"Name": name, "Size": size, "Deed": deed, "Price": e["Price"], "Storage": e["Storage"],
                      "Lockdowns": e["Lockdowns"], "Vendors": e["Vendors"], "From": e["Name"]})

    path = os.path.join(content, "Housing.json")
    old = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {}
    rules = old.get("Rules", {})
    rules.update({"AccountLimit": limit, "YardSize": yard})
    data = {
        "Source": "ServUO Config/Housing.cfg, Multis/HousePlacement.cs, Multis/HousePlacementTool.cs (one-time migration; this file is canonical now)",
        "Rules": rules,
        "Layers": old.get("Layers") or {
            "Road": {"Land": road_ranges, "Note": "HousePlacement.RoadIDs: no plot tile may lie on a road."},
            "Blocked": {"StaticFlags": ["Impassable"], "LandFlags": ["Impassable"], "ExceptLayers": ["Lumberjacking.Logs"],
                        "Note": "Anything impassable on the map or among the decorations blocks a plot. Trees don't: as on SeriousFun (TreeClearing), they're cleared when the plot is claimed."},
        },
        "Plots": tiers,
        "Styles": old.get("Styles", []),
        "RoofStyles": old.get("RoofStyles", []),
        "Pieces": old.get("Pieces", []),
        "Blueprints": [dict(e, Kind="Classic") for e in classic],
        "Notes": old.get("Notes", []),
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    print(f"regions {len(regions)} ({sum(1 for r in regions if r.get('NoHousing'))} no housing, {sum(1 for r in regions if r.get('Guarded'))} guarded); "
          f"tiers {[(t['Name'], t['Price']) for t in tiers]}; roads {len(road_ranges)} ranges; blueprints {len(classic)}; limit {limit}")


if __name__ == "__main__":
    main()
