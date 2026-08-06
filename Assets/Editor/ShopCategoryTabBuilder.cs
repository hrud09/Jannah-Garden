#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Keeps the category tab bar in the In-Game Shop prefab in step with
/// <see cref="ShopTaxonomy.ShopCategories"/>.
///
/// The tab buttons and their scrolling panels are authored by hand in the prefab, so adding a
/// collection to the enum leaves the shop with items that have nowhere to spawn. Rather than
/// building the UI from scratch, this clones an existing shop tab — button, panel, viewport,
/// content, layout components and all — so a new collection inherits whatever styling the shop
/// already has, then wires it into the manager's <c>categoryTabs</c> list.
///
/// Safe to re-run: categories that already have a tab are left untouched.
/// </summary>
public static class ShopCategoryTabBuilder
{
    const string ShopPrefabPath = "Assets/Prefabs/UI/In-Game Shop.prefab";

    [MenuItem("Tools/Shop/Rebuild Category Tabs")]
    public static void RebuildTabs()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ShopPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[ShopTabs] Could not open \"{ShopPrefabPath}\".");
            return;
        }

        try
        {
            var manager = root.GetComponentInChildren<InGameShopManager>(true);
            if (manager == null)
            {
                Debug.LogError($"[ShopTabs] No InGameShopManager inside \"{ShopPrefabPath}\".");
                return;
            }

            if (manager.categoryTabs == null) manager.categoryTabs = new List<CategoryTab>();

            // A fully wired shop tab to copy the layout from. The Noor Coins tab is skipped as a
            // template — it is a coin storefront, not an item grid.
            CategoryTab template = manager.categoryTabs.FirstOrDefault(
                t => t != null && t.tabButton != null && t.categoryPanel != null && t.contentParent != null
                     && t.category != ShopItemCategory.NoorCoins
                     && ShopTaxonomy.IsShopCategory(t.category));

            if (template == null)
            {
                Debug.LogError("[ShopTabs] No existing shop tab to use as a template — at least one "
                    + "category tab must have its Tab Button, Category Panel and Content Parent set.");
                return;
            }

            var added = new List<ShopItemCategory>();

            foreach (ShopItemCategory category in ShopTaxonomy.ShopCategories)
            {
                if (manager.categoryTabs.Any(t => t != null && t.category == category)) continue;

                CategoryTab tab = CloneTab(template, category);
                if (tab == null) continue;

                // Keep the new collection ahead of the Noor Coins tab, which reads as the last stop.
                int coinsIndex = manager.categoryTabs.FindIndex(
                    t => t != null && t.category == ShopItemCategory.NoorCoins);

                if (coinsIndex >= 0) manager.categoryTabs.Insert(coinsIndex, tab);
                else manager.categoryTabs.Add(tab);

                added.Add(category);
            }

            RelabelTabs(manager);

            if (added.Count > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, ShopPrefabPath);
                AssetDatabase.SaveAssets();
            }

            Report(manager, added);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Repoints the manager's <c>shopItemDatas</c> at every ShopItemData under Resources, in shop
    /// order. Regenerating the item assets gives them fresh GUIDs, which leaves the hand-authored
    /// list in the prefab pointing at deleted assets and the shop spawning nothing — run this after
    /// any bulk regeneration.
    /// </summary>
    [MenuItem("Tools/Shop/Refresh Shop Item List")]
    public static void RefreshShopItemList()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ShopPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[ShopTabs] Could not open \"{ShopPrefabPath}\".");
            return;
        }

        try
        {
            var manager = root.GetComponentInChildren<InGameShopManager>(true);
            if (manager == null)
            {
                Debug.LogError($"[ShopTabs] No InGameShopManager inside \"{ShopPrefabPath}\".");
                return;
            }

            // Resources holds both the placeable catalogue and the coin/ad offers.
            ShopItemData[] all = AssetDatabase.FindAssets("t:ShopItemData")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/Resources/"))
                .Select(AssetDatabase.LoadAssetAtPath<ShopItemData>)
                .Where(d => d != null)
                .OrderBy(d => CategoryOrder(d.itemCategory))
                .ThenBy(d => (int)d.itemTier)
                .ThenBy(d => d.sortOrder)
                .ThenBy(d => d.itemName)
                .ToArray();

            int before = manager.shopItemDatas != null ? manager.shopItemDatas.Length : 0;
            int missing = manager.shopItemDatas != null ? manager.shopItemDatas.Count(d => d == null) : 0;

            manager.shopItemDatas = all;

            PrefabUtility.SaveAsPrefabAsset(root, ShopPrefabPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ShopTabs] Shop item list: {before} entries ({missing} broken) → {all.Length}.\n  "
                + string.Join("\n  ", all.GroupBy(d => d.itemCategory)
                    .Select(g => $"{ShopTaxonomy.GetCategoryLongName(g.Key)}: {g.Count()}")));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Position of a category in the tab bar, so the item list matches the tab order.</summary>
    static int CategoryOrder(ShopItemCategory category)
    {
        int index = System.Array.IndexOf(ShopTaxonomy.ShopCategories, category);
        if (index >= 0) return index;
        return category == ShopItemCategory.NoorCoins ? ShopTaxonomy.ShopCategories.Length : int.MaxValue;
    }

    /// <summary>
    /// Duplicates the template's button and panel in place, empties the copy of any leftover item
    /// cards, and returns the wired-up entry.
    /// </summary>
    static CategoryTab CloneTab(CategoryTab template, ShopItemCategory category)
    {
        string suffix = category.ToString();

        // The path from the panel down to its content holder, so the same slot can be found in the copy.
        string contentPath = GetRelativePath(template.categoryPanel.transform, template.contentParent);
        if (contentPath == null)
        {
            Debug.LogError($"[ShopTabs] The template tab's Content Parent is not inside its Category "
                + $"Panel, so the {suffix} tab cannot be cloned. Fix the {template.category} tab first.");
            return null;
        }

        GameObject buttonCopy = Object.Instantiate(template.tabButton.gameObject,
                                                  template.tabButton.transform.parent);
        buttonCopy.name = $"Category Tab - {suffix}";
        buttonCopy.transform.SetSiblingIndex(template.tabButton.transform.GetSiblingIndex() + 1);

        GameObject panelCopy = Object.Instantiate(template.categoryPanel,
                                                  template.categoryPanel.transform.parent);
        panelCopy.name = $"Category Panel - {suffix}";

        Transform contentCopy = panelCopy.transform.Find(contentPath);
        if (contentCopy == null)
        {
            Debug.LogError($"[ShopTabs] Lost the content holder while cloning the {suffix} tab.");
            Object.DestroyImmediate(buttonCopy);
            Object.DestroyImmediate(panelCopy);
            return null;
        }

        // Item cards are spawned at runtime; anything the template was carrying is not ours.
        for (int i = contentCopy.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(contentCopy.GetChild(i).gameObject);
        }

        // The manager drives visibility, but a cloned panel should not start on top of the others.
        panelCopy.SetActive(false);

        var button = buttonCopy.GetComponent<Button>();
        if (button != null) button.onClick.RemoveAllListeners();

        return new CategoryTab
        {
            category = category,
            tabButton = button,
            categoryPanel = panelCopy,
            contentParent = contentCopy
        };
    }

    /// <summary>Writes the current display names onto the tab labels so the prefab reads correctly
    /// in the editor, not just at runtime.</summary>
    static void RelabelTabs(InGameShopManager manager)
    {
        foreach (CategoryTab tab in manager.categoryTabs)
        {
            if (tab == null || tab.tabButton == null) continue;

            var label = tab.tabButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = ShopTaxonomy.GetCategoryName(tab.category);
        }
    }

    /// <summary>Slash-separated path from <paramref name="ancestor"/> down to
    /// <paramref name="descendant"/>, or null when they are unrelated.</summary>
    static string GetRelativePath(Transform ancestor, Transform descendant)
    {
        var parts = new List<string>();

        for (Transform t = descendant; t != null; t = t.parent)
        {
            if (t == ancestor)
            {
                parts.Reverse();
                return string.Join("/", parts);
            }
            parts.Add(t.name);
        }

        return null;
    }

    static void Report(InGameShopManager manager, List<ShopItemCategory> added)
    {
        var sb = new StringBuilder("[ShopTabs] Category tabs:\n");

        foreach (ShopItemCategory category in ShopTaxonomy.ShopCategories
                     .Concat(new[] { ShopItemCategory.NoorCoins }))
        {
            CategoryTab tab = manager.categoryTabs.FirstOrDefault(t => t != null && t.category == category);
            string state = tab == null ? "MISSING"
                         : tab.contentParent == null ? "no content parent"
                         : added.Contains(category) ? "added" : "ok";

            sb.AppendLine($"  {ShopTaxonomy.GetCategoryLongName(category)} — {state}");
        }

        if (added.Count > 0)
        {
            sb.AppendLine($"Saved \"{ShopPrefabPath}\". Position and restyle the new tab(s) in the "
                        + "prefab — they are exact copies of an existing tab.");
            Debug.Log(sb.ToString());
        }
        else
        {
            sb.AppendLine("Nothing to add.");
            Debug.Log(sb.ToString());
        }
    }
}
#endif
