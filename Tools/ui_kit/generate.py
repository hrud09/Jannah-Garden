"""
Jannah Garden UI kit generator.

Source of truth for the game's UI sprite set. The palette and geometry are
derived from the original badge art (settings-gear-badge.png et al), so the
generated frames, buttons and controls sit in the same visual family as the
side button strip.

Run:  python Tools/ui_kit/generate.py
Out:  Assets/2D Assets/UI/Jannah UI Kit/*.png

After regenerating, run the "Jannah/UI Kit/Reimport Sprites" menu command in
Unity so the 9-slice borders are reapplied to the new files.
"""

import os
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", "Assets", "2D Assets", "UI", "Jannah UI Kit",
)

SS = 4  # supersample factor for anti-aliasing

# ---------------------------------------------------------------- palette ---
CREAM = (250, 246, 238)
CREAM_DIM = (232, 226, 213)
# Warm sand, the kit's one darker cream. Used as a border where a cream body
# needs an edge but cannot take a contrasting hue (see jg_card_cream).
CREAM_EDGE = (221, 208, 184)

SAGE_HI = (170, 192, 172)
SAGE_LO = (136, 163, 141)

GOLD_HI = (212, 165, 80)
GOLD_LO = (192, 138, 46)

GREEN_HI = (127, 160, 127)
GREEN_LO = (95, 132, 99)

# "deep" used to be a second, darker green carried by panels, cards, tabs and
# the slider/scroll tracks. The UI now runs on a single green, so it is an
# alias of the button ramp; the name is kept because sprites are named after it.
DEEP_HI = GREEN_HI
DEEP_LO = GREEN_LO

CLAY_HI = (194, 132, 112)
CLAY_LO = (166, 106, 87)

# Semantic ramps. Every button and frame picks from here so the whole UI stays
# inside the badge art's colour family.
RAMPS = {
    "sage": (SAGE_HI, SAGE_LO),
    "deep": (DEEP_HI, DEEP_LO),
    "gold": (GOLD_HI, GOLD_LO),
    "green": (GREEN_HI, GREEN_LO),
    "clay": (CLAY_HI, CLAY_LO),
    "cream": (CREAM, CREAM_DIM),
}


# ----------------------------------------------------------------- shapes ---
def rounded_mask(size, radius, inset=0):
    """Anti-aliased rounded-rect alpha mask."""
    w, h = size
    m = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle(
        [inset * SS, inset * SS, (w - inset) * SS - 1, (h - inset) * SS - 1],
        radius=max(0, (radius - inset)) * SS,
        fill=255,
    )
    return m.resize((w, h), Image.LANCZOS)


def ellipse_mask(size, box):
    w, h = size
    m = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(m)
    d.ellipse([c * SS for c in box], fill=255)
    return m.resize((w, h), Image.LANCZOS)


def vgradient(size, top, bottom):
    w, h = size
    g = Image.new("RGB", (1, h))
    for y in range(h):
        t = y / max(1, h - 1)
        g.putpixel((0, y), tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return g.resize((w, h), Image.NEAREST)


def gloss(size, radius, border, limit=None, strength=0.30):
    """Soft elliptical highlight, as on the badge art.

    `limit` is the sprite's 9-slice border. The highlight is kept entirely
    above it so a stretched middle slice never smears the sheen.
    """
    w, h = size
    top = border + 1
    ceiling = limit if limit is not None else int(radius * 1.7) + top
    band = max(3, (ceiling - top) // 2)
    m = ellipse_mask((w, h), [border * 1.6, top, w - border * 1.6, top + band * 2])
    m = m.filter(ImageFilter.GaussianBlur(radius=max(1.0, band * 0.30)))
    return m.point(lambda v: int(v * strength))


# ------------------------------------------------------------------ parts ---
def framed(size, radius, border, ramp, glossy=True, slice_border=None,
           border_color=CREAM):
    """Cream-bordered squircle with a gradient body, the core badge look.

    `border_color` only moves off cream for sprites whose body is itself cream
    and would otherwise have no visible edge.
    """
    w, h = size
    hi, lo = RAMPS[ramp]

    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))

    outer = rounded_mask((w, h), radius)
    img.paste(Image.new("RGBA", (w, h), border_color + (255,)), (0, 0), outer)

    inner = rounded_mask((w, h), radius, inset=border)
    body = vgradient((w, h), hi, lo).convert("RGBA")
    img.paste(body, (0, 0), inner)

    if glossy:
        # Cream reads as a highlight already, so it needs far less sheen than
        # the saturated ramps or it turns milky.
        strength = 0.16 if ramp == "cream" else 0.30
        g = gloss((w, h), radius, border, limit=slice_border, strength=strength)
        g = Image.composite(g, Image.new("L", (w, h), 0), inner)
        img.paste(Image.new("RGBA", (w, h), (255, 255, 255, 255)), (0, 0), g)

    return img


