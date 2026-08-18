#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System.IO;
using System.Collections.Generic;

public class ShopItemCreatorWindow : EditorWindow
{
    private int activeTab = 0;
    private readonly string[] tabTitles = { "1. Prefabs Generator", "2. ScriptableObjects Creator" };

    private Vector2 mainScrollPos;

    // =========================================================================
    // TAB 1: PREFAB GENERATOR VARIABLES
    // =========================================================================
    public enum PrefabItemType { Tree, Building, Decoration, Generic }

    [System.Serializable]
    public class MeshCreationConfig
    {
        public Object meshOrModel; // Mesh or GameObject model asset
        public string customName;
        public PrefabItemType itemType = PrefabItemType.Tree;
        public bool selected = true;
    }

    private List<MeshCreationConfig> meshConfigs = new List<MeshCreationConfig>();
    private string prefabOutputFolder = "Assets/Prefabs/Shop Items";
    private GameObject timerAreaPrefab;
    private Material defaultMaterial;
    private float defaultPlacementDuration = 60f;
    private PrefabItemType bulkItemType = PrefabItemType.Tree;
    private Vector2 meshScrollPos;

    // Single Prefab generator fields
    private Object singleMeshOrModel;
    private string singlePrefabName = "New_Shop_Item";
    private PrefabItemType singleItemType = PrefabItemType.Tree;

    // Bulk Mesh Collider options
    private bool meshCollidersConvex = false;
    private bool replaceRootPrimitiveCollider = false;

    // Mode inside Tab 1 (0 = Single / Multi selection list, 1 = Bulk folder drop)
    private int tab1SubMode = 0;
    private readonly string[] tab1SubModeTitles = { "Single / Custom List", "Batch Add From Folder" };
    private DefaultAsset folderToScanForMeshes;

    // =========================================================================
    // TAB 2: SCRIPTABLE OBJECT CREATOR VARIABLES
    // =========================================================================
    [System.Serializable]
    public class ShopDataCreationConfig
    {
        public GameObject prefab;
        public bool selected = true;
        public string itemName;
        public string itemDescription;
        public Sprite itemIcon;
        public ShopAcquisitionType acquisitionType = ShopAcquisitionType.NoorCoins;
        public int noorCoinCost = 50;
        public ShopItemCategory itemCategory = ShopItemCategory.PlantsAndGardens;
        public ShopItemTier itemTier = ShopItemTier.Tier1;
        public int requiredXPLevel = 1;
        public int sortOrder = 0;
        public float placementTimerDuration = 360f;
        public GameObject placementGhostPrefab;
    }

    private string scriptableObjectOutputFolder = "Assets/Resources/Natural Placeable Shop Items";
    private string shopPrefabsSourceFolder = "Assets/Prefabs/Shop Items";
    private List<ShopDataCreationConfig> shopDataConfigs = new List<ShopDataCreationConfig>();
    private Vector2 shopDataScrollPos;

    // Global defaults for SO batch creation
    private ShopAcquisitionType defaultAcquisitionType = ShopAcquisitionType.NoorCoins;
    private int defaultNoorCoinCost = 50;
    private ShopItemCategory defaultCategory = ShopItemCategory.PlantsAndGardens;
    private ShopItemTier defaultTier = ShopItemTier.Tier1;
    private int defaultRequiredXP = 1;
    private float defaultSOPlausibleDuration = 360f;

    [MenuItem("Tools/Shop/Shop Item Creator")]
    public static void ShowWindow()
    {
        ShopItemCreatorWindow window = GetWindow<ShopItemCreatorWindow>("Shop Item Creator");
        window.minSize = new Vector2(420, 500);
    }

    private void OnEnable()
    {
        // Load default Timer Area prefab if present
        string timerAreaPath = "Assets/Prefabs/UI/Timer Area.prefab";
        timerAreaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(timerAreaPath);

        RefreshShopPrefabsList();
    }

