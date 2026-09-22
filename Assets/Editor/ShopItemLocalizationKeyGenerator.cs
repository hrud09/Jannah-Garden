using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scans every ShopItemData and TreasureBoxRewardItemData asset and exports a starter CSV
/// (key, en, ar, bn, ur) pre-filled with each item's current English itemName/itemDescription, so
/// translating the shop's ~90 items is just filling in three spreadsheet columns.
///
/// Keys follow "item.{itemID}.name" / "item.{itemID}.desc" — assets without an itemID are skipped and
/// listed in the console; run Tools/Shop/Assign Shop Item IDs (ShopItemIdAssigner) first if you see any.
///
/// This CSV uses the same 5-column shape as the main UI strings CSV, but is exported separately since
/// LocalizationCsvImporter.ImportCsv() overwrites ui_*.json wholesale. Merge its rows into your master
/// CSV before importing, rather than importing this file on its own.
/// </summary>
public static class ShopItemLocalizationKeyGenerator
{
    [MenuItem("Tools/Localization/Export Shop & Treasure Box Item Strings CSV...")]
    public static void ExportItemStringsCsv()
    {
        string path = EditorUtility.SaveFilePanel("Export Item Strings CSV", Application.dataPath, "item_strings", "csv");
        if (string.IsNullOrEmpty(path)) return;

        var sb = new StringBuilder();
        sb.AppendLine("key,en,ar,bn,ur");

        int rowCount = 0;
        var skipped = new List<string>();

        rowCount += AppendShopItemRows(sb, skipped);

        rowCount += AppendRows<TreasureBoxRewardItemData>(sb, skipped,
            asset => asset.itemID, asset => asset.itemName, asset => asset.itemDescription);

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

        Debug.Log($"[ShopItemLocalizationKeyGenerator] Exported {rowCount} row(s) to '{path}'." +
                  (skipped.Count > 0 ? $" Skipped {skipped.Count} asset(s) with no itemID: {string.Join(", ", skipped)}" : string.Empty));

        EditorUtility.RevealInFinder(path);
    }

    // ShopItemData entries live inside the single ShopItemsData aggregator asset instead of as their own
    // .asset files, so they need their own loop rather than the generic AssetDatabase.FindAssets<T> path.
    private static int AppendShopItemRows(StringBuilder sb, List<string> skipped)
    {
        ShopItemsData database = ShopItemsDataUtility.GetOrCreateDatabase();
        int count = 0;

        foreach (var asset in ShopItemsDataUtility.AllItems(database))
        {
            if (asset == null) continue;

            if (string.IsNullOrEmpty(asset.itemID))
            {
                skipped.Add($"{ShopItemsDataUtility.DatabaseAssetPath} :: {asset.itemName}");
                continue;
            }

            AppendRow(sb, $"item.{asset.itemID}.name", asset.itemName);
            AppendRow(sb, $"item.{asset.itemID}.desc", asset.itemDescription);
            count += 2;
        }

        return count;
    }

    private static int AppendRows<T>(
        StringBuilder sb,
        List<string> skipped,
        System.Func<T, string> getId,
        System.Func<T, string> getName,
        System.Func<T, string> getDescription) where T : Object
    {
        int count = 0;
        string typeName = typeof(T).Name;
        string[] guids = AssetDatabase.FindAssets($"t:{typeName}");

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null) continue;

            string id = getId(asset);
            if (string.IsNullOrEmpty(id))
            {
                skipped.Add(assetPath);
                continue;
            }

            AppendRow(sb, $"item.{id}.name", getName(asset));
            AppendRow(sb, $"item.{id}.desc", getDescription(asset));
            count += 2;
        }

        return count;
    }

    private static void AppendRow(StringBuilder sb, string key, string english)
    {
        sb.Append(CsvUtil.Escape(key));
        sb.Append(',');
        sb.Append(CsvUtil.Escape(english ?? string.Empty));
        sb.Append(","); // ar
        sb.Append(","); // bn
        sb.Append(","); // ur
        sb.AppendLine();
    }
}
