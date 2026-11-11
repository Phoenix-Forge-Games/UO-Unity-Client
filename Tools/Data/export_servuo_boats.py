"""One-time migration of UO's boats into Content/Boats.json: every ship deed ServUO sells or drops (deed class, its
name, the boat it places, the docked-boat item a dry-docked ship becomes, the multi IDs per heading, hold and tillerman
distances) and the sailing speeds from BaseBoat (a tile every 250 / 500 / 1000 ms). Footprints come from the player's
UO multi data, measured by the editor into a bounds file (multi ID -> [minX, minY, maxX, maxY] per heading north,
east, south, west):

  python -I export_servuo_boats.py <ServUO root> <bounds.json> <Content/Boats.json>

The game owns the file from then on.
"""
import json
import os
import re
import sys

DEEDS = [  # deed class, its file under Scripts
    ("SmallBoatDeed", "Multis/Boats/SmallBoat.cs"),
    ("SmallDragonBoatDeed", "Multis/Boats/SmallDragonBoat.cs"),
    ("MediumBoatDeed", "Multis/Boats/MediumBoat.cs"),
    ("MediumDragonBoatDeed", "Multis/Boats/MediumDragonBoat.cs"),
    ("LargeBoatDeed", "Multis/Boats/LargeBoat.cs"),
    ("LargeDragonBoatDeed", "Multis/Boats/LargeDragonBoat.cs"),
    ("RowBoatDeed", "Services/Expansions/High Seas/Multis/Rowboat.cs"),
    ("OrcishGalleonDeed", "Services/Expansions/High Seas/Multis/OrcishGalleon.cs"),
    ("GargishGalleonDeed", "Services/Expansions/High Seas/Multis/GargishGalleon.cs"),
    ("TokunoGalleonDeed", "Services/Expansions/High Seas/Multis/TokunoGalleon.cs"),
    ("BritannianShipDeed", "Services/Expansions/High Seas/Multis/BritannianShip.cs"),
]


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def prop(src, name):
    m = re.search(rf"override\s+\w+\s+{name}\s*\{{\s*get\s*\{{\s*return\s*([^;]+);", src)
    return m.group(1).strip() if m else None


def main():
    root, bounds_path, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
    bounds = json.load(open(bounds_path, encoding="utf-8"))
    base = read(os.path.join(root, "Scripts/Multis/Boats/BaseBoat.cs"))
    speeds = {}
    for k in ("Fast", "Normal", "Slow"):
        m = re.search(rf"public int {k}Interval\s*\{{[^}}]*\}}\s*=\s*(\d+);", base)
        assert m, k
        speeds[k] = int(m.group(1))
    boats = []
    for deed, rel in DEEDS:
        src = read(os.path.join(root, "Scripts", rel))
        dm = re.search(rf"class {deed}\b[\s\S]*?LabelNumber\s*\{{\s*get\s*\{{\s*return\s*(\d+)", src)
        boat_cls = re.search(rf"class {deed}\b[\s\S]*?BaseBoat\s+Boat\s*\{{\s*get\s*\{{\s*return\s*new\s+(\w+)\(", src)
        assert boat_cls, deed
        boat = boat_cls.group(1)
        bm = src[src.index(f"class {boat}"):]
        north = prop(bm, "NorthID")
        north = int(re.match(r"\s*(0x[0-9A-Fa-f]+|\d+)", north).group(1), 0)  # galleons: + 4 per damage level; the sound ship
        docked = re.search(r"DockedBoat\s*\{\s*get\s*\{\s*return\s*new\s+(\w+)\(", bm)
        b = bounds.get(str(north)) or bounds.get("60")  # the pumpkin rowboat has no multi of its own: a rowboat's
        n = b[0]
        rec = {
            "Deed": deed, "Boat": boat, "Name": int(dm.group(1)) if dm else None, "NorthId": north,
            "Docked": docked.group(1) if docked else None,
            "Width": n[2] - n[0] + 1, "Length": n[3] - n[1] + 1, "Bow": -n[1], "Stern": n[3],
            "Bounds": b,
            "HoldDistance": int(prop(bm, "HoldDistance") or 0), "TillerManDistance": int(prop(bm, "TillerManDistance") or 0),
            "Classic": "BaseGalleon" not in bm[:400],
        }
        boats.append(rec)
    data = {
        "Source": "ServUO Scripts/Multis/Boats (BaseBoat speeds, the deeds and boats), footprints from the UO multi data",
        "Speeds": {"FastMs": speeds["Fast"], "NormalMs": speeds["Normal"], "SlowMs": speeds["Slow"],
                   "Note": "a tile every so many milliseconds: full speed, normal, slow; the ship's window toggles fast and slow"},
        "Rules": {"BoardReach": 8, "ShoreSearch": 10, "DeckLift": 0.3, "Draft": 0.05, "CoastReach": 6, "PlaceReach": 30,
                  "Note": "a ship is placed with a ghost: wholly on water, with dry land within 6 tiles of its hull (near the coast), its centre within 30 tiles of you; you may board from 8 m and go ashore to dry land within 10 tiles of the hull"},
        "Boats": boats,
    }
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(len(boats), "boats;", speeds)


main()
