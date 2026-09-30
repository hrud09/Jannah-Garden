"""
Guards the restyle mapping against the hollow-to-solid bug.

Several of the bought-pack frames have a transparent centre and are drawn in
front of other UI. Mapping one of those to a solid kit sprite silently paints
over whatever sits behind it -- that is what hid the entire shop item grid
behind the treasure inventory's list frame.

This reads the SpriteMap straight out of JannahUIRestyle.cs (so it cannot drift
from the real mapping), measures each source sprite's centre alpha, and fails if
a hollow source maps to a solid kit sprite.

Run:  python Tools/ui_kit/audit_mappings.py
"""

import os
import re
import sys
import glob
from PIL import Image

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ASSETS = os.path.join(ROOT, "Assets")
RESTYLE = os.path.join(ASSETS, "Editor", "JannahUIRestyle.cs")
KIT = os.path.join(ASSETS, "2D Assets", "UI", "Jannah UI Kit")

# A source may be hollow and still map to a solid sprite when nothing is ever
# drawn behind it. Each entry needs a reason.
ALLOWED_SOLID = {
    # Tutorial instruction panel: a standalone dark panel, tinted opaque black
    # already, with nothing behind it that must show through.
    "ListFrame_01_InnerGlow": "standalone tutorial panel background",
    # The source was a hollow border ring; the kit gives the rotation slider a
    # real filled track, which is what a slider should have. Nothing sits behind
    # it that needs to show through.
    "Slider_Play_02_Border": "slider track is meant to be solid",
}

HOLLOW_THRESHOLD = 60  # mean alpha (0-255) over the centre third


def centre_alpha(path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    total = count = 0
    for y in range(h // 3, max(h // 3 + 1, 2 * h // 3)):
        for x in range(w // 3, max(w // 3 + 1, 2 * w // 3)):
            total += px[x, y][3]
            count += 1
    return total / max(1, count)


def load_map():
    src = open(RESTYLE, encoding="utf-8").read()
    # { "Source_Sprite", "jg_target" },  -- ignores commented-out lines
    pairs = {}
    for line in src.splitlines():
        if line.strip().startswith("//"):
            continue
        m = re.search(r'\{\s*"([^"]+)"\s*,\s*"(jg_[^"]+)"\s*\}', line)
        if m:
            pairs[m.group(1)] = m.group(2)
    return pairs


def index_sprites():
    idx = {}
    for pattern in ("**/*.png", "**/*.Png", "**/*.PNG"):
        for p in glob.glob(os.path.join(ASSETS, pattern), recursive=True):
            idx.setdefault(os.path.splitext(os.path.basename(p))[0], p)
    return idx


def main():
    mapping = load_map()
    sprites = index_sprites()

    kit_hollow = {
        os.path.splitext(os.path.basename(p))[0]
        for p in glob.glob(os.path.join(KIT, "*.png"))
        if centre_alpha(p) < HOLLOW_THRESHOLD
    }

    failures = []
    for source, target in sorted(mapping.items()):
        path = sprites.get(source)
        if path is None:
            continue  # source sprite not in the project (already-mapped kit name, etc.)
        alpha = centre_alpha(path)
        if alpha >= HOLLOW_THRESHOLD:
            continue
        if target in kit_hollow:
            continue
        if source in ALLOWED_SOLID:
            print(f"  allowed: {source} -> {target} ({ALLOWED_SOLID[source]})")
            continue
        failures.append((source, target, alpha))

    if failures:
        print("\nFAIL: hollow source sprite mapped to a solid kit sprite:")
        for source, target, alpha in failures:
            print(f"  {source} (centre alpha {alpha:.0f}) -> {target}")
        print("\nMap these to a hollow kit sprite (jg_frame_window / jg_frame_window_s)")
        print("or add a justified entry to ALLOWED_SOLID.")
        return 1

    print(f"OK: {len(mapping)} mappings checked, no hollow-to-solid regressions.")
    print(f"    hollow kit sprites: {', '.join(sorted(kit_hollow))}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
