using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sizes shop items against the player.
///
/// <para><b>Audit</b> measures every placeable shop item's prefab at its authored (fully grown) scale and
/// writes <c>Logs/ShopItemSizes.csv</c>. Bounds are taken from MeshFilter/SkinnedMeshRenderer shared
/// meshes folded through the prefab's local transforms, so nothing is instantiated — loading every
/// Meshy FBX into the scene is slow enough to stall the editor.</para>
///
/// <para><b>Apply</b> rescales each prefab's <see cref="PlaceableItem.itemGFX"/> (or the root when there
/// is none) so its height matches <see cref="TargetHeights"/>. PlaceableItem reads the GFX scale as its
/// full-grown size in Awake, and ItemFootprint measures the prefab asset, so the growth animation, ground
/// alignment and grid footprint all follow automatically.</para>
///
/// <para>Heights are in metres against a ~1.8 m player whose eyes (the play-mode camera) sit ~1.65 m off
/// the ground. <b>Measure Eye Height</b> re-checks that in play mode.</para>
/// </summary>
public static class ShopItemSizeTool
{
    const string DatabasePath = "Assets/Resources/ShopItemsData.asset";
    const string ReportPath = "Logs/ShopItemSizes.csv";

    /// <summary>Target full-grown size (m) per shop item name. Positive = height; negative = longest
    /// horizontal span (for flat things — rugs, ponds, paths — where height says nothing about size).
    /// Items not listed are left alone.</summary>
    public static readonly Dictionary<string, float> TargetHeights = new Dictionary<string, float>
    {
        // Plants & gardens
        { "Coconut Palm", 6.0f },
        { "Moonlit Cactus Bloom", 1.6f },
        { "Ornate Bonsai Planter", 1.0f },
        { "Luminous Weeping Willow", 6.0f },
        { "Celestial Blossom Tree", 4.5f },
        { "Isle of Spring", -3.0f },
        { "Petal Cascade", 1.5f },
        { "Pink Rose Garden", -3.0f },  // a bed, sized by footprint
        { "Pomegranate Tree", 3.5f },
        { "Pomegranate Tree II", 3.5f },
        { "Tulip Cluster", 0.6f },
        { "Starflower Trellis", 2.4f },
        { "Prismatic Bloom", 1.2f },
        { "Tree of Light", 5.0f },
        { "Whimsical Apple Tree", 4.0f },
        { "Whimsical Floral Cascade", 1.5f },
        { "Whispering Blossom Tree", 4.5f },

        // Water
        { "Blue Mosaic Octagon", -3.0f },
        { "Lotus Pond", -3.5f },
        { "Ancient Stone Pool", -3.5f },
        { "Ancient Stone Fountain", 2.2f },
        { "Eight Point Star Mosaic", -3.0f },
        { "Marble Water Channel", -4.0f },
        { "Meandering River Island", -5.0f },
        { "Golden Fountain of Light", 2.5f },
        { "Pineapple Crown Fountain", 2.0f },
        { "Prismatic Cascade", 2.5f },
        { "River of Gold", -5.0f },
        { "Whispering Well", -2.0f },   // a low ring, not a roofed well
        { "Tiered Cascade", 2.2f },

        // Architecture — arches clear the player's head with room to spare
        { "Floating Stone Path", -3.0f },
        { "Arched Timber Bridge", -5.0f },
        { "Arabic Script Archway", 3.2f },
        { "Corinthian Column", 3.5f },
        { "Bismillah Portal Ring", 3.2f },
        { "Crystal Citadel", 5.0f },
        { "Golden Archway of Light", 3.2f },
        { "Ropebound Bamboo Arch", 2.8f },
        { "Ivy Clad Cottage", 4.5f },
        { "Muqarnas Calligraphy Arch", 3.4f },
        { "Ruby Gold Arched Gate", 3.2f },
        { "Golden Gateway to Paradise", 4.0f },
        { "Vine Wrapped Bamboo Arch", 2.8f },
        { "Turquoise Mosaic Arch", 3.2f },

        // Decor
        { "Blue Star Mosaic Slab", -1.5f },
        { "Azure Mosaic Bench", -1.6f },
        { "Arabic Calligraphy Stand", 1.2f },
        { "Emerald Velvet Cushion", -0.7f },
        { "Crimson Diamond Pedestal", 1.0f },
        { "Golden Quranic Panel", 1.6f },
        { "Ornate Medallion Rug", -2.5f },
        { "Ornate Golden Censer", 0.6f },
        { "Ornate Golden Goblet", 0.5f },

        // Lights & wonders
        { "Floating Meadow", -3.0f },
        { "Eight Pointed Star Lantern", 1.2f },
        { "Iridescent Trail", -3.0f },
        { "Lantern Ascension", 3.0f },
        { "Golden Crescent Star", 2.5f },
        { "Luminal Rift", -3.0f },      // flat disc
        { "Floating Lotus Altar", 1.5f },
        { "Moonlit Stone Ring", -3.0f },
        { "Ornate Lantern Gallery", 2.5f },
        { "Runic Fire Ring", -2.0f },
        { "Star Lantern", 1.0f },
    };

    /// <summary>The measured value a target compares against: height, or the longest horizontal span.</summary>
    static float Current(Bounds b, float target) => target >= 0f ? b.size.y : Mathf.Max(b.size.x, b.size.z);

