using UnityEngine;

/// <summary>
/// Small procedural sprites for UI effects — a soft glow, a four-point sparkle and a sunburst of
/// rays. Generated once on first use and cached, so effects need no imported art (and no
/// import-setting or Addressables bookkeeping). All are white; tint them with <c>Image.color</c>.
/// </summary>
public static class UIFxSprites
{
    private static Sprite _glow;
    private static Sprite _sparkle;
    private static Sprite _rays;

    /// <summary>Radial soft glow, opaque centre fading to transparent at the edge.</summary>
    public static Sprite Glow => _glow != null ? _glow : (_glow = Build("uifx_glow", 64, GlowAlpha));

    /// <summary>Four-point star with a soft core — the "twinkle" used on fills and bursts.</summary>
    public static Sprite Sparkle => _sparkle != null ? _sparkle : (_sparkle = Build("uifx_sparkle", 64, SparkleAlpha));

    /// <summary>Twelve soft rays fanning out from the centre, for behind medals and badges.</summary>
    public static Sprite Rays => _rays != null ? _rays : (_rays = Build("uifx_rays", 256, RaysAlpha));

    /// <summary>Thin soft-edged ring, for ripples ("look here") around buttons.</summary>
    public static Sprite Ring => _ring != null ? _ring : (_ring = Build("uifx_ring", 128, RingAlpha));
    private static Sprite _ring;

    private static float RingAlpha(float x, float y)
    {
        float d = Mathf.Sqrt(x * x + y * y);
        float band = 1f - Mathf.Abs(d - 0.86f) / 0.1f; // a band about 0.2 wide just inside the edge
        return Mathf.Clamp01(band) * Mathf.Clamp01(band * 1.5f);
    }

    private static float GlowAlpha(float x, float y)
    {
        float d = Mathf.Sqrt(x * x + y * y);
        float a = Mathf.Clamp01(1f - d);
        return a * a;
    }

    private static float SparkleAlpha(float x, float y)
    {
        float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
        // Two thin, tapering beams plus a soft core.
        float beamH = Mathf.Clamp01(1f - ax) * Mathf.Clamp01(1f - ay / 0.07f);
        float beamV = Mathf.Clamp01(1f - ay) * Mathf.Clamp01(1f - ax / 0.07f);
        float core = GlowAlpha(x / 0.35f, y / 0.35f);
        return Mathf.Clamp01(Mathf.Max(Mathf.Max(beamH, beamV) * 1.2f, core));
    }

    private static float RaysAlpha(float x, float y)
    {
        const int rayCount = 12;
        float d = Mathf.Sqrt(x * x + y * y);
        if (d > 1f) return 0f;
        float angle = Mathf.Atan2(y, x);
        // Cosine lobes give soft-edged rays; raising the power narrows them.
        float lobes = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * rayCount * 0.5f)), 6f);
        float radial = Mathf.Clamp01(1f - d) * Mathf.Clamp01(d * 6f); // fade out at the rim and the very centre
        return lobes * radial;
    }

    private static Sprite Build(string name, int size, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };

        var pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float a = alpha((px - half) / half, (py - half) / half);
                pixels[py * size + px] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true); // no longer readable: frees the CPU copy

        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = name;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }
}