def plain(size, radius, ramp, glossy=False, slice_border=None):
    """Borderless gradient squircle, for fills and recessed plates."""
    w, h = size
    hi, lo = RAMPS[ramp]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    mask = rounded_mask((w, h), radius)
    img.paste(vgradient((w, h), hi, lo).convert("RGBA"), (0, 0), mask)
    if glossy:
        g = gloss((w, h), radius, 0, limit=slice_border)
        g = Image.composite(g, Image.new("L", (w, h), 0), mask)
        img.paste(Image.new("RGBA", (w, h), (255, 255, 255, 255)), (0, 0), g)
    return img


def ring(size, radius, border):
    """Cream frame with a hollow centre, for framing maps, photos and previews."""
    w, h = size
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    outer = rounded_mask((w, h), radius)
    inner = rounded_mask((w, h), radius, inset=border)
    # Knock the inner area back out so whatever sits behind shows through.
    band = Image.composite(Image.new("L", (w, h), 0), outer, inner)
    img.paste(Image.new("RGBA", (w, h), CREAM + (255,)), (0, 0), band)
    return img


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.normpath(os.path.join(OUT, name + ".png"))
    img.save(path)
    return path


# ------------------------------------------------------------------- kit ----
# name -> (9-slice border in px, or None for a Simple sprite)
BORDERS = {}


def main():
    # --- buttons: 128px, radius 36, cream border 5 -------------------------
    for ramp in ("sage", "deep", "gold", "green", "clay", "cream"):
        save(framed((128, 128), 36, 5, ramp, slice_border=38), f"jg_btn_{ramp}")
        BORDERS[f"jg_btn_{ramp}"] = 38

    # pressed/disabled state for the neutral button
    save(framed((128, 128), 36, 5, "sage", glossy=False), "jg_btn_sage_pressed")
    BORDERS["jg_btn_sage_pressed"] = 38

    # --- circular buttons (close, icon) : Simple sprites -------------------
    for ramp in ("sage", "clay", "cream", "deep"):
        save(framed((128, 128), 64, 5, ramp), f"jg_btn_circle_{ramp}")

    # --- frames / panels: 192px, radius 44, cream border 7 -----------------
    # jg_panel_green shares the button ramp so a panel carrying green buttons
    # reads as one piece of art instead of two greens that nearly match.
    for ramp, name in (("deep", "jg_panel"), ("sage", "jg_panel_sage"),
                       ("cream", "jg_panel_cream"), ("green", "jg_panel_green")):
        save(framed((192, 192), 44, 7, ramp, slice_border=46), name)
        BORDERS[name] = 46

    # card: lighter border weight for nested content
    for ramp, name in (("deep", "jg_card"), ("sage", "jg_card_sage")):
        save(framed((160, 160), 32, 5, ramp, slice_border=34), name)
        BORDERS[name] = 34

    # Cream card body, for the shop item card. A green card carrying green
    # buttons on a green list plate collapsed into one block, so the card is the
    # light surface the green widgets sit on instead. The border is a deeper
    # cream rather than green: ShopItemUI tints this Image per item, and a tint
    # drags a coloured border off-hue while two creams shift together.
    save(framed((160, 160), 32, 6, "cream", slice_border=34,
                border_color=CREAM_EDGE), "jg_card_cream")
    BORDERS["jg_card_cream"] = 34

    # recessed plate for list backgrounds and read-only fields
    save(plain((160, 160), 28, "deep"), "jg_inset")
    BORDERS["jg_inset"] = 30

    # --- hollow frames (minimap, photo preview, item previews) -------------
    save(ring((192, 192), 44, 7), "jg_frame_window")
    BORDERS["jg_frame_window"] = 46
    save(ring((160, 160), 32, 5), "jg_frame_window_s")
    BORDERS["jg_frame_window_s"] = 34
    # Heavy band for framing a square render texture (the minimap): it has to
    # overlap the texture far enough inward to cover its hard corners.
    save(ring((192, 192), 44, 30), "jg_frame_window_thick")
    BORDERS["jg_frame_window_thick"] = 46

    # --- title ribbon ------------------------------------------------------
    for ramp, name in (("gold", "jg_ribbon_gold"), ("deep", "jg_ribbon_deep")):
        save(framed((192, 96), 28, 5, ramp, slice_border=30), name)
        BORDERS[name] = 30

    # --- tabs --------------------------------------------------------------
    for ramp, name in (("gold", "jg_tab_active"), ("deep", "jg_tab_inactive")):
        save(framed((128, 128), 30, 5, ramp, slice_border=32), name)
        BORDERS[name] = 32

    # --- toggle ------------------------------------------------------------
    save(framed((96, 96), 26, 5, "cream", slice_border=28), "jg_toggle_bg")
    BORDERS["jg_toggle_bg"] = 28
    save(check_mark(96), "jg_toggle_check")

    # --- slider ------------------------------------------------------------
    save(plain((64, 32), 16, "deep"), "jg_slider_track")
    BORDERS["jg_slider_track"] = 16
    save(plain((64, 32), 16, "gold", glossy=True, slice_border=16), "jg_slider_fill")
    BORDERS["jg_slider_fill"] = 16
    save(framed((96, 96), 48, 6, "cream"), "jg_slider_handle")

    # --- scrollbar ---------------------------------------------------------
    save(plain((48, 48), 24, "sage"), "jg_scroll_handle")
    BORDERS["jg_scroll_handle"] = 24
    save(plain((48, 48), 24, "deep"), "jg_scroll_track")
    BORDERS["jg_scroll_track"] = 24

    # --- scrim / dim overlay ----------------------------------------------
    scrim = Image.new("RGBA", (16, 16), (16, 28, 20, 214))
    save(scrim, "jg_scrim")

    category_icons()

    write_border_manifest()
    print(f"Wrote {len(os.listdir(os.path.normpath(OUT)))} files to {os.path.normpath(OUT)}")


