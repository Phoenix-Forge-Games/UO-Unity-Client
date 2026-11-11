"""Generates Assets/UOClient/Resources/Places.json from the shard's Data/Regions.xml: named towns, dungeons and
other places per facet, with a label point (the region's own \"go\" point, else the middle of its largest rectangle). The minimap shows them.
Re-run after the server's regions change:
    python -I make_places.py "G:/Ultima Online/ServUO/Data/Regions.xml"
"""
import json, sys
import xml.etree.ElementTree as ET

path = sys.argv[1] if len(sys.argv) > 1 else "G:/Ultima Online/ServUO/Data/Regions.xml"
FACETS = {"Felucca": 0, "Trammel": 1, "Ilshenar": 2, "Malas": 3, "Tokuno": 4, "TerMur": 5}
TOWNS = {"TownRegion", "NewMaginciaRegion", "SeaMarketRegion", "VerLorRegCity", "TokunoDocksRegion"}
DUNGEONS = {"DungeonRegion", "BlackthornDungeon"}
PLACES = {"MondainRegion", "TombOfKingsRegion", "TwistedWealdDesert", "BlackthornCastle", "GreenAcres", "CrystalField",
          "IcyRiver", "AcidRiver", "PoisonedTree", "PoisonedSemetery", "DoomGuardianRegion", "ToKBridgeRegion",
          "TheLostCityEntry", "ParoxysmusBossEntry", "Underwater"}

def rect(r):
    """A <rect> as (x, y, width, height); the file uses both x/y/width/height and x1/y1/x2/y2."""
    if r.get("width") is not None:
        return int(r.get("x")), int(r.get("y")), int(r.get("width")), int(r.get("height"))
    x1, y1, x2, y2 = (int(r.get(k)) for k in ("x1", "y1", "x2", "y2"))
    return x1, y1, x2 - x1, y2 - y1

places = []
seen = set()
def walk(node, facet):
    for region in node.findall("region"):
        kind_attr = region.get("type", "")
        name = region.get("name")
        kind = "town" if kind_attr in TOWNS else "dungeon" if kind_attr in DUNGEONS else "place" if kind_attr in PLACES else None
        rects = [rect(r) for r in region.findall("rect")]
        if kind and name and rects:
            go = region.find("go")
            if go is not None and go.get("x") is not None:
                lx, ly = int(go.get("x")), int(go.get("y"))
            else:
                x, y, w, h = max(rects, key=lambda r: r[2] * r[3])
                lx, ly = x + w // 2, y + h // 2
            key = (facet, name, lx // 64, ly // 64)
            if key not in seen:
                seen.add(key)
                places.append({"Name": name, "Kind": kind, "Facet": facet, "X": lx, "Y": ly})
        walk(region, facet)

root = ET.parse(path).getroot()
for facet in root.findall("Facet"):
    walk(facet, FACETS.get(facet.get("name"), -1))

out = "G:/Ultima Online/UnityClient/Assets/UOClient/Resources/Places.json"
with open(out, "w", encoding="utf-8") as f:
    json.dump({"Places": places}, f, indent=1)
from collections import Counter
print(len(places), Counter(p["Kind"] for p in places))
