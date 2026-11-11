"""Extracts spawn points (data only) from ServUO's XmlSpawner files into Content/Spawns.json.

  python -I export_servuo_spawns.py <ServUO folder> <output json>

Reads Spawns/*.xml and RevampedSpawns/*.xml. Each point keeps its facet, centre (UO tile x, y, z), home range,
spawn area, max count, respawn delay (minutes) and the creature types it spawns, with each type's own max.
Positions stay in UO tiles; the server converts them to metres (WorldSpace).
"""
import json
import os
import sys
import xml.etree.ElementTree as ET

FACETS = {"Felucca": 0, "Trammel": 1, "Ilshenar": 2, "Malas": 3, "Tokuno": 4, "TerMur": 5}


def text(node, tag, default=None):
    child = node.find(tag)
    return child.text if child is not None and child.text is not None else default


def objects(spec):
    """'Type:MX=3:...:OBJ=Type2:MX=2:...' -> [(type, max)]"""
    result = []
    if not spec:
        return result
    for part in spec.split(":OBJ="):
        fields = part.split(":")
        kind = fields[0].strip()
        if kind.startswith("OBJ="):
            kind = kind[4:]
        maximum = 1
        for f in fields[1:]:
            if f.startswith("MX="):
                try:
                    maximum = int(f[3:])
                except ValueError:
                    pass
        if kind:
            # XmlSpawner entries can carry settings ("Agralem/Name/Agralem", "Type,{RND,1,5}"); the type comes first
            # and is matched case-insensitively.
            base = kind.split("/")[0].split(",")[0].strip()
            entry = {"Type": base, "Max": maximum}
            if base != kind:
                entry["Spec"] = kind
            result.append(entry)
    return result


def main():
    root, out = sys.argv[1], sys.argv[2]
    points, skipped = [], 0
    for folder in ("Spawns", "RevampedSpawns"):
        path = os.path.join(root, folder)
        if not os.path.isdir(path):
            continue
        for f in sorted(os.listdir(path)):
            if not f.endswith(".xml"):
                continue
            try:
                tree = ET.parse(os.path.join(path, f))
            except ET.ParseError as e:
                print(f"skipped {f}: {e}")
                continue
            for p in tree.getroot().iter("Points"):
                facet = FACETS.get(text(p, "Map", ""))
                if facet is None or text(p, "IsRunning", "True") != "True":
                    skipped += 1
                    continue
                spawns = objects(text(p, "Objects2"))
                if not spawns:
                    skipped += 1
                    continue
                delay_in_seconds = text(p, "DelayInSec", "False") == "True"
                scale = 1 / 60 if delay_in_seconds else 1
                points.append({
                    "Name": text(p, "Name", ""),
                    "Id": text(p, "UniqueId", ""),
                    "File": f"{folder}/{f}",
                    "Facet": facet,
                    "X": int(text(p, "CentreX", "0")),
                    "Y": int(text(p, "CentreY", "0")),
                    "Z": int(text(p, "CentreZ", "0")),
                    "Width": int(text(p, "Width", "0")),
                    "Height": int(text(p, "Height", "0")),
                    "Range": int(text(p, "Range", "10")),
                    "MaxCount": int(text(p, "MaxCount", "1")),
                    "MinDelay": float(text(p, "MinDelay", "5")) * scale,
                    "MaxDelay": float(text(p, "MaxDelay", "10")) * scale,
                    "Spawns": spawns,
                })
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"Source": "ServUO XmlSpawner files", "Count": len(points), "Points": points}, f, indent=1)
    per = {}
    for p in points:
        per[p["Facet"]] = per.get(p["Facet"], 0) + 1
    print(f"{len(points)} spawn points ({skipped} skipped), per facet {dict(sorted(per.items()))} -> {out}")


if __name__ == "__main__":
    main()
