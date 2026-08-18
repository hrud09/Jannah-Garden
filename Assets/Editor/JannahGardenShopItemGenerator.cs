using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Creates one <see cref="ShopItemData"/> per Meshy asset folder under
/// "Assets/3D Assets/Jannah Garden Assets" and points it at that folder's FBX.
///
/// The .asset files themselves are checked in, but a model's root GameObject only
/// gets its file ID when Unity imports the FBX, so <c>itemPrefabRef</c> cannot be
/// written outside the editor. Run this once after pulling the assets to bind them.
///
/// Safe to re-run. Name, description, category, tier and sort order are re-applied from
/// the table below; price, XP gate and icon are only written when the asset is first
/// created, so hand-tuning the economy in the inspector survives.
///
/// The table is grouped by <see cref="ShopItemCategory"/> and, inside each group, runs
/// Tier 1 to Tier 4. Tier is relative to the category: it tracks where an item sits in
/// its own collection's price ladder, not the shop-wide price range — the plant
/// collection tops out at 300 coins while the water collection tops out at 650, and
/// both still have a Tier 4.
/// </summary>
public static class JannahGardenShopItemGenerator
{
    const string AssetsRoot = "Assets/3D Assets/Jannah Garden Assets";
    const string OutputFolder = "Assets/Resources/Natural Placeable Shop Items";
    const string FbxFile = "Meshy_AI_model.fbx";
    const float PlacementTimerDuration = 360f;

    readonly struct Item
    {
        public readonly string Folder;              // asset folder name (a UUID)
        public readonly string Name;                // display name, unique
        public readonly ShopItemCategory Category;
        public readonly ShopItemTier Tier;
        public readonly int NoorCoinCost;
        public readonly int RequiredXPLevel;

        public Item(string folder, string name, ShopItemCategory category, ShopItemTier tier,
                    int noorCoinCost, int requiredXPLevel)
        {
            Folder = folder;
            Name = name;
            Category = category;
            Tier = tier;
            NoorCoinCost = noorCoinCost;
            RequiredXPLevel = requiredXPLevel;
        }

        public string AssetPath => $"{OutputFolder}/{Name.Replace(" ", "_")}_Data.asset";
    }

