#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// Guesses a <see cref="ShopItemCategory"/> and <see cref="ShopItemTier"/> from an asset's name and
/// price, for the bulk generators that create shop items from a folder of prefabs.
///
/// This is a starting point, not an authority — the hand-curated table in
/// <see cref="JannahGardenShopItemGenerator"/> is what the shipped catalogue actually uses. A
/// keyword guess only has to be right often enough to save the designer some clicking.
/// </summary>
public static class ShopItemClassifier
{
    /// <summary>
    /// Picks the collection an item belongs to. Order matters: water and light beat the generic
    /// plant/structure keywords, because "Golden Fountain of Light" is water before it is anything
    /// else and "Star Lantern" is light before it is decor.
    /// </summary>
    public static ShopItemCategory DetermineCategory(string prefabName)
    {
        string lower = (prefabName ?? string.Empty).ToLower();

        if (ContainsAny(lower, "fountain", "pool", "pond", "water", "river", "stream", "cascade",
                               "waterfall", "well", "channel", "lake", "spring"))
        {
            return ShopItemCategory.WaterOfGarden;
        }

        if (ContainsAny(lower, "lantern", "light", "lamp", "star", "crescent", "moon", "celestial",
                               "glow", "luminal", "rift", "aurora", "iridescent", "island", "isle",
                               "meadow", "floating"))
        {
            return ShopItemCategory.CelestialAndLight;
        }

        if (ContainsAny(lower, "tree", "bush", "flower", "grass", "reed", "cattail", "lily", "leaf",
                               "plant", "blossom", "rose", "tulip", "palm", "willow", "bonsai",
                               "petal", "vine", "garden"))
        {
            return ShopItemCategory.PlantsAndGardens;
        }

        if (ContainsAny(lower, "arch", "gate", "gateway", "bridge", "column", "pillar", "citadel",
                               "cottage", "house", "building", "tower", "wall", "dome", "portal",
                               "path", "stair"))
        {
            return ShopItemCategory.Architecture;
        }

        return ShopItemCategory.DecorAndSacredObjects;
    }

    /// <summary>
    /// Existing rarity keywords for the treasure-box reward tabs. Unchanged behaviour — these are
    /// inventory rarities, not shop collections.
    /// </summary>
    public static ShopItemCategory DetermineRewardTier(string prefabName)
    {
        string lower = (prefabName ?? string.Empty).ToLower();

        if (ContainsAny(lower, "diamond", "legendary", "exclusive")) return ShopItemCategory.Diamond;
        if (ContainsAny(lower, "platinum", "epic")) return ShopItemCategory.Platinum;
        if (ContainsAny(lower, "gold", "rare")) return ShopItemCategory.Gold;

        return ShopItemCategory.Silver;
    }

    /// <summary>
    /// Places an item in a quality band from what it costs. Tier is meant to be relative to its own
    /// collection, so a generated batch should be reviewed once the whole collection exists — these
    /// thresholds only spread a single batch sensibly across the four bands.
    /// </summary>
    public static ShopItemTier DetermineTier(int noorCoinCost)
    {
        if (noorCoinCost >= 500) return ShopItemTier.Tier4;
        if (noorCoinCost >= 350) return ShopItemTier.Tier3;
        if (noorCoinCost >= 225) return ShopItemTier.Tier2;
        return ShopItemTier.Tier1;
    }

    /// <summary>Flavour text matching the collection an item landed in.</summary>
    public static string GenerateDescription(string itemName, ShopItemCategory category)
    {
        switch (category)
        {
            case ShopItemCategory.PlantsAndGardens:
                return $"A beautiful green {itemName} to add vibrant life and natural beauty to your garden.";
            case ShopItemCategory.WaterOfGarden:
                return $"A tranquil {itemName} to bring flowing water and calm to your garden.";
            case ShopItemCategory.Architecture:
                return $"An elegant {itemName} structure to provide comfort and architectural beauty to your estate.";
            case ShopItemCategory.CelestialAndLight:
                return $"A radiant {itemName} to lift your garden with light and wonder.";
            case ShopItemCategory.DecorAndSacredObjects:
            default:
                return $"A unique {itemName} decoration to personalize and enhance the atmosphere of your garden.";
        }
    }

    static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (haystack.Contains(needle)) return true;
        }
        return false;
    }
}
#endif