    private void OnGUI()
    {
        // Window Title & Header
        DrawHeader();

        // Responsive Main Tabs
        GUILayout.Space(5);
        activeTab = GUILayout.Toolbar(activeTab, tabTitles, GUILayout.Height(32));
        GUILayout.Space(10);

        mainScrollPos = EditorGUILayout.BeginScrollView(mainScrollPos);

        if (activeTab == 0)
        {
            DrawTab1PrefabGenerator();
        }
        else
        {
            DrawTab2ScriptableObjectCreator();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("🛍️ Shop Item Creator Studio", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Create Shop Item Prefabs (with PlaceableItem & Colliders) from 3D Meshes on the Left Tab, " +
            "and build ShopItemData ScriptableObjects on the Right Tab.", MessageType.Info);
        EditorGUILayout.EndVertical();
    }

    // =========================================================================
    // TAB 1 GUI: PREFAB GENERATOR
    // =========================================================================
    private string meshSourceFolder = "Assets/3D Assets/Jannah Garden Assets";

    private void DrawTab1PrefabGenerator()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("📦 Section 0: Extract Meshes & Generate Materials", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        meshSourceFolder = EditorGUILayout.TextField("3D Assets Source Folder", meshSourceFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(65)))
        {
            string absPath = EditorUtility.OpenFolderPanel("Select 3D Assets Source Folder", "Assets", "");
            if (!string.IsNullOrEmpty(absPath) && absPath.StartsWith(Application.dataPath))
            {
                meshSourceFolder = "Assets" + absPath.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🎨 Generate Materials For All Asset Folders", GUILayout.Height(30)))
        {
            GenerateMaterialsForFolder(meshSourceFolder);
        }
        if (GUILayout.Button("🔍 Extract 3D Models/Meshes to List Below", GUILayout.Height(30)))
        {
            ExtractMeshesFromFolder(meshSourceFolder);
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("🖌️ Assign Respective Materials To Meshes", GUILayout.Height(30)))
        {
            AssignMaterialsToMeshes(meshSourceFolder);
        }
        EditorGUILayout.LabelField("Remaps each model to the material in its own asset folder.", EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();
        GUILayout.Space(10);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("🛠️ Section 1: Generate Shop Item Prefabs", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // Target Folder Setup
        EditorGUILayout.BeginHorizontal();
        prefabOutputFolder = EditorGUILayout.TextField("Output Prefab Folder", prefabOutputFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(65)))
        {
            string absPath = EditorUtility.OpenFolderPanel("Select Prefab Destination Folder", "Assets", "");
            if (!string.IsNullOrEmpty(absPath) && absPath.StartsWith(Application.dataPath))
            {
                prefabOutputFolder = "Assets" + absPath.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        // Asset settings
        timerAreaPrefab = (GameObject)EditorGUILayout.ObjectField("Timer Area Prefab (UI)", timerAreaPrefab, typeof(GameObject), false);
        defaultMaterial = (Material)EditorGUILayout.ObjectField("Default Mesh Material (Optional)", defaultMaterial, typeof(Material), false);
        defaultPlacementDuration = EditorGUILayout.FloatField("Placement Duration (s)", defaultPlacementDuration);

        EditorGUILayout.EndVertical();
        GUILayout.Space(10);

        DrawTab1SingleAndCustomList();
        GUILayout.Space(10);

        DrawTab1MeshColliderSection();
    }

    private void DrawTab1MeshColliderSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("🧱 Section 2: Mesh Colliders For Existing Prefabs", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Targets every prefab inside '{prefabOutputFolder}'.", EditorStyles.miniLabel);
        EditorGUILayout.Space(4);

        meshCollidersConvex = EditorGUILayout.Toggle(
            new GUIContent("Convex Colliders", "Needed for triggers and non-kinematic Rigidbodies. Leave off for static scenery so the collider follows the mesh exactly."),
            meshCollidersConvex);

        replaceRootPrimitiveCollider = EditorGUILayout.Toggle(
            new GUIContent("Remove Root Box/Capsule", "Deletes the primitive collider generated on the prefab root once its meshes have MeshColliders."),
            replaceRootPrimitiveCollider);

        if (replaceRootPrimitiveCollider)
        {
            EditorGUILayout.HelpBox("PlaceableItem.Awake() re-adds a BoxCollider at runtime when the root has no Collider, " +
                                    "so the root will still end up with one in play mode.", MessageType.Warning);
        }

        GUI.backgroundColor = new Color(1f, 0.75f, 0.3f);
        if (GUILayout.Button("🧱 Add Mesh Colliders To All Shop Item Prefab Meshes", GUILayout.Height(38)))
        {
            AddMeshCollidersToAllPrefabs(prefabOutputFolder);
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.LabelField("Adds a MeshCollider to every mesh in each prefab. Timer / UI objects are skipped.", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();
    }

    private void DrawTab1SingleAndCustomList()
    {
        // Quick Drag & Add Section
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Add Mesh / Model Asset", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        singleMeshOrModel = EditorGUILayout.ObjectField("Mesh / Model", singleMeshOrModel, typeof(Object), false);
        singleItemType = (PrefabItemType)EditorGUILayout.EnumPopup(singleItemType, GUILayout.Width(100));

        if (GUILayout.Button("➕ Add To List", GUILayout.Width(100)))
        {
            if (singleMeshOrModel != null)
            {
                AddMeshToConfig(singleMeshOrModel, singleItemType);
                singleMeshOrModel = null;
            }
            else
            {
                EditorUtility.DisplayDialog("Warning", "Please select a Mesh or Model GameObject first.", "OK");
            }
        }
        EditorGUILayout.EndHorizontal();

        // Drag and drop area box
        Rect dropArea = GUILayoutUtility.GetRect(0f, 45f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "\n📥 Drag & Drop Meshes / FBX Models Here to Add", EditorStyles.helpBox);
        HandleDragAndDrop(dropArea);

        EditorGUILayout.EndVertical();
        GUILayout.Space(10);

        // List View of Added Items
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Selected Meshes ({meshConfigs.Count})", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Select All", GUILayout.Width(75)))
        {
            meshConfigs.ForEach(c => c.selected = true);
        }
        if (GUILayout.Button("Deselect All", GUILayout.Width(85)))
        {
            meshConfigs.ForEach(c => c.selected = false);
        }
        if (GUILayout.Button("Clear List", GUILayout.Width(75)))
        {
            meshConfigs.Clear();
        }
        EditorGUILayout.EndHorizontal();

        if (meshConfigs.Count == 0)
        {
            EditorGUILayout.HelpBox("No meshes added yet. Assign a mesh above or drag & drop assets into the drop zone.", MessageType.Warning);
        }
        else
        {
            meshScrollPos = EditorGUILayout.BeginScrollView(meshScrollPos, GUILayout.MaxHeight(250));
            for (int i = 0; i < meshConfigs.Count; i++)
            {
                var cfg = meshConfigs[i];
                EditorGUILayout.BeginHorizontal("box");
                cfg.selected = EditorGUILayout.Toggle(cfg.selected, GUILayout.Width(20));
                cfg.meshOrModel = EditorGUILayout.ObjectField(cfg.meshOrModel, typeof(Object), false, GUILayout.Width(180));
                cfg.customName = EditorGUILayout.TextField(cfg.customName);
                cfg.itemType = (PrefabItemType)EditorGUILayout.EnumPopup(cfg.itemType, GUILayout.Width(90));

                if (GUILayout.Button("❌", GUILayout.Width(25)))
                {
                    meshConfigs.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.3f, 0.9f, 0.3f);
        if (GUILayout.Button("⚙️ Generate Selected Prefabs", GUILayout.Height(38)))
        {
            GeneratePrefabsFromList();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndVertical();
    }

    private void HandleDragAndDrop(Rect dropArea)
    {
        Event evt = Event.current;
        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            if (!dropArea.Contains(evt.mousePosition)) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object draggedObj in DragAndDrop.objectReferences)
                {
                    AddMeshToConfig(draggedObj, bulkItemType);
                }
            }
            evt.Use();
        }
    }

    private void AddMeshToConfig(Object obj, PrefabItemType type)
    {
        if (obj is Mesh || obj is GameObject)
        {
            meshConfigs.Add(new MeshCreationConfig
            {
                meshOrModel = obj,
                customName = CleanMeshName(obj.name),
                itemType = type,
                selected = true
            });
        }
    }

    private void ScanFolderForMeshes(string folderPath)
    {
        meshConfigs.Clear();
        string[] guids = AssetDatabase.FindAssets("t:Mesh t:Model", new[] { folderPath });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset != null && (asset is Mesh || asset is GameObject))
            {
                AddMeshToConfig(asset, bulkItemType);
            }
        }
        tab1SubMode = 0; // Switch to list view
    }

    private void GeneratePrefabsFromList()
    {
        if (!AssetDatabase.IsValidFolder(prefabOutputFolder))
        {
            Directory.CreateDirectory(prefabOutputFolder);
            AssetDatabase.Refresh();
        }

        int createdCount = 0;
        foreach (var cfg in meshConfigs)
        {
            if (!cfg.selected || cfg.meshOrModel == null) continue;

            string rawName = string.IsNullOrEmpty(cfg.customName) ? cfg.meshOrModel.name : cfg.customName;
            string cleanName = CleanMeshName(rawName);

            string prefabPath = $"{prefabOutputFolder}/{cleanName}.prefab";

            // Create Root GameObject
            GameObject rootObj = new GameObject(cleanName);
            rootObj.layer = 15; // Set to layer 15 like building prefabs, or default

            // Handle mesh attachment
            GameObject modelChild = null;
            MeshFilter mf = null;
            MeshRenderer mr = null;

            if (cfg.meshOrModel is GameObject modelPrefab)
            {
                modelChild = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
                modelChild.transform.SetParent(rootObj.transform, false);
                modelChild.name = cleanName + "_Model";
            }
            else if (cfg.meshOrModel is Mesh meshAsset)
            {
                modelChild = new GameObject(cleanName + "_Mesh");
                modelChild.transform.SetParent(rootObj.transform, false);
                mf = modelChild.AddComponent<MeshFilter>();
                mf.sharedMesh = meshAsset;
                mr = modelChild.AddComponent<MeshRenderer>();
                if (defaultMaterial != null)
                {
                    mr.sharedMaterial = defaultMaterial;
                }
            }

            // Calculate Combined Bounds for Collider & Timer height
            Bounds combinedBounds = CalculateBounds(rootObj);

            // Add Collider
            Collider col = null;
            if (cfg.itemType == PrefabItemType.Tree)
            {
                CapsuleCollider capCol = rootObj.AddComponent<CapsuleCollider>();
                capCol.center = combinedBounds.center;
                capCol.radius = Mathf.Max(combinedBounds.extents.x, combinedBounds.extents.z) * 0.5f;
                if (capCol.radius < 0.2f) capCol.radius = 0.25f;
                capCol.height = Mathf.Max(combinedBounds.size.y, 1f);
                capCol.direction = 1; // Y-axis
                col = capCol;
            }
            else
            {
                BoxCollider boxCol = rootObj.AddComponent<BoxCollider>();
                boxCol.center = combinedBounds.center;
                boxCol.size = Vector3.Max(combinedBounds.size, new Vector3(0.5f, 0.5f, 0.5f));
                col = boxCol;
            }

            // Instantiate Timer Area UI Prefab if present
            GameObject timerInstance = null;
            if (timerAreaPrefab != null)
            {
                timerInstance = (GameObject)PrefabUtility.InstantiatePrefab(timerAreaPrefab, rootObj.transform);
                timerInstance.name = "Timer Area";
                timerInstance.transform.localPosition = new Vector3(0, combinedBounds.max.y + 0.5f, 0);
            }

            // Add PlaceableItem Script
            PlaceableItem placeable = rootObj.AddComponent<PlaceableItem>();
            placeable.placementDuration = defaultPlacementDuration;
            placeable.remainingDuration = defaultPlacementDuration;
            placeable.isTree = (cfg.itemType == PrefabItemType.Tree);
            placeable.isBuilding = (cfg.itemType == PrefabItemType.Building);
            if (timerInstance != null)
            {
                placeable.timerHolder = timerInstance;
            }

            // Find all MeshRenderers in hierarchy and attach to placeable.itemRenderers
            MeshRenderer[] renderers = rootObj.GetComponentsInChildren<MeshRenderer>(true);
            placeable.itemRenderers = renderers;

            // Save as Prefab
            PrefabUtility.SaveAsPrefabAsset(rootObj, prefabPath);
            DestroyImmediate(rootObj);

            createdCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Success", $"Successfully created {createdCount} Prefab(s) under '{prefabOutputFolder}'!", "OK");
        RefreshShopPrefabsList();
    }

    // Walks every prefab in the folder and gives each of its meshes a MeshCollider
    private void AddMeshCollidersToAllPrefabs(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("Error", $"Folder path '{folderPath}' does not exist.", "OK");
            return;
        }

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
        if (prefabGuids.Length == 0)
        {
            EditorUtility.DisplayDialog("Nothing To Do", $"No prefabs found under '{folderPath}'.", "OK");
            return;
        }

        bool proceed = EditorUtility.DisplayDialog("Add Mesh Colliders",
            $"Add a MeshCollider to every mesh inside {prefabGuids.Length} prefab(s) under '{folderPath}'?\n\n" +
            "This edits the prefab assets directly and cannot be undone with Ctrl+Z.",
            "Add Colliders", "Cancel");
        if (!proceed) return;

        int addedColliders = 0;
        int changedPrefabs = 0;
        int alreadyHad = 0;
        int removedPrimitives = 0;
        List<string> withoutMeshes = new List<string>();

        try
        {
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                string prefabName = Path.GetFileNameWithoutExtension(prefabPath);

                EditorUtility.DisplayProgressBar("Adding Mesh Colliders",
                    $"{prefabName}  ({i + 1}/{prefabGuids.Length})", (float)i / prefabGuids.Length);

                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null) continue;

                try
                {
                    bool dirty = false;
                    int meshCount = 0;

                    foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null) continue;
                        if (IsTimerOrUIObject(filter.transform)) continue;

                        meshCount++;

                        MeshCollider existing = filter.GetComponent<MeshCollider>();
                        if (existing != null)
                        {
                            alreadyHad++;

                            // Repair colliders left without a mesh, otherwise leave them untouched
                            if (existing.sharedMesh == null)
                            {
                                existing.sharedMesh = filter.sharedMesh;
                                existing.convex = meshCollidersConvex;
                                dirty = true;
                            }
                            continue;
                        }

                        MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        collider.convex = meshCollidersConvex;

                        addedColliders++;
                        dirty = true;
                    }

                    if (meshCount == 0)
                    {
                        withoutMeshes.Add(prefabName);
                    }
                    else if (replaceRootPrimitiveCollider)
                    {
                        foreach (Collider rootCol in root.GetComponents<Collider>())
                        {
                            if (rootCol is MeshCollider) continue;

                            DestroyImmediate(rootCol, true);
                            removedPrimitives++;
                            dirty = true;
                        }
                    }

                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                        changedPrefabs++;
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

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string report = $"Added {addedColliders} MeshCollider(s) across {changedPrefabs} of {prefabGuids.Length} prefab(s).";
        if (alreadyHad > 0) report += $"\n{alreadyHad} mesh(es) already had a MeshCollider and were left as-is.";
        if (removedPrimitives > 0) report += $"\nRemoved {removedPrimitives} primitive collider(s) from prefab roots.";

        if (withoutMeshes.Count > 0)
        {
            Debug.LogWarning($"[Shop Item Creator] No meshes found in {withoutMeshes.Count} prefab(s):\n" +
                             string.Join("\n", withoutMeshes.ToArray()));

            int previewCount = Mathf.Min(withoutMeshes.Count, 8);
            report += $"\n\nNo meshes found in {withoutMeshes.Count} prefab(s):\n" +
                      string.Join("\n", withoutMeshes.GetRange(0, previewCount).ToArray());
            if (withoutMeshes.Count > previewCount) report += "\n... (see Console for the full list)";
        }

        EditorUtility.DisplayDialog("Mesh Colliders", report, "OK");
    }

    // Timer Area / world-space canvases render through MeshFilters too, but must stay collider-free
    private static bool IsTimerOrUIObject(Transform target)
    {
        if (target is RectTransform) return true;
        if (target.GetComponentInParent<Canvas>() != null) return true;

        for (Transform t = target; t != null; t = t.parent)
        {
            if (t.name.IndexOf("Timer", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private Bounds CalculateBounds(GameObject target)
    {
        MeshRenderer[] renderers = target.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }
        // Convert to local space
        b.center -= target.transform.position;
        return b;
    }

    // =========================================================================
    // TAB 2 GUI: SCRIPTABLEOBJECT CREATOR
    // =========================================================================
    private string iconsSourceFolder = "Assets/2D Assets";

    private void DrawTab2ScriptableObjectCreator()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("📦 Section 2: Create ShopItemData Assets", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        // Folders Configuration
        EditorGUILayout.BeginHorizontal();
        scriptableObjectOutputFolder = EditorGUILayout.TextField("Data Output Folder", scriptableObjectOutputFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(65)))
        {
            string absPath = EditorUtility.OpenFolderPanel("Select Data Output Folder", "Assets", "");
            if (!string.IsNullOrEmpty(absPath) && absPath.StartsWith(Application.dataPath))
            {
                scriptableObjectOutputFolder = "Assets" + absPath.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        shopPrefabsSourceFolder = EditorGUILayout.TextField("Shop Prefabs Folder", shopPrefabsSourceFolder);
        if (GUILayout.Button("Refresh", GUILayout.Width(65)))
        {
            RefreshShopPrefabsList();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        iconsSourceFolder = EditorGUILayout.TextField("Icons Source Folder", iconsSourceFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(65)))
        {
            string absPath = EditorUtility.OpenFolderPanel("Select Icons Folder", "Assets", "");
            if (!string.IsNullOrEmpty(absPath) && absPath.StartsWith(Application.dataPath))
            {
                iconsSourceFolder = "Assets" + absPath.Substring(Application.dataPath.Length);
            }
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("🖼️ Auto-Match & Assign Icons in Bulk", GUILayout.Height(26)))
        {
            AutoAssignIconsInBulk();
        }

        EditorGUILayout.EndVertical();
        GUILayout.Space(10);

        // Batch Settings Adjuster Box
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("⚡ Batch Default Values (Applied to newly scanned prefabs)", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        defaultCategory = (ShopItemCategory)EditorGUILayout.EnumPopup("Category", defaultCategory);
        defaultTier = (ShopItemTier)EditorGUILayout.EnumPopup("Tier", defaultTier);
        defaultAcquisitionType = (ShopAcquisitionType)EditorGUILayout.EnumPopup("Acquisition", defaultAcquisitionType);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        defaultNoorCoinCost = EditorGUILayout.IntField("Cost (Coins)", defaultNoorCoinCost);
        defaultRequiredXP = EditorGUILayout.IntField("Req. XP Level", defaultRequiredXP);
        defaultSOPlausibleDuration = EditorGUILayout.FloatField("Placement Duration", defaultSOPlausibleDuration);
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Apply Defaults to All Items Below"))
        {
            foreach (var item in shopDataConfigs)
            {
                item.itemCategory = defaultCategory;
                item.itemTier = defaultTier;
                item.acquisitionType = defaultAcquisitionType;
                item.noorCoinCost = defaultNoorCoinCost;
                item.requiredXPLevel = defaultRequiredXP;
                item.placementTimerDuration = defaultSOPlausibleDuration;
            }
        }
        EditorGUILayout.EndVertical();
        GUILayout.Space(10);

        // Item Cards / Table List
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Discovered Shop Prefabs ({shopDataConfigs.Count})", EditorStyles.boldLabel);
        if (GUILayout.Button("Select All", GUILayout.Width(75))) shopDataConfigs.ForEach(c => c.selected = true);
        if (GUILayout.Button("Deselect All", GUILayout.Width(85))) shopDataConfigs.ForEach(c => c.selected = false);
        EditorGUILayout.EndHorizontal();

        if (shopDataConfigs.Count == 0)
        {
            EditorGUILayout.HelpBox($"No shop prefabs found in '{shopPrefabsSourceFolder}'. Generate prefabs in Tab 1 first!", MessageType.Warning);
        }
        else
        {
            shopDataScrollPos = EditorGUILayout.BeginScrollView(shopDataScrollPos, GUILayout.MaxHeight(320));
            for (int i = 0; i < shopDataConfigs.Count; i++)
            {
                DrawShopDataRow(shopDataConfigs[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(8);
        GUI.backgroundColor = new Color(0.2f, 0.7f, 1.0f);
        if (GUILayout.Button("✨ Create Selected ShopItemData ScriptableObjects", GUILayout.Height(40)))
        {
            GenerateShopItemDataAssets();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndVertical();
    }

    private void DrawShopDataRow(ShopDataCreationConfig cfg)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        cfg.selected = EditorGUILayout.Toggle(cfg.selected, GUILayout.Width(20));
        cfg.prefab = (GameObject)EditorGUILayout.ObjectField(cfg.prefab, typeof(GameObject), false, GUILayout.Width(160));
        cfg.itemName = EditorGUILayout.TextField("Name", cfg.itemName);
        cfg.itemIcon = (Sprite)EditorGUILayout.ObjectField("Icon", cfg.itemIcon, typeof(Sprite), false, GUILayout.Width(140));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        cfg.itemCategory = (ShopItemCategory)EditorGUILayout.EnumPopup("Category", cfg.itemCategory);
        cfg.itemTier = (ShopItemTier)EditorGUILayout.EnumPopup("Tier", cfg.itemTier);
        cfg.acquisitionType = (ShopAcquisitionType)EditorGUILayout.EnumPopup("Acquisition", cfg.acquisitionType);
        if (cfg.acquisitionType == ShopAcquisitionType.NoorCoins)
        {
            cfg.noorCoinCost = EditorGUILayout.IntField("Cost", cfg.noorCoinCost);
        }
        cfg.requiredXPLevel = EditorGUILayout.IntField("XP Req", cfg.requiredXPLevel);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void RefreshShopPrefabsList()
    {
        shopDataConfigs.Clear();
        if (!AssetDatabase.IsValidFolder(shopPrefabsSourceFolder)) return;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { shopPrefabsSourceFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                string cleanName = CleanMeshName(prefab.name).Replace("_", " ");
                shopDataConfigs.Add(new ShopDataCreationConfig
                {
                    prefab = prefab,
                    itemName = cleanName,
                    itemDescription = $"Beautiful {cleanName} to decorate your garden.",
                    itemCategory = defaultCategory,
                    itemTier = defaultTier,
                    acquisitionType = defaultAcquisitionType,
                    noorCoinCost = defaultNoorCoinCost,
                    requiredXPLevel = defaultRequiredXP,
                    placementTimerDuration = defaultSOPlausibleDuration,
                    selected = true
                });
            }
        }
    }

    private void AutoAssignIconsInBulk()
    {
        if (!AssetDatabase.IsValidFolder(iconsSourceFolder))
        {
            EditorUtility.DisplayDialog("Error", $"Icons source folder '{iconsSourceFolder}' is invalid.", "OK");
            return;
        }

        string[] spriteGuids = AssetDatabase.FindAssets("t:Sprite", new[] { iconsSourceFolder });
        if (spriteGuids.Length == 0)
        {
            spriteGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { iconsSourceFolder });
        }

        Dictionary<string, Sprite> iconMap = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);

        foreach (string guid in spriteGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null)
                {
                    string importerPath = AssetDatabase.GetAssetPath(tex);
                    TextureImporter importer = AssetImporter.GetAtPath(importerPath) as TextureImporter;
                    if (importer != null && importer.textureType != TextureImporterType.Sprite)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.SaveAndReimport();
                        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    }
                }
            }

            if (sprite != null)
            {
                string cleanedSpriteName = CleanMeshName(sprite.name).Replace("_", "").Replace(" ", "").ToLower();
                if (!iconMap.ContainsKey(cleanedSpriteName))
                {
                    iconMap[cleanedSpriteName] = sprite;
                }
            }
        }

        int matchedCount = 0;
        foreach (var item in shopDataConfigs)
        {
            if (item.prefab == null) continue;

            string cleanedItemName = CleanMeshName(item.itemName).Replace("_", "").Replace(" ", "").ToLower();
            string cleanedPrefabName = CleanMeshName(item.prefab.name).Replace("_", "").Replace(" ", "").ToLower();

            Sprite match = null;
            if (iconMap.TryGetValue(cleanedItemName, out match) || iconMap.TryGetValue(cleanedPrefabName, out match))
            {
                item.itemIcon = match;
                matchedCount++;
            }
            else
            {
                // Fuzzy/Substring Match fallback
                foreach (var kvp in iconMap)
                {
                    if (cleanedItemName.Contains(kvp.Key) || kvp.Key.Contains(cleanedItemName) ||
                        cleanedPrefabName.Contains(kvp.Key) || kvp.Key.Contains(cleanedPrefabName))
                    {
                        item.itemIcon = kvp.Value;
                        matchedCount++;
                        break;
                    }
                }
            }
        }

        EditorUtility.DisplayDialog("Icons Matched", $"Successfully matched and assigned icons for {matchedCount} of {shopDataConfigs.Count} items!", "OK");
    }

    private void GenerateShopItemDataAssets()
    {
        if (!AssetDatabase.IsValidFolder(scriptableObjectOutputFolder))
        {
            Directory.CreateDirectory(scriptableObjectOutputFolder);
            AssetDatabase.Refresh();
        }

        AddressableAssetSettings addressableSettings = AddressableAssetSettingsDefaultObject.Settings;
        var remoteGroup = AddressableItemAuthoring.GetOrCreateRemoteGroup(addressableSettings);

        int createdCount = 0;
        foreach (var cfg in shopDataConfigs)
        {
            if (!cfg.selected || cfg.prefab == null) continue;

            string cleanAssetName = string.IsNullOrEmpty(cfg.itemName) ? cfg.prefab.name : cfg.itemName.Replace(" ", "_");
            string assetPath = $"{scriptableObjectOutputFolder}/{cleanAssetName}_Data.asset";

            // Create Instance
            ShopItemData itemData = CreateInstance<ShopItemData>();

            itemData.itemName = cfg.itemName;
            itemData.itemDescription = cfg.itemDescription;
            itemData.itemIcon = cfg.itemIcon;
            itemData.itemID = System.Guid.NewGuid().ToString("N");

            itemData.acquisitionType = cfg.acquisitionType;
            itemData.noorCoinCost = cfg.noorCoinCost;

            itemData.itemCategory = cfg.itemCategory;
            itemData.itemTier = cfg.itemTier;
            itemData.requiredXPLevel = cfg.requiredXPLevel;
            itemData.sortOrder = cfg.sortOrder;

            AddressableItemAuthoring.AssignPrefab(itemData.itemPrefabRef, cfg.prefab, addressableSettings, remoteGroup);
            AddressableItemAuthoring.AssignPrefab(itemData.itemPlacementModelPrefabRef, cfg.placementGhostPrefab, addressableSettings, remoteGroup);
            itemData.placementTimerDuration = cfg.placementTimerDuration;

            AssetDatabase.CreateAsset(itemData, assetPath);
            createdCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Success", $"Successfully created {createdCount} ShopItemData asset(s) under '{scriptableObjectOutputFolder}'!", "OK");
    }

    private void ExtractMeshesFromFolder(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("Error", $"Folder path '{folderPath}' does not exist.", "OK");
            return;
        }

        meshConfigs.Clear();
        string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { folderPath });
        int added = 0;

        foreach (string guid in modelGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (modelPrefab != null)
            {
                meshConfigs.Add(new MeshCreationConfig
                {
                    meshOrModel = modelPrefab,
                    customName = CleanMeshName(modelPrefab.name),
                    itemType = bulkItemType,
                    selected = true
                });
                added++;
            }
        }

        EditorUtility.DisplayDialog("Mesh Extraction Complete", $"Found and extracted {added} 3D model asset(s) into the generation list!", "OK");
        tab1SubMode = 0; // Switch to list view
    }

    private void GenerateMaterialsForFolder(string rootFolderPath)
    {
        if (!AssetDatabase.IsValidFolder(rootFolderPath))
        {
            EditorUtility.DisplayDialog("Error", $"Folder path '{rootFolderPath}' does not exist.", "OK");
            return;
        }

        // Get subdirectories (asset folders)
        string fullRootPath = Path.Combine(Directory.GetCurrentDirectory(), rootFolderPath).Replace("\\", "/");
        if (!Directory.Exists(fullRootPath)) return;

        string[] subDirs = Directory.GetDirectories(fullRootPath, "*", SearchOption.AllDirectories);
        int matCount = 0;

        // Standard Universal Render Pipeline or Built-in Lit shader check
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");

        foreach (string subDir in subDirs)
        {
            string relativeDirPath = "Assets" + subDir.Substring(Application.dataPath.Length).Replace("\\", "/");
            string[] pngGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { relativeDirPath });

            if (pngGuids.Length == 0) continue;

            Texture2D baseTex = null;
            Texture2D normalTex = null;
            Texture2D metallicTex = null;
            Texture2D roughnessTex = null;
            Texture2D emissionTex = null;

            foreach (string guid in pngGuids)
            {
                string texPath = AssetDatabase.GUIDToAssetPath(guid);
                string texName = Path.GetFileNameWithoutExtension(texPath).ToLower();
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

                if (texName.EndsWith("_normal")) normalTex = tex;
                else if (texName.EndsWith("_metallic")) metallicTex = tex;
                else if (texName.EndsWith("_roughness")) roughnessTex = tex;
                else if (texName.EndsWith("_emission")) emissionTex = tex;
                else if (!texName.Contains("_") || texName.EndsWith("_texture")) baseTex = tex;
            }

            if (baseTex == null && pngGuids.Length > 0)
            {
                baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(pngGuids[0]));
            }

            if (baseTex != null)
            {
                string folderName = Path.GetFileName(relativeDirPath);
                string matName = CleanMeshName(folderName) + "_Mat";
                string matPath = $"{relativeDirPath}/{matName}.mat";

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(litShader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }

                // Assign textures to material
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", baseTex);
                else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", baseTex);

                if (normalTex != null)
                {
                    if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normalTex);
                }
                if (metallicTex != null)
                {
                    if (mat.HasProperty("_MetallicGlossMap")) mat.SetTexture("_MetallicGlossMap", metallicTex);
                }
                if (roughnessTex != null)
                {
                    if (mat.HasProperty("_SpecGlossMap")) mat.SetTexture("_SpecGlossMap", roughnessTex);
                }
                if (emissionTex != null)
                {
                    if (mat.HasProperty("_EmissionMap"))
                    {
                        mat.SetTexture("_EmissionMap", emissionTex);
                        mat.EnableKeyword("_EMISSION");
                    }
                }

                EditorUtility.SetDirty(mat);
                matCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Material Generation", $"Successfully created/updated {matCount} Material(s) across subfolders!", "OK");
    }

    private void AssignMaterialsToMeshes(string rootFolderPath)
    {
        if (!AssetDatabase.IsValidFolder(rootFolderPath))
        {
            EditorUtility.DisplayDialog("Error", $"Folder path '{rootFolderPath}' does not exist.", "OK");
            return;
        }

        string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { rootFolderPath });
        if (modelGuids.Length == 0)
        {
            EditorUtility.DisplayDialog("Nothing To Assign", $"No 3D models found under '{rootFolderPath}'.", "OK");
            return;
        }

        // Name lookup used as a fallback for models that have no material in their own folder tree
        Dictionary<string, Material> materialsByName = new Dictionary<string, Material>(System.StringComparer.OrdinalIgnoreCase);
        foreach (string matGuid in AssetDatabase.FindAssets("t:Material", new[] { rootFolderPath }))
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(matGuid));
            if (mat == null) continue;

            string key = MaterialMatchKey(mat.name);
            if (!materialsByName.ContainsKey(key)) materialsByName[key] = mat;
        }

        int changedModels = 0;
        int assignedSlots = 0;
        List<string> unmatched = new List<string>();

        try
        {
            for (int i = 0; i < modelGuids.Length; i++)
            {
                string modelPath = AssetDatabase.GUIDToAssetPath(modelGuids[i]);
                string modelName = Path.GetFileNameWithoutExtension(modelPath);

                EditorUtility.DisplayProgressBar("Assigning Materials",
                    $"{modelName}  ({i + 1}/{modelGuids.Length})", (float)i / modelGuids.Length);

                ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (importer == null) continue;

                Material match = FindMaterialForModel(modelPath, rootFolderPath, materialsByName);
                if (match == null)
                {
                    unmatched.Add(modelName);
                    continue;
                }

                // Materials must be imported for the model to expose remappable slots
                if (importer.materialImportMode == ModelImporterMaterialImportMode.None)
                {
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    importer.SaveAndReimport();
                }

                // Remaps are only honoured while the model keeps its materials embedded
                bool changed = importer.materialLocation != ModelImporterMaterialLocation.InPrefab;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

                List<AssetImporter.SourceAssetIdentifier> slots = GetModelMaterialSlots(importer);
                if (slots.Count == 0)
                {
                    unmatched.Add($"{modelName} (no material slots)");
                    continue;
                }

                Dictionary<AssetImporter.SourceAssetIdentifier, Object> externalMap = importer.GetExternalObjectMap();
                foreach (AssetImporter.SourceAssetIdentifier slot in slots)
                {
                    Object current;
                    if (externalMap.TryGetValue(slot, out current) && current == match) continue;

                    importer.AddRemap(slot, match);
                    assignedSlots++;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    changedModels++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string report = $"Assigned materials to {assignedSlots} slot(s) across {changedModels} of {modelGuids.Length} model(s).";
        if (unmatched.Count > 0)
        {
            Debug.LogWarning($"[Shop Item Creator] No material matched for {unmatched.Count} model(s):\n" +
                             string.Join("\n", unmatched.ToArray()));

            int previewCount = Mathf.Min(unmatched.Count, 8);
            report += $"\n\nNo material matched for {unmatched.Count} model(s):\n" +
                      string.Join("\n", unmatched.GetRange(0, previewCount).ToArray());
            if (unmatched.Count > previewCount) report += "\n... (see Console for the full list)";
        }

        EditorUtility.DisplayDialog("Material Assignment", report, "OK");
    }

    // Locates the material belonging to a model: nearest one in its own folder tree, then by name
    private static Material FindMaterialForModel(string modelPath, string rootFolderPath,
        Dictionary<string, Material> materialsByName)
    {
        string modelKey = MaterialMatchKey(Path.GetFileNameWithoutExtension(modelPath));
        string root = rootFolderPath.Replace("\\", "/").TrimEnd('/');
        string dir = Path.GetDirectoryName(modelPath).Replace("\\", "/");

        while (!string.IsNullOrEmpty(dir))
        {
            Material fallback = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { dir }))
            {
                string matPath = AssetDatabase.GUIDToAssetPath(guid);

                // FindAssets recurses, so keep only materials sitting directly in this folder
                if (Path.GetDirectoryName(matPath).Replace("\\", "/") != dir) continue;

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) continue;

                if (MaterialMatchKey(mat.name) == modelKey) return mat;
                if (fallback == null) fallback = mat;
            }

            if (fallback != null) return fallback;

            if (dir.Length <= root.Length) break;
            dir = Path.GetDirectoryName(dir).Replace("\\", "/");
        }

        Material byName;
        return materialsByName.TryGetValue(modelKey, out byName) ? byName : null;
    }

    // Material slots of an imported model, covering both fresh imports and already-remapped ones
    private static List<AssetImporter.SourceAssetIdentifier> GetModelMaterialSlots(ModelImporter importer)
    {
        List<AssetImporter.SourceAssetIdentifier> slots = new List<AssetImporter.SourceAssetIdentifier>();
        HashSet<string> seen = new HashSet<string>();

        // Previously remapped slots keep their identifier, which makes re-runs idempotent
        foreach (var kvp in importer.GetExternalObjectMap())
        {
            if (kvp.Key.type == typeof(Material) && seen.Add(kvp.Key.name))
            {
                slots.Add(kvp.Key);
            }
        }

        // First run: the model's own materials are imported as sub-assets
        foreach (Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(importer.assetPath))
        {
            Material embedded = sub as Material;
            if (embedded != null && seen.Add(embedded.name))
            {
                slots.Add(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name));
            }
        }

        return slots;
    }

    // Normalised key so "Meshy_AI_Corinthian_Column_0721143532_texture" matches "AI_Corinthian_Column_Mat"
    private static string MaterialMatchKey(string rawName)
    {
        string name = CleanMeshName(rawName);
        if (name.EndsWith("_Mat", System.StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - 4);
        }
        return name.Replace("_", "").Replace(" ", "").ToLower();
    }

    public static string CleanMeshName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return "Shop_Item";

        string name = rawName;

        // Remove Meshy_ or Meshy_AI_ prefix if present
        if (name.StartsWith("Meshy_"))
        {
            name = name.Substring(6);
        }

        // Remove trailing _texture or _texture_0 etc if present
        name = System.Text.RegularExpressions.Regex.Replace(name, @"_texture.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove timestamp strings like _0721143608 or _1234567890 (8-14 digits surrounded by underscores or at end)
        name = System.Text.RegularExpressions.Regex.Replace(name, @"_\d{8,14}(?=_|$)", "");

        // Trim trailing/leading underscores
        name = name.Trim('_');

        return string.IsNullOrEmpty(name) ? rawName : name;
    }
}
#endif
