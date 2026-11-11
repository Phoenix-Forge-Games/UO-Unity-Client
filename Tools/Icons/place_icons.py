"""Copies the icon art chosen in Assets/UOClient/Content/Icons.json into Assets/Secure/Icons/<category>/<slug>.png,
the folder Tools/SecureArt/SetupSecureArt.eval.cs turns into the Protected_Icons Addressables group (address
icon/<category>/<slug>). Safe to run again: it rewrites changed files and removes icons no longer listed in a category
it manages (skill, spell, ability, prompt). Item icons are rendered at runtime and never live here.

Usage (from UnityClient/):  python Tools/Icons/place_icons.py
"""
import json, os, re, shutil, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.join(ROOT, "Assets", "UOClient", "Content", "Icons.json")
OUT = os.path.join(ROOT, "Assets", "Secure", "Icons")


def slug(text):
    # Same rule as UOClient.Content.SecureArtAddresses.Slug.
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def main():
    data = json.load(open(DATA, encoding="utf-8"))
    copied = kept = removed = 0
    missing = []
    for category, entries in data.items():
        if not isinstance(entries, dict):
            continue
        folder = os.path.join(OUT, category)
        os.makedirs(folder, exist_ok=True)
        wanted = set()
        for name, source in entries.items():
            src = os.path.join(ROOT, source)
            if not os.path.isfile(src):
                missing.append(source)
                continue
            dst = os.path.join(folder, slug(name) + ".png")
            wanted.add(os.path.basename(dst))
            if os.path.isfile(dst) and open(dst, "rb").read() == open(src, "rb").read():
                kept += 1
                continue
            shutil.copyfile(src, dst)
            copied += 1
        for f in os.listdir(folder):
            if f.endswith(".png") and f not in wanted:
                os.remove(os.path.join(folder, f))
                if os.path.isfile(os.path.join(folder, f + ".meta")):
                    os.remove(os.path.join(folder, f + ".meta"))
                removed += 1
    print(f"copied {copied}, unchanged {kept}, removed {removed}")
    for m in missing:
        print("MISSING source:", m)
    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
