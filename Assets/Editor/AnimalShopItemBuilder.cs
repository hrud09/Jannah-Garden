using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Turns the garden's ambient animals into shop items. The camels and pegasi used to be baked into the
/// Jannah Garden scene; now they are bought (by watching a rewarded ad) from the shop's Animals tab and
/// placed like any other item.
///
/// For each of the two animals (the classic-coat camel and the pegasus) this
/// builds a placeable prefab — a still root carrying <see cref="PlaceableItem"/> + <see cref="PlacedAnimal"/>,
/// with the animal itself as a child so it can wander and be ridden without dragging its saved position
/// along — renders a card icon, registers the prefab in the remote Addressables group, and adds or
/// updates an ad-only entry in the shop database. Safe to re-run: items are matched by itemID.
///
/// The scene animals are read as the source of truth (scale, coat, tuning), so run Build before Remove.
/// </summary>
public static class AnimalShopItemBuilder
{
    private const string ShopPrefabPath = "Assets/Prefabs/UI/In-Game Shop.prefab";
    private const string PrefabFolder = "Assets/Prefabs/Shop Animals";
    private const string IconFolder = "Assets/2D Assets/UI/Shop Animal Icons";

    private const float ArrivalSeconds = 60f;
    private const float CamelWander = 6f;
    private const float PegasusWander = 8f;

    private struct Look
    {
        public string id, name, file;
        public int tier, sort, level;
    }

    // The game has two animals: one camel (the classic coat) and the pegasus. Scene camels in the other
    // coat colourways are skipped rather than turned into extra items.
    private static readonly Dictionary<string, Look> CamelLooks = new Dictionary<string, Look>
    {
        { "Camel_Coat", new Look { id = "animal_camel", name = "Camel", file = "Camel", tier = 1, sort = 0, level = 1 } },
    };

    private static readonly Look PegasusLook =
        new Look { id = "animal_pegasus", name = "Pegasus", file = "Pegasus", tier = 4, sort = 10, level = 1 };

    private const string CamelDescription =
        "A gentle camel in a hand-woven coat. It grazes and strolls near where you place it. " +
        "Walk up to it to ride, even across the lake.";
    private const string PegasusDescription =
        "A winged horse that grazes near where you place it. Climb on to gallop, take off and fly over the garden.";

    [MenuItem("Jannah/Animals/Build Animal Shop Items")]
    public static void Build()
    {
        GrazingAnimal[] sceneAnimals = Object.FindObjectsByType<GrazingAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(a => !EditorUtility.IsPersistent(a) && a.GetComponentInParent<PlaceableItem>() == null)
            .OrderBy(a => a.name)
            .ToArray();
        if (sceneAnimals.Length == 0)
        {
            Debug.LogError("[AnimalShop] No scene animals to build from. Open the Jannah Garden scene (before removing them).");
            return;
        }

        EnsureFolder(PrefabFolder);
        EnsureFolder(IconFolder);

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetGroup group = settings != null ? AddressableItemAuthoring.GetOrCreateRemoteGroup(settings) : null;
        if (group == null) Debug.LogWarning("[AnimalShop] No Addressables settings — prefabs will not be registered.");

        ShopItemsData database = LoadDatabase();
        if (database == null) return;

        var built = new List<string>();
        var seen = new HashSet<string>();
        foreach (GrazingAnimal source in sceneAnimals)
        {
            Look look = LookFor(source);
            if (string.IsNullOrEmpty(look.id) || !seen.Add(look.id)) continue; // Not a shop animal, or already built.

            GameObject prefab = BuildPrefab(source, look);
            if (prefab == null) continue;

            Sprite icon = RenderIcon(prefab, look.file);

            ShopItemData item = FindOrAddItem(database, look.id);
            item.itemName = look.name;
            item.itemDescription = source.canFly ? PegasusDescription : CamelDescription;
            item.itemIcon = icon;
            item.acquisitionType = ShopAcquisitionType.RewardedAd;
            item.isDailyOffer = false;
            item.noorCoinCost = 0;
            item.noorCoinReward = 0;
            item.itemCategory = ShopItemCategory.Animals;
            item.itemTier = (ShopItemTier)look.tier;
            item.sortOrder = look.sort;
            item.requiredXPLevel = look.level;
            item.placementTimerDuration = ArrivalSeconds;
            item.gridFootprintOverride = FootprintFor(prefab);
            item.footprintScale = 1f;
            if (group != null)
            {
                if (item.itemPrefabRef == null) item.itemPrefabRef = new UnityEngine.AddressableAssets.AssetReferenceGameObject(string.Empty);
                AddressableItemAuthoring.AssignPrefab(item.itemPrefabRef, prefab, settings, group);
            }
            item.itemPlacementModelPrefabRef = new UnityEngine.AddressableAssets.AssetReferenceGameObject(string.Empty);

            built.Add($"{look.name} ({look.id}) → {AssetDatabase.GetAssetPath(prefab)}");
        }

        EditorUtility.SetDirty(database);
        if (settings != null) EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        // Give the new category its tab (no-op if it already has one) and keep the database in tab order.
        ShopCategoryTabBuilder.RebuildTabs();
        ShopCategoryTabBuilder.RefreshShopItemList();

        Debug.Log($"[AnimalShop] Built {built.Count} animal shop items:\n  " + string.Join("\n  ", built));
    }