    static readonly Item[] Items =
    {
        // ── Category 1: Plants & Gardens ──────────────────────────────────
        new Item("019fd74b-cab9-717f-a7b7-809a21aeb81a", "Celestial Blossom Tree", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier1, 100, 1),
        new Item("019fd743-a857-7fc5-a17e-8cf68524ae1f", "Coconut Palm", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier1, 100, 1),
        new Item("019fd74b-9334-70ef-aab0-c92929f3bfde", "Luminous Weeping Willow", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier1, 150, 2),
        new Item("019fd742-a4b7-7fce-81a4-8b5fda8dcd11", "Moonlit Cactus Bloom", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier1, 150, 2),
        new Item("019fd751-ab9d-7182-9357-4d53dc2ff68b", "Ornate Bonsai Planter", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier1, 150, 2),
        new Item("019fd740-bc3f-7f84-b973-0441da9fb98c", "Petal Cascade", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier2, 200, 3),
        new Item("019fd74b-4b66-7198-87df-f86877fcc113", "Pink Rose Garden", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier2, 200, 3),
        new Item("019fd741-4df5-7f58-979d-34c4f23ef887", "Pomegranate Tree", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier2, 200, 3),
        new Item("019fd74a-e240-70df-a77e-f5c12d24f9ef", "Pomegranate Tree II", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier2, 200, 3),
        new Item("019fd74a-2cd7-70c7-b6a1-1ae3e4cd06dc", "Prismatic Bloom", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier3, 250, 4),
        new Item("019fd741-7de3-7f6b-abe9-62c5ddd38ca5", "Starflower Trellis", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier3, 250, 4),
        new Item("019fd743-0af6-7fb8-8c9a-e132bcf8cef6", "Tree of Light", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier3, 250, 4),
        new Item("019fd741-bc42-7f9c-a542-23cd902d6799", "Tulip Cluster", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier3, 250, 4),
        new Item("019fd743-764e-7ede-b743-9668b0e6c7ab", "Whimsical Apple Tree", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier4, 300, 5),
        new Item("019fd74b-1aa0-70e2-9086-1618e73d50a0", "Whimsical Floral Cascade", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier4, 300, 5),
        new Item("019fd742-cd08-7ec7-87c0-dc90890109ac", "Whispering Blossom Tree", ShopItemCategory.PlantsAndGardens, ShopItemTier.Tier4, 300, 5),

        // ── Category 2: Water of Garden ───────────────────────────────────
        new Item("019fd74a-60cd-7060-8a23-e30b39b79f33", "Lotus Pond", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier1, 150, 2),
        new Item("019fd744-4bb4-7ff0-b1b0-7690092c0126", "Ancient Stone Fountain", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier1, 250, 2),
        new Item("019fd749-e140-7155-8e56-fabf63718ee1", "Ancient Stone Pool", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier1, 250, 2),
        new Item("019fd742-7d6c-7fa3-82fd-dddac95b30a1", "Meandering River Island", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier2, 375, 4),
        new Item("019fd750-e0ff-71e7-9465-52e242f85963", "Golden Fountain of Light", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier2, 450, 4),
        new Item("019fd74a-864e-713e-a0da-054ccba49f8f", "Marble Water Channel", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier2, 450, 4),
        new Item("019fd751-df5f-7283-a69d-21ce0b39341f", "Prismatic Cascade", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier3, 525, 6),
        new Item("019fd74f-0822-7127-882f-0a86b12e50db", "River of Gold", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier3, 525, 6),
        new Item("019fd748-592c-70bc-bbd8-afc1405d16df", "Pineapple Crown Fountain", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier3, 550, 5),
        new Item("019fd751-3e70-71fc-9ae6-0115408b6d33", "Tiered Cascade", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier4, 600, 7),
        new Item("019fd742-4b48-7eb0-aea6-9601826b4185", "Whispering Well", ShopItemCategory.WaterOfGarden, ShopItemTier.Tier4, 650, 6),

        // ── Category 3: Architecture ──────────────────────────────────────
        new Item("019fd74c-3d9b-71a3-b3f3-2bf34d956ec8", "Arabic Script Archway", ShopItemCategory.Architecture, ShopItemTier.Tier1, 250, 2),
        new Item("019fd74f-aa4e-713a-82a8-74c3bc0eec60", "Arched Timber Bridge", ShopItemCategory.Architecture, ShopItemTier.Tier1, 250, 2),
        new Item("019fd752-448c-7299-9aa7-c7d261a155d0", "Floating Stone Path", ShopItemCategory.Architecture, ShopItemTier.Tier1, 300, 3),
        new Item("019fd750-afcf-7285-a4d1-f6c608e037ea", "Bismillah Portal Ring", ShopItemCategory.Architecture, ShopItemTier.Tier2, 350, 3),
        new Item("019fd752-9dea-71ac-983c-25a48ddc5d03", "Corinthian Column", ShopItemCategory.Architecture, ShopItemTier.Tier2, 350, 3),
        new Item("019fd755-2101-7327-85f8-093ed25da565", "Crystal Citadel", ShopItemCategory.Architecture, ShopItemTier.Tier2, 350, 3),
        new Item("019fd75a-6e2d-734c-95b1-73c0c5a1e96c", "Golden Archway of Light", ShopItemCategory.Architecture, ShopItemTier.Tier2, 350, 3),
        new Item("019fd754-37bc-71e2-85b4-ce7a16526388", "Golden Gateway to Paradise", ShopItemCategory.Architecture, ShopItemTier.Tier3, 450, 4),
        new Item("019fd752-18df-7230-9073-3541023b0cfa", "Ivy Clad Cottage", ShopItemCategory.Architecture, ShopItemTier.Tier3, 450, 4),
        new Item("019fd74f-e0b9-722e-954c-2463277b4b8a", "Muqarnas Calligraphy Arch", ShopItemCategory.Architecture, ShopItemTier.Tier3, 550, 5),
        new Item("019fd752-cb5c-71b2-9d5b-f10b344739ec", "Ropebound Bamboo Arch", ShopItemCategory.Architecture, ShopItemTier.Tier3, 550, 5),
        new Item("019fd750-8a50-71df-9dd6-ef95167026a5", "Ruby Gold Arched Gate", ShopItemCategory.Architecture, ShopItemTier.Tier3, 550, 5),
        new Item("019fd74f-706b-71ae-80a1-0dbf244f2162", "Turquoise Mosaic Arch", ShopItemCategory.Architecture, ShopItemTier.Tier4, 650, 6),
        new Item("019fd752-7040-72a1-b415-d57b2058a277", "Vine Wrapped Bamboo Arch", ShopItemCategory.Architecture, ShopItemTier.Tier4, 650, 6),

        // ── Category 4: Decor & Sacred Objects ────────────────────────────
        new Item("019fd75b-2808-7467-8085-10b81c90a994", "Arabic Calligraphy Stand", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier1, 150, 1),
        new Item("019fd754-0629-71de-a125-f877e6542a40", "Azure Mosaic Bench", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier1, 150, 1),
        new Item("019fd743-ef2c-7fd5-a669-b9334202d205", "Blue Mosaic Octagon", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier1, 150, 1),
        new Item("019fd74b-fc35-7190-8fa3-3d172ab2bad5", "Blue Star Mosaic Slab", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier1, 150, 1),
        new Item("019fd75b-9103-73e5-8b56-61c73bdee108", "Crimson Diamond Pedestal", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier2, 225, 2),
        new Item("019fd750-27df-7248-aba6-4d2d897609fe", "Eight Point Star Mosaic", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier2, 225, 2),
        new Item("019fd751-7507-720f-85f8-e780338df37b", "Emerald Velvet Cushion", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier2, 225, 2),
        new Item("019fd75b-cbd0-73f1-824c-cdf9640f52dd", "Floating Lotus Altar", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier3, 300, 3),
        new Item("019fd75a-9879-73a7-bcfc-cf491522314f", "Golden Quranic Panel", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier3, 300, 3),
        new Item("019fd755-d532-7223-92e7-dbad92e34f13", "Ornate Golden Censer", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier4, 450, 5),
        new Item("019fd75b-5c4b-73d8-b942-4956b951d703", "Ornate Golden Goblet", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier4, 450, 5),
        new Item("019fd751-1240-716c-b8bc-558ad26335b8", "Ornate Medallion Rug", ShopItemCategory.DecorAndSacredObjects, ShopItemTier.Tier4, 525, 6),

        // ── Category 5: Celestial & Light / Landscape ─────────────────────
        new Item("019fd75c-1e1b-74e2-8a88-d59f2daaa530", "Floating Meadow", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier1, 100, 1),
        new Item("019fd740-fb00-7f45-8883-6277e7b83fe7", "Isle of Spring", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier1, 100, 1),
        new Item("019fd755-497b-7200-b2fb-c1a9f43bc1a7", "Eight Pointed Star Lantern", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier1, 225, 2),
        new Item("019fd755-a65c-721b-8406-8b1a4b5239fe", "Golden Crescent Star", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier2, 300, 3),
        new Item("019fd754-8dfa-728f-b272-559d8c3f4a17", "Iridescent Trail", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier2, 375, 4),
        new Item("019fd75b-f6e8-73c0-9fb2-3b835b73a8c3", "Lantern Ascension", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier2, 375, 4),
        new Item("019fd742-0aae-7046-87c9-1b52e8d937e6", "Luminal Rift", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier2, 375, 4),
        new Item("019fd750-600a-715b-81ec-f8fc40a60e38", "Moonlit Stone Ring", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier3, 450, 5),
        new Item("019fd75a-df94-7474-929e-e37928161495", "Ornate Lantern Gallery", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier3, 450, 5),
        new Item("019fd755-7ddd-733b-bae0-c91e472c4657", "Runic Fire Ring", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier4, 525, 6),
        new Item("019fd754-f7ac-7315-8fa2-521e6487e994", "Star Lantern", ShopItemCategory.CelestialAndLight, ShopItemTier.Tier4, 600, 7),
    };

