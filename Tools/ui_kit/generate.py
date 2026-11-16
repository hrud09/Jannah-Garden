"""
Jannah Garden UI kit generator.

Source of truth for the game's UI sprite set.

Style ("ornate jade", 2026-11-15): jade-teal bodies inside bevelled gold
frames with a dark bronze rim, a fine inner gold line with notched corners and
teal gem studs on the large plates, chamfered (pointed) button ends, and
gold-ringed round buttons. Bespoke decor sprites (medallion, divider,
flourish) are added to large popups by Jannah/UI Kit/Decorate Panels.

Every sprite keeps its name, so all scene and prefab references pick up the new
art in place. Everything is drawn at SCALE x the old pixel size and imported at
SCALE x 100 pixels-per-unit, so on-screen sizes and 9-slice borders are
unchanged while the ornament detail stays crisp on high-DPI phones.

9-slice rule: anything that is not a plain straight line (gems, notches,
chamfers, gloss) must sit entirely inside the sprite's slice border, or it
smears when the middle slices stretch.

Run:  python Tools/ui_kit/generate.py
Out:  Assets/2D Assets/UI/Jannah UI Kit/*.png
      Assets/2D Assets/UI/UI Elements/*-badge.png (reframed side buttons)

After regenerating, run "Jannah/UI Kit/Reimport Sprites" in Unity so the
9-slice borders and pixels-per-unit are reapplied to the new files.
"""

import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "2D Assets", "UI", "Jannah UI Kit")
BADGE_OUT = os.path.join(HERE, "..", "..", "Assets", "2D Assets", "UI", "UI Elements")
GLYPHS = os.path.join(HERE, "glyphs")

SS = 4      # supersample factor for anti-aliasing
SCALE = 2   # texture px per old kit px; the importer uses PPU = 100 * SCALE
PPU = 100 * SCALE

# ---------------------------------------------------------------- palette ---
IVORY = (248, 243, 226)
IVORY_LO = (232, 221, 190)
# Darker ivory, still kept for the old name (jg_card_cream edge).
CREAM = IVORY
CREAM_DIM = IVORY_LO

GOLD_HI = (246, 222, 150)
GOLD_MID = (214, 168, 84)
GOLD_LO = (160, 112, 44)
BRONZE = (86, 58, 28)          # outer rim and the dark line inside the frame

TEAL_GEM = (96, 214, 204)
TEAL_GEM_HI = (214, 252, 246)
TEAL_GEM_LO = (28, 118, 116)

JADE_HI = (82, 140, 124)
JADE_LO = (40, 92, 86)
JADE_DEEP_HI = (34, 78, 74)    # recessed plates and tracks
JADE_DEEP_LO = (46, 94, 88)

SAGE_HI = (156, 186, 170)      # neutral: teal-leaning sage
SAGE_LO = (112, 148, 136)

GOLD_BODY_HI = (222, 176, 92)  # gold buttons/ribbons; deep enough for ivory text
GOLD_BODY_LO = (178, 124, 46)

CLAY_HI = (196, 124, 106)      # destructive: rose copper
CLAY_LO = (152, 86, 74)

RAMPS = {
    "sage": (SAGE_HI, SAGE_LO),
    "green": (JADE_HI, JADE_LO),
    # "deep" has been an alias of the green ramp since 2026-10-20 (one green
    # across panels, cards, tabs and buttons); kept because sprites use the name.
    "deep": (JADE_HI, JADE_LO),
    "gold": (GOLD_BODY_HI, GOLD_BODY_LO),
    "clay": (CLAY_HI, CLAY_LO),
    "cream": (IVORY, IVORY_LO),
    "recess": (JADE_DEEP_HI, JADE_DEEP_LO),
}


def S(v):
    """Old kit px -> texture px."""
    return int(round(v * SCALE))


# ------------------------------------------------------------- primitives ---
def shape_mask(kind, size, inset=0.0, radius=0.0, cut=0.0):
    """Anti-aliased alpha mask for one of the kit's three outlines.

    kind  'round'   rounded rectangle with corner `radius`
          'chamfer' octagon whose corners are cut 45 degrees by `cut`
          'circle'  ellipse filling the box
    `inset` shrinks the outline uniformly (every edge moves in by `inset`).
    """
    w, h = size
    m = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(m)
    x0, y0, x1, y1 = inset, inset, w - inset, h - inset
    if x1 <= x0 or y1 <= y0:
        return Image.new("L", (w, h), 0)
    box = [x0 * SS, y0 * SS, x1 * SS - 1, y1 * SS - 1]
    if kind == "round":
        d.rounded_rectangle(box, radius=max(0.0, radius - inset) * SS, fill=255)
    elif kind == "circle":
        d.ellipse(box, fill=255)
    elif kind == "chamfer":
        # An inset 45-degree edge shortens the cut by inset * (2 - sqrt 2).
        c = max(0.0, cut - inset * (2 - math.sqrt(2)))
        pts = [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c),
               (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]
        d.polygon([(px * SS, py * SS) for px, py in pts], fill=255)
    else:
        raise ValueError(kind)
    return m.resize((w, h), Image.LANCZOS)


