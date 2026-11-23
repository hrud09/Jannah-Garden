"""Recolours the majlis tent: the crimson ground of the fabric (canopy/walls/cushions) and the props sheet
(rug, bolsters) is moved to a new hue, keeping brightness and weave. Everything that isn't red (the cream,
black, green, blue and gold trims) stays as it is, so each tent keeps the same sadu pattern in a new colour.

Usage: python recolor_tent.py <Textures folder> [write]
  Writes Colour Variants/T_Majlis_<Part>_<Name>.png and a preview sheet next to this script."""
import sys, os, numpy as np
from PIL import Image, ImageDraw

# name: (target hue deg, saturation mul, value mul)
PALETTES = {
    "Crimson": None,               # the original
    "Indigo":  (226, 0.85, 0.95),
    "Emerald": (150, 0.80, 0.85),
    "Saffron": (32, 0.95, 1.30),
}
PARTS = ["Fabric", "Props"]

def remap(img, p):
    alpha = img.getchannel("A") if img.mode == "RGBA" else None
    hsv = np.asarray(img.convert("RGB").convert("HSV")).astype(np.float32)
    h = hsv[..., 0] * 360 / 255; s = hsv[..., 1] / 255; v = hsv[..., 2] / 255
    th, sm, vm = p
    # Reds: hue within 25 deg of 355 (wraps through 0), soft edge so anti-aliased borders don't fringe.
    dist = np.minimum(np.abs(h - 355), 360 - np.abs(h - 355))
    w = np.clip((25 - dist) / 8, 0, 1) * np.clip((s - 0.30) / 0.15, 0, 1)
    # Keep each pixel's offset from the base red so darker/lighter reds stay distinct.
    off = ((h - 355 + 180) % 360) - 180
    nh = np.where(w > 0, (th + off) % 360, h)
    ns = np.clip(s * (1 - w + w * sm), 0, 1)
    nv = np.clip(v * (1 - w + w * vm), 0, 1)
    out = np.stack([nh / 360 * 255 % 256, ns * 255, nv * 255], -1).astype(np.uint8)
    res = Image.fromarray(out, "HSV").convert("RGB")
    if alpha is not None: res.putalpha(alpha)
    return res

src_dir = sys.argv[1]
mode = sys.argv[2] if len(sys.argv) > 2 else "preview"
out_dir = os.path.join(src_dir, "Colour Variants")
os.makedirs(out_dir, exist_ok=True)
tiles = []
for name, p in PALETTES.items():
    for part in PARTS:
        img = Image.open(os.path.join(src_dir, f"T_Majlis_{part}.png"))
        r = img if p is None else remap(img, p)
        if mode == "write" and p is not None:
            r.save(os.path.join(out_dir, f"T_Majlis_{part}_{name}.png"))
        t = r.convert("RGB").resize((256, 256)); d = ImageDraw.Draw(t)
        d.rectangle([0, 0, 120, 16], fill=(0, 0, 0)); d.text((3, 3), f"{name} {part}", fill=(255, 255, 255))
        tiles.append(t)
sheet = Image.new("RGB", (256 * 4, 256 * 2))
for i, t in enumerate(tiles): sheet.paste(t, ((i // 2) * 256, (i % 2) * 256))
sheet.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "remap_sheet.png"))
print("ok")
