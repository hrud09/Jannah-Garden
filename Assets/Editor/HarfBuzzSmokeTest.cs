using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Throwaway validation for the HarfBuzz P/Invoke binding (Assets/Scripts/Localization/HarfBuzzNative.cs
/// + HarfBuzzShaper.cs) — confirms the real project code shapes correctly inside the Editor, not just
/// the standalone C test used to validate the shaping engine itself. Safe to delete once ShapedTextGraphic
/// lands and has its own tests.
/// </summary>
public static class HarfBuzzSmokeTest
{
    [MenuItem("Tools/HarfBuzz/Smoke Test (Bengali)")]
    public static void RunBengali()
    {
        Run("খলীলুল্লাহ", AppLocale.bn);
    }

    [MenuItem("Tools/HarfBuzz/Smoke Test (Arabic)")]
    public static void RunArabic()
    {
        Run("بسم الله", AppLocale.ar);
    }

    public static string Run(string text, AppLocale locale)
    {
        List<ShapedGlyph> glyphs = HarfBuzzShaper.Shape(text, locale);
        uint upem = HarfBuzzShaper.UnitsPerEm(locale);

        var sb = new StringBuilder();
        sb.AppendLine($"[HarfBuzzSmokeTest] locale={locale} upem={upem} input=\"{text}\" -> {glyphs.Count} glyphs");
        bool found243 = false;
        foreach (ShapedGlyph g in glyphs)
        {
            sb.AppendLine($"  glyph id={g.GlyphId} cluster={g.Cluster} xAdvance={g.XAdvance} xOffset={g.XOffset} yOffset={g.YOffset}");
            if (g.GlyphId == 243) found243 = true;
        }
        if (locale == AppLocale.bn)
            sb.AppendLine($"Glyph 243 (known ল্ল half-form ligature) present: {found243}");

        string log = sb.ToString();
        Debug.Log(log);
        return log;
    }
}
