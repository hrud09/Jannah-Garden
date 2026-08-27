using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scatters Shop Item prefabs across a grid of area chunks to produce an example decorated
/// garden layout. Purely an editor-time authoring tool: it bakes plain, static GameObjects into
/// the open scene, then removes each instance's <see cref="PlaceableItem"/> component, since
/// generated dressing isn't a player-owned economy item and doesn't need growth timers,
/// save/load, or the relocate/return flow. A <see cref="PrePlacedAsset"/> marker takes its place
/// so the player can still look at the dressing and get pointed at the Shop instead.
/// </summary>
public class EnvironmentGeneratorWindow : EditorWindow
{
    private const string ShopItemsRootFolder = "Assets/Prefabs/Shop Items";
    private const string GeneratedRootName = "Environment Generated";

    // Deliberately NOT StaticEditorFlags.OccluderStatic, unlike JannahGardenPrefabGenerator's
    // StaticFlags constant that this otherwise matches: feeding small/high-poly decorative props
    // (some Shop Item meshes run into the millions of triangles) into Umbra's occluder computation
    // as occluders — geometry meant to BLOCK visibility of other things — crashes the bake with
    // "Error occurred in occluder data computation" / "Failure in split phase". Garden dressing
    // should only ever be an occludee (something that CAN be hidden); only large simple closed
    // shapes like terrain/walls/buildings should ever be occluders.
#pragma warning disable 618
    private const StaticEditorFlags GeneratedStaticFlags =
        StaticEditorFlags.OccludeeStatic |
        StaticEditorFlags.NavigationStatic |
        StaticEditorFlags.OffMeshLinkGeneration |
        StaticEditorFlags.ReflectionProbeStatic;
#pragma warning restore 618

    private class CategoryEntry
    {
        public string name;
        public bool enabled = true;
        public int minCountPerChunk = 1;
        public int maxCountPerChunk = 1;
        public readonly List<GameObject> prefabs = new List<GameObject>();
    }

    private Terrain terrain;
    private int gridSizeX = 6;
    private int gridSizeZ = 6;
    private float minSpacing = 5f;
    private float edgeMargin = 1.5f;
    private int randomSeed = 12345;
    private Vector2 uniformScaleJitter = new Vector2(0.9f, 1.1f);

    private List<CategoryEntry> categories;
    private readonly HashSet<Vector2Int> selectedChunks = new HashSet<Vector2Int>();
    private Vector2 scroll;

    [MenuItem("Tools/Environment/Environment Generator")]
    private static void Open()
    {
        GetWindow<EnvironmentGeneratorWindow>("Environment Generator");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        if (terrain == null) terrain = Terrain.activeTerrain;
        RefreshCategories();
        if (selectedChunks.Count == 0) SelectAll();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void RefreshCategories()
    {
        List<CategoryEntry> previous = categories;
        categories = new List<CategoryEntry>();
        if (!AssetDatabase.IsValidFolder(ShopItemsRootFolder)) return;

        foreach (string categoryFolder in AssetDatabase.GetSubFolders(ShopItemsRootFolder))
        {
            string categoryName = Path.GetFileName(categoryFolder);
            CategoryEntry existing = previous?.Find(c => c.name == categoryName);
            (int defaultMin, int defaultMax) = GetDefaultCountRange(categoryName);

            var entry = new CategoryEntry
            {
                name = categoryName,
                enabled = existing?.enabled ?? true,
                minCountPerChunk = existing?.minCountPerChunk ?? defaultMin,
                maxCountPerChunk = existing?.maxCountPerChunk ?? defaultMax,
            };

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { categoryFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null) entry.prefabs.Add(prefab);
            }

            if (entry.prefabs.Count > 0) categories.Add(entry);
        }
    }

    /// <summary>Default per-chunk item count for a freshly-discovered category — PlantsAndGardens reads as dense ground cover, everything else as a sparser accent.</summary>
    private static (int min, int max) GetDefaultCountRange(string categoryName)
    {
        return categoryName == "PlantsAndGardens" ? (10, 20) : (1, 1);
    }

