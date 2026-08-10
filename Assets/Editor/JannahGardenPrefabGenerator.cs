using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds one placeable shop prefab per Meshy asset under
/// "Assets/3D Assets/Jannah Garden Assets", into "Assets/Prefabs/Shop Items".
///
/// The shape mirrors the hand-built BroadleafTree_01_Green:
///
///   &lt;Item Name&gt;                 Transform, LODGroup, PlaceableItem
///     Timer Area                 nested instance of Prefabs/UI/Timer Area
///     LOD0                       Transform, MeshFilter, MeshRenderer, MeshCollider
///
/// The Meshy models are single-mesh with no authored LODs, so the LODGroup gets
/// one level that culls at the same screen height as the reference's last level.
/// Where a model does carry several mesh nodes they all become siblings under the
/// root and share that level.
///
/// Materials come from whatever the FBX resolves to, which is the OmniShade
/// material once <see cref="JannahGardenMaterialGenerator"/> has run. Generate
/// runs it first if any material is missing, so the two steps can be invoked in
/// either order.
///
/// Safe to re-run: prefabs are written back to the same paths, keeping their
/// GUIDs and any ShopItemData that already points at them.
/// </summary>
public static class JannahGardenPrefabGenerator
{
    const string AssetsRoot = "Assets/3D Assets/Jannah Garden Assets";
    const string FbxFile = "Meshy_AI_model.fbx";
    const string OutputFolder = "Assets/Prefabs/Shop Items";
    const string TimerAreaPath = "Assets/Prefabs/UI/Timer Area.prefab";

    /// <summary>Screen height below which the item stops drawing — the reference's last LOD threshold.</summary>
    const float CullScreenHeight = 0.01f;

    const float PlacementDuration = 60f;

    /// <summary>
    /// 122 on the reference: everything except ContributeGI and BatchingStatic.
    /// These meshes are too dense to batch and are lit dynamically.
    /// </summary>
    // NavigationStatic and OffMeshLinkGeneration are deprecated in favour of
    // NavMeshBuildMarkup, but they are part of the 122 the reference carries and
    // Unity still round-trips them, so keep the value identical.
#pragma warning disable 618
    const StaticEditorFlags StaticFlags =
        StaticEditorFlags.OccluderStatic |
        StaticEditorFlags.OccludeeStatic |
        StaticEditorFlags.NavigationStatic |
        StaticEditorFlags.OffMeshLinkGeneration |
        StaticEditorFlags.ReflectionProbeStatic;
#pragma warning restore 618

    static readonly string[] TreeWords = { "tree", "palm", "willow", "bonsai" };
    static readonly string[] BuildingWords = { "cottage", "citadel", "house", "building" };

    [MenuItem("Tools/Generate Jannah Garden Shop Prefabs")]
    public static void Generate()
    {
        var timerArea = AssetDatabase.LoadAssetAtPath<GameObject>(TimerAreaPath);
        if (timerArea == null)
        {
            Debug.LogError($"[JannahGarden] Timer Area prefab not found at \"{TimerAreaPath}\".");
            return;
        }

        var entries = JannahGardenAssetTable.Entries;

        // The prefabs capture whatever materials the FBX resolves to at the moment
        // they are built, so the material pass has to have happened first.
        if (entries.Any(e => AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(e)) == null))
        {
            Debug.Log("[JannahGarden] Materials missing — running the material generator first.");
            JannahGardenMaterialGenerator.Generate();
        }

        EnsureFolder(OutputFolder);

        var used = new HashSet<string>();
        var problems = new List<string>();
        int created = 0, updated = 0;

        try
        {
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                string name = PrefabName(e.MaterialName, used);
                EditorUtility.DisplayProgressBar($"Building prefabs ({i + 1}/{entries.Length})",
                                                 name, (float)i / entries.Length);

                string fbxPath = $"{AssetsRoot}/{e.Folder}/{FbxFile}";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (model == null)
                {
                    problems.Add($"{name}: no model at {fbxPath}");
                    continue;
                }

                var sources = model.GetComponentsInChildren<MeshFilter>(true)
                                   .Where(mf => mf.sharedMesh != null)
                                   .ToArray();
                if (sources.Length == 0)
                {
                    problems.Add($"{name}: {FbxFile} has no mesh");
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(e)) == null)
                    problems.Add($"{name}: using the FBX's own material, {e.MaterialName}.mat was not found");

