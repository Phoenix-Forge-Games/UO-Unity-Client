"""Copies the icon art chosen in Assets/UOClient/Content/Icons.json into Assets/Secure/Icons/<category>/<slug>.png,
the folder Tools/SecureArt/SetupSecureArt.eval.cs turns into the Protected_Icons Addressables group (address
icon/<category>/<slug>). Safe to run again: it rewrites changed files and removes icons no longer listed in a category
it manages (skill, spell, ability, prompt, item).

Skill, spell, ability and prompt art is copied as it is. Item art (keyed by Items.json Type) comes as objects on a
clear background, so it is laid on black, like the rendered equipment pictures (ItemIcons). An item entry can also be
{"art": <png>, "hue": <UO hue>}: the art is then recoloured in that hue's swatch colour from hues.mul (the colour the
game draws dyed things in), keeping the picture's light and shade, for families that share one picture (wood, leather,
hides, scales, granite). Equipment that has a body model keeps its rendered picture and is not listed here.

Usage (from UnityClient/):  python Tools/Icons/place_icons.py [path to hues.mul]
Needs Pillow for the item icons (pip install pillow).
"""
import io, json, os, re, struct, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.join(ROOT, "Assets", "UOClient", "Content", "Icons.json")
OUT = os.path.join(ROOT, "Assets", "Secure", "Icons")
HUES = os.path.join(os.path.dirname(ROOT), "ServUO", "ClientFiles", "hues.mul")
ITEM = "item"
SWATCH_SHADE = 22


def slug(text):
    # Same rule as UOClient.Content.SecureArtAddresses.Slug.
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def load_ramps(path):
    """hues.mul as a list of 32-colour ramps (index = hue - 1), as UOClient.Data.Hues reads it."""
    data = open(path, "rb").read()
    entry, group = 32 * 2 + 2 + 2 + 20, 4 + 8 * (32 * 2 + 2 + 2 + 20)
    ramps = []
    for g in range(len(data) // group):
        for e in range(8):
            off = g * group + 4 + e * entry
            raw = struct.unpack_from("<32H", data, off)
            ramps.append([(((c >> 10) & 31) * 255 // 31, ((c >> 5) & 31) * 255 // 31, (c & 31) * 255 // 31) for c in raw])
    return ramps


def item_png(source, hue, ramps):
    """The art laid on black, recoloured in a hue when one is given; PNG bytes."""
    from PIL import Image
    art = Image.open(source).convert("RGBA")
    if hue:
        # The hue's swatch shade (UOClient.Data.Hues.SwatchShade, what dyed things are drawn in), scaled by each
        # pixel's brightness against the picture's average, with the brightest parts eased toward white.
        swatch = ramps[(hue & 0x3FFF) - 1][SWATCH_SHADE]
        px = art.load()
        lum = {}
        for y in range(art.height):
            for x in range(art.width):
                r, g, b, a = px[x, y]
                lum[x, y] = (r * 299 + g * 587 + b * 114) / 255000
        solid = [lum[x, y] for y in range(art.height) for x in range(art.width) if px[x, y][3] > 128]
        mean = max(0.05, min(0.95, sum(solid) / max(1, len(solid))))
        for (x, y), l in lum.items():
            a = px[x, y][3]
            if a:
                c = [min(255.0, s * l / mean * 0.85) for s in swatch]
                lift = ((l - mean) / (1 - mean)) ** 2 * 0.6 if l > mean else 0.0
                px[x, y] = tuple(int(v + (255 - v) * lift) for v in c) + (a,)
    flat = Image.new("RGBA", art.size, (0, 0, 0, 255))
    flat.alpha_composite(art)
    buf = io.BytesIO()
    flat.convert("RGB").save(buf, "PNG", optimize=True)
    return buf.getvalue()


def main():
    data = json.load(open(DATA, encoding="utf-8"))
    hues_path = sys.argv[1] if len(sys.argv) > 1 else HUES
    ramps = None
    copied = kept = removed = 0
    missing = []
    for category, entries in data.items():
        if not isinstance(entries, dict):
            continue
        folder = os.path.join(OUT, category)
        os.makedirs(folder, exist_ok=True)
        wanted = set()
        for name, value in entries.items():
            source, hue = (value["art"], int(value.get("hue", 0))) if isinstance(value, dict) else (value, 0)
            src = os.path.join(ROOT, source)
            if not os.path.isfile(src):
                missing.append(source)
                continue
            dst = os.path.join(folder, slug(name) + ".png")
            wanted.add(os.path.basename(dst))
            if category == ITEM:
                if hue and ramps is None:
                    if not os.path.isfile(hues_path):
                        print("No hues.mul at", hues_path, "- pass its path as the first argument.")
                        return 1
                    ramps = load_ramps(hues_path)
                content = item_png(src, hue, ramps)
            else:
                content = open(src, "rb").read()
            if os.path.isfile(dst) and open(dst, "rb").read() == content:
                kept += 1
                continue
            with open(dst, "wb") as f:
                f.write(content)
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
