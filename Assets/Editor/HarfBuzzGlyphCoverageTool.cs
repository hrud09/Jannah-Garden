using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ensures a TMP_FontAsset's atlas actually contains every glyph HarfBuzz's shaping can emit for this
/// game's Bengali/Arabic content — including glyphs reachable only via GSUB substitution (conjuncts,
/// half-forms, contextual marks), which have no Unicode codepoint and so are invisible to TMP's public,
/// character-driven population API (<see cref="TMP_FontAsset.TryAddCharacters(string,bool)"/> and
/// friends).
///
/// The only glyph-index-driven population path is <c>TMP_FontAsset.TryAddGlyphInternal</c> — the same
/// method TMP's own dynamic/static baking calls internally — but it's <c>internal</c>, so this reaches
/// it via reflection rather than duplicating (and risking drifting from) TMP's own packing logic.
/// </summary>
public static class HarfBuzzGlyphCoverageTool
{
    private static MethodInfo _tryAddGlyphInternal;

    private static MethodInfo TryAddGlyphInternalMethod =>
        _tryAddGlyphInternal ??= typeof(TMP_FontAsset).GetMethod("TryAddGlyphInternal", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>Attempts to add one glyph (by glyph index, not Unicode) to the font asset's atlas.
    /// Returns true if the glyph is now present with real rasterized data.</summary>
    public static bool TryAddGlyphByIndex(TMP_FontAsset fontAsset, uint glyphIndex, out string error)
    {
        error = null;
        MethodInfo method = TryAddGlyphInternalMethod;
        if (method == null)
        {
            error = "TMP_FontAsset.TryAddGlyphInternal not found via reflection — TMP internals may have changed.";
            return false;
        }

        object[] args = { glyphIndex, null };
        bool added = (bool)method.Invoke(fontAsset, args);
        if (!added)
        {
            error = $"TryAddGlyphInternal returned false for glyph {glyphIndex} (atlas full, or glyph index invalid for this face).";
            return false;
        }
        return true;
    }

    [MenuItem("Tools/HarfBuzz/Test Add Missing Glyphs (Bengali)")]
    public static void TestAddMissingBengaliGlyphs()
    {
        string fontPath = "Assets/Font/Noto_Kufi_Arabic,Noto_Sans_Arabic,Noto_Sans_Bengali/Noto_Sans_Bengali/static/NotoSansBengali-Regular Dynamic SDF.asset";
        var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);

        uint[] missing = { 273, 316 };
        foreach (uint glyphIndex in missing)
        {
            bool alreadyPresent = fontAsset.glyphLookupTable.ContainsKey(glyphIndex);
            if (alreadyPresent)
            {
                Debug.Log($"[HarfBuzzGlyphCoverageTool] Glyph {glyphIndex} already present.");
                continue;
            }

            bool ok = TryAddGlyphByIndex(fontAsset, glyphIndex, out string error);
            Debug.Log(ok
                ? $"[HarfBuzzGlyphCoverageTool] Added glyph {glyphIndex} successfully."
                : $"[HarfBuzzGlyphCoverageTool] Failed to add glyph {glyphIndex}: {error}");
        }

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
    }

    // Mirrors LocalizationManager's internal LocalizationEntry/LocalizationTable shape (same field
    // names, since JsonUtility matches by name) — that runtime type is `internal` and not visible to
    // this Editor assembly, so this is a local stand-in rather than a second source of truth.
    [Serializable] private class UiEntry { public string key; public string value; }
    [Serializable] private class UiTable { public UiEntry[] entries; }

    private const string FontPathBengali = "Assets/Font/Noto_Kufi_Arabic,Noto_Sans_Arabic,Noto_Sans_Bengali/Noto_Sans_Bengali/static/NotoSansBengali-Regular Dynamic SDF.asset";
    private const string FontPathArabic = "Assets/Font/Noto_Kufi_Arabic,Noto_Sans_Arabic,Noto_Sans_Bengali/Noto_Sans_Arabic/static/NotoSansArabic-Regular SDF.asset";