    [MenuItem("Jannah/Animals/Remove Ambient Animals From Scene")]
    public static void RemoveSceneAnimals()
    {
        var roots = new List<GameObject>();
        GameObject env = GameObject.Find("Environment");
        if (env != null)
        {
            foreach (string name in new[] { "Camels", "Pegasus" })
            {
                Transform t = env.transform.Find(name);
                if (t != null) roots.Add(t.gameObject);
            }
        }

        // Anything stray outside those roots too — but never a placed (shop) animal.
        foreach (GrazingAnimal a in Object.FindObjectsByType<GrazingAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (EditorUtility.IsPersistent(a) || a.GetComponentInParent<PlaceableItem>() != null) continue;
            if (roots.Any(r => a.transform.IsChildOf(r.transform))) continue;
            roots.Add(a.gameObject);
        }

        if (roots.Count == 0)
        {
            Debug.Log("[AnimalShop] No ambient animals left in the scene.");
            return;
        }

        foreach (GameObject go in roots)
        {
            Debug.Log($"[AnimalShop] Removing {go.name} ({go.transform.childCount} children).");
            Undo.DestroyObjectImmediate(go);
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // ---------------------------------------------------------------------------------------------

    private static Look LookFor(GrazingAnimal animal)
    {
        if (animal.canFly) return PegasusLook;

        foreach (Renderer r in animal.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m != null && CamelLooks.TryGetValue(m.name, out Look look)) return look;
            }
        }

        return default; // Not one of the shop's animals.
    }

    private static GameObject BuildPrefab(GrazingAnimal source, Look look)
    {
        GameObject sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(source.gameObject);
        string path = $"{PrefabFolder}/{look.file}.prefab";

        var root = new GameObject(look.file);
        try
        {
            GameObject body = sourcePrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab, root.transform)
                : Object.Instantiate(source.gameObject, root.transform);
            body.name = source.canFly ? "Pegasus" : "Camel";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = source.transform.lossyScale;

            // Coat and tuning come from the scene instance.
            Renderer[] from = source.GetComponentsInChildren<Renderer>(true);
            Renderer[] to = body.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < to.Length && i < from.Length; i++)
            {
                if (to[i].name == from[i].name) to[i].sharedMaterials = from[i].sharedMaterials;
            }

