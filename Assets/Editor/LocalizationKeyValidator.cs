using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Cross-checks every localization key actually referenced by the game (C# call sites, LocalizedText
/// components on prefabs/scenes, and the per-item/per-tier keys generated at runtime from ShopItemData /
/// TreasureBoxRewardItemData / TreasureBoxData assets) against the four ui_{locale}.json tables.
///
/// Reports two kinds of drift: a referenced key missing from one or more locales (shows up in-game as the
/// raw key string, or falls back to English — see LocalizationManager.Get/GetOrDefault), and a key sitting
/// in the JSON tables that nothing references any more (the kind of dead weight the old tutorial.* keys
/// were before this pass retired them). Run after adding new UI text or new shop/treasure-box items.
/// </summary>
public static class LocalizationKeyValidator
{
    private const string ResourceFolder = "Assets/Resources/Localization";
    private static readonly string[] Locales = { "en", "ar", "bn", "ur" };

    // Any key-shaped string literal ("word.word" or deeper, lowercase/underscore segments) anywhere in a
    // .cs file — deliberately not anchored to ".Get("/".GetOrDefault(" so it also catches keys passed
    // through a ternary (Get(cond ? "a" : "b")) or through a project-specific wrapper like
    // ShopTaxonomy/XPTaskTaxonomy/ItemRarityTaxonomy's private Localized(key, fallback) helpers.
    private static readonly Regex LiteralKeyRegex =
        new Regex("\"([a-z][a-z0-9_]*(?:\\.[a-z0-9_]+){1,4})\"", RegexOptions.Compiled);

    // LocalizedText's `key` field as serialized directly on a prefab/scene's own MonoBehaviour block.
    private static readonly Regex SerializedKeyRegex =
        new Regex("^\\s*key:\\s*(.+)\\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    // The same field when it's overridden on a *nested* PrefabInstance (e.g. a Settings Option UI instance
    // inside Settings Panel Holder.prefab) — Unity serializes that as a PropertyModification with
    // "propertyPath: key" and the value on the following line, not as a plain "key: ..." line.
    private static readonly Regex PrefabModificationKeyRegex =
        new Regex("propertyPath: key\\r?\\n\\s*value: (.+)\\r?$", RegexOptions.Compiled | RegexOptions.Multiline);

    [MenuItem("Tools/Localization/Validate Keys")]
    public static void Validate()
    {
        Dictionary<string, HashSet<string>> tables = Locales.ToDictionary(l => l, LoadTableKeys);

        // Bounds the broad LiteralKeyRegex (any "word.word" string) to strings that plausibly ARE
        // localization keys, so an unrelated dotted literal elsewhere in the codebase (a product id like
        // "com.amal.jannahgarden.coins_500", a resource path, ...) can't masquerade as a missing key.
        var knownPrefixes = new HashSet<string>(tables["en"].Select(k => k.Split('.')[0]));
        knownPrefixes.Add("item"); // generated keys only, never present in ui_en.json itself

        var usedKeys = new HashSet<string>();
        var codeKeySources = new Dictionary<string, string>(); // key -> first file that referenced it

        foreach (string path in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            foreach (Match m in LiteralKeyRegex.Matches(text))
            {
                string key = m.Groups[1].Value;
                if (!knownPrefixes.Contains(key.Split('.')[0])) continue;

                usedKeys.Add(key);
                if (!codeKeySources.ContainsKey(key)) codeKeySources[key] = path;
            }
        }

        var prefabKeySources = new Dictionary<string, List<string>>(); // key -> files using it via LocalizedText
        foreach (string path in Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories)
                     .Concat(Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories)))
        {
            string text = File.ReadAllText(path);
            if (!text.Contains("LocalizedText")) continue; // cheap pre-filter before the per-line regex

            foreach (Match m in SerializedKeyRegex.Matches(text))
            {
                string key = m.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(key)) continue;
                usedKeys.Add(key);
                if (!prefabKeySources.TryGetValue(key, out var list)) prefabKeySources[key] = list = new List<string>();
                if (!list.Contains(path)) list.Add(path);
            }

            foreach (Match m in PrefabModificationKeyRegex.Matches(text))
            {
                string key = m.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(key)) continue;
                usedKeys.Add(key);
                if (!prefabKeySources.TryGetValue(key, out var list)) prefabKeySources[key] = list = new List<string>();
                if (!list.Contains(path)) list.Add(path);
            }
        }

        // Per-item and per-tier keys are built at runtime as $"item.{itemID}.name" etc (see
        // ShopItemData.LocalizedName / TreasureBoxTier's Localized* helpers) — a literal-string regex over
        // the .cs files can't see those, so generate the concrete keys straight from the data assets.
        int generatedItemKeys = 0;
        foreach (string typeName in new[] { "ShopItemData", "TreasureBoxRewardItemData" })
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{typeName}"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                var itemIdField = so?.GetType().GetField("itemID");
                string itemId = itemIdField?.GetValue(so) as string;
                if (string.IsNullOrEmpty(itemId)) continue;

                usedKeys.Add($"item.{itemId}.name");
                usedKeys.Add($"item.{itemId}.desc");
                generatedItemKeys += 2;
            }
        }

        foreach (string tier in new[] { "silver", "gold", "platinum", "diamond" })
        {
            usedKeys.Add($"treasurebox.tier_name.{tier}");
            usedKeys.Add($"treasurebox.tier_short.{tier}");
        }

        // ── Report: referenced keys missing from a locale ──────────────────────
        // item.*.name/.desc and treasurebox.tier_name.*/tier_short.* deliberately have no "en" entry —
        // GetOrDefault falls back to the asset's own itemName/itemDescription/tierDisplayName field for
        // English instead of duplicating it into ui_en.json (see ShopItemData.LocalizedName). Flagging
        // those as "missing [en]" would just be 164+ false positives, so skip English for that shape.
        bool hasCodeLevelEnglishFallback(string key) =>
            (key.StartsWith("item.") && (key.EndsWith(".name") || key.EndsWith(".desc")))
            || key.StartsWith("treasurebox.tier_name.")
            || key.StartsWith("treasurebox.tier_short.");

        var report = new System.Text.StringBuilder();
        int missingCount = 0;
        foreach (string key in usedKeys.OrderBy(k => k))
        {
            var localesToCheck = hasCodeLevelEnglishFallback(key) ? Locales.Where(l => l != "en") : Locales.AsEnumerable();
            var missingLocales = localesToCheck.Where(l => !tables[l].Contains(key)).ToList();
            if (missingLocales.Count == 0) continue;

            missingCount++;
            string source = codeKeySources.TryGetValue(key, out string codePath) ? codePath
                : prefabKeySources.TryGetValue(key, out var prefabPaths) ? string.Join(", ", prefabPaths)
                : "(generated from item/tier data)";
            report.AppendLine($"MISSING [{string.Join(",", missingLocales)}]  {key}   — used in {source}");
        }

        // ── Report: keys defined but never referenced ──────────────────────────
        var dead = tables["en"].Where(k => !usedKeys.Contains(k)).OrderBy(k => k).ToList();

        report.AppendLine();
        report.AppendLine($"Referenced keys: {usedKeys.Count} (code literals: {codeKeySources.Count}, " +
                           $"prefab/scene LocalizedText: {prefabKeySources.Count}, generated item/tier: {generatedItemKeys + 8})");
        report.AppendLine($"Missing translations: {missingCount}");
        report.AppendLine($"Unreferenced keys in ui_en.json: {dead.Count}");
        if (dead.Count > 0) report.AppendLine("  " + string.Join("\n  ", dead));

        Debug.Log($"[LocalizationKeyValidator] {report}");
    }

    private static HashSet<string> LoadTableKeys(string locale)
    {
        var keys = new HashSet<string>();
        string path = $"{ResourceFolder}/ui_{locale}.json";
        if (!File.Exists(path)) return keys;

        // Same shape as LocalizationManager/LocalizationCsvImporter's LocalizationTable — duplicated here
        // (rather than reused) since those are internal to their own files and this is an Editor-only tool.
        var parsed = JsonUtility.FromJson<Table>(File.ReadAllText(path));
        if (parsed?.entries == null) return keys;

        foreach (var entry in parsed.entries)
        {
            if (!string.IsNullOrEmpty(entry.key)) keys.Add(entry.key);
        }
        return keys;
    }

    [System.Serializable]
    private class Entry
    {
        public string key;
        public string value;
    }

    [System.Serializable]
    private class Table
    {
        public Entry[] entries;
    }
}
