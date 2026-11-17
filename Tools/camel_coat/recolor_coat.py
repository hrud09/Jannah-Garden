"""Recolours the camel coat: base (orange), stripe (green/teal) and accent (maroon) hue families are each
moved to a target hue/saturation, keeping brightness and weave. Low-saturation pixels (fringe) stay."""
import sys, os, numpy as np
from PIL import Image, ImageDraw

# name: (base hue deg, base sat mul, stripe hue, stripe sat mul, accent hue, accent sat mul, value mul for base)
PALETTES = {
    "Original":        None,
    "IndigoGold":      (222, 0.80, 45, 1.00, 350, 1.0, 0.80, 2.0),
    "CrimsonCream":    (352, 0.95, 40, 0.30, 220, 0.9, 0.80, 2.2),
    "SaffronIndigo":   (44, 1.00, 232, 0.85, 350, 1.0, 1.00, 0.9),
    "TealCoral":       (180, 0.75, 10, 0.90, 25, 0.9, 0.75, 1.7),
    "PlumGold":        (285, 0.60, 45, 0.95, 340, 0.9, 0.80, 2.0),
    "ForestIvory":     (135, 0.55, 45, 0.22, 0, 1.0, 0.70, 2.2),
}

def remap(img, p):
    rgb = img.convert("RGB"); alpha = img.getchannel("A") if img.mode == "RGBA" else None
    hsv = np.asarray(rgb.convert("HSV")).astype(np.float32)
    h = hsv[..., 0] * 360 / 255; s = hsv[..., 1] / 255; v = hsv[..., 2] / 255
    bh, bs, sh, ss, ah, as_, bv, sv = p
    sat_w = np.clip((s - 0.42) / 0.15, 0, 1)            # leave the pale fringe alone
    base = ((h >= 18) & (h < 70)).astype(np.float32)   # orange/yellow weave
    stripe = ((h >= 90) & (h < 260)).astype(np.float32) # green/teal stripes
    accent = 1 - np.clip(base + stripe, 0, 1)            # reds/maroon
    nh = base * bh + stripe * sh + accent * ah
    nsm = base * bs + stripe * ss + accent * as_
    nvm = base * bv + stripe * sv + accent * 1
    # Blend by saturation weight so greys stay put.
    out_h = np.where(sat_w > 0, nh, h)
    out_s = s * (1 - sat_w + sat_w * nsm)
    out_v = np.clip(v * (1 - sat_w + sat_w * nvm), 0, 1)
    out = np.stack([out_h / 360 * 255 % 256, out_s * 255, out_v * 255], -1).astype(np.uint8)
    res = Image.fromarray(out, "HSV").convert("RGB")
    if alpha is not None: res.putalpha(alpha)
    return res

# Usage: python recolor_coat.py <Camel_Coat_BaseColor.png> <"Coat Variants" folder> write
src, outdir = sys.argv[1], sys.argv[2]
mode = sys.argv[3] if len(sys.argv) > 3 else "preview"
img = Image.open(src)
os.makedirs(outdir, exist_ok=True)
tiles = []
for name, p in PALETTES.items():
    r = img.convert("RGB") if p is None else remap(img, p)
    if mode == "write" and p is not None:
        r.save(os.path.join(outdir, f"Camel_Coat_{name}_BaseColor.png"))
    t = r.convert("RGB").resize((256, 256)); d = ImageDraw.Draw(t); d.rectangle([0, 0, 110, 16], fill=(0, 0, 0)); d.text((3, 3), name, fill=(255, 255, 255))
    tiles.append(t)
sheet = Image.new("RGB", (256 * 4, 256 * 2))
for i, t in enumerate(tiles): sheet.paste(t, ((i % 4) * 256, (i // 4) * 256))
sheet.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "remap_sheet.png"))
print("ok")