# -------------------------------------------------------- category icons ---
# Flat cream/gold glyphs for the shop's collection tabs. They sit on a green
# jg_panel plate, so they are drawn as silhouettes in the kit's creams rather
# than as full-colour art - the stock emoji-style icons they replace read as
# borrowed clip art against the rest of the UI.
#
# Each glyph is drawn at SS times its final size and downsampled, the same
# anti-aliasing trick the rest of the kit uses.

ICON = 128  # final px for every category glyph


def _icon_canvas():
    return Image.new("RGBA", (ICON * SS, ICON * SS), (0, 0, 0, 0))


def _finish(img):
    return img.resize((ICON, ICON), Image.LANCZOS)


def _star_points(cx, cy, outer, inner, points=8, phase=0.0):
    """Alternating outer/inner vertices for an n-pointed star."""
    import math
    pts = []
    for i in range(points * 2):
        r = outer if i % 2 == 0 else inner
        a = phase + math.pi * i / points
        pts.append((cx + r * math.sin(a), cy - r * math.cos(a)))
    return pts


def icon_plants():
    """Plants & Gardens: a round-canopied tree."""
    img = _icon_canvas()
    d = ImageDraw.Draw(img)
    s = ICON * SS
    cream = CREAM + (255,)
    # trunk
    d.rounded_rectangle([0.455 * s, 0.56 * s, 0.545 * s, 0.86 * s],
                        radius=0.035 * s, fill=GOLD_LO + (255,))
    # canopy: three overlapping lobes so the silhouette reads as foliage
    d.ellipse([0.16 * s, 0.26 * s, 0.56 * s, 0.66 * s], fill=cream)
    d.ellipse([0.44 * s, 0.26 * s, 0.84 * s, 0.66 * s], fill=cream)
    d.ellipse([0.28 * s, 0.12 * s, 0.72 * s, 0.56 * s], fill=cream)
    return _finish(img)


def icon_water():
    """Water of Garden: a tiered fountain with two arcing jets."""
    img = _icon_canvas()
    d = ImageDraw.Draw(img)
    s = ICON * SS
    cream = CREAM + (255,)
    gold = GOLD_LO + (255,)
    # lower basin and upper bowl
    d.rounded_rectangle([0.14 * s, 0.70 * s, 0.86 * s, 0.82 * s],
                        radius=0.06 * s, fill=cream)
    d.rounded_rectangle([0.30 * s, 0.50 * s, 0.70 * s, 0.58 * s],
                        radius=0.04 * s, fill=cream)
    # stem
    d.rounded_rectangle([0.465 * s, 0.30 * s, 0.535 * s, 0.52 * s],
                        radius=0.03 * s, fill=cream)
    # jets arcing out of the top, drawn as arcs so they read as moving water
    w = int(0.045 * s)
    d.arc([0.18 * s, 0.22 * s, 0.50 * s, 0.62 * s], 250, 360, fill=gold, width=w)
    d.arc([0.50 * s, 0.22 * s, 0.82 * s, 0.62 * s], 180, 290, fill=gold, width=w)
    # droplet crowning the stem
    d.ellipse([0.455 * s, 0.17 * s, 0.545 * s, 0.27 * s], fill=gold)
    return _finish(img)


