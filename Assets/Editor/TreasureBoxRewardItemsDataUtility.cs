#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared lookup/creation helper for the single <see cref="TreasureBoxRewardItemsData"/> aggregator asset
/// that now holds every <see cref="TreasureBoxRewardItemData"/> entry, used by every editor tool that used
/// to create/find individual TreasureBoxRewardItemData .asset files. Mirrors ShopItemsDataUtility.cs.
/// </summary>
public static class TreasureBoxRewardItemsDataUtility
{
    public const string DatabaseAssetPath = "Assets/Resources/TreasureBoxRewardItemsData.asset";

    /// <summary>
    /// Finds the aggregator asset (by its well-known path first, then by type anywhere in the project),
    /// creating it at <see cref="DatabaseAssetPath"/> if none exists yet.
    /// </summary>
    public static TreasureBoxRewardItemsData GetOrCreateDatabase()
    {
        TreasureBoxRewardItemsData db = AssetDatabase.LoadAssetAtPath<TreasureBoxRewardItemsData>(DatabaseAssetPath);
        if (db != null) return db;

        string[] guids = AssetDatabase.FindAssets("t:TreasureBoxRewardItemsData");
        if (guids.Length > 0)
        {
            db = AssetDatabase.LoadAssetAtPath<TreasureBoxRewardItemsData>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (db != null) return db;
        }

        EnsureFolderExists(Path.GetDirectoryName(DatabaseAssetPath).Replace("\\", "/"));

        db = ScriptableObject.CreateInstance<TreasureBoxRewardItemsData>();
        AssetDatabase.CreateAsset(db, DatabaseAssetPath);
        AssetDatabase.SaveAssets();
        return db;
    }

    /// <summary>Marks the aggregator dirty and saves it — call after mutating its <c>categories</c>.</summary>
    public static void Save(TreasureBoxRewardItemsData db)
    {
        if (db == null) return;
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Every item across every category group, in group order.</summary>
    public static IEnumerable<TreasureBoxRewardItemData> AllItems(TreasureBoxRewardItemsData db)
    {
        if (db == null || db.categories == null) yield break;
        foreach (var group in db.categories)
        {
            if (group?.items == null) continue;
            foreach (var item in group.items)
            {
                if (item != null) yield return item;
            }
        }
    }

    /// <summary>The category group for <paramref name="category"/>, creating an empty one if none exists yet.</summary>
    public static TreasureBoxRewardCategoryGroup GetOrCreateGroup(TreasureBoxRewardItemsData db, ShopItemCategory category)
    {
        db.categories ??= new List<TreasureBoxRewardCategoryGroup>();

        foreach (var group in db.categories)
        {
            if (group != null && group.category == category) return group;
        }

        var newGroup = new TreasureBoxRewardCategoryGroup { category = category };
        db.categories.Add(newGroup);
        return newGroup;
    }

    /// <summary>
    /// Adds a newly created item to the group matching its own <see cref="TreasureBoxRewardItemData.itemCategory"/> —
    /// the group is the source of truth for category, so this is the one place an item should be inserted.
    /// </summary>
    public static void AddItem(TreasureBoxRewardItemsData db, TreasureBoxRewardItemData item)
    {
        if (db == null || item == null) return;
        GetOrCreateGroup(db, item.itemCategory).items.Add(item);
    }

    /// <summary>Finds an existing entry bound to this prefab (via itemPrefabRef), or null.</summary>
    public static TreasureBoxRewardItemData FindByPrefab(TreasureBoxRewardItemsData db, GameObject prefab)
    {
        if (db == null || prefab == null) return null;
        foreach (var item in AllItems(db))
        {
            if (item.itemPrefabRef != null &&
                item.itemPrefabRef.RuntimeKeyIsValid() && item.itemPrefabRef.editorAsset == prefab)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>Finds an existing entry by exact itemName match, or null.</summary>
    public static TreasureBoxRewardItemData FindByName(TreasureBoxRewardItemsData db, string itemName)
    {
        if (db == null || string.IsNullOrEmpty(itemName)) return null;
        foreach (var item in AllItems(db))
        {
            if (item.itemName == itemName) return item;
        }
        return null;
    }

    /// <summary>Finds an existing entry by exact itemID match, or null.</summary>
    public static TreasureBoxRewardItemData FindByID(TreasureBoxRewardItemsData db, string itemID)
    {
        if (db == null || string.IsNullOrEmpty(itemID)) return null;
        foreach (var item in AllItems(db))
        {
            if (item.itemID == itemID) return item;
        }
        return null;
    }

    public static void EnsureFolderExists(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath)) return;

        string[] folders = folderPath.Split('/');
        string currentPath = folders[0];
        for (int i = 1; i < folders.Length; i++)
        {
            string nextPath = currentPath + "/" + folders[i];
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, folders[i]);
            }
            currentPath = nextPath;
        }
    }
}
#endif