    private void SelectAll()
    {
        selectedChunks.Clear();
        for (int cz = 0; cz < gridSizeZ; cz++)
            for (int cx = 0; cx < gridSizeX; cx++)
                selectedChunks.Add(new Vector2Int(cx, cz));

        SceneView.RepaintAll();
    }

    private void InvertSelection()
    {
        var inverted = new HashSet<Vector2Int>();
        for (int cz = 0; cz < gridSizeZ; cz++)
            for (int cx = 0; cx < gridSizeX; cx++)
            {
                var coord = new Vector2Int(cx, cz);
                if (!selectedChunks.Contains(coord)) inverted.Add(coord);
            }

        selectedChunks.Clear();
        selectedChunks.UnionWith(inverted);
        SceneView.RepaintAll();
    }

    /// <summary>Clickable grid of chunk toggles laid out top-down (row 0 = furthest +Z), mirroring the terrain layout.</summary>
    private void DrawChunkSelectionGrid()
    {
        const float cellSize = 22f;
        Color previousColor = GUI.backgroundColor;

        for (int cz = gridSizeZ - 1; cz >= 0; cz--)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                for (int cx = 0; cx < gridSizeX; cx++)
                {
                    var coord = new Vector2Int(cx, cz);
                    bool selected = selectedChunks.Contains(coord);

                    GUI.backgroundColor = selected ? new Color(0.35f, 0.85f, 0.45f) : previousColor;
                    if (GUILayout.Button(GUIContent.none, GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
                    {
                        if (selected) selectedChunks.Remove(coord);
                        else selectedChunks.Add(coord);
                        SceneView.RepaintAll();
                    }
                }
                GUILayout.FlexibleSpace();
            }
        }

        GUI.backgroundColor = previousColor;
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "Scatters Shop Item prefabs across a grid of area chunks as example garden dressing. " +
            "Generated instances are plain decoration — the PlaceableItem component (growth timer, " +
            "save/load, relocate/return) is stripped after placement, leaving just the mesh and collider " +
            "plus a PrePlacedAsset marker so looking at one still offers a \"manage\" prompt that points " +
            "the player at the Shop.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();

        terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Chunk Grid", EditorStyles.boldLabel);
        int newGridSizeX = EditorGUILayout.IntSlider("Chunks (X)", gridSizeX, 1, 12);
        int newGridSizeZ = EditorGUILayout.IntSlider("Chunks (Z)", gridSizeZ, 1, 12);
        if (newGridSizeX != gridSizeX || newGridSizeZ != gridSizeZ)
        {
            gridSizeX = newGridSizeX;
            gridSizeZ = newGridSizeZ;
            SelectAll();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Area Selection", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Pick which chunks to generate into — unselected chunks are cleared and left empty.",
            MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Select All")) SelectAll();
            if (GUILayout.Button("Select None")) selectedChunks.Clear();
            if (GUILayout.Button("Invert")) InvertSelection();
        }
        DrawChunkSelectionGrid();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
        minSpacing = EditorGUILayout.FloatField("Min Spacing", minSpacing);
        edgeMargin = EditorGUILayout.FloatField("Chunk Edge Margin", edgeMargin);
        uniformScaleJitter = EditorGUILayout.Vector2Field("Scale Jitter (min, max)", uniformScaleJitter);
        randomSeed = EditorGUILayout.IntField("Random Seed", randomSeed);

        if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Asset Usage Per Folder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Min/Max is how many items from that folder get scattered into each selected chunk.",
            MessageType.None);
        if (categories == null || categories.Count == 0)
        {
            EditorGUILayout.HelpBox($"No prefabs found under {ShopItemsRootFolder}.", MessageType.Warning);
        }
        else
        {
            foreach (CategoryEntry category in categories)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    category.enabled = EditorGUILayout.ToggleLeft(
                        $"{category.name} ({category.prefabs.Count})", category.enabled, GUILayout.MinWidth(160));

                    using (new EditorGUI.DisabledScope(!category.enabled))
                    {
                        GUILayout.Label("Min", GUILayout.Width(28));
                        category.minCountPerChunk = EditorGUILayout.IntField(category.minCountPerChunk, GUILayout.Width(32));
                        GUILayout.Label("Max", GUILayout.Width(28));
                        category.maxCountPerChunk = EditorGUILayout.IntField(category.maxCountPerChunk, GUILayout.Width(32));
                    }
                }