    [MenuItem("Tools/Shop/Generate Jannah Garden Shop Items")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            Debug.LogError($"[JannahGarden] Missing output folder \"{OutputFolder}\".");
            return;
        }

        int created = 0, bound = 0, alreadyBound = 0;
        var problems = new List<string>();

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        var remoteGroup = AddressableItemAuthoring.GetOrCreateRemoteGroup(settings);

        try
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < Items.Length; i++)
                {
                    var item = Items[i];
                    EditorUtility.DisplayProgressBar("Generating shop items", item.Name,
                                                     (float)i / Items.Length);

                    string fbxPath = $"{AssetsRoot}/{item.Folder}/{FbxFile}";
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                    if (model == null) problems.Add($"missing model: {fbxPath}");

                    var data = AssetDatabase.LoadAssetAtPath<ShopItemData>(item.AssetPath);
                    bool isNew = data == null;
                    if (isNew) data = ScriptableObject.CreateInstance<ShopItemData>();

                    // Authoritative for every run — these come from the table.
                    data.itemName = item.Name;
                    data.itemDescription = Describe(item);
                    data.itemCategory = item.Category;
                    data.itemTier = item.Tier;
                    data.acquisitionType = ShopAcquisitionType.NoorCoins;

                    // The table is already in shop order, so the row index is the sort order.
                    // Kept authoritative so re-grouping the table re-orders the shop.
                    data.sortOrder = (i + 1) * 10;

                    if (string.IsNullOrEmpty(data.itemID))
                        data.itemID = System.Guid.NewGuid().ToString("N");

                    // Only seeded on creation so inspector tuning is not clobbered.
                    if (isNew)
                    {
                        data.noorCoinCost = item.NoorCoinCost;
                        data.requiredXPLevel = item.RequiredXPLevel;
                        data.placementTimerDuration = PlacementTimerDuration;
                    }

                    if (model != null)
                    {
                        bool alreadyCorrect = data.itemPrefabRef.RuntimeKeyIsValid() && data.itemPrefabRef.editorAsset == model;
                        if (alreadyCorrect)
                        {
                            alreadyBound++;
                        }
                        else
                        {
                            AddressableItemAuthoring.AssignPrefab(data.itemPrefabRef, model, settings, remoteGroup);
                            bound++;
                        }
                    }

                    if (isNew)
                    {
                        AssetDatabase.CreateAsset(data, item.AssetPath);
                        created++;
                    }
                    else
                    {
                        EditorUtility.SetDirty(data);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[JannahGarden] Shop items: {Items.Length} processed, {created} created, " +
                  $"{bound} newly bound to a model, {alreadyBound} already bound.");

        if (problems.Count > 0)
            Debug.LogWarning($"[JannahGarden] {problems.Count} problem(s):\n  " +
                             string.Join("\n  ", problems));
    }

    static string Describe(Item item)
    {
        switch (item.Category)
        {
            case ShopItemCategory.PlantsAndGardens:
                return $"A beautiful {item.Name} to add vibrant life and natural beauty to your garden.";
            case ShopItemCategory.WaterOfGarden:
                return $"A tranquil {item.Name} to bring flowing water and calm to your garden.";
            case ShopItemCategory.Architecture:
                return $"An elegant {item.Name} structure to provide comfort and architectural beauty to your estate.";
            case ShopItemCategory.DecorAndSacredObjects:
                return $"An ornate {item.Name} to adorn your garden with craftsmanship and quiet reverence.";
            case ShopItemCategory.CelestialAndLight:
                return $"A radiant {item.Name} to lift your garden with light and wonder.";
            default:
                return $"A unique {item.Name} decoration to personalize and enhance the atmosphere of your garden.";
        }
    }
}