    /// <summary>Every string this game can actually display for one locale: quiz content
    /// (category/questionText/options) plus the UI string table.</summary>
    private static List<string> CollectContentStrings(string localeSuffix)
    {
        var strings = new List<string>();

        TextAsset questionsAsset = Resources.Load<TextAsset>($"questions_{localeSuffix}");
        if (questionsAsset != null)
        {
            var list = JsonUtility.FromJson<QuestionList>(questionsAsset.text);
            if (list?.questions != null)
            {
                foreach (QuestionData q in list.questions)
                {
                    if (!string.IsNullOrEmpty(q.category)) strings.Add(q.category);
                    if (!string.IsNullOrEmpty(q.questionText)) strings.Add(q.questionText);
                    if (q.options != null)
                        foreach (string opt in q.options)
                            if (!string.IsNullOrEmpty(opt)) strings.Add(opt);
                }
            }
        }
        else
        {
            Debug.LogWarning($"[HarfBuzzGlyphCoverageTool] No questions_{localeSuffix} asset found.");
        }

        TextAsset uiAsset = Resources.Load<TextAsset>($"Localization/ui_{localeSuffix}");
        if (uiAsset != null)
        {
            var table = JsonUtility.FromJson<UiTable>(uiAsset.text);
            if (table?.entries != null)
                foreach (UiEntry e in table.entries)
                    if (!string.IsNullOrEmpty(e.value)) strings.Add(e.value);
        }
        else
        {
            Debug.LogWarning($"[HarfBuzzGlyphCoverageTool] No Localization/ui_{localeSuffix} asset found.");
        }

        return strings;
    }

    /// <summary>Shapes every collected string for <paramref name="locale"/>, collects the full distinct
    /// glyph-index set HarfBuzz emits, and adds whatever isn't already in the font asset's atlas.</summary>
    private static string ScanAndFillCoverage(TMP_FontAsset fontAsset, AppLocale locale, string localeSuffix)
    {
        List<string> strings = CollectContentStrings(localeSuffix);
        var neededGlyphs = new HashSet<uint>();

        foreach (string s in strings)
        {
            List<ShapedGlyph> shaped = HarfBuzzShaper.Shape(s, locale);
            foreach (ShapedGlyph g in shaped) neededGlyphs.Add(g.GlyphId);
        }

        int alreadyPresent = 0, added = 0, failed = 0;
        var failures = new List<string>();
        foreach (uint glyphIndex in neededGlyphs)
        {
            if (fontAsset.glyphLookupTable.ContainsKey(glyphIndex)) { alreadyPresent++; continue; }

            if (TryAddGlyphByIndex(fontAsset, glyphIndex, out string error)) added++;
            else { failed++; failures.Add(error); }
        }

        EditorUtility.SetDirty(fontAsset);
        string summary = $"{fontAsset.name} ({locale}): scanned {strings.Count} strings, " +
                          $"{neededGlyphs.Count} distinct glyphs needed — {alreadyPresent} already present, {added} added, {failed} failed.";
        if (failures.Count > 0) summary += "\n  " + string.Join("\n  ", failures);
        return summary;
    }

    [MenuItem("Tools/HarfBuzz/Scan And Fill Glyph Coverage (All Content)")]
    private static void ScanAndFillAllCoverageMenuItem() => ScanAndFillAllCoverage();

    public static string ScanAndFillAllCoverage()
    {
        var report = new System.Text.StringBuilder();
        var bengali = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPathBengali);
        var arabic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPathArabic);

        if (bengali == null) report.AppendLine($"ERROR: could not load {FontPathBengali}");
        else report.AppendLine(ScanAndFillCoverage(bengali, AppLocale.bn, "bn"));

        if (arabic == null) report.AppendLine($"ERROR: could not load {FontPathArabic}");
        else
        {
            // Arabic and Urdu content share the same font face (HarfBuzzShaper maps ur -> the Arabic
            // face too), so both get scanned into the same asset even though Urdu isn't a supported
            // locale yet (LocalizationManager.SupportedLocales) — the data files already exist and
            // cost nothing extra to cover now.
            report.AppendLine(ScanAndFillCoverage(arabic, AppLocale.ar, "ar"));
            report.AppendLine(ScanAndFillCoverage(arabic, AppLocale.ur, "ur"));
        }

        AssetDatabase.SaveAssets();
        string result = report.ToString();
        Debug.Log("[HarfBuzzGlyphCoverageTool] " + result);
        return result;
    }
}
