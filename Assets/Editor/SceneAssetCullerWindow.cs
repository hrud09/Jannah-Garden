using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene Asset Culler — deletes a random percentage of scene objects, grouped by
/// the asset they came from (source prefab, or mesh for loose renderers).
///
/// SCENE ONLY. Deletion goes through Undo.DestroyObjectImmediate on GameObjects that
/// live in the open scene. Nothing here calls AssetDatabase.DeleteAsset or touches the
/// Project folder, so the prefab/mesh/texture files stay exactly where they are — only
/// their instances leave the scene.
///
/// Scope can be the whole active scene, everything under one parent GameObject you
/// point the window at, or the current hierarchy selection.
///
/// Deletion picks instances at random from a fixed seed, so the same seed + percentage
/// always removes the same instances, and raising the percentage only ever adds to the
/// previous selection. Everything is undoable with Ctrl+Z; save the scene to keep it.
///
/// Menu: Tools > Performance > Scene Asset Culler
/// </summary>
public class SceneAssetCullerWindow : EditorWindow
{
    private enum Scope { ActiveScene, ParentObject, Selection }
    private enum GroupBy { SourcePrefab, Mesh, ObjectName }
    private enum SortBy { InstanceCount, Triangles, Name }

    /// <summary>One asset and every instance of it found in the scanned scope.</summary>
    private class AssetGroup
    {
        public string displayName;
        public string assetPath;                 // empty when the group has no backing asset
        public readonly List<GameObject> instances = new List<GameObject>();
        public bool include = true;
        public float percentOverride = -1f;      // < 0 means "use the global percentage"
        public long triangles;                   // total tris across all instances

        public long TrianglesPerInstance => instances.Count == 0 ? 0 : triangles / instances.Count;
    }

    // --- scan settings ---
    private Scope _scope = Scope.ActiveScene;
    private GameObject _parentObject;            // scope root for Scope.ParentObject
    private bool _includeParentItself = false;
    private GroupBy _groupBy = GroupBy.SourcePrefab;
    private bool _prefabRootsAsUnits = true;
    private bool _requireRenderer = true;
    private bool _protectImportant = true;
    private bool _includeInactive = true;
    private string _nameFilter = "";
    private string _excludeFilter = "";

    // --- cull settings ---
    private float _globalPercent = 50f;
    private int _seed = 12345;
    private int _minKeepPerGroup = 0;

    // --- list state ---
    private readonly List<AssetGroup> _groups = new List<AssetGroup>();
    private string _search = "";
    private SortBy _sortBy = SortBy.InstanceCount;
    private Vector2 _scroll;
    private bool _showScanSettings = true;
    private int _scannedUnits;
    private long _scannedTriangles;
    private string _scannedScopeLabel = "";
    private string _status = "";

    // Mesh triangle counts are reused constantly while scanning; cache per mesh.
    private readonly Dictionary<Mesh, long> _triCache = new Dictionary<Mesh, long>();

    private const int MaxPreviewSelection = 2000;

    [MenuItem("Tools/Performance/Scene Asset Culler")]
    public static void Open()
    {
        var w = GetWindow<SceneAssetCullerWindow>("Asset Culler");
        w.minSize = new Vector2(560f, 400f);
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawScanSettings();

        if (_groups.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Nothing scanned yet. Pick a scope above and press \"Scan\".",
                MessageType.Info);
            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
            return;
        }