                category.minCountPerChunk = Mathf.Max(0, category.minCountPerChunk);
                category.maxCountPerChunk = Mathf.Max(category.minCountPerChunk, category.maxCountPerChunk);
            }
        }

        if (GUILayout.Button("Refresh Prefab List")) RefreshCategories();

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(terrain == null))
        {
            if (GUILayout.Button("Generate", GUILayout.Height(32))) Generate();
        }
        if (GUILayout.Button("Clear Generated")) ClearGenerated();

        if (GUILayout.Button(new GUIContent(
            "Tag Existing Generated As Pre-Placed",
            "Adds the PrePlacedAsset marker to dressing generated before this marker existed, without " +
            "re-rolling any layout. Safe to run any time — instances that already have it are skipped.")))
        {
            TagExistingGeneratedAsPrePlaced();
        }

        if (GUILayout.Button(new GUIContent(
            "Mark Generated Static",
            "Sets Editor static flags (occludee/navigation/off-mesh-link/reflection-probe) on the " +
            "whole generated hierarchy, matching how the rest of the project's Shop Item scenery is " +
            "marked, for batching/lightmapping/reflection probes.")))
        {
            MarkGeneratedStatic();
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// Draws the chunk grid as a ground-hugging footprint in the Scene view: selected chunks filled
    /// green (will be generated), unselected chunks a dim outline only (will be cleared/left empty).
    /// </summary>
    private void OnSceneGUI(SceneView sceneView)
    {
        if (terrain == null || terrain.terrainData == null) return;

        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        float chunkWidth = data.size.x / gridSizeX;
        float chunkDepth = data.size.z / gridSizeZ;

        for (int cz = 0; cz < gridSizeZ; cz++)
        {
            for (int cx = 0; cx < gridSizeX; cx++)
            {
                Vector3 chunkMin = origin + new Vector3(cx * chunkWidth, 0f, cz * chunkDepth);
                Vector3 center = chunkMin + new Vector3(chunkWidth * 0.5f, 0f, chunkDepth * 0.5f);
                float groundY = terrain.SampleHeight(center) + terrain.transform.position.y + 0.05f;

                Vector3 c0 = new Vector3(chunkMin.x, groundY, chunkMin.z);
                Vector3 c1 = new Vector3(chunkMin.x + chunkWidth, groundY, chunkMin.z);
                Vector3 c2 = new Vector3(chunkMin.x + chunkWidth, groundY, chunkMin.z + chunkDepth);
                Vector3 c3 = new Vector3(chunkMin.x, groundY, chunkMin.z + chunkDepth);

                bool selected = selectedChunks.Contains(new Vector2Int(cx, cz));
                if (selected)
                {
                    Handles.DrawSolidRectangleWithOutline(
                        new[] { c0, c1, c2, c3 },
                        new Color(0.2f, 1f, 0.4f, 0.15f),
                        new Color(0.2f, 1f, 0.4f, 0.9f));
                }
                else
                {
                    Handles.color = new Color(1f, 1f, 1f, 0.25f);
                    Handles.DrawPolyLine(c0, c1, c2, c3, c0);
                }
            }
        }
    }

    private void Generate()
    {
        if (terrain == null || terrain.terrainData == null)
        {
            EditorUtility.DisplayDialog("Environment Generator", "Assign a Terrain first.", "OK");
            return;
        }

        List<CategoryEntry> enabledCategories = categories
            .Where(c => c.enabled && c.prefabs.Count > 0)
            .ToList();

        if (enabledCategories.Count == 0)
        {
            EditorUtility.DisplayDialog("Environment Generator", "No Shop Item prefabs available — enable at least one category.", "OK");
            return;
        }

        if (selectedChunks.Count == 0)
        {
            EditorUtility.DisplayDialog("Environment Generator", "Select at least one chunk area to generate into.", "OK");
            return;
        }

        GameObject root = GameObject.Find(GeneratedRootName);
        if (root == null)
        {
            root = new GameObject(GeneratedRootName);
            Undo.RegisterCreatedObjectUndo(root, "Generate Environment");
        }

        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        float chunkWidth = data.size.x / gridSizeX;
        float chunkDepth = data.size.z / gridSizeZ;

        var rng = new System.Random(randomSeed);

        for (int cz = 0; cz < gridSizeZ; cz++)
        {
            for (int cx = 0; cx < gridSizeX; cx++)
            {
                string chunkName = $"Chunk_{cx}_{cz}";
                Transform existingChunk = root.transform.Find(chunkName);
                if (existingChunk != null) Undo.DestroyObjectImmediate(existingChunk.gameObject);

                // Unselected chunks are only cleared, never regenerated — they stay empty.
                if (!selectedChunks.Contains(new Vector2Int(cx, cz))) continue;

                GenerateChunk(root.transform, chunkName, cx, cz, origin, chunkWidth, chunkDepth, enabledCategories, rng);
            }
        }
    }

    private void GenerateChunk(Transform root, string chunkName, int cx, int cz, Vector3 origin, float chunkWidth, float chunkDepth,
        List<CategoryEntry> enabledCategories, System.Random rng)
    {
        var chunkGo = new GameObject(chunkName);
        Undo.RegisterCreatedObjectUndo(chunkGo, "Generate Environment");
        chunkGo.transform.SetParent(root);

        Vector3 chunkMin = origin + new Vector3(cx * chunkWidth, 0f, cz * chunkDepth);
        chunkGo.transform.position = chunkMin + new Vector3(chunkWidth * 0.5f, 0f, chunkDepth * 0.5f);

        float usableWidth = Mathf.Max(0f, chunkWidth - edgeMargin * 2f);
        float usableDepth = Mathf.Max(0f, chunkDepth - edgeMargin * 2f);

        var placedXZ = new List<Vector3>();

        // Each folder's item count is rolled independently, so usage per category is tunable
        // rather than every prefab in the pool having an equal chance regardless of folder size.
        foreach (CategoryEntry category in enabledCategories)
        {
            int count = rng.Next(category.minCountPerChunk, category.maxCountPerChunk + 1);

            for (int i = 0; i < count; i++)
            {
                if (!TryFindSpot(chunkMin, edgeMargin, usableWidth, usableDepth, minSpacing, placedXZ, rng, out Vector3 spotXZ))
                    continue;

                placedXZ.Add(spotXZ);

                GameObject prefab = category.prefabs[rng.Next(category.prefabs.Count)];
                float groundY = terrain.SampleHeight(spotXZ) + terrain.transform.position.y;
                Vector3 worldPos = new Vector3(spotXZ.x, groundY, spotXZ.z);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(instance, "Generate Environment");
                instance.transform.SetParent(chunkGo.transform, false);
                instance.transform.position = worldPos;
                instance.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                instance.transform.localScale *= Mathf.Lerp(uniformScaleJitter.x, uniformScaleJitter.y, (float)rng.NextDouble());

                StripPlaceable(instance);
                AddPrePlacedMarker(instance);
            }
        }
    }

    private static bool TryFindSpot(Vector3 chunkMin, float margin, float usableWidth, float usableDepth,
        float minSpacing, List<Vector3> placedXZ, System.Random rng, out Vector3 spotXZ)
    {
        const int maxAttempts = 10;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float x = chunkMin.x + margin + (float)rng.NextDouble() * usableWidth;
            float z = chunkMin.z + margin + (float)rng.NextDouble() * usableDepth;
            var candidate = new Vector3(x, 0f, z);

            bool tooClose = false;
            foreach (Vector3 existing in placedXZ)
            {
                if (Vector3.Distance(candidate, existing) < minSpacing)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
            {
                spotXZ = candidate;
                return true;
            }
        }

        spotXZ = default;
        return false;
    }

    /// <summary>
    /// Removes the PlaceableItem component from a freshly-spawned example instance.
    /// PlaceableItem.Awake() measures how far the model's geometry hangs below its pivot and lifts
    /// it back onto the ground plane (see PlaceableItem.MeasureGfxGroundOffset) — that never runs
    /// automatically here since Awake only fires in Play mode, so it's invoked directly (it's a
    /// private Unity lifecycle method, hence reflection) before the component is discarded. This
    /// keeps the stripped decoration grounded exactly like a real placed item, without carrying any
    /// of the growth-timer/save/interaction behavior that a static example doesn't need.
    /// </summary>
    private static void StripPlaceable(GameObject instance)
    {
        var placeable = instance.GetComponent<PlaceableItem>();
        if (placeable == null) return;

        MethodInfo awake = typeof(PlaceableItem).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        awake?.Invoke(placeable, null);

        Transform timerArea = instance.transform.Find("Timer Area");
        if (timerArea != null) Undo.DestroyObjectImmediate(timerArea.gameObject);

        Undo.DestroyObjectImmediate(placeable);
    }

    /// <summary>
    /// Adds the <see cref="PrePlacedAsset"/> marker that lets the interaction system recognize this
    /// instance as example dressing — see <see cref="TagExistingGeneratedAsPrePlaced"/> for backfilling
    /// dressing generated before this marker existed.
    /// </summary>
    private static void AddPrePlacedMarker(GameObject instance)
    {
        if (instance.GetComponent<PrePlacedAsset>() != null) return;
        Undo.AddComponent<PrePlacedAsset>(instance);
    }

    /// <summary>
    /// Walks whatever is already sitting under <see cref="GeneratedRootName"/> and adds the
    /// PrePlacedAsset marker to any item instance that doesn't have one yet — for dressing generated
    /// before the marker existed, without re-rolling the layout by regenerating from scratch.
    /// </summary>
    private static void TagExistingGeneratedAsPrePlaced()
    {
        GameObject root = GameObject.Find(GeneratedRootName);
        if (root == null)
        {
            EditorUtility.DisplayDialog("Environment Generator", "Nothing generated yet — click Generate first.", "OK");
            return;
        }

        int tagged = 0;
        foreach (Transform chunk in root.transform)
        {
            foreach (Transform item in chunk)
            {
                if (item.GetComponent<PrePlacedAsset>() != null) continue;

                Undo.AddComponent<PrePlacedAsset>(item.gameObject);
                tagged++;
            }
        }

        EditorUtility.DisplayDialog("Environment Generator", $"Tagged {tagged} generated item(s) as pre-placed.", "OK");
    }

    private static void ClearGenerated()
    {
        GameObject root = GameObject.Find(GeneratedRootName);
        if (root == null) return;

        if (!EditorUtility.DisplayDialog("Clear Generated Environment",
            $"Delete '{GeneratedRootName}' and everything under it?", "Delete", "Cancel"))
            return;

        Undo.DestroyObjectImmediate(root);
    }

    /// <summary>
    /// Marks the whole generated hierarchy as static scenery (occludee/navigation/off-mesh-link/
    /// reflection-probe), matching how the rest of the project's Shop Item scenery is marked, for
    /// batching/lightmapping/reflection probes. No culling mechanism is wired up here — Unity's baked
    /// occlusion culling (Umbra) reproducibly crashes on this scene regardless of generated content
    /// (a pre-existing engine/scene issue), and distance-based culling via RuntimeEnvironmentGenerator
    /// was tried and then removed.
    /// </summary>
    private static void MarkGeneratedStatic()
    {
        GameObject root = GameObject.Find(GeneratedRootName);
        if (root == null)
        {
            EditorUtility.DisplayDialog("Environment Generator", "Nothing generated yet — click Generate first.", "OK");
            return;
        }

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in all)
        {
            GameObject go = t.gameObject;
            Undo.RecordObject(go, "Mark Generated Static");
            GameObjectUtility.SetStaticEditorFlags(go, GeneratedStaticFlags);
            EditorUtility.SetDirty(go);
        }

        Debug.Log($"[EnvironmentGeneratorWindow] Marked {all.Length} generated GameObjects static.");

        Scene scene = root.scene;
        if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
    }
}