                string path = $"{OutputFolder}/{name}.prefab";
                bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;

                if (Build(name, path, model, sources, timerArea, problems))
                {
                    if (existed) updated++; else created++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[JannahGarden] {created} shop prefab(s) created, {updated} updated, in \"{OutputFolder}\".");
        if (problems.Count > 0)
            Debug.LogWarning("[JannahGarden] Issues:\n  " + string.Join("\n  ", problems));
    }

    // ------------------------------------------------------------------ build

    static bool Build(string name, string path, GameObject model, MeshFilter[] sources,
                      GameObject timerArea, List<string> problems)
    {
        var root = new GameObject(name);
        try
        {
            GameObjectUtility.SetStaticEditorFlags(root, StaticFlags);

            // Timer Area comes first so the child order matches the reference.
            var timer = PrefabUtility.InstantiatePrefab(timerArea) as GameObject;
            if (timer == null)
            {
                problems.Add($"{name}: could not instantiate the Timer Area prefab");
                return false;
            }
            timer.transform.SetParent(root.transform, false);

            var renderers = new List<Renderer>(sources.Length);
            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources[i];
                var child = new GameObject(sources.Length == 1 ? "LOD0" : $"LOD0_{src.name}");
                child.transform.SetParent(root.transform, false);
                GameObjectUtility.SetStaticEditorFlags(child, StaticFlags);

                // Keep the node's placement inside the FBX — Meshy models are not
                // all authored about their own origin.
                ApplyRelativeTransform(child.transform, model.transform, src.transform);

                var mesh = src.sharedMesh;
                child.AddComponent<MeshFilter>().sharedMesh = mesh;

                var renderer = child.AddComponent<MeshRenderer>();
                var srcRenderer = src.GetComponent<MeshRenderer>();
                if (srcRenderer != null) renderer.sharedMaterials = srcRenderer.sharedMaterials;
                else problems.Add($"{name}: {src.name} has no MeshRenderer to copy materials from");
                renderers.Add(renderer);

                child.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            var lodGroup = root.AddComponent<LODGroup>();
            lodGroup.fadeMode = LODFadeMode.None;
            lodGroup.animateCrossFading = true;
            lodGroup.SetLODs(new[] { new LOD(CullScreenHeight, renderers.ToArray()) });
            lodGroup.RecalculateBounds();

            var placeable = root.AddComponent<PlaceableItem>();
            placeable.placementDuration = PlacementDuration;
            placeable.remainingDuration = PlacementDuration;
            placeable.timerHolder = timer;
            placeable.itemRenderers = renderers.ToArray();
            placeable.isTree = Matches(name, TreeWords);
            placeable.isBuilding = !placeable.isTree && Matches(name, BuildingWords);

            PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            if (!ok) problems.Add($"{name}: Unity refused to save {path}");
            return ok;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void ApplyRelativeTransform(Transform target, Transform modelRoot, Transform source)
    {
        Matrix4x4 rel = modelRoot.worldToLocalMatrix * source.localToWorldMatrix;
        target.localPosition = rel.GetColumn(3);
        target.localRotation = rel.rotation;
        target.localScale = rel.lossyScale;
    }

    // ------------------------------------------------------------------ naming

    static string MaterialPath(JannahGardenAssetTable.Entry e) =>
        $"{AssetsRoot}/{e.Folder}/{e.MaterialName}.mat";

    /// <summary>
    /// Meshy truncates its names at 21 characters, which leaves some of them
    /// ending on the underscore of a word it cut. Drop that, then make sure the
    /// trim has not collided two names together.
    /// </summary>
    static string PrefabName(string materialName, HashSet<string> used)
    {
        string name = materialName.TrimEnd('_');
        if (name.Length == 0) name = materialName;

        string candidate = name;
        for (int n = 2; !used.Add(candidate); n++)
            candidate = $"{name}_{n}";
        return candidate;
    }

    static bool Matches(string name, string[] words)
    {
        string lower = name.ToLowerInvariant();
        return words.Any(w => lower.Contains(w));
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
