using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Round-trips Resources/Localization/ui_{locale}.json against a single translator-facing CSV (columns:
/// key, en, ar, bn, notes), so translating UI strings never requires opening Unity or touching JSON by
/// hand. Import overwrites all three ui_*.json files; Export regenerates the CSV from them (handy the
/// first time, and any time someone hand-edits a json file and you want to get back to one source of truth).
/// </summary>
public static class LocalizationCsvImporter
{
    private const string ResourceFolder = "Assets/Resources/Localization";
    private static readonly string[] Locales = { "en", "ar", "bn" };

    [MenuItem("Tools/Localization/Import CSV...")]
    public static void ImportCsv()
    {
        string path = EditorUtility.OpenFilePanel("Import Localization CSV", Application.dataPath, "csv");
        if (string.IsNullOrEmpty(path)) return;

        List<Dictionary<string, string>> rows = CsvUtil.ReadRows(File.ReadAllText(path, Encoding.UTF8));
        if (rows.Count == 0)
        {
            EditorUtility.DisplayDialog("Import Localization CSV", "That file had no data rows.", "OK");
            return;
        }

        Directory.CreateDirectory(ResourceFolder);

        var counts = new Dictionary<string, int>();
        foreach (string locale in Locales) counts[locale] = 0;

        foreach (string locale in Locales)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("    \"entries\": [");

            var lines = new List<string>();
            foreach (var row in rows)
            {
                if (!row.TryGetValue("key", out string key) || string.IsNullOrWhiteSpace(key)) continue;
                if (!row.TryGetValue(locale, out string value) || string.IsNullOrEmpty(value)) continue; // leave untranslated rows to fall back to English at runtime

                lines.Add($"        {{ \"key\": {JsonString(key)}, \"value\": {JsonString(value)} }}");
                counts[locale]++;
            }

            sb.Append(string.Join(",\n", lines));
            sb.AppendLine();
            sb.AppendLine("    ]");
            sb.Append("}");

            File.WriteAllText($"{ResourceFolder}/ui_{locale}.json", sb.ToString(), new UTF8Encoding(false));
        }

        AssetDatabase.Refresh();

        Debug.Log($"[LocalizationCsvImporter] Imported {rows.Count} row(s) from '{path}' -> " +
                  string.Join(", ", BuildSummary(counts)));

        EditorUtility.DisplayDialog("Import Localization CSV",
            $"Imported {rows.Count} row(s).\n" + string.Join("\n", BuildSummary(counts)), "OK");
    }

    [MenuItem("Tools/Localization/Export CSV...")]
    public static void ExportCsv()
    {
        string path = EditorUtility.SaveFilePanel("Export Localization CSV", Application.dataPath, "ui_strings", "csv");
        if (string.IsNullOrEmpty(path)) return;

        // Union of every key across all three tables, so a key present only in English (not yet
        // translated anywhere) still gets a row for translators to fill in.
        var orderedKeys = new List<string>();
        var seen = new HashSet<string>();
        var tables = new Dictionary<string, Dictionary<string, string>>();

        foreach (string locale in Locales)
        {
            var table = LoadTable(locale);
            tables[locale] = table;
            foreach (string key in table.Keys)
            {
                if (seen.Add(key)) orderedKeys.Add(key);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("key,en,ar,bn");
        foreach (string key in orderedKeys)
        {
            sb.Append(CsvUtil.Escape(key));
            foreach (string locale in Locales)
            {
                sb.Append(',');
                sb.Append(CsvUtil.Escape(tables[locale].TryGetValue(key, out string v) ? v : string.Empty));
            }
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"[LocalizationCsvImporter] Exported {orderedKeys.Count} key(s) to '{path}'.");
        EditorUtility.RevealInFinder(path);
    }

    private static Dictionary<string, string> LoadTable(string locale)
    {
        var table = new Dictionary<string, string>();
        string path = $"{ResourceFolder}/ui_{locale}.json";
        if (!File.Exists(path)) return table;

        LocalizationTableJson parsed = JsonUtility.FromJson<LocalizationTableJson>(File.ReadAllText(path, Encoding.UTF8));
        if (parsed?.entries == null) return table;

        foreach (var entry in parsed.entries)
        {
            if (!string.IsNullOrEmpty(entry.key)) table[entry.key] = entry.value ?? string.Empty;
        }
        return table;
    }

    private static List<string> BuildSummary(Dictionary<string, int> counts)
    {
        var lines = new List<string>();
        foreach (string locale in Locales) lines.Add($"{locale}: {counts[locale]} string(s)");
        return lines;
    }

    private static string JsonString(string s)
    {
        // JsonUtility has no writer API for this shape, so the importer builds JSON text directly —
        // this escapes exactly what JsonUtility.FromJson needs to read it back correctly.
        var sb = new StringBuilder("\"", s.Length + 2);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    [System.Serializable]
    private class LocalizationEntryJson
    {
        public string key;
        public string value;
    }

    [System.Serializable]
    private class LocalizationTableJson
    {
        public LocalizationEntryJson[] entries;
    }
}

/// <summary>Minimal RFC4180-ish CSV reader/writer — quoted fields, "" escaping, embedded commas/newlines.</summary>
internal static class CsvUtil
{
    public static List<Dictionary<string, string>> ReadRows(string csvText)
    {
        List<List<string>> records = ParseRecords(csvText);
        var rows = new List<Dictionary<string, string>>();
        if (records.Count == 0) return rows;

        List<string> header = records[0];
        for (int r = 1; r < records.Count; r++)
        {
            List<string> record = records[r];
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0])) continue; // blank line

            var row = new Dictionary<string, string>();
            for (int c = 0; c < header.Count; c++)
            {
                string columnName = header[c].Trim().ToLowerInvariant();
                row[columnName] = c < record.Count ? record[c] : string.Empty;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static List<List<string>> ParseRecords(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    public static string Escape(string value)
    {
        if (value == null) return string.Empty;
        bool needsQuotes = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
        string escaped = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{escaped}\"" : escaped;
    }
}