def icon_architecture():
    """Architecture: a pointed arch on two piers."""
    img = _icon_canvas()
    d = ImageDraw.Draw(img)
    s = ICON * SS
    cream = CREAM + (255,)
    stroke = int(0.085 * s)
    # the arch head: two arcs meeting in a point at the top
    d.arc([0.16 * s, 0.14 * s, 0.98 * s, 0.96 * s], 180, 270, fill=cream, width=stroke)
    d.arc([0.02 * s, 0.14 * s, 0.84 * s, 0.96 * s], 270, 360, fill=cream, width=stroke)
    # piers down to the floor
    d.rectangle([0.16 * s - stroke / 2, 0.55 * s, 0.16 * s + stroke / 2, 0.88 * s], fill=cream)
    d.rectangle([0.84 * s - stroke / 2, 0.55 * s, 0.84 * s + stroke / 2, 0.88 * s], fill=cream)
    # plinth
    d.rounded_rectangle([0.08 * s, 0.86 * s, 0.92 * s, 0.94 * s],
                        radius=0.03 * s, fill=GOLD_LO + (255,))
    return _finish(img)


def icon_decor():
    """Decor & Sacred Objects: the eight-point khatam star, the motif the
    collection's own mosaic items are built from."""
    img = _icon_canvas()
    d = ImageDraw.Draw(img)
    s = ICON * SS
    c = 0.5 * s
    d.polygon(_star_points(c, c, 0.44 * s, 0.185 * s), fill=CREAM + (255,))
    d.polygon(_star_points(c, c, 0.20 * s, 0.085 * s), fill=GOLD_LO + (255,))
    return _finish(img)


def icon_celestial():
    """Celestial & Light: a crescent with a sparkle."""
    img = _icon_canvas()
    s = ICON * SS
    cream = CREAM + (255,)

    # Crescent by subtraction: a full disc minus an offset disc. Done on a
    # mask so the bite has the same anti-aliased edge as the outer rim.
    full = Image.new("L", (s, s), 0)
    ImageDraw.Draw(full).ellipse([0.10 * s, 0.10 * s, 0.78 * s, 0.78 * s], fill=255)
    bite = Image.new("L", (s, s), 0)
    ImageDraw.Draw(bite).ellipse([0.28 * s, 0.03 * s, 0.96 * s, 0.71 * s], fill=255)
    full.paste(0, (0, 0), bite)

    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    img.paste(Image.new("RGBA", (s, s), cream), (0, 0), full)

    # four-point sparkle off the crescent's open side
    d = ImageDraw.Draw(img)
    d.polygon(_star_points(0.76 * s, 0.76 * s, 0.20 * s, 0.045 * s, points=4),
              fill=GOLD_LO + (255,))
    return _finish(img)


CATEGORY_ICONS = {
    "jg_cat_plants": icon_plants,
    "jg_cat_water": icon_water,
    "jg_cat_architecture": icon_architecture,
    "jg_cat_decor": icon_decor,
    "jg_cat_celestial": icon_celestial,
}


def category_icons():
    for name, fn in CATEGORY_ICONS.items():
        save(fn(), name)


def check_mark(size):
    """Gold check glyph for toggles."""
    img = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    s = size * SS
    d.line(
        [(0.22 * s, 0.52 * s), (0.42 * s, 0.72 * s), (0.78 * s, 0.28 * s)],
        fill=GOLD_LO + (255,),
        width=int(0.13 * s),
        joint="curve",
    )
    return img.resize((size, size), Image.LANCZOS)


def write_border_manifest():
    """Unity reads this to reapply 9-slice borders after a regenerate."""
    path = os.path.normpath(os.path.join(OUT, "borders.txt"))
    with open(path, "w", encoding="utf-8") as f:
        for name, b in sorted(BORDERS.items()):
            f.write(f"{name}={b}\n")


if __name__ == "__main__":
    main()