    struct Measured { public ShopItemData item; public ShopItemCategory cat; public GameObject prefab; public Bounds bounds; public bool ok; }

    [MenuItem("Jannah/Shop/Audit Item Sizes")]
    public static void Audit()
    {
        var list = MeasureAll();
        var sb = new StringBuilder("category,name,prefab,height,width,depth,gfxScale,target,factor\n");
        foreach (var m in list)
        {
            float target = TargetHeights.TryGetValue(m.item.itemName, out float t) ? t : 0f;
            Transform scaled = ScaleTarget(m.prefab);
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0},\"{1}\",{2},{3:F2},{4:F2},{5:F2},{6:F3},{7:F2},{8:F3}\n",
                m.cat, m.item.itemName, m.prefab != null ? m.prefab.name : "MISSING",
                m.bounds.size.y, m.bounds.size.x, m.bounds.size.z,
                scaled != null ? scaled.localScale.x : 0f, target,
                target != 0f && Current(m.bounds, target) > 0f ? Mathf.Abs(target) / Current(m.bounds, target) : 0f);
        }
        File.WriteAllText(ReportPath, sb.ToString());
        Debug.Log($"[ShopItemSizeTool] Measured {list.Count} items → {Path.GetFullPath(ReportPath)}");
    }

    [MenuItem("Jannah/Shop/Apply Player-Height Sizes")]
    public static void Apply() => ApplyBatch(int.MaxValue);

    /// <summary>Rescales at most <paramref name="max"/> prefabs and returns how many it changed.
    /// Idempotent — already-sized prefabs are skipped — so calling it until it returns 0 does them all
    /// in short steps (handy over MCP, where one long editor call can time the bridge out).</summary>
    public static int ApplyBatch(int max)
    {
        int changed = 0;
        var done = new HashSet<GameObject>();
        foreach (var m in MeasureAll())
        {
            if (changed >= max) break;
            if (!m.ok || m.prefab == null || done.Contains(m.prefab)) continue;
            if (!TargetHeights.TryGetValue(m.item.itemName, out float target) || target == 0f) continue;
            float current = Current(m.bounds, target);
            if (current <= 0f) continue;
            float factor = Mathf.Abs(target) / current;
            if (Mathf.Abs(factor - 1f) < 0.02f) continue;
            done.Add(m.prefab);

            string path = AssetDatabase.GetAssetPath(m.prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform t = ScaleTarget(root);
                t.localScale *= factor;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                changed++;
                Debug.Log($"[ShopItemSizeTool] {m.item.itemName}: {current:F2} m → {Mathf.Abs(target):F2} m {(target > 0 ? "tall" : "across")} (x{factor:F3})");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[ShopItemSizeTool] Rescaled {changed} prefabs.");
        Audit();
        return changed;
    }

    [MenuItem("Jannah/Shop/Measure Eye Height (Play Mode)")]
    public static void MeasureEyeHeight()
    {
        var cam = Camera.main;
        if (!Application.isPlaying || cam == null) { Debug.LogWarning("[ShopItemSizeTool] Enter play mode first."); return; }
        Vector3 eye = cam.transform.position;
        var cc = Object.FindFirstObjectByType<CharacterController>();
        float feet = cc != null ? cc.transform.TransformPoint(cc.center).y - cc.height * 0.5f * cc.transform.lossyScale.y : float.NaN;
        string ground = Physics.Raycast(eye, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore)
            ? (eye.y - hit.point.y).ToString("F2") : "no hit";
        Debug.Log($"[ShopItemSizeTool] Eye height: {eye.y - feet:F2} m above CC feet, {ground} m above ground hit (camera y {eye.y:F2}).");
    }

    static Transform ScaleTarget(GameObject prefab)
    {
        if (prefab == null) return null;
        var pi = prefab.GetComponent<PlaceableItem>();
        return pi != null && pi.itemGFX != null ? pi.itemGFX : prefab.transform;
    }

    static List<Measured> MeasureAll()
    {
        var db = AssetDatabase.LoadAssetAtPath<ShopItemsData>(DatabasePath);
        var result = new List<Measured>();
        if (db == null) { Debug.LogError("[ShopItemSizeTool] No database at " + DatabasePath); return result; }
        foreach (var group in db.categories)
        foreach (var item in group.items)
        {
            if (item == null || !item.IsPlaceable) continue;
            string guid = item.itemPrefabRef.AssetGUID;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var m = new Measured { item = item, cat = group.category, prefab = prefab };
            if (prefab != null) m.ok = TryMeshBounds(prefab.transform, out m.bounds);
            result.Add(m);
        }
        return result;
    }

    /// <summary>Renderer-mesh bounds as the prefab spawns in the world (root scale included, root
    /// position removed), so a root-scaled prefab measures at the size the player actually sees.</summary>
    static bool TryMeshBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool has = false;
        Matrix4x4 toRoot = Matrix4x4.Translate(-root.position);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); mesh = mf != null ? mf.sharedMesh : null; }
            else if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            if (mesh == null) continue;
            if (r.GetComponentInParent<TimerHoldingPlank>(true) != null) continue; // signboard, not the item
            Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
            Bounds b = mesh.bounds;
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!has) { bounds = new Bounds(p, Vector3.zero); has = true; } else bounds.Encapsulate(p);
            }
        }
        return has;
    }
}
