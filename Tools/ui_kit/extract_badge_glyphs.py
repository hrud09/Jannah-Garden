"""
One-off: lift the icon glyphs out of the original sage badge art.

The side-button badges (Assets/2D Assets/UI/UI Elements/*-badge.png) were
painted as a glyph on a sage squircle. generate.py redraws the squircle in the
current kit style and composites these glyphs back on, so the glyphs are kept
under Tools/ui_kit/glyphs/ as the source of truth.

The badges are overwritten by generate.py, so this reads the ORIGINAL art from
git (commit 504023e16, before the ornate restyle). Re-running it against the
regenerated badges would not work: the background is no longer sage.

Run:  python Tools/ui_kit/extract_badge_glyphs.py
"""

import io
import os
import subprocess

from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(HERE, "glyphs")
SOURCE_COMMIT = "504023e16"
BADGE_DIR = "Assets/2D Assets/UI/UI Elements"

BADGES = [
    "camera-icon-badge", "drawer-toggle-badge", "explore-garden-badge",
    "inspector-mode-badge", "move-to-unlocked-area-badge",
    "return-to-base-badge", "settings-gear-badge", "shop-badge",
]

# Badges where an enclosed gap in the mask is glyph colour that happened to look
# sage (the explore badge's hills, the gloss spot on the magnifier lens).
# Elsewhere enclosed gaps are real background (camera lens ring, gear centre,
# awning) and must stay see-through.
FILL_HOLES = {"explore-garden-badge", "inspector-mode-badge"}

# Erosion that clears each badge's cream border. The inspector badge's
# squircle fills the canvas and carries a much thicker border.
BORDER_ERODE = {"inspector-mode-badge": 81}


def fill_holes(mask):
    """Fill background regions the outside cannot reach."""
    w, h = mask.size
    probe = mask.copy()
    ImageDraw.floodfill(probe, (0, 0), 128)
    return probe.point(lambda v: 0 if v == 128 else 255)


def load_original(name):
    blob = subprocess.check_output(
        ["git", "show", f"{SOURCE_COMMIT}:{BADGE_DIR}/{name}.png"], cwd=ROOT)
    return Image.open(io.BytesIO(blob)).convert("RGBA")


def is_sage(r, g, b):
    """The badge body: a low-saturation green, mid-to-light, incl. its gloss."""
    v = max(r, g, b)
    return (4 <= g - r <= 40) and (0 <= g - b <= 34) and 120 <= v <= 236


def extract(img, fill=False, erode=61):
    w, h = img.size
    px = img.load()

    # Body area = the opaque squircle shrunk past its cream border.
    alpha = img.getchannel("A").point(lambda a: 255 if a > 200 else 0)
    # MinFilter pads with edge pixels, so a squircle touching the canvas edge
    # would never erode there. Pad with transparency first.
    pad = erode
    padded = Image.new("L", (w + 2 * pad, h + 2 * pad), 0)
    padded.paste(alpha, (pad, pad))
    body = padded.filter(ImageFilter.MinFilter(erode)).crop((pad, pad, pad + w, pad + h))

    mask = Image.new("L", (w, h), 0)
    mp = mask.load()
    bp = body.load()
    for y in range(h):
        for x in range(w):
            if not bp[x, y]:
                continue
            r, g, b, a = px[x, y]
            if a > 0 and not is_sage(r, g, b):
                mp[x, y] = 255

    # Drop speckle from gloss/gradient noise, then soften the cut edge.
    mask = mask.filter(ImageFilter.MedianFilter(5))
    if fill:
        mask = fill_holes(mask)
    mask = mask.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.8))

    out = img.copy()
    out.putalpha(ImageChops.multiply(mask, img.getchannel("A")))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for name in BADGES:
        glyph = extract(load_original(name), fill=name in FILL_HOLES,
                        erode=BORDER_ERODE.get(name, 61))
        glyph.save(os.path.join(OUT, name + ".png"))
        print("glyph:", name)


if __name__ == "__main__":
    main()
