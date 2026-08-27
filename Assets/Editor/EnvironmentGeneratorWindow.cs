using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scatters Shop Item prefabs across a grid of area chunks to produce an example decorated
/// garden layout. Purely an editor-time authoring tool: it bakes plain, static GameObjects into
/// the open scene, then removes each instance's <see cref="PlaceableItem"/> component, since
/// generated dressing isn't a player-owned economy item and doesn't need growth timers,
/// save/load, or the relocate/return flow.
/// </summary>
public class EnvironmentGeneratorWindow : EditorWindow
{
    private const string ShopItemsRootFolder = "Assets/Prefabs/Shop Items";
    private const string GeneratedRootName = "Environment Generated";

    private class CategoryEntry
    {
        public string name;
        public bool enabled = true;
        public readonly List<GameObject> prefabs = new List<GameObject>();
    }

    private Terrain terrain;
    private int gridSizeX = 4;
    private int gridSizeZ = 4;
    private int minItemsPerChunk = 2;
    private int maxItemsPerChunk = 5;
    private float minSpacing = 3f;
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
        categories = new List<CategoryEntry>();
        if (!AssetDatabase.IsValidFolder(ShopItemsRootFolder)) return;

        foreach (string categoryFolder in AssetDatabase.GetSubFolders(ShopItemsRootFolder))
        {
            var entry = new CategoryEntry { name = Path.GetFileName(categoryFolder) };

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { categoryFolder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null) entry.prefabs.Add(prefab);
            }

            if (entry.prefabs.Count > 0) categories.Add(entry);
        }
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
            "save/load, relocate/return) is stripped after placement, leaving just the mesh and collider.",
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
        EditorGUILayout.LabelField("Scatter", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            minItemsPerChunk = EditorGUILayout.IntField("Min Items / Chunk", minItemsPerChunk);
            maxItemsPerChunk = EditorGUILayout.IntField("Max Items / Chunk", maxItemsPerChunk);
        }
        minItemsPerChunk = Mathf.Max(0, minItemsPerChunk);
        maxItemsPerChunk = Mathf.Max(minItemsPerChunk, maxItemsPerChunk);

        minSpacing = EditorGUILayout.FloatField("Min Spacing", minSpacing);
        edgeMargin = EditorGUILayout.FloatField("Chunk Edge Margin", edgeMargin);
        uniformScaleJitter = EditorGUILayout.Vector2Field("Scale Jitter (min, max)", uniformScaleJitter);
        randomSeed = EditorGUILayout.IntField("Random Seed", randomSeed);

        if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Categories", EditorStyles.boldLabel);
        if (categories == null || categories.Count == 0)
        {
            EditorGUILayout.HelpBox($"No prefabs found under {ShopItemsRootFolder}.", MessageType.Warning);
        }
        else
        {
            foreach (CategoryEntry category in categories)
            {
                category.enabled = EditorGUILayout.ToggleLeft($"{category.name} ({category.prefabs.Count})", category.enabled);
            }
        }

        if (GUILayout.Button("Refresh Prefab List")) RefreshCategories();

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(terrain == null))
        {
            if (GUILayout.Button("Generate", GUILayout.Height(32))) Generate();
        }
        if (GUILayout.Button("Clear Generated")) ClearGenerated();

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

        List<GameObject> enabledPrefabs = categories
            .Where(c => c.enabled)
            .SelectMany(c => c.prefabs)
            .ToList();

        if (enabledPrefabs.Count == 0)
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

                GenerateChunk(root.transform, chunkName, cx, cz, origin, chunkWidth, chunkDepth, enabledPrefabs, rng);
            }
        }
    }

    private void GenerateChunk(Transform root, string chunkName, int cx, int cz, Vector3 origin, float chunkWidth, float chunkDepth,
        List<GameObject> enabledPrefabs, System.Random rng)
    {
        var chunkGo = new GameObject(chunkName);
        Undo.RegisterCreatedObjectUndo(chunkGo, "Generate Environment");
        chunkGo.transform.SetParent(root);

        Vector3 chunkMin = origin + new Vector3(cx * chunkWidth, 0f, cz * chunkDepth);
        chunkGo.transform.position = chunkMin + new Vector3(chunkWidth * 0.5f, 0f, chunkDepth * 0.5f);

        float usableWidth = Mathf.Max(0f, chunkWidth - edgeMargin * 2f);
        float usableDepth = Mathf.Max(0f, chunkDepth - edgeMargin * 2f);

        int count = rng.Next(minItemsPerChunk, maxItemsPerChunk + 1);
        var placedXZ = new List<Vector3>();

        for (int i = 0; i < count; i++)
        {
            if (!TryFindSpot(chunkMin, edgeMargin, usableWidth, usableDepth, minSpacing, placedXZ, rng, out Vector3 spotXZ))
                continue;

            placedXZ.Add(spotXZ);

            GameObject prefab = enabledPrefabs[rng.Next(enabledPrefabs.Count)];
            float groundY = terrain.SampleHeight(spotXZ) + terrain.transform.position.y;
            Vector3 worldPos = new Vector3(spotXZ.x, groundY, spotXZ.z);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Generate Environment");
            instance.transform.SetParent(chunkGo.transform, false);
            instance.transform.position = worldPos;
            instance.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            instance.transform.localScale *= Mathf.Lerp(uniformScaleJitter.x, uniformScaleJitter.y, (float)rng.NextDouble());

            StripPlaceable(instance);
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

    private static void ClearGenerated()
    {
        GameObject root = GameObject.Find(GeneratedRootName);
        if (root == null) return;

        if (!EditorUtility.DisplayDialog("Clear Generated Environment",
            $"Delete '{GeneratedRootName}' and everything under it?", "Delete", "Cancel"))
            return;

        Undo.DestroyObjectImmediate(root);
    }
}
