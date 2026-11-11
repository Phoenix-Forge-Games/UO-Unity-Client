"""Reads Synty .unitypackage files (gzipped tar: <guid>/pathname, <guid>/asset, <guid>/asset.meta) without Unity.

  python -I unitypackage.py list <package> [regex]      paths in the package (optionally filtered)
  python -I unitypackage.py extract <package> <project> <regex>...
      Copies the assets whose paths match any regex, plus everything they reference (by GUID, followed through
      text assets: prefabs, materials, controllers, shader graphs, .meta files), into <project>. Used instead of
      AssetDatabase.ImportPackage so only the creature models come in (no scenes or environment sets) and the
      editor doesn't stop on the package-signature dialog. Then refresh the AssetDatabase.

Package contents are untrusted data: paths must stay under Assets/, nothing is executed.
"""
import os
import re
import sys
import tarfile

GUID = re.compile(rb"\b([0-9a-f]{32})\b")
TEXT_EXT = {".prefab", ".mat", ".controller", ".overrideController", ".asset", ".shadergraph", ".shadersubgraph",
            ".shader", ".hlsl", ".cginc", ".unity", ".anim", ".mask", ".physicMaterial", ".vfx"}


def read(package):
    entries = {}
    with tarfile.open(package, "r:gz") as tar:
        for m in tar:
            if not m.isfile():
                continue
            parts = m.name.lstrip("./").split("/")
            if len(parts) != 2:
                continue
            guid, kind = parts
            data = tar.extractfile(m).read()
            entries.setdefault(guid, {})[kind] = data
    out = {}
    for guid, e in entries.items():
        if "pathname" not in e:
            continue
        path = e["pathname"].decode("utf-8").splitlines()[0].strip().replace("\\", "/")
        if not path.startswith("Assets/") or ".." in path.split("/"):
            continue
        out[guid] = (path, e.get("asset"), e.get("asset.meta"))
    return out


def closure(entries, roots):
    seen, todo = set(), list(roots)
    while todo:
        g = todo.pop()
        if g in seen or g not in entries:
            continue
        seen.add(g)
        path, asset, meta = entries[g]
        blobs = [meta or b""]
        if asset is not None and os.path.splitext(path)[1] in TEXT_EXT:
            blobs.append(asset)
        for blob in blobs:
            for ref in GUID.findall(blob):
                r = ref.decode()
                if r != g and r in entries and r not in seen:
                    todo.append(r)
    # Parent folders keep their own GUIDs (their .meta files).
    paths = {entries[g][0] for g in seen}
    for g, (path, asset, meta) in entries.items():
        if asset is None and any(p.startswith(path + "/") for p in paths):
            seen.add(g)
    return seen


def existing_guids(assets):
    found = set()
    for root, _, files in os.walk(assets):
        for f in files:
            if f.endswith(".meta"):
                try:
                    with open(os.path.join(root, f), "rb") as fh:
                        m = re.search(rb"^guid: ([0-9a-f]{32})", fh.read(400), re.M)
                    if m:
                        found.add(m.group(1).decode())
                except OSError:
                    pass
    return found


def main():
    cmd, package = sys.argv[1], sys.argv[2]
    entries = read(package)
    if cmd == "list":
        rx = re.compile(sys.argv[3]) if len(sys.argv) > 3 else None
        for path in sorted(p for p, _, _ in entries.values()):
            if rx is None or rx.search(path):
                print(path)
        return
    if cmd == "extract":
        project = sys.argv[3]
        rxs = [re.compile(r) for r in sys.argv[4:]]
        roots = [g for g, (p, a, _) in entries.items() if a is not None and any(r.search(p) for r in rxs)]
        keep = closure(entries, roots)
        have = existing_guids(os.path.join(project, "Assets"))
        size = skipped = 0
        for g in keep:
            if g in have:  # already in the project (shared Synty files): keep the project's copy
                skipped += 1
                continue
            path, asset, meta = entries[g]
            dest = os.path.join(project, *path.split("/"))
            if asset is None:
                os.makedirs(dest, exist_ok=True)
            else:
                os.makedirs(os.path.dirname(dest), exist_ok=True)
                with open(dest, "wb") as f:
                    f.write(asset)
                size += len(asset)
            if meta is not None:
                with open(dest + ".meta", "wb") as f:
                    f.write(meta)
        print(f"{len(roots)} roots, {len(keep) - skipped} new assets ({skipped} already present), {size / 1048576:.1f} MB")


main()