def band(outer, inner):
    """outer minus inner."""
    return ImageChops.subtract(outer, inner)


def vgradient(size, top, bottom):
    w, h = size
    g = Image.new("RGB", (1, h))
    for y in range(h):
        t = y / max(1, h - 1)
        g.putpixel((0, y), tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return g.resize((w, h), Image.NEAREST).convert("RGBA")


def fill(img, mask, src):
    """Paint a colour (RGB, or RGBA for partial opacity) or an image through
    `mask` onto `img`."""
    if isinstance(src, tuple):
        if len(src) == 4:
            mask = mask.point(lambda v, a=src[3]: v * a // 255)
        src = Image.new("RGBA", img.size, src[:3] + (255,))
    img.paste(src, (0, 0), mask)


def scale_mask(mask, k):
    return mask.point(lambda v: int(v * k))


def gloss(size, body, top, ceiling, strength=0.26):
    """Soft elliptical sheen kept between `top` and `ceiling` (the slice border)."""
    w, h = size
    span = max(4, ceiling - top)
    m = Image.new("L", (w * SS, h * SS), 0)
    ImageDraw.Draw(m).ellipse(
        [w * 0.14 * SS, top * SS, w * 0.86 * SS, (top + span * 1.1) * SS], fill=255)
    m = m.resize((w, h), Image.LANCZOS).filter(ImageFilter.GaussianBlur(span * 0.22))
    # Hard stop at the slice line so the stretched middle never carries sheen.
    clip = Image.new("L", (w, h), 0)
    ImageDraw.Draw(clip).rectangle([0, 0, w, ceiling], fill=255)
    m = ImageChops.multiply(m, clip)
    return ImageChops.multiply(scale_mask(m, strength), body)


def gem(img, cx, cy, r, color=TEAL_GEM, hi=TEAL_GEM_HI, lo=TEAL_GEM_LO):
    """Faceted diamond stud with a gold setting, centred on (cx, cy)."""
    layer = Image.new("RGBA", (img.width * SS, img.height * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    X, Y, R = cx * SS, cy * SS, r * SS

    def diamond(rr):
        return [(X, Y - rr), (X + rr, Y), (X, Y + rr), (X - rr, Y)]

    d.polygon(diamond(R * 1.45), fill=BRONZE + (255,))
    d.polygon(diamond(R * 1.28), fill=GOLD_MID + (255,))
    d.polygon(diamond(R), fill=lo + (255,))
    # Upper-left facets catch the light.
    d.polygon([(X, Y - R), (X, Y), (X - R, Y)], fill=color + (255,))
    d.polygon([(X, Y - R), (X + R, Y), (X, Y)], fill=tuple((a + b) // 2 for a, b in zip(color, lo)) + (255,))
    d.ellipse([X - R * 0.45, Y - R * 0.62, X - R * 0.12, Y - R * 0.30], fill=hi + (255,))
    layer = layer.resize(img.size, Image.LANCZOS)
    img.alpha_composite(layer)


def notched_line(img, inset, notch, width, color, size=None):
    """A fine inner line with concave (notched) corners, as on the card art.

    Straight runs stretch cleanly under 9-slicing; the notches sit inside the
    corner slices.
    """
    w, h = img.size
    layer = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    a, q, lw = inset * SS, notch * SS, max(1, int(width * SS))
    W, H = w * SS, h * SS
    col = color + (255,) if len(color) == 3 else color
    d.line([(a + q, a), (W - a - q, a)], fill=col, width=lw)
    d.line([(a + q, H - a), (W - a - q, H - a)], fill=col, width=lw)
    d.line([(a, a + q), (a, H - a - q)], fill=col, width=lw)
    d.line([(W - a, a + q), (W - a, H - a - q)], fill=col, width=lw)
    # Quarter arcs centred on each corner point, bowing inward.
    for cx, cy, start in ((a, a, 0), (W - a, a, 90), (W - a, H - a, 180), (a, H - a, 270)):
        d.arc([cx - q, cy - q, cx + q, cy + q], start, start + 90, fill=col, width=lw)
    img.alpha_composite(layer.resize((w, h), Image.LANCZOS))


# ------------------------------------------------------------------ parts ---
def ornate(size, kind, ramp, frame, slice_border, radius=0.0, cut=0.0,
           glossy=True, inner_line=False, gems=False, body_img=None,
           frame_color=None):
    """The kit's core look: bronze rim, bevelled gold band, dark keyline, body.

    frame        total frame thickness in texture px
    inner_line   fine notched gold line inside the body (large plates)
    gems         teal studs at the inner line's corners
    frame_color  override the gold band (tuple hi, lo)
    """
    w, h = size
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    kw = dict(radius=radius, cut=cut)

    outer = shape_mask(kind, size, 0, **kw)
    gold_o = shape_mask(kind, size, frame * 0.16, **kw)
    key = shape_mask(kind, size, frame * 0.70, **kw)
    body = shape_mask(kind, size, frame * 0.84, **kw)

    fill(img, outer, BRONZE)
    hi, lo = frame_color or (GOLD_HI, GOLD_LO)
    fill(img, gold_o, vgradient(size, hi, lo))
    # Bright bevel line on the gold band's outer half.
    bevel = band(shape_mask(kind, size, frame * 0.22, **kw),
                 shape_mask(kind, size, frame * 0.34, **kw))
    fill(img, scale_mask(bevel, 0.55), (255, 248, 222))
    fill(img, key, BRONZE)

    top, bottom = RAMPS[ramp]
    body_src = body_img if body_img is not None else vgradient(size, top, bottom)
    fill(img, body, body_src)

    # Soft inner vignette so the body reads as recessed into the frame.
    shade = band(body, shape_mask(kind, size, frame * 0.84 + max(2, frame * 0.5), **kw))
    shade = shade.filter(ImageFilter.GaussianBlur(max(1, frame * 0.35)))
    fill(img, ImageChops.multiply(scale_mask(shade, 0.35), body), (0, 0, 0))

    if inner_line:
        li = frame + max(S(5), frame * 0.55)
        notched_line(img, li, max(S(5), frame * 0.6), max(1.2, SCALE * 0.9),
                     GOLD_MID if ramp != "cream" else GOLD_LO)
        if gems:
            gr = max(S(3.4), frame * 0.36)
            for cx, cy in ((li, li), (w - li, li), (w - li, h - li), (li, h - li)):
                gem(img, cx, cy, gr)

    if glossy:
        strength = 0.12 if ramp == "cream" else 0.24
        img.paste((255, 255, 255, 255), (0, 0),
                  gloss(size, body, int(frame * 0.9), slice_border - 1, strength))
    return img


def recessed(size, radius, slice_border, ramp="recess", outline=GOLD_LO):
    """Sunken plate for lists, tracks and read-only fields: dark body, inner
    top shadow, thin gold inlay edge."""
    w, h = size
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    outer = shape_mask("round", size, 0, radius=radius)
    inner = shape_mask("round", size, max(1.5, SCALE * 1.1), radius=radius)
    fill(img, outer, outline)
    top, bottom = RAMPS[ramp]
    fill(img, inner, vgradient(size, top, bottom))
    # Top inner shadow, kept inside the top slice.
    sh = Image.new("L", (w, h), 0)
    depth = max(3, min(slice_border - 2, int(h * 0.22)))
    for y in range(depth):
        ImageDraw.Draw(sh).line([(0, y), (w, y)], fill=int(110 * (1 - y / depth) ** 2))
    fill(img, ImageChops.multiply(sh, inner), (0, 0, 0))
    return img


def ring(size, radius, frame, slice_border):
    """Hollow gold frame for maps, photos and previews."""
    w, h = size
    img = ornate(size, "round", "green", frame, slice_border, radius=radius,
                 glossy=False)
    # Knock the body back out so whatever sits behind shows through.
    hole = shape_mask("round", size, frame * 0.84, radius=radius)
    a = ImageChops.subtract(img.getchannel("A"), hole)
    img.putalpha(a)
    # Gold inner lip so the hole edge is finished, not cut.
    lip = band(shape_mask("round", size, frame * 0.84 - max(1.0, SCALE * 0.8), radius=radius), hole)
    fill(img, lip, GOLD_MID)
    return img


def save(img, name, folder=OUT):
    os.makedirs(folder, exist_ok=True)
    path = os.path.normpath(os.path.join(folder, name + ".png"))
    img.save(path)
    return path


# ------------------------------------------------------------------- kit ----
# name -> 9-slice border in TEXTURE px (written to borders.txt)
BORDERS = {}


def kit(name, img, border=None):
    save(img, name)
    if border:
        BORDERS[name] = border


def main():
    # --- buttons: chamfered ends, as on the pill buttons in the references --
    # 256px tex (128 old), slice 76 (38 old). The chamfer lives in the corner
    # slice, so stretching a button wide or tall keeps a clean octagon.
    B, BS, BF, BC = S(128), S(38), S(6), S(26)
    for ramp in ("sage", "deep", "gold", "green", "clay", "cream"):
        kit(f"jg_btn_{ramp}",
            ornate((B, B), "chamfer", ramp, BF, BS, cut=BC), BS)
    kit("jg_btn_sage_pressed",
        ornate((B, B), "chamfer", "sage", BF, BS, cut=BC, glossy=False), BS)

    # --- round buttons (close, icon): gold ring around a jewel-like disc ----
    for ramp in ("sage", "clay", "cream", "deep"):
        img = ornate((B, B), "circle", ramp, S(8), B // 2)
        save(img, f"jg_btn_circle_{ramp}")

    # --- panels: the big popup plates --------------------------------------
    P, PS, PF = S(192), S(46), S(9)
    for ramp, name in (("deep", "jg_panel"), ("sage", "jg_panel_sage"),
                       ("cream", "jg_panel_cream"), ("green", "jg_panel_green")):
        kit(name, ornate((P, P), "round", ramp, PF, PS, radius=S(20),
                         inner_line=True, gems=True, glossy=False), PS)

    # --- cards: nested content, lighter frame, inner line but no gems ------
    C, CS, CF = S(160), S(34), S(6)
    for ramp, name in (("deep", "jg_card"), ("sage", "jg_card_sage")):
        kit(name, ornate((C, C), "round", ramp, CF, CS, radius=S(16),
                         inner_line=True, glossy=False), CS)
    # Shop item card. ShopItemUI tints this Image per item, so it stays a light
    # neutral surface; a tint shifts the gold frame and ivory body together.
    kit("jg_card_cream", ornate((C, C), "round", "cream", CF, CS, radius=S(16),
                                inner_line=True, glossy=False), CS)

    # --- recessed plate for list backgrounds and read-only fields ----------
    kit("jg_inset", recessed((S(160), S(160)), S(18), S(30)), S(30))

    # --- hollow frames ------------------------------------------------------
    kit("jg_frame_window", ring((P, P), S(20), S(8), S(46)), S(46))
    kit("jg_frame_window_s", ring((C, C), S(16), S(6), S(34)), S(34))
    # Heavy band for framing a square render texture (the minimap): it has to
    # overlap the texture far enough inward to cover its hard corners.
    kit("jg_frame_window_thick", ring((P, P), S(44), S(30), S(46)), S(46))

    # --- title ribbons: pointed ends like the banner bars in the references -
    R, RH, RS = S(192), S(96), S(30)
    for ramp, name in (("gold", "jg_ribbon_gold"), ("deep", "jg_ribbon_deep")):
        img = ornate((R, RH), "chamfer", ramp, S(5), RS, cut=S(24))
        for cx in (S(14), R - S(14)):
            gem(img, cx, RH / 2, S(4.2))
        # Gems sit on the ends' vertical centre line, which only stretches if
        # the ribbon is taller than its native height; ribbons never are.
        kit(name, img, RS)

    # --- tabs ---------------------------------------------------------------
    T, TS = S(128), S(32)
    kit("jg_tab_active", ornate((T, T), "chamfer", "gold", S(5), TS, cut=S(20)), TS)
    kit("jg_tab_inactive", ornate((T, T), "chamfer", "deep", S(5), TS, cut=S(20),
                                  glossy=False), TS)

    # --- toggle -------------------------------------------------------------
    kit("jg_toggle_bg", ornate((S(96), S(96)), "chamfer", "cream", S(5), S(28),
                               cut=S(18)), S(28))
    save(check_mark(S(96)), "jg_toggle_check")

    # --- slider -------------------------------------------------------------
    kit("jg_slider_track", recessed((S(64), S(32)), S(16), S(16)), S(16))
    fill_img = recessed((S(64), S(32)), S(16), S(16), ramp="gold", outline=GOLD_LO)
    fill_img.paste((255, 255, 255, 255), (0, 0),
                   gloss(fill_img.size, fill_img.getchannel("A"), 2, S(16) - 1, 0.35))
    kit("jg_slider_fill", fill_img, S(16))
    save(jewel_handle(S(96)), "jg_slider_handle")

    # --- scrollbar ----------------------------------------------------------
    kit("jg_scroll_handle", recessed((S(48), S(48)), S(24), S(24), ramp="gold",
                                     outline=BRONZE), S(24))
    kit("jg_scroll_track", recessed((S(48), S(48)), S(24), S(24)), S(24))

    # --- scrim / dim overlay ------------------------------------------------
    save(Image.new("RGBA", (16, 16), (10, 30, 30, 214)), "jg_scrim")

    # --- decor (Simple sprites, placed by Jannah/UI Kit/Decorate Panels) ----
    save(medallion(S(128)), "jg_deco_medallion")
    save(divider(S(256), S(32)), "jg_deco_divider")
    save(flourish(S(128)), "jg_deco_flourish")

    category_icons()
    badges()
    joystick()

    write_border_manifest()
    print(f"Wrote kit to {os.path.normpath(OUT)}")


# ------------------------------------------------------------ bespoke art ---
def jewel_handle(size):
    """Slider knob: gold ring set with a teal jewel."""
    img = ornate((size, size), "circle", "green", size * 0.16, size // 2, glossy=False)
    gem_body = shape_mask("circle", (size, size), size * 0.26)
    fill(img, gem_body, vgradient((size, size), TEAL_GEM, TEAL_GEM_LO))
    hl = Image.new("L", (size, size), 0)
    ImageDraw.Draw(hl).ellipse([size * 0.36, size * 0.32, size * 0.52, size * 0.44], fill=230)
    hl = hl.filter(ImageFilter.GaussianBlur(size * 0.02))
    fill(img, hl, TEAL_GEM_HI)
    return img


def check_mark(size):
    """Gold check with a bronze outline so it reads on the ivory toggle."""
    s = size * SS
    pts = [(0.22 * s, 0.52 * s), (0.42 * s, 0.72 * s), (0.78 * s, 0.28 * s)]
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line(pts, fill=BRONZE + (255,), width=int(0.18 * s), joint="curve")
    for p in (pts[0], pts[2]):
        r = 0.09 * s
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=BRONZE + (255,))
    d.line(pts, fill=GOLD_MID + (255,), width=int(0.11 * s), joint="curve")
    for p in (pts[0], pts[2]):
        r = 0.055 * s
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=GOLD_MID + (255,))
    return img.resize((size, size), Image.LANCZOS)


def _star(cx, cy, outer, inner, points=8, phase=0.0):
    pts = []
    for i in range(points * 2):
        r = outer if i % 2 == 0 else inner
        a = phase + math.pi * i / points
        pts.append((cx + r * math.sin(a), cy - r * math.cos(a)))
    return pts


def medallion(size):
    """Top-centre rosette for popups: khatam star in gold, jade disc, ivory
    eight-petal flower with a teal gem heart."""
    s = size * SS
    c = s / 2
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def sq(r, rot):
        return [(c + r * math.cos(rot + k * math.pi / 2), c + r * math.sin(rot + k * math.pi / 2))
                for k in range(4)]

    # Two overlapping squares = the eight-point khatam.
    for r, col in ((0.50, BRONZE), (0.45, GOLD_LO), (0.41, GOLD_MID)):
        d.polygon(sq(r * s, math.pi / 4), fill=col + (255,))
        d.polygon(sq(r * s, 0), fill=col + (255,))
    d.ellipse([c - 0.27 * s, c - 0.27 * s, c + 0.27 * s, c + 0.27 * s], fill=BRONZE + (255,))
    d.ellipse([c - 0.245 * s, c - 0.245 * s, c + 0.245 * s, c + 0.245 * s], fill=JADE_LO + (255,))
    # Petals
    for k in range(8):
        a = k * math.pi / 4
        px, py = c + 0.13 * s * math.sin(a), c - 0.13 * s * math.cos(a)
        r = 0.075 * s
        d.ellipse([px - r, py - r, px + r, py + r], fill=IVORY + (255,))
    for k in range(8):
        a = k * math.pi / 4 + math.pi / 8
        px, py = c + 0.15 * s * math.sin(a), c - 0.15 * s * math.cos(a)
        r = 0.022 * s
        d.ellipse([px - r, py - r, px + r, py + r], fill=GOLD_MID + (255,))
    img = img.resize((size, size), Image.LANCZOS)
    gem(img, size / 2, size / 2, size * 0.07)
    return img


def divider(w, h):
    """Gold rule tapering to points, with a gem flanked by two small diamonds."""
    s = SS
    img = Image.new("RGBA", (w * s, h * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cy = h * s / 2
    t = h * s * 0.10
    for col, k in ((BRONZE, 1.0), (GOLD_MID, 0.55)):
        d.polygon([(0, cy), (w * s * 0.42, cy - t * k * 1.4), (w * s * 0.58, cy - t * k * 1.4),
                   (w * s, cy), (w * s * 0.58, cy + t * k * 1.4), (w * s * 0.42, cy + t * k * 1.4)],
                  fill=col + (255,))
    for dx in (-0.12, 0.12):
        x = w * s * (0.5 + dx)
        r = h * s * 0.22
        d.polygon([(x, cy - r), (x + r, cy), (x, cy + r), (x - r, cy)], fill=BRONZE + (255,))
        r *= 0.66
        d.polygon([(x, cy - r), (x + r, cy), (x, cy + r), (x - r, cy)], fill=GOLD_HI + (255,))
    img = img.resize((w, h), Image.LANCZOS)
    gem(img, w / 2, h / 2, h * 0.24)
    return img


def _taper(d, pts, r0, r1, col):
    """Stroke a polyline as overlapping discs whose radius runs r0 -> r1."""
    n = len(pts)
    for i, (x, y) in enumerate(pts):
        r = r0 + (r1 - r0) * i / max(1, n - 1)
        d.ellipse([x - r, y - r, x + r, y + r], fill=col + (255,))


def flourish(size):
    """Bottom-left corner vine: a tapering gold scroll that runs in along the
    bottom edge and curls into a spiral, with jade leaves and a gem at its eye.
    Mirrored on the X axis for the bottom-right corner."""
    s = size * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    cx, cy = 0.30 * s, 0.62 * s
    spiral = []
    steps = 160
    for i in range(steps):
        t = i / (steps - 1)
        th = math.pi / 2 + t * 2.1 * math.pi
        r = 0.24 * s * math.exp(-0.95 * t * 2.1)
        spiral.append((cx + r * math.cos(th), cy + r * math.sin(th)))
    # Tail: from the right edge, sagging slightly, into the spiral's foot.
    tail = []
    x0, y0, x1, y1 = 0.98 * s, 0.80 * s, cx, cy + 0.24 * s
    for i in range(80):
        t = i / 79
        tail.append((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t + math.sin(t * math.pi) * 0.035 * s))
    # A sprig leaving the spiral's outer loop and rising toward the upper right.
    bx, by = spiral[int(steps * 0.42)]
    sprig = []
    for i in range(60):
        t = i / 59
        sprig.append((bx + t * 0.40 * s, by - math.sin(t * 1.5) * 0.30 * s - t * 0.04 * s))

    w = 0.028 * s
    for col, grow in ((BRONZE, 0.012 * s), (GOLD_MID, 0.0)):
        _taper(d, tail, 0.25 * w + grow, w + grow, col)
        _taper(d, spiral, w + grow, 0.30 * w + grow, col)
        _taper(d, sprig, 0.75 * w + grow, 0.22 * w + grow, col)
    # Highlight along the upper side of the strokes.
    _taper(d, [(x, y - 0.006 * s) for x, y in tail[20:]], 0.08 * w, 0.30 * w, GOLD_HI)

    def leaf(x, y, ang, ln, col_in):
        a = math.radians(ang)
        tx, ty = x + ln * math.cos(a), y - ln * math.sin(a)
        nx, ny = math.sin(a) * ln * 0.30, math.cos(a) * ln * 0.30
        mx, my = (x + tx) / 2, (y + ty) / 2
        outline = [(x, y), (mx + nx, my + ny), (tx, ty), (mx - nx, my - ny)]
        d.polygon(outline, fill=BRONZE + (255,))
        k = 0.78
        inner = [(x + (px - x) * k + (tx - x) * 0.08, y + (py - y) * k + (ty - y) * 0.08) for px, py in outline]
        d.polygon(inner, fill=col_in + (255,))
        d.line([(x, y), (tx, ty)], fill=GOLD_MID + (255,), width=max(1, int(0.006 * s)))

    def leaf_on(path, idx, side, ln):
        """Leaf growing off `path` at `idx`, angled 50 degrees off its tangent."""
        (x0_, y0_), (x1_, y1_) = path[max(0, idx - 2)], path[min(len(path) - 1, idx + 2)]
        tang = math.degrees(math.atan2(-(y1_ - y0_), x1_ - x0_))
        x, y = path[idx]
        leaf(x, y, tang + 50 * side, ln, (112, 190, 168))

    leaf_on(tail, 22, -1, 0.15 * s)
    leaf_on(tail, 46, 1, 0.17 * s)
    leaf_on(sprig, 24, 1, 0.14 * s)
    leaf_on(sprig, 42, -1, 0.12 * s)

    img = img.resize((size, size), Image.LANCZOS)
    gem(img, cx / SS, cy / SS + size * 0.02, size * 0.055)
    ex, ey = sprig[-1]
    gem(img, ex / SS, ey / SS, size * 0.035)
    return img


# -------------------------------------------------------- category icons ---
# Ivory/gold glyphs for the shop's collection tabs, sitting on a jade plate.
ICON = S(128)


def _icon_canvas():
    return Image.new("RGBA", (ICON * SS, ICON * SS), (0, 0, 0, 0))


def _finish(img):
    """Downsample and add a soft bronze outline so glyphs hold on gold too."""
    img = img.resize((ICON, ICON), Image.LANCZOS)
    a = img.getchannel("A").filter(ImageFilter.MaxFilter(3))
    out = Image.new("RGBA", img.size, BRONZE + (0,))
    out.putalpha(scale_mask(a, 0.8))
    out.alpha_composite(img)
    return out


def icon_plants():
    img = _icon_canvas()
    d = ImageDraw.Draw(img)
    s = ICON * SS
    d.rounded_rectangle([0.455 * s, 0.56 * s, 0.545 * s, 0.86 * s], radius=0.035 * s, fill=GOLD_MID + (255,))
    for box in ([0.16, 0.26, 0.56, 0.66], [0.44, 0.26, 0.84, 0.66], [0.28, 0.12, 0.72, 0.56]):
        d.ellipse([v * s for v in box], fill=IVORY + (255,))
    return _finish(img)


def icon_water():
    img = _icon_canvas()
    d = ImageDraw.Draw(img)
    s = ICON * SS
    iv, gold = IVORY + (255,), GOLD_MID + (255,)
    d.rounded_rectangle([0.14 * s, 0.70 * s, 0.86 * s, 0.82 * s], radius=0.06 * s, fill=iv)
    d.rounded_rectangle([0.30 * s, 0.50 * s, 0.70 * s, 0.58 * s], radius=0.04 * s, fill=iv)
    d.rounded_rectangle([0.465 * s, 0.30 * s, 0.535 * s, 0.52 * s], radius=0.03 * s, fill=iv)
    w = int(0.045 * s)
    d.arc([0.18 * s, 0.22 * s, 0.50 * s, 0.62 * s], 250, 360, fill=gold, width=w)
    d.arc([0.50 * s, 0.22 * s, 0.82 * s, 0.62 * s], 180, 290, fill=gold, width=w)
    d.ellipse([0.455 * s, 0.17 * s, 0.545 * s, 0.27 * s], fill=TEAL_GEM + (255,))
    return _finish(img)


def icon_architecture():
    img = _icon_canvas()
    d = ImageDraw.Draw(img)
    s = ICON * SS
    iv = IVORY + (255,)
    st = int(0.085 * s)
    d.arc([0.16 * s, 0.14 * s, 0.98 * s, 0.96 * s], 180, 270, fill=iv, width=st)
    d.arc([0.02 * s, 0.14 * s, 0.84 * s, 0.96 * s], 270, 360, fill=iv, width=st)
    d.rectangle([0.16 * s - st / 2, 0.55 * s, 0.16 * s + st / 2, 0.88 * s], fill=iv)
    d.rectangle([0.84 * s - st / 2, 0.55 * s, 0.84 * s + st / 2, 0.88 * s], fill=iv)
    d.rounded_rectangle([0.08 * s, 0.86 * s, 0.92 * s, 0.94 * s], radius=0.03 * s, fill=GOLD_MID + (255,))
    return _finish(img)


def icon_decor():
    img = _icon_canvas()
    d = ImageDraw.Draw(img)
    s = ICON * SS
    c = 0.5 * s
    d.polygon(_star(c, c, 0.44 * s, 0.185 * s), fill=IVORY + (255,))
    d.polygon(_star(c, c, 0.20 * s, 0.085 * s), fill=GOLD_MID + (255,))
    return _finish(img)


def icon_celestial():
    s = ICON * SS
    full = Image.new("L", (s, s), 0)
    ImageDraw.Draw(full).ellipse([0.10 * s, 0.10 * s, 0.78 * s, 0.78 * s], fill=255)
    bite = Image.new("L", (s, s), 0)
    ImageDraw.Draw(bite).ellipse([0.28 * s, 0.03 * s, 0.96 * s, 0.71 * s], fill=255)
    full.paste(0, (0, 0), bite)
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    img.paste(Image.new("RGBA", (s, s), IVORY + (255,)), (0, 0), full)
    ImageDraw.Draw(img).polygon(_star(0.76 * s, 0.76 * s, 0.20 * s, 0.045 * s, points=4),
                                fill=GOLD_MID + (255,))
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


# ----------------------------------------------------------------- badges ---
# The side-button badges are reframed in the kit style: their original glyphs
# (lifted once by extract_badge_glyphs.py) composited onto a gold-framed jade
# plate. Same file names, so both gardens pick them up in place.
BADGE_SIZE = 600
BADGE_SHAPE = {"drawer-toggle-badge": "circle"}


def badges():
    if not os.path.isdir(GLYPHS):
        print("No glyphs folder; skipping badges (run extract_badge_glyphs.py).")
        return
    for f in sorted(os.listdir(GLYPHS)):
        if not f.endswith(".png"):
            continue
        name = f[:-4]
        kind = BADGE_SHAPE.get(name, "round")
        n = BADGE_SIZE
        # The original badges left a ~4% transparent margin; keep it so the
        # buttons render at the same visual size.
        m = 24
        inner = n - 2 * m
        face = ornate((inner, inner), kind, "deep", 32, inner // 2, radius=140,
                      glossy=True)
        plate = Image.new("RGBA", (n, n), (0, 0, 0, 0))
        plate.alpha_composite(face, (m, m))
        if kind == "round":
            g = m + 62
            for cx, cy in ((g, g), (n - g, g), (n - g, n - g), (g, n - g)):
                gem(plate, cx, cy, 13)
        glyph = Image.open(os.path.join(GLYPHS, f)).convert("RGBA")
        if glyph.size != (n, n):
            glyph = glyph.resize((n, n), Image.LANCZOS)
        # Slightly smaller, so the heavier gold frame doesn't crowd the glyph.
        k = 0.9
        small = glyph.resize((int(n * k), int(n * k)), Image.LANCZOS)
        plate.alpha_composite(small, ((n - small.width) // 2, (n - small.height) // 2))
        save(plate, name, BADGE_OUT)


# --------------------------------------------------------------- joystick ---
# The Floating Joystick prefab (used in both gardens) points at these two
# project-local sprites; nothing else does. Same names and sizes as the
# originals so the prefab needs no changes.
def joystick():
    n = 512
    base = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    outer = shape_mask("circle", (n, n), 6)
    well = shape_mask("circle", (n, n), 30)
    fill(base, outer, BRONZE)
    fill(base, band(shape_mask("circle", (n, n), 10), well), vgradient((n, n), GOLD_HI, GOLD_LO))
    fill(base, scale_mask(band(shape_mask("circle", (n, n), 14), shape_mask("circle", (n, n), 19)), 0.6),
         (255, 248, 222))
    fill(base, band(well, shape_mask("circle", (n, n), 36)), BRONZE)
    # Translucent jade well so the garden shows through.
    inner = shape_mask("circle", (n, n), 36)
    fill(base, scale_mask(inner, 0.42), JADE_LO)
    fill(base, scale_mask(band(shape_mask("circle", (n, n), 70), shape_mask("circle", (n, n), 74)), 0.55),
         GOLD_MID)
    # Direction chevrons, as the original outline art had.
    lay = Image.new("RGBA", (n * SS, n * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    c = n * SS / 2
    for k in range(4):
        a = k * math.pi / 2
        def P(r, off):
            return (c + r * math.sin(a) + off * math.cos(a), c - r * math.cos(a) + off * math.sin(a))
        r0, r1, half = 0.335 * n * SS, 0.395 * n * SS, 0.07 * n * SS
        tri = [P(r1, 0), P(r0, half), P(r0, -half)]
        d.polygon(tri, fill=BRONZE + (255,))
        k2 = 0.62
        cx_, cy_ = sum(p[0] for p in tri) / 3, sum(p[1] for p in tri) / 3
        d.polygon([(cx_ + (x - cx_) * k2, cy_ + (y - cy_) * k2) for x, y in tri], fill=GOLD_HI + (255,))
    base.alpha_composite(lay.resize((n, n), Image.LANCZOS))
    save(base, "AllAxis_Outline", BADGE_OUT)

    save(jewel_handle(256).resize((257, 256), Image.LANCZOS), "Handle_Plain", BADGE_OUT)


def write_border_manifest():
    """Unity reads this to reapply 9-slice borders and PPU after a regenerate."""
    path = os.path.normpath(os.path.join(OUT, "borders.txt"))
    with open(path, "w", encoding="utf-8") as f:
        f.write(f"#ppu={PPU}\n")
        for name, b in sorted(BORDERS.items()):
            f.write(f"{name}={b}\n")


if __name__ == "__main__":
    main()
