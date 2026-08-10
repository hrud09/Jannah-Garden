using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates one <see cref="ShopItemData"/> per Meshy asset folder under
/// "Assets/3D Assets/Jannah Garden Assets" and points it at that folder's FBX.
///
/// The .asset files themselves are checked in, but a model's root GameObject only
/// gets its file ID when Unity imports the FBX, so <c>itemPrefab</c> cannot be
/// written outside the editor. Run this once after pulling the assets to bind them.
///
/// Safe to re-run. Name, description and category are re-applied from the table
/// below; price, XP gate, sort order and icon are only written when the asset is
/// first created, so hand-tuning in the inspector survives.
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
        public readonly int NoorCoinCost;
        public readonly int RequiredXPLevel;
        public readonly int SortOrder;

        public Item(string folder, string name, ShopItemCategory category,
                    int noorCoinCost, int requiredXPLevel, int sortOrder)
        {
            Folder = folder;
            Name = name;
            Category = category;
            NoorCoinCost = noorCoinCost;
            RequiredXPLevel = requiredXPLevel;
            SortOrder = sortOrder;
        }

        public string AssetPath => $"{OutputFolder}/{Name.Replace(" ", "_")}_Data.asset";
    }

    static readonly Item[] Items =
    {
        new Item("019fd74b-cab9-717f-a7b7-809a21aeb81a", "Celestial Blossom Tree", ShopItemCategory.Plants, 100, 1, 10),
        new Item("019fd743-a857-7fc5-a17e-8cf68524ae1f", "Coconut Palm", ShopItemCategory.Plants, 100, 1, 20),
        new Item("019fd75c-1e1b-74e2-8a88-d59f2daaa530", "Floating Meadow", ShopItemCategory.Plants, 100, 1, 30),
        new Item("019fd740-fb00-7f45-8883-6277e7b83fe7", "Isle of Spring", ShopItemCategory.Plants, 100, 1, 40),
        new Item("019fd74a-60cd-7060-8a23-e30b39b79f33", "Lotus Pond", ShopItemCategory.Plants, 150, 2, 50),
        new Item("019fd74b-9334-70ef-aab0-c92929f3bfde", "Luminous Weeping Willow", ShopItemCategory.Plants, 150, 2, 60),
        new Item("019fd742-a4b7-7fce-81a4-8b5fda8dcd11", "Moonlit Cactus Bloom", ShopItemCategory.Plants, 150, 2, 70),
        new Item("019fd751-ab9d-7182-9357-4d53dc2ff68b", "Ornate Bonsai Planter", ShopItemCategory.Plants, 150, 2, 80),
        new Item("019fd740-bc3f-7f84-b973-0441da9fb98c", "Petal Cascade", ShopItemCategory.Plants, 200, 3, 90),
        new Item("019fd74b-4b66-7198-87df-f86877fcc113", "Pink Rose Garden", ShopItemCategory.Plants, 200, 3, 100),
        new Item("019fd741-4df5-7f58-979d-34c4f23ef887", "Pomegranate Tree", ShopItemCategory.Plants, 200, 3, 110),
        new Item("019fd74a-e240-70df-a77e-f5c12d24f9ef", "Pomegranate Tree II", ShopItemCategory.Plants, 200, 3, 120),
        new Item("019fd74a-2cd7-70c7-b6a1-1ae3e4cd06dc", "Prismatic Bloom", ShopItemCategory.Plants, 250, 4, 130),
        new Item("019fd741-7de3-7f6b-abe9-62c5ddd38ca5", "Starflower Trellis", ShopItemCategory.Plants, 250, 4, 140),
        new Item("019fd743-0af6-7fb8-8c9a-e132bcf8cef6", "Tree of Light", ShopItemCategory.Plants, 250, 4, 150),
        new Item("019fd741-bc42-7f9c-a542-23cd902d6799", "Tulip Cluster", ShopItemCategory.Plants, 250, 4, 160),
        new Item("019fd743-764e-7ede-b743-9668b0e6c7ab", "Whimsical Apple Tree", ShopItemCategory.Plants, 300, 5, 170),
        new Item("019fd74b-1aa0-70e2-9086-1618e73d50a0", "Whimsical Floral Cascade", ShopItemCategory.Plants, 300, 5, 180),
        new Item("019fd742-cd08-7ec7-87c0-dc90890109ac", "Whispering Blossom Tree", ShopItemCategory.Plants, 300, 5, 190),
        new Item("019fd744-4bb4-7ff0-b1b0-7690092c0126", "Ancient Stone Fountain", ShopItemCategory.Buildings, 250, 2, 200),
        new Item("019fd749-e140-7155-8e56-fabf63718ee1", "Ancient Stone Pool", ShopItemCategory.Buildings, 250, 2, 210),
        new Item("019fd74c-3d9b-71a3-b3f3-2bf34d956ec8", "Arabic Script Archway", ShopItemCategory.Buildings, 250, 2, 220),
        new Item("019fd74f-aa4e-713a-82a8-74c3bc0eec60", "Arched Timber Bridge", ShopItemCategory.Buildings, 250, 2, 230),
        new Item("019fd750-afcf-7285-a4d1-f6c608e037ea", "Bismillah Portal Ring", ShopItemCategory.Buildings, 350, 3, 240),
        new Item("019fd752-9dea-71ac-983c-25a48ddc5d03", "Corinthian Column", ShopItemCategory.Buildings, 350, 3, 250),
        new Item("019fd755-2101-7327-85f8-093ed25da565", "Crystal Citadel", ShopItemCategory.Buildings, 350, 3, 260),
        new Item("019fd75a-6e2d-734c-95b1-73c0c5a1e96c", "Golden Archway of Light", ShopItemCategory.Buildings, 350, 3, 270),
        new Item("019fd750-e0ff-71e7-9465-52e242f85963", "Golden Fountain of Light", ShopItemCategory.Buildings, 450, 4, 280),
        new Item("019fd754-37bc-71e2-85b4-ce7a16526388", "Golden Gateway to Paradise", ShopItemCategory.Buildings, 450, 4, 290),
        new Item("019fd752-18df-7230-9073-3541023b0cfa", "Ivy Clad Cottage", ShopItemCategory.Buildings, 450, 4, 300),
        new Item("019fd74a-864e-713e-a0da-054ccba49f8f", "Marble Water Channel", ShopItemCategory.Buildings, 450, 4, 310),
        new Item("019fd74f-e0b9-722e-954c-2463277b4b8a", "Muqarnas Calligraphy Arch", ShopItemCategory.Buildings, 550, 5, 320),
        new Item("019fd748-592c-70bc-bbd8-afc1405d16df", "Pineapple Crown Fountain", ShopItemCategory.Buildings, 550, 5, 330),
        new Item("019fd752-cb5c-71b2-9d5b-f10b344739ec", "Ropebound Bamboo Arch", ShopItemCategory.Buildings, 550, 5, 340),
        new Item("019fd750-8a50-71df-9dd6-ef95167026a5", "Ruby Gold Arched Gate", ShopItemCategory.Buildings, 550, 5, 350),
        new Item("019fd74f-706b-71ae-80a1-0dbf244f2162", "Turquoise Mosaic Arch", ShopItemCategory.Buildings, 650, 6, 360),
        new Item("019fd752-7040-72a1-b415-d57b2058a277", "Vine Wrapped Bamboo Arch", ShopItemCategory.Buildings, 650, 6, 370),
        new Item("019fd742-4b48-7eb0-aea6-9601826b4185", "Whispering Well", ShopItemCategory.Buildings, 650, 6, 380),
        new Item("019fd75b-2808-7467-8085-10b81c90a994", "Arabic Calligraphy Stand", ShopItemCategory.Decorations, 150, 1, 390),
        new Item("019fd754-0629-71de-a125-f877e6542a40", "Azure Mosaic Bench", ShopItemCategory.Decorations, 150, 1, 400),
        new Item("019fd743-ef2c-7fd5-a669-b9334202d205", "Blue Mosaic Octagon", ShopItemCategory.Decorations, 150, 1, 410),
        new Item("019fd74b-fc35-7190-8fa3-3d172ab2bad5", "Blue Star Mosaic Slab", ShopItemCategory.Decorations, 150, 1, 420),
        new Item("019fd75b-9103-73e5-8b56-61c73bdee108", "Crimson Diamond Pedestal", ShopItemCategory.Decorations, 225, 2, 430),
        new Item("019fd750-27df-7248-aba6-4d2d897609fe", "Eight Point Star Mosaic", ShopItemCategory.Decorations, 225, 2, 440),
        new Item("019fd755-497b-7200-b2fb-c1a9f43bc1a7", "Eight Pointed Star Lantern", ShopItemCategory.Decorations, 225, 2, 450),
        new Item("019fd751-7507-720f-85f8-e780338df37b", "Emerald Velvet Cushion", ShopItemCategory.Decorations, 225, 2, 460),
        new Item("019fd75b-cbd0-73f1-824c-cdf9640f52dd", "Floating Lotus Altar", ShopItemCategory.Decorations, 300, 3, 470),
        new Item("019fd752-448c-7299-9aa7-c7d261a155d0", "Floating Stone Path", ShopItemCategory.Decorations, 300, 3, 480),
        new Item("019fd755-a65c-721b-8406-8b1a4b5239fe", "Golden Crescent Star", ShopItemCategory.Decorations, 300, 3, 490),
        new Item("019fd75a-9879-73a7-bcfc-cf491522314f", "Golden Quranic Panel", ShopItemCategory.Decorations, 300, 3, 500),
        new Item("019fd754-8dfa-728f-b272-559d8c3f4a17", "Iridescent Trail", ShopItemCategory.Decorations, 375, 4, 510),
        new Item("019fd75b-f6e8-73c0-9fb2-3b835b73a8c3", "Lantern Ascension", ShopItemCategory.Decorations, 375, 4, 520),
        new Item("019fd742-0aae-7046-87c9-1b52e8d937e6", "Luminal Rift", ShopItemCategory.Decorations, 375, 4, 530),
        new Item("019fd742-7d6c-7fa3-82fd-dddac95b30a1", "Meandering River Island", ShopItemCategory.Decorations, 375, 4, 540),
        new Item("019fd750-600a-715b-81ec-f8fc40a60e38", "Moonlit Stone Ring", ShopItemCategory.Decorations, 450, 5, 550),
        new Item("019fd755-d532-7223-92e7-dbad92e34f13", "Ornate Golden Censer", ShopItemCategory.Decorations, 450, 5, 560),
        new Item("019fd75b-5c4b-73d8-b942-4956b951d703", "Ornate Golden Goblet", ShopItemCategory.Decorations, 450, 5, 570),
        new Item("019fd75a-df94-7474-929e-e37928161495", "Ornate Lantern Gallery", ShopItemCategory.Decorations, 450, 5, 580),
        new Item("019fd751-1240-716c-b8bc-558ad26335b8", "Ornate Medallion Rug", ShopItemCategory.Decorations, 525, 6, 590),
        new Item("019fd751-df5f-7283-a69d-21ce0b39341f", "Prismatic Cascade", ShopItemCategory.Decorations, 525, 6, 600),
        new Item("019fd74f-0822-7127-882f-0a86b12e50db", "River of Gold", ShopItemCategory.Decorations, 525, 6, 610),
        new Item("019fd755-7ddd-733b-bae0-c91e472c4657", "Runic Fire Ring", ShopItemCategory.Decorations, 525, 6, 620),
        new Item("019fd754-f7ac-7315-8fa2-521e6487e994", "Star Lantern", ShopItemCategory.Decorations, 600, 7, 630),
        new Item("019fd751-3e70-71fc-9ae6-0115408b6d33", "Tiered Cascade", ShopItemCategory.Decorations, 600, 7, 640),
    };

    [MenuItem("Tools/Generate Jannah Garden Shop Items")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            Debug.LogError($"[JannahGarden] Missing output folder \"{OutputFolder}\".");
            return;
        }

        int created = 0, bound = 0, alreadyBound = 0;
        var problems = new List<string>();

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

                    // Authoritative for every run � these come from the table.
                    data.itemName = item.Name;
                    data.itemDescription = Describe(item);
                    data.itemCategory = item.Category;
                    data.acquisitionType = ShopAcquisitionType.NoorCoins;

                    if (string.IsNullOrEmpty(data.itemID))
                        data.itemID = System.Guid.NewGuid().ToString("N");

                    // Only seeded on creation so inspector tuning is not clobbered.
                    if (isNew)
                    {
                        data.noorCoinCost = item.NoorCoinCost;
                        data.requiredXPLevel = item.RequiredXPLevel;
                        data.sortOrder = item.SortOrder;
                        data.placementTimerDuration = PlacementTimerDuration;
                    }

                    if (model != null)
                    {
                        if (data.itemPrefab == model) alreadyBound++;
                        else { data.itemPrefab = model; bound++; }
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
            case ShopItemCategory.Plants:
                return $"A beautiful {item.Name} to add vibrant life and natural beauty to your garden.";
            case ShopItemCategory.Buildings:
                return $"An elegant {item.Name} structure to provide comfort and architectural beauty to your estate.";
            default:
                return $"A unique {item.Name} decoration to personalize and enhance the atmosphere of your garden.";
        }
    }
}