        DrawCullSettings();
        DrawListToolbar();
        DrawGroupList();
        DrawFooter();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Randomly cull scene instances by asset", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Deletes GameObjects from the OPEN SCENE only. Prefabs, meshes and textures in the " +
            "Project folder are never modified or deleted — only their scene instances are removed.",
            MessageType.None);
        EditorGUILayout.Space(2f);
    }

    private void DrawScanSettings()
    {
        _showScanSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showScanSettings, "Scan Settings");
        if (_showScanSettings)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _scope = (Scope)EditorGUILayout.EnumPopup(
                    new GUIContent("Scope",
                        "Active Scene: every root object in the open scene.\n" +
                        "Parent Object: only the children of one GameObject you assign below.\n" +
                        "Selection: only what is currently selected in the Hierarchy."),
                    _scope);

                if (_scope == Scope.ParentObject)
                    DrawParentObjectField();

                _groupBy = (GroupBy)EditorGUILayout.EnumPopup(
                    new GUIContent("Group by",
                        "Source Prefab: bucket instances by the prefab asset they came from.\n" +
                        "Mesh: bucket by the mesh the renderer draws.\n" +
                        "Object Name: bucket by GameObject name (ignores the (1), (2) suffixes)."),
                    _groupBy);

                _prefabRootsAsUnits = EditorGUILayout.Toggle(
                    new GUIContent("Prefab root = one unit",
                        "Treat each prefab instance as a single deletable unit instead of culling its children individually. " +
                        "Never escalates past the scope root, so a scoped cull stays inside that subtree."),
                    _prefabRootsAsUnits);

                _requireRenderer = EditorGUILayout.Toggle(
                    new GUIContent("Must have a renderer",
                        "Only consider objects that draw something (MeshRenderer, SkinnedMeshRenderer, sprites, particles...)."),
                    _requireRenderer);

                _includeInactive = EditorGUILayout.Toggle(
                    new GUIContent("Include inactive", "Also scan disabled objects."),
                    _includeInactive);

                _protectImportant = EditorGUILayout.Toggle(
                    new GUIContent("Protect key objects",
                        "Never touch anything containing a Camera, Light, Terrain, Canvas, AudioListener, " +
                        "ReflectionProbe or LightProbeGroup."),
                    _protectImportant);

                _nameFilter = EditorGUILayout.TextField(
                    new GUIContent("Name contains (CSV)", "Optional. Only scan objects whose name matches one of these fragments."),
                    _nameFilter);

                _excludeFilter = EditorGUILayout.TextField(
                    new GUIContent("Name excludes (CSV)", "Optional. Skip objects whose name matches one of these fragments."),
                    _excludeFilter);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(ScanButtonLabel(), GUILayout.Height(24f)))
                        Scan();

                    using (new EditorGUI.DisabledScope(_groups.Count == 0))
                    {
                        if (GUILayout.Button("Clear Results", GUILayout.Height(24f), GUILayout.Width(110f)))
                        {
                            _groups.Clear();
                            _status = "";
                        }
                    }
                }
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private void DrawParentObjectField()
    {
        using (new EditorGUI.IndentLevelScope())
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _parentObject = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("Parent Object", "Drag a GameObject from the Hierarchy. Only its children are scanned."),
                    _parentObject, typeof(GameObject), true);

                using (new EditorGUI.DisabledScope(Selection.activeGameObject == null))
                {
                    if (GUILayout.Button(new GUIContent("Use Selected", "Assign the object currently selected in the Hierarchy."),
                            GUILayout.Width(96f)))
                        _parentObject = Selection.activeGameObject;
                }
            }

            _includeParentItself = EditorGUILayout.Toggle(
                new GUIContent("Include the parent too",
                    "Off (default): the parent itself can never be deleted, only its descendants."),
                _includeParentItself);

            if (_parentObject == null)
            {
                EditorGUILayout.HelpBox("Assign a parent GameObject from the Hierarchy.", MessageType.Warning);
            }
            else if (!_parentObject.scene.IsValid())
            {
                EditorGUILayout.HelpBox(
                    "That is a prefab asset from the Project window. Drag the instance from the Hierarchy instead — " +
                    "this tool only edits scene objects.",
                    MessageType.Error);
            }
            else
            {
                int descendants = _parentObject.GetComponentsInChildren<Transform>(true).Length - 1;
                EditorGUILayout.LabelField($"{descendants:n0} descendants under \"{_parentObject.name}\"",
                    EditorStyles.miniLabel);
            }
        }
    }

    private string ScanButtonLabel()
    {
        switch (_scope)
        {
            case Scope.ParentObject:
                return _parentObject != null ? $"Scan \"{_parentObject.name}\"" : "Scan Parent Object";
            case Scope.Selection:
                return "Scan Selection";
            default:
                return "Scan Scene";
        }
    }

    private void DrawCullSettings()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(
                $"{_scannedUnits:n0} units in {_groups.Count:n0} assets  •  {_scannedTriangles:n0} tris",
                EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(_scannedScopeLabel))
                EditorGUILayout.LabelField($"Scope: {_scannedScopeLabel}", EditorStyles.miniLabel);

            _globalPercent = EditorGUILayout.Slider(
                new GUIContent("Delete %", "Percentage of each included asset's instances to delete at random."),
                _globalPercent, 0f, 100f);

            using (new EditorGUILayout.HorizontalScope())
            {
                _seed = EditorGUILayout.IntField(
                    new GUIContent("Random seed", "Same seed + same percentage always deletes the same instances."),
                    _seed);
                if (GUILayout.Button("Reroll", GUILayout.Width(60f)))
                    _seed = Random.Range(1, int.MaxValue);
            }

            _minKeepPerGroup = Mathf.Max(0, EditorGUILayout.IntField(
                new GUIContent("Always keep at least", "Per asset, never let the surviving instance count drop below this."),
                _minKeepPerGroup));
        }
    }

    private void DrawListToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(180f));

            _sortBy = (SortBy)EditorGUILayout.EnumPopup(_sortBy, EditorStyles.toolbarPopup, GUILayout.Width(120f));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("All", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                SetIncluded(g => true);
            if (GUILayout.Button("None", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                SetIncluded(g => false);
            if (GUILayout.Button("Invert", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                foreach (var g in VisibleGroups()) g.include = !g.include;
            if (GUILayout.Button("Clear % overrides", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                foreach (var g in _groups) g.percentOverride = -1f;
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.miniLabel))
        {
            GUILayout.Space(22f);
            GUILayout.Label("Asset", EditorStyles.miniBoldLabel, GUILayout.MinWidth(120f));
            GUILayout.Label("Count", EditorStyles.miniBoldLabel, GUILayout.Width(56f));
            GUILayout.Label("Tris", EditorStyles.miniBoldLabel, GUILayout.Width(72f));
            GUILayout.Label("Delete %", EditorStyles.miniBoldLabel, GUILayout.Width(64f));
            GUILayout.Label("Deleting", EditorStyles.miniBoldLabel, GUILayout.Width(88f));
            GUILayout.Space(60f);
        }
    }

    private void DrawGroupList()
    {
        var visible = SortGroups(VisibleGroups()).ToList();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var g in visible)
            DrawGroupRow(g);
        EditorGUILayout.EndScrollView();
    }

    private void DrawGroupRow(AssetGroup g)
    {
        int alive = CountAlive(g);
        int doomed = DeleteCountFor(g);

        using (new EditorGUILayout.HorizontalScope())
        {
            g.include = EditorGUILayout.Toggle(g.include, GUILayout.Width(18f));

            using (new EditorGUI.DisabledScope(!g.include))
            {
                var icon = string.IsNullOrEmpty(g.assetPath)
                    ? EditorGUIUtility.IconContent("GameObject Icon")
                    : new GUIContent(AssetDatabase.GetCachedIcon(g.assetPath));
                var label = new GUIContent(g.displayName, icon.image,
                    string.IsNullOrEmpty(g.assetPath)
                        ? g.displayName
                        : g.assetPath + "\n(click to reveal in the Project window — the asset is never deleted)");

                if (GUILayout.Button(label, EditorStyles.label, GUILayout.MinWidth(120f)))
                    PingAsset(g);

                GUILayout.Label(alive.ToString("n0"), GUILayout.Width(56f));
                GUILayout.Label(FormatCount(g.triangles), GUILayout.Width(72f));

                float effective = EffectivePercent(g);
                float typed = EditorGUILayout.FloatField(effective, GUILayout.Width(44f));
                if (!Mathf.Approximately(typed, effective))
                    g.percentOverride = Mathf.Clamp(typed, 0f, 100f);

                if (g.percentOverride >= 0f)
                {
                    if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(18f)))
                        g.percentOverride = -1f;
                }
                else
                {
                    GUILayout.Space(20f);
                }

                GUILayout.Label($"-{doomed:n0} ({FormatCount(doomed * g.TrianglesPerInstance)} tris)",
                    GUILayout.Width(88f));

                if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52f)))
                    SelectObjects(g.instances.Where(o => o != null).ToList());
            }
        }
    }

    private void DrawFooter()
    {
        var included = _groups.Where(g => g.include).ToList();
        int totalDoomed = included.Sum(DeleteCountFor);
        long trisSaved = included.Sum(g => DeleteCountFor(g) * g.TrianglesPerInstance);

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(
                $"Will delete {totalDoomed:n0} of {_scannedUnits:n0} units " +
                $"(~{FormatCount(trisSaved)} tris, {(_scannedTriangles > 0 ? trisSaved * 100f / _scannedTriangles : 0f):0.#}% of scanned geometry)",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(totalDoomed == 0))
                {
                    if (GUILayout.Button(new GUIContent("Select Doomed",
                            "Select the exact objects that would be deleted, so you can eyeball them first."),
                            GUILayout.Height(26f)))
                        SelectObjects(CollectDoomed());

                    if (GUILayout.Button(new GUIContent("Frame In Scene",
                            "Select them and frame the scene view on the result."), GUILayout.Height(26f)))
                    {
                        SelectObjects(CollectDoomed());
                        SceneView.FrameLastActiveSceneView();
                    }

                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                    if (GUILayout.Button(new GUIContent($"Delete {totalDoomed:n0} From Scene",
                            "Removes the scene instances only. Undoable with Ctrl+Z; save the scene to keep it."),
                            GUILayout.Height(26f)))
                        DeleteDoomed();
                    GUI.backgroundColor = prev;
                }
            }

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
        }
    }

    // ---------------------------------------------------------------- scanning

    private void Scan()
    {
        _groups.Clear();
        _triCache.Clear();
        _scannedUnits = 0;
        _scannedTriangles = 0;

        // scopeRoot is the object a scoped cull must stay inside of; null = whole scene.
        GameObject scopeRoot = null;
        bool scopeRootDeletable = true;
        List<GameObject> roots;

        switch (_scope)
        {
            case Scope.ParentObject:
                if (_parentObject == null)
                {
                    _status = "Assign a parent GameObject from the Hierarchy first.";
                    return;
                }
                if (!_parentObject.scene.IsValid())
                {
                    _status = "That parent is a prefab asset in the Project window — drag the Hierarchy instance instead.";
                    return;
                }
                scopeRoot = _parentObject;
                scopeRootDeletable = _includeParentItself;
                roots = new List<GameObject> { _parentObject };
                _scannedScopeLabel = $"children of \"{_parentObject.name}\"" +
                                     (_includeParentItself ? " (parent included)" : "");
                break;

            case Scope.Selection:
                roots = Selection.gameObjects.Where(g => g != null && g.scene.IsValid()).ToList();
                _scannedScopeLabel = $"{roots.Count} selected object(s)";
                break;

            default:
                var active = SceneManager.GetActiveScene();
                roots = active.IsValid() ? active.GetRootGameObjects().ToList() : new List<GameObject>();
                _scannedScopeLabel = $"scene \"{active.name}\"";
                break;
        }

        if (roots.Count == 0)
        {
            _status = _scope == Scope.Selection
                ? "Nothing selected in the Hierarchy."
                : "The active scene has no root objects.";
            return;
        }

        var includeKeys = ParseCsv(_nameFilter);
        var excludeKeys = ParseCsv(_excludeFilter);

        // A single GameObject can be reached through several children once prefab
        // roots collapse into one unit, so dedupe before grouping.
        var seen = new HashSet<GameObject>();
        var byKey = new Dictionary<string, AssetGroup>();

        try
        {
            for (int r = 0; r < roots.Count; r++)
            {
                if (roots[r] == null) continue;

                EditorUtility.DisplayProgressBar("Scene Asset Culler",
                    $"Scanning {roots[r].name}...", (float)r / roots.Count);

                foreach (var t in roots[r].GetComponentsInChildren<Transform>(_includeInactive))
                {
                    var go = t.gameObject;

                    // The scope root itself is off limits unless explicitly allowed.
                    if (go == scopeRoot && !scopeRootDeletable) continue;

                    if (!MatchesNameFilters(go.name, includeKeys, excludeKeys)) continue;

                    var unit = ResolveUnit(go, scopeRoot, scopeRootDeletable);
                    if (unit == null || !seen.Add(unit)) continue;

                    if (_requireRenderer && unit.GetComponentInChildren<Renderer>(true) == null)
                        continue;

                    if (_protectImportant && IsProtected(unit))
                        continue;

                    string key = GroupKeyFor(unit, out string display, out string assetPath);
                    if (!byKey.TryGetValue(key, out var group))
                    {
                        group = new AssetGroup { displayName = display, assetPath = assetPath };
                        byKey.Add(key, group);
                        _groups.Add(group);
                    }

                    group.instances.Add(unit);
                    long tris = CountTriangles(unit);
                    group.triangles += tris;
                    _scannedTriangles += tris;
                    _scannedUnits++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        _status = $"Scanned {_scannedUnits:n0} units across {_groups.Count:n0} assets in {_scannedScopeLabel}.";
        Debug.Log($"[SceneAssetCuller] {_status}");
    }

    /// <summary>
    /// Maps an object to the thing we would actually delete. When prefab roots are
    /// units, an object escalates to its nearest prefab instance root — but only while
    /// that root stays inside the scanned scope. Otherwise a scoped cull on the children
    /// of one big prefab instance would resolve every child to the parent and wipe it.
    /// </summary>
    private GameObject ResolveUnit(GameObject go, GameObject scopeRoot, bool scopeRootDeletable)
    {
        if (!_prefabRootsAsUnits) return go;

        // Nearest (not outermost) root, so foliage nested inside a bigger prefab
        // instance still culls individually instead of taking the parent with it.
        var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
        if (root == null) return go;

        return IsWithinScope(root, scopeRoot, scopeRootDeletable) ? root : go;
    }

    private static bool IsWithinScope(GameObject candidate, GameObject scopeRoot, bool scopeRootDeletable)
    {
        if (scopeRoot == null) return true;
        if (candidate == scopeRoot) return scopeRootDeletable;
        return candidate.transform.IsChildOf(scopeRoot.transform);
    }

    private string GroupKeyFor(GameObject unit, out string display, out string assetPath)
    {
        assetPath = "";

        if (_groupBy == GroupBy.SourcePrefab)
        {
            assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(unit);
            if (!string.IsNullOrEmpty(assetPath))
            {
                display = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                return "prefab:" + assetPath;
            }
            // Not a prefab instance — fall through to a mesh/name bucket so it is
            // still cullable rather than silently dropped.
        }

        if (_groupBy == GroupBy.SourcePrefab || _groupBy == GroupBy.Mesh)
        {
            var mesh = FindFirstMesh(unit);
            if (mesh != null)
            {
                string meshPath = AssetDatabase.GetAssetPath(mesh);
                assetPath = meshPath;
                display = string.IsNullOrEmpty(meshPath)
                    ? $"[mesh] {mesh.name}"
                    : $"{mesh.name}  ({System.IO.Path.GetFileName(meshPath)})";
                return "mesh:" + (string.IsNullOrEmpty(meshPath) ? mesh.name : meshPath + "/" + mesh.name);
            }
        }

        string clean = StripInstanceSuffix(unit.name);
        display = $"[name] {clean}";
        return "name:" + clean;
    }

    private static Mesh FindFirstMesh(GameObject unit)
    {
        var mf = unit.GetComponentInChildren<MeshFilter>(true);
        if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;

        var smr = unit.GetComponentInChildren<SkinnedMeshRenderer>(true);
        return smr != null ? smr.sharedMesh : null;
    }

    /// <summary>"Rock_01 (12)" -> "Rock_01" so copies land in the same bucket.</summary>
    private static string StripInstanceSuffix(string name)
    {
        int open = name.LastIndexOf('(');
        if (open <= 0 || !name.EndsWith(")")) return name;

        string inner = name.Substring(open + 1, name.Length - open - 2);
        return int.TryParse(inner, out _) ? name.Substring(0, open).TrimEnd() : name;
    }

    private static bool IsProtected(GameObject unit)
    {
        return unit.GetComponentInChildren<Camera>(true) != null
            || unit.GetComponentInChildren<Light>(true) != null
            || unit.GetComponentInChildren<Terrain>(true) != null
            || unit.GetComponentInChildren<Canvas>(true) != null
            || unit.GetComponentInChildren<AudioListener>(true) != null
            || unit.GetComponentInChildren<ReflectionProbe>(true) != null
            || unit.GetComponentInChildren<LightProbeGroup>(true) != null;
    }

    private static bool MatchesNameFilters(string name, List<string> include, List<string> exclude)
    {
        string lower = name.ToLowerInvariant();

        foreach (var key in exclude)
            if (lower.Contains(key)) return false;

        if (include.Count == 0) return true;

        foreach (var key in include)
            if (lower.Contains(key)) return true;

        return false;
    }

    private static List<string> ParseCsv(string csv)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(csv)) return result;

        foreach (var part in csv.Split(','))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0) result.Add(trimmed.ToLowerInvariant());
        }
        return result;
    }

    private long CountTriangles(GameObject unit)
    {
        long total = 0;

        foreach (var mf in unit.GetComponentsInChildren<MeshFilter>(true))
            total += TrianglesOf(mf.sharedMesh);

        foreach (var smr in unit.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            total += TrianglesOf(smr.sharedMesh);

        return total;
    }

    private long TrianglesOf(Mesh mesh)
    {
        if (mesh == null) return 0;
        if (_triCache.TryGetValue(mesh, out long cached)) return cached;

        long tris = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            tris += mesh.GetIndexCount(i) / 3;

        _triCache[mesh] = tris;
        return tris;
    }

    // ----------------------------------------------------------------- culling

    private float EffectivePercent(AssetGroup g)
    {
        return g.percentOverride >= 0f ? g.percentOverride : _globalPercent;
    }

    private int CountAlive(AssetGroup g)
    {
        int alive = 0;
        foreach (var o in g.instances)
            if (o != null) alive++;
        return alive;
    }

    private int DeleteCountFor(AssetGroup g)
    {
        if (!g.include) return 0;

        int alive = CountAlive(g);
        if (alive == 0) return 0;

        int want = Mathf.RoundToInt(alive * EffectivePercent(g) / 100f);
        int allowed = Mathf.Max(0, alive - _minKeepPerGroup);
        return Mathf.Clamp(want, 0, allowed);
    }

    /// <summary>
    /// Per group: shuffle deterministically from (seed, group key), then take the
    /// first N. Because the shuffle order does not depend on the percentage,
    /// raising the percentage only ever extends the previous kill set.
    /// </summary>
    private List<GameObject> DoomedIn(AssetGroup g)
    {
        int take = DeleteCountFor(g);
        if (take == 0) return new List<GameObject>();

        var alive = g.instances.Where(o => o != null).ToList();
        var rng = new System.Random(StableSeed(g));
        for (int i = alive.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (alive[i], alive[j]) = (alive[j], alive[i]);
        }

        return alive.GetRange(0, Mathf.Min(take, alive.Count));
    }

    private List<GameObject> CollectDoomed()
    {
        var all = new List<GameObject>();
        foreach (var g in _groups)
            if (g.include) all.AddRange(DoomedIn(g));
        return all;
    }

    /// <summary>
    /// FNV-1a over the group key. string.GetHashCode is not guaranteed stable
    /// between editor sessions, and this seed has to be.
    /// </summary>
    private int StableSeed(AssetGroup g)
    {
        string key = (string.IsNullOrEmpty(g.assetPath) ? g.displayName : g.assetPath) ?? "";
        uint hash = 2166136261u;
        foreach (char c in key)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return unchecked((int)hash ^ _seed);
    }

    private void DeleteDoomed()
    {
        var doomed = CollectDoomed();
        if (doomed.Count == 0) return;

        var scene = SceneManager.GetActiveScene();
        string where = _scope == Scope.ParentObject && _parentObject != null
            ? $"from under \"{_parentObject.name}\""
            : $"from scene \"{scene.name}\"";

        bool ok = EditorUtility.DisplayDialog("Scene Asset Culler",
            $"Delete {doomed.Count:n0} GameObjects {where}?\n\n" +
            "Scene instances only — the prefab and mesh assets in the Project folder are not touched.\n" +
            "Undoable with Ctrl+Z, and the scene is not saved automatically.",
            "Delete From Scene", "Cancel");
        if (!ok) return;

        Undo.SetCurrentGroupName($"Cull {doomed.Count} Scene Objects");
        int undoGroup = Undo.GetCurrentGroup();

        int deleted = 0;
        try
        {
            for (int i = 0; i < doomed.Count; i++)
            {
                if (i % 250 == 0)
                {
                    EditorUtility.DisplayProgressBar("Scene Asset Culler",
                        $"Deleting {i:n0} / {doomed.Count:n0}...", (float)i / doomed.Count);
                }

                var go = doomed[i];
                if (go == null) continue;

                // Scene GameObject destruction only — never AssetDatabase.DeleteAsset.
                Undo.DestroyObjectImmediate(go);
                deleted++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);

        PruneDeleted();

        _status = $"Deleted {deleted:n0} scene objects. {_scannedUnits:n0} units left — save the scene (Ctrl+S) to keep it.";
        Debug.Log($"[SceneAssetCuller] {_status}");
        Repaint();
    }

    /// <summary>Drops destroyed instances and recomputes the totals after a cull.</summary>
    private void PruneDeleted()
    {
        _scannedUnits = 0;
        _scannedTriangles = 0;

        for (int i = _groups.Count - 1; i >= 0; i--)
        {
            var g = _groups[i];
            long perInstance = g.TrianglesPerInstance;

            g.instances.RemoveAll(o => o == null);
            if (g.instances.Count == 0)
            {
                _groups.RemoveAt(i);
                continue;
            }

            g.triangles = perInstance * g.instances.Count;
            _scannedUnits += g.instances.Count;
            _scannedTriangles += g.triangles;
        }
    }

    // -------------------------------------------------------------- list utils

    private IEnumerable<AssetGroup> VisibleGroups()
    {
        if (string.IsNullOrWhiteSpace(_search)) return _groups;

        string needle = _search.ToLowerInvariant();
        return _groups.Where(g => g.displayName.ToLowerInvariant().Contains(needle));
    }

    private IEnumerable<AssetGroup> SortGroups(IEnumerable<AssetGroup> groups)
    {
        switch (_sortBy)
        {
            case SortBy.Triangles: return groups.OrderByDescending(g => g.triangles);
            case SortBy.Name: return groups.OrderBy(g => g.displayName);
            default: return groups.OrderByDescending(g => g.instances.Count);
        }
    }

    private void SetIncluded(System.Func<AssetGroup, bool> value)
    {
        foreach (var g in VisibleGroups())
            g.include = value(g);
    }

    private void SelectObjects(List<GameObject> objects)
    {
        if (objects.Count == 0) return;

        if (objects.Count > MaxPreviewSelection)
        {
            _status = $"Selecting the first {MaxPreviewSelection:n0} of {objects.Count:n0} objects " +
                      "(selecting more than that makes the editor crawl).";
            objects = objects.GetRange(0, MaxPreviewSelection);
        }
        else
        {
            _status = $"Selected {objects.Count:n0} objects.";
        }

        Selection.objects = objects.ToArray();
    }

    /// <summary>Reveals the source asset in the Project window. Read-only — nothing is deleted there.</summary>
    private static void PingAsset(AssetGroup g)
    {
        if (!string.IsNullOrEmpty(g.assetPath))
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(g.assetPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                return;
            }
        }

        var first = g.instances.FirstOrDefault(o => o != null);
        if (first != null) EditorGUIUtility.PingObject(first);
    }

    private static string FormatCount(long value)
    {
        if (value >= 1000000) return $"{value / 1000000f:0.#}M";
        if (value >= 1000) return $"{value / 1000f:0.#}k";
        return value.ToString();
    }
}
