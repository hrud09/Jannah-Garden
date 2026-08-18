using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch maintenance for the shop item prefabs.
///
/// Reports renderer bounds for every shop item and, on request, offsets the mesh child so its
/// bounds rest on y=0 — a placed item then sits on the ground instead of sinking into it.
///
/// The mesh object this operates on is whatever <see cref="PlaceableItem.itemGFX"/> points at,
/// which is the LOD0 child directly under the prefab root.
///
/// NOTE ON THE GROWTH ANIMATION: <see cref="PlaceableItem.SetScaleMultiplier"/> drives
/// itemGFX.localScale from 0.2 up to 1. Scaling a transform does not scale its localPosition,
/// so once a pivot offset is applied here the mesh will hover during growth — at 0.2 scale the
/// geometry shrinks toward the mesh object's own origin, which now sits a full offset above the
/// ground. Fixing that needs SetScaleMultiplier to scale the offset too; see the report output.
/// </summary>
public static class ShopItemPrefabFixer
{
    private const string PrefabFolder = "Assets/Prefabs/Shop Items";

    /// <summary>Offsets smaller than this aren't worth a prefab rewrite.</summary>
    private const float PivotEpsilon = 0.0001f;

    /// <summary>A mesh counts as "lengthy in Y" when its height dominates its footprint by this factor.</summary>
    private const float LengthyYRatio = 1.25f;

    [MenuItem("Tools/Shop/Prefab Fixer/1. Report Bounds (no changes)", priority = 0)]
    private static void MenuReport() => Run(dryRun: true, tallOnly: false);

    [MenuItem("Tools/Shop/Prefab Fixer/2. Drop Pivot To Bottom (all)", priority = 1)]
    private static void MenuPivotAll() => Run(dryRun: false, tallOnly: false);

    [MenuItem("Tools/Shop/Prefab Fixer/3. Drop Pivot To Bottom (Y-lengthy only)", priority = 2)]
    private static void MenuPivotTall() => Run(dryRun: false, tallOnly: true);

    private static void Run(bool dryRun, bool tallOnly)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        if (guids.Length == 0)
        {
            Debug.LogError($"[ShopItemPrefabFixer] No prefabs found under {PrefabFolder}");
            return;
        }

        var log = new StringBuilder();
        int pivoted = 0, lengthyY = 0, alreadyOk = 0, failed = 0;

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                EditorUtility.DisplayProgressBar("Shop Item Prefab Fixer", name, (float)i / guids.Length);

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var placeable = root.GetComponent<PlaceableItem>();
                    if (placeable == null || placeable.itemGFX == null)
                    {
                        log.AppendLine($"SKIP  {name}: PlaceableItem missing or itemGFX unassigned.");
                        failed++;
                        continue;
                    }

                    Transform gfx = placeable.itemGFX;
                    if (gfx.parent != root.transform)
                    {
                        log.AppendLine($"SKIP  {name}: itemGFX is not a direct child of the root.");
                        failed++;
                        continue;
                    }

                    // Bounds in ROOT space — the root is the ground contact point, so root-space
                    // y=0 is exactly where we want the bottom of the geometry to land.
                    if (!TryGetBounds(gfx, root.transform, out Bounds b))
                    {
                        log.AppendLine($"SKIP  {name}: no readable mesh bounds under itemGFX.");
                        failed++;
                        continue;
                    }

                    float footprint = Mathf.Max(b.size.x, b.size.z);
                    bool isTall = footprint > 0f && (b.size.y / footprint) >= LengthyYRatio;
                    if (isTall) lengthyY++;

                    float drop = -b.min.y;                       // shift up so the bottom lands on y=0
                    bool needs = Mathf.Abs(drop) > PivotEpsilon;
                    if (!needs) alreadyOk++;

                    log.AppendLine(
                        $"{(isTall ? "TALL " : "     ")} {name,-32} " +
                        $"size=({b.size.x:F3}, {b.size.y:F3}, {b.size.z:F3}) " +
                        $"minY={b.min.y:F4}  {(needs ? $"drop={drop:F4}" : "pivot OK")}");

                    bool inScope = !tallOnly || isTall;
                    if (!dryRun && needs && inScope)
                    {
                        gfx.localPosition += new Vector3(0f, drop, 0f);
                        pivoted++;

                        var lod = root.GetComponent<LODGroup>();
                        if (lod != null) lod.RecalculateBounds();

                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (!dryRun) AssetDatabase.SaveAssets();

        Debug.Log(
            $"[ShopItemPrefabFixer] {(dryRun ? "REPORT (nothing written)" : "APPLIED")}" +
            $"{(tallOnly ? " — Y-lengthy only" : "")}\n" +
            $"Prefabs scanned : {guids.Length}\n" +
            $"Lengthy in Y    : {lengthyY}\n" +
            $"Pivot already OK: {alreadyOk}\n" +
            $"Pivots dropped  : {pivoted}\n" +
            $"Skipped/failed  : {failed}\n\n" +
            log.ToString());
    }

    /// <summary>
    /// Combined renderer bounds under <paramref name="gfx"/>, expressed in <paramref name="space"/>'s
    /// local coordinates. Mesh-local corners are transformed individually so the -90° X rotation on
    /// the mesh object is accounted for properly.
    /// </summary>
    private static bool TryGetBounds(Transform gfx, Transform space, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        Matrix4x4 toSpace = space.worldToLocalMatrix;

        foreach (var mf in gfx.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (!mf.TryGetComponent<Renderer>(out _)) continue;      // invisible geometry shouldn't set bounds
            Accumulate(mf.sharedMesh.bounds, toSpace * mf.transform.localToWorldMatrix, ref bounds, ref any);
        }

        foreach (var sm in gfx.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (sm.sharedMesh == null) continue;
            Accumulate(sm.sharedMesh.bounds, toSpace * sm.transform.localToWorldMatrix, ref bounds, ref any);
        }

        return any;
    }

    private static void Accumulate(Bounds local, Matrix4x4 m, ref Bounds acc, ref bool any)
    {
        Vector3 c = local.center, e = local.extents;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                c.x + (((i & 1) == 0) ? -e.x : e.x),
                c.y + (((i & 2) == 0) ? -e.y : e.y),
                c.z + (((i & 4) == 0) ? -e.z : e.z));

            Vector3 p = m.MultiplyPoint3x4(corner);

            if (!any) { acc = new Bounds(p, Vector3.zero); any = true; }
            else acc.Encapsulate(p);
        }
    }
}