            GrazingAnimal animal = body.GetComponent<GrazingAnimal>();
            if (animal == null) animal = body.AddComponent<GrazingAnimal>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), animal);
            animal.seatBone = null; // A scene reference; found again by name at runtime.
            animal.wanderRadius = source.canFly ? PegasusWander : CamelWander;
            animal.rideable = true;
            animal.enabled = false; // PlacedAnimal wakes it once the item is really placed.

            // A body collider so the player can aim at it (move / store), and bump into it. Sized from the
            // mesh in the animal's own space.
            Bounds local = LocalMeshBounds(body.transform);
            var box = body.GetComponent<BoxCollider>();
            if (box == null) box = body.AddComponent<BoxCollider>();
            box.center = local.center;
            // Width capped to the body: the pegasus's mesh bounds include its spread wings.
            box.size = new Vector3(Mathf.Min(local.size.x * 0.8f, local.size.z * 0.45f), local.size.y * 0.9f, local.size.z * 0.85f);

            // The still root: what gets placed, saved and streamed. PlaceableItem insists on a collider of
            // its own; a tiny trigger keeps it from adding a solid one, and is ignored by the aim ray.
            var marker = root.AddComponent<BoxCollider>();
            marker.isTrigger = true;
            marker.size = Vector3.one * 0.05f;
            marker.center = Vector3.up * 0.05f;

            PlaceableItem placeable = root.AddComponent<PlaceableItem>();
            placeable.itemGFX = null;
            placeable.alignGfxToGround = false;
            placeable.prefabName = look.name;

            root.AddComponent<PlacedAnimal>().animal = animal;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>Bounds of every mesh under <paramref name="t"/>, in <paramref name="t"/>'s local space.</summary>
    private static Bounds LocalMeshBounds(Transform t)
    {
        bool any = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.one);
        Matrix4x4 toLocal = t.worldToLocalMatrix;
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            // Skinned meshes sit in their bind pose under the root bone; the mesh bounds through the
            // renderer's own transform are close enough for a collider.
            Bounds mb = mesh.bounds;
            Matrix4x4 m = toLocal * r.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(c);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    /// <summary>Cells the placed animal claims: its body, a little tight, so it can stand among other items.</summary>
    private static Vector2Int FootprintFor(GameObject prefab)
    {
        const float cell = 1f / 12f; // GardenGrid.cellSize
        GrazingAnimal animal = prefab.GetComponentInChildren<GrazingAnimal>(true);
        BoxCollider box = animal != null ? animal.GetComponent<BoxCollider>() : null;
        if (box == null) return new Vector2Int(24, 24);
        Vector3 scale = animal.transform.localScale;
        int x = Mathf.Max(6, Mathf.CeilToInt(box.size.x * scale.x * 0.85f / cell));
        int z = Mathf.Max(6, Mathf.CeilToInt(box.size.z * scale.z * 0.85f / cell));
        return new Vector2Int(x, z);
    }

    private static ShopItemsData LoadDatabase()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ShopPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[AnimalShop] Could not open \"{ShopPrefabPath}\".");
            return null;
        }
        try
        {
            var manager = root.GetComponentInChildren<InGameShopManager>(true);
            if (manager == null || manager.shopItemsDatabase == null)
            {
                Debug.LogError("[AnimalShop] The shop prefab has no InGameShopManager with a shopItemsDatabase.");
                return null;
            }
            return manager.shopItemsDatabase;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static ShopItemData FindOrAddItem(ShopItemsData database, string id)
    {
        foreach (ShopItemCategoryGroup g in database.categories)
        {
            ShopItemData existing = g.items.FirstOrDefault(i => i != null && i.itemID == id);
            if (existing == null) continue;
            if (g.category != ShopItemCategory.Animals)
            {
                g.items.Remove(existing);
                GroupFor(database).items.Add(existing);
            }
            return existing;
        }

        var item = new ShopItemData { itemID = id };
        GroupFor(database).items.Add(item);
        return item;
    }

    private static ShopItemCategoryGroup GroupFor(ShopItemsData database)
    {
        ShopItemCategoryGroup group = database.categories.FirstOrDefault(g => g.category == ShopItemCategory.Animals);
        if (group == null)
        {
            group = new ShopItemCategoryGroup { category = ShopItemCategory.Animals };
            database.categories.Add(group);
        }
        return group;
    }

    // ---------------------------------------------------------------------------------------------
    // Icons — a three-quarter portrait on a transparent background, like the ride prompt icons.
    // ---------------------------------------------------------------------------------------------

    private static Sprite RenderIcon(GameObject prefab, string fileName)
    {
        const int size = 512;
        var stage = new GameObject("~AnimalIconStage");
        stage.transform.position = new Vector3(0f, 900f, 0f);
        GameObject subject = Object.Instantiate(prefab, stage.transform);
        subject.transform.localPosition = Vector3.zero;
        subject.transform.localRotation = Quaternion.Euler(0f, 180f + 35f, 0f);

        RenderTexture rt = null;
        try
        {
            foreach (Renderer r in subject.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is SpriteRenderer) r.enabled = false;
            }
            // Only the best LOD in the portrait.
            foreach (LODGroup lg in subject.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = lg.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers) if (r != null) r.enabled = false;
            }

            Bounds b = default;
            bool any = false;
            foreach (Renderer r in subject.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                Mesh mesh = null;
                bool baked = false;
                Matrix4x4 toWorld = r.transform.localToWorldMatrix;
                if (r is SkinnedMeshRenderer skinned)
                {
                    mesh = new Mesh();
                    skinned.BakeMesh(mesh, false);
                    baked = true;
                    toWorld = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                }
                else if (r.TryGetComponent(out MeshFilter filter)) mesh = filter.sharedMesh;
                if (mesh == null) continue;
                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 w = toWorld.MultiplyPoint3x4(v);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
                if (baked) Object.DestroyImmediate(mesh);
            }
            if (!any) b = new Bounds(subject.transform.position, Vector3.one);

            var camGo = new GameObject("~IconCam");
            camGo.transform.SetParent(stage.transform);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.allowHDR = false;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            float radius = b.extents.magnitude;
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.78f;
            Vector3 dir = new Vector3(0f, 0.28f, -1f).normalized;
            cam.transform.position = b.center + dir * distance;
            cam.transform.LookAt(b.center);

            var lightGo = new GameObject("~IconLight");
            lightGo.transform.SetParent(stage.transform);
            lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;

            rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            string path = IconFolder + "/" + fileName + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        finally
        {
            if (rt != null) Object.DestroyImmediate(rt);
            Object.DestroyImmediate(stage);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
