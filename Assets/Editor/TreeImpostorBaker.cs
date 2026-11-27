using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Gives every big tree in the open scene that has no cheap far LOD a flat "impostor" stand-in: two
/// crossed cards (4 triangles, one shared material and atlas) appended as the LODGroup's last level.
///
/// Without it, far trees in the jungle and scattered foliage never drop below their last mesh LOD:
/// 300-1,300 triangles and two draws (bark + leaves) each, all the way out to the fog. The Synty trees
/// already end in a card LOD; this gives the Stylized Nature and Idyllic trees the same.
///
/// Each tree type is photographed once from the side under the scene's daytime sun (the game is always
/// daytime - see DayNightCycle), so the cards use an Unlit material and still match the lit trees.
/// Run "Remove Tree Impostors" to undo it exactly; re-run "Bake" after adding or replacing trees.
/// </summary>
public static class TreeImpostorBaker
{
    private const string OutputFolder = "Assets/Art/Impostors";
    private const string AtlasPath = OutputFolder + "/TreeImpostorAtlas.png";
    private const string MaterialPath = OutputFolder + "/TreeImpostor.mat";
    private const string MeshesPath = OutputFolder + "/TreeImpostorMeshes.asset";
    private const string ChildName = "__Impostor";

    private const int Tile = 256;
    private const int Columns = 8;          // 8 x 8 tiles of 256 = a 2048 atlas, room for 64 tree types
    private const int Supersample = 2;

    /// <summary>
    /// Screen-relative height at which the last mesh LOD hands over to the cards. With the Mobile
    /// lodBias of 0.6 a 9 m tree switches at about 45 m and an 18 m tree at about 90 m.
    /// </summary>
    private const float ImpostorTransition = 0.12f;

    private const float MinTreeHeight = 3f;
    private const int MaxLastLodTriangles = 40; // a last LOD this small already is a card

    // ------------------------------------------------------------------------------------------

    [MenuItem("Jannah/Performance/Bake Tree Impostors")]
    public static void Bake()
    {
        var targets = FindTargets(out int skipped);
        if (targets.Count == 0)
        {
            Debug.Log("[TreeImpostorBaker] No trees without a card LOD found in the open scene.");
            return;
        }

        // Remove an earlier bake first, so re-running never stacks impostors.
        int removed = RemoveAll(save: false);

        var byPrefab = targets.GroupBy(t => t.prefab).ToList();
        if (byPrefab.Count > Columns * Columns)
        {
            Debug.LogError($"[TreeImpostorBaker] {byPrefab.Count} tree types do not fit the {Columns}x{Columns} atlas.");
            return;
        }

        Directory.CreateDirectory(OutputFolder);

        var atlas = new Texture2D(Tile * Columns, Tile * Columns, TextureFormat.RGBA32, false);
        atlas.SetPixels32(new Color32[atlas.width * atlas.height]);

        var meshes = new Dictionary<GameObject, Mesh>();
        var captureSetup = new CaptureSetup();
        try
        {
            captureSetup.Begin();
            for (int i = 0; i < byPrefab.Count; i++)
            {
                GameObject prefab = byPrefab[i].Key;
                EditorUtility.DisplayProgressBar("Baking tree impostors", prefab.name, i / (float)byPrefab.Count);

                int col = i % Columns, row = i / Columns;
                if (!Capture(prefab, captureSetup, atlas, col * Tile, row * Tile, out Rect uv, out Vector3 min, out Vector3 max))
                {
                    Debug.LogWarning("[TreeImpostorBaker] Could not capture " + prefab.name);
                    continue;
                }
                meshes[prefab] = BuildCrossMesh(prefab.name, uv, min, max);
            }
        }
        finally
        {
            captureSetup.End();
            EditorUtility.ClearProgressBar();
        }

        atlas.Apply();
        File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
        ConfigureAtlasImporter();

        Material material = CreateOrUpdateMaterial(AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath));
        SaveMeshes(meshes);

        int added = 0;
        foreach (Target t in targets)
        {
            if (!meshes.TryGetValue(t.prefab, out Mesh mesh)) continue;
            if (AddImpostor(t.group, mesh, material)) added++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[TreeImpostorBaker] Baked {meshes.Count} tree types, added impostors to {added} trees " +
                  $"(replaced {removed} old ones, skipped {skipped} non-prefab trees).");
    }

    [MenuItem("Jannah/Performance/Remove Tree Impostors")]
    public static void Remove()
    {
        int removed = RemoveAll(save: true);
        Debug.Log($"[TreeImpostorBaker] Removed {removed} tree impostors.");
    }

    // ------------------------------------------------------------------------------------------

    private struct Target
    {
        public LODGroup group;
        public GameObject prefab;
    }

    private static List<Target> FindTargets(out int skipped)
    {
        skipped = 0;
        var list = new List<Target>();

        // Only the decorative forest the mobile optimizer manages (Jungle, Scattered Foliage): buildings,
        // tents and other gameplay structures elsewhere keep their real geometry.
        var optimizer = Object.FindFirstObjectByType<MobileEnvironmentOptimizer>(FindObjectsInactive.Include);
        if (optimizer == null || optimizer.cullRoots == null)
        {
            Debug.LogError("[TreeImpostorBaker] No MobileEnvironmentOptimizer with cull roots in the open scene.");
            return list;
        }

        var groups = new List<LODGroup>();
        foreach (Transform root in optimizer.cullRoots)
            if (root != null) groups.AddRange(root.GetComponentsInChildren<LODGroup>(true));

        foreach (LODGroup group in groups)
        {
            if (group.GetComponentInChildren<TreeImpostorMarker>(true) != null)
            {
                // Already baked: treat as a target again (its old impostor is removed before re-baking).
            }
            else if (!NeedsImpostor(group))
            {
                continue;
            }

            if (group.GetComponentInParent<PlaceableItem>(true) != null) continue; // shop items manage their own look

            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(group.gameObject);
            if (source == null || !PrefabUtility.IsAnyPrefabInstanceRoot(group.gameObject))
            {
                skipped++;
                continue;
            }

            string lower = source.name.ToLowerInvariant();
            if (lower.Contains("rock")) continue;

            list.Add(new Target { group = group, prefab = source });
        }
        return list;
    }

    private static bool NeedsImpostor(LODGroup group)
    {
        LOD[] lods = group.GetLODs();
        if (lods.Length == 0) return false;

        int tris = 0;
        foreach (Renderer r in lods[lods.Length - 1].renderers)
        {
            if (r == null) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += (int)(mf.sharedMesh.GetIndexCount(s) / 3);
        }
        if (tris <= MaxLastLodTriangles) return false;

        Bounds? bounds = null;
        foreach (LOD lod in lods)
        foreach (Renderer r in lod.renderers)
        {
            if (r == null) continue;
            if (bounds == null) bounds = r.bounds;
            else { Bounds b = bounds.Value; b.Encapsulate(r.bounds); bounds = b; }
        }
        return bounds != null && bounds.Value.size.y >= MinTreeHeight;
    }

    // ------------------------------------------------------------------------------------------ capture

    /// <summary>Scene state changed for the duration of the capture, restored in End().</summary>
    private class CaptureSetup
    {
        public int layer;
        public Camera camera;
        public Vector3 viewDirection;
        private bool fog;
        private Light sun;
        private Quaternion sunRotation;
        private float sunIntensity;

        public void Begin()
        {
            layer = FindFreeLayer();

            fog = RenderSettings.fog;
            RenderSettings.fog = false;

            // Light the capture exactly as the game will be lit: DayNightCycle sets the sun in Start.
            var cycle = Object.FindFirstObjectByType<DayNightCycle>();
            sun = cycle != null && cycle.directionalLight != null ? cycle.directionalLight : RenderSettings.sun;
            if (sun != null)
            {
                sunRotation = sun.transform.rotation;
                sunIntensity = sun.intensity;
                if (cycle != null)
                {
                    sun.transform.rotation = Quaternion.Euler(cycle.sunRotation);
                    sun.intensity = cycle.maxIntensity;
                }
            }

            // Look across the sun at 45 degrees: the card shows a lit and a shaded side, like the
            // real tree seen from an average angle, rather than its fully lit or fully dark face.
            Vector3 sunForward = sun != null ? sun.transform.forward : Vector3.forward;
            sunForward.y = 0f;
            if (sunForward.sqrMagnitude < 0.001f) sunForward = Vector3.forward;
            viewDirection = Quaternion.Euler(0f, 45f, 0f) * sunForward.normalized;

            var go = new GameObject("__ImpostorCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.cullingMask = 1 << layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
        }

        public void End()
        {
            RenderSettings.fog = fog;
            if (sun != null)
            {
                sun.transform.rotation = sunRotation;
                sun.intensity = sunIntensity;
            }
            if (camera != null) Object.DestroyImmediate(camera.gameObject);
        }
    }

    private static int FindFreeLayer()
    {
        for (int i = 31; i >= 8; i--)
        {
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
        }
        return 31;
    }

    /// <summary>
    /// Renders the prefab's most detailed LOD into one atlas tile. Alpha comes from rendering twice over
    /// black and over white (URP does not reliably keep alpha in its intermediate target).
    /// </summary>
    private static bool Capture(GameObject prefab, CaptureSetup setup, Texture2D atlas, int px, int py,
                                out Rect uv, out Vector3 localMin, out Vector3 localMax)
    {
        uv = default;
        localMin = localMax = default;

        GameObject instance = Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            instance.transform.SetPositionAndRotation(new Vector3(0f, -2000f, 0f), Quaternion.identity);
            instance.transform.localScale = prefab.transform.localScale;

            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = setup.layer;
            foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);

            var group = instance.GetComponent<LODGroup>();
            Renderer[] lod0;
            if (group != null)
            {
                LOD[] lods = group.GetLODs();
                lod0 = lods.Length > 0 ? lods[0].renderers.Where(r => r != null).ToArray() : new Renderer[0];
                group.enabled = false;
                foreach (LOD lod in lods)
                foreach (Renderer r in lod.renderers)
                    if (r != null) r.enabled = lod0.Contains(r);
            }
            else
            {
                lod0 = instance.GetComponentsInChildren<Renderer>(true);
            }
            if (lod0.Length == 0) return false;

            Bounds bounds = lod0[0].bounds;
            foreach (Renderer r in lod0) bounds.Encapsulate(r.bounds);

            // A square view around the tree, as wide as its widest horizontal extent or as tall as it is.
            float halfWidth = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
            float half = Mathf.Max(bounds.extents.y, halfWidth) * 1.02f;

            Camera cam = setup.camera;
            cam.orthographicSize = half;
            cam.aspect = 1f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = half * 4f + 10f;
            cam.transform.rotation = Quaternion.LookRotation(setup.viewDirection, Vector3.up);
            cam.transform.position = bounds.center - setup.viewDirection * (half * 2f + 5f);

            int size = Tile * Supersample;
            Color[] overBlack = Render(cam, size, Color.black);
            Color[] overWhite = Render(cam, size, Color.white);

            var tile = new Color[Tile * Tile];
            for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++)
            {
                Color acc = Color.clear;
                float aSum = 0f;
                for (int sy = 0; sy < Supersample; sy++)
                for (int sx = 0; sx < Supersample; sx++)
                {
                    int i = (y * Supersample + sy) * size + (x * Supersample + sx);
                    Color b = overBlack[i], w = overWhite[i];
                    float a = Mathf.Clamp01(1f - ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / 3f);
                    acc += b; // over black = colour premultiplied by alpha
                    aSum += a;
                }
                float n = Supersample * Supersample;
                float alpha = aSum / n;
                Color c = alpha > 0.001f ? acc / (n * alpha) : Color.clear;
                c.a = alpha > 0.5f ? 1f : alpha; // leaves are alpha-tested; keep edges crisp
                tile[y * Tile + x] = c;
            }
            Dilate(tile, Tile, 4);
            atlas.SetPixels(px, py, Tile, Tile, tile);

            uv = new Rect(px / (float)atlas.width, py / (float)atlas.height, Tile / (float)atlas.width, Tile / (float)atlas.height);

            // Card size in the prefab's local space: the square captured, centred on the bounds centre.
            Vector3 centre = instance.transform.InverseTransformPoint(bounds.center);
            Vector3 scale = instance.transform.lossyScale;
            float halfLocalY = half / Mathf.Max(0.0001f, scale.y);
            float halfLocalX = half / Mathf.Max(0.0001f, scale.x);
            localMin = new Vector3(-halfLocalX, centre.y - halfLocalY, 0f) + new Vector3(centre.x, 0f, centre.z);
            localMax = new Vector3(halfLocalX, centre.y + halfLocalY, 0f) + new Vector3(centre.x, 0f, centre.z);
            return true;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static Color[] Render(Camera cam, int size, Color clear)
    {
        var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.backgroundColor = clear;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pixels = tex.GetPixels();
        Object.DestroyImmediate(tex);
        return pixels;
    }

    /// <summary>Bleeds edge colours into transparent texels so mip levels do not pick up a dark fringe.</summary>
    private static void Dilate(Color[] px, int size, int passes)
    {
        var next = (Color[])px.Clone();
        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                if (px[i].a > 0f) continue;
                Color sum = Color.clear;
                int count = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                    Color c = px[ny * size + nx];
                    if (c.a <= 0f && c.r + c.g + c.b <= 0f) continue;
                    sum += c;
                    count++;
                }
                if (count > 0)
                {
                    Color c = sum / count;
                    c.a = 0f;
                    next[i] = c;
                }
            }
            System.Array.Copy(next, px, px.Length);
        }
    }

    // ------------------------------------------------------------------------------------------ assets

    private static Mesh BuildCrossMesh(string name, Rect uv, Vector3 min, Vector3 max)
    {
        float hx = (max.x - min.x) * 0.5f;
        float cx = (max.x + min.x) * 0.5f;
        float cz = (max.z + min.z) * 0.5f;
        float y0 = min.y, y1 = max.y;

        var v = new List<Vector3>();
        var t = new List<Vector2>();
        var idx = new List<int>();

        void Quad(Vector3 axis)
        {
            int b = v.Count;
            Vector3 c = new Vector3(cx, 0f, cz);
            v.Add(c - axis * hx + Vector3.up * y0);
            v.Add(c + axis * hx + Vector3.up * y0);
            v.Add(c + axis * hx + Vector3.up * y1);
            v.Add(c - axis * hx + Vector3.up * y1);
            t.Add(new Vector2(uv.xMin, uv.yMin));
            t.Add(new Vector2(uv.xMax, uv.yMin));
            t.Add(new Vector2(uv.xMax, uv.yMax));
            t.Add(new Vector2(uv.xMin, uv.yMax));
            idx.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }

        Quad(Vector3.right);
        Quad(Vector3.forward);

        var mesh = new Mesh { name = name + "_Impostor" };
        mesh.SetVertices(v);
        mesh.SetUVs(0, t);
        mesh.SetTriangles(idx, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void SaveMeshes(Dictionary<GameObject, Mesh> meshes)
    {
        AssetDatabase.DeleteAsset(MeshesPath);
        var container = ScriptableObject.CreateInstance<TreeImpostorMeshes>();
        AssetDatabase.CreateAsset(container, MeshesPath);
        foreach (Mesh mesh in meshes.Values) AssetDatabase.AddObjectToAsset(mesh, container);
        AssetDatabase.SaveAssets();
    }

    private static void ConfigureAtlasImporter()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.5f;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    private static Material CreateOrUpdateMaterial(Texture2D atlas)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.SetTexture("_BaseMap", atlas);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetFloat("_Surface", 0f);
        material.renderQueue = (int)RenderQueue.AlphaTest;
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    // ------------------------------------------------------------------------------------------ scene edits

    private static bool AddImpostor(LODGroup group, Mesh mesh, Material material)
    {
        LOD[] lods = group.GetLODs();
        if (lods.Length == 0) return false;

        var child = new GameObject(ChildName);
        child.transform.SetParent(group.transform, false);
        child.layer = group.gameObject.layer;
        GameObjectUtility.SetStaticEditorFlags(child, GameObjectUtility.GetStaticEditorFlags(group.gameObject) & ~StaticEditorFlags.ContributeGI);

        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = true;

        var marker = child.AddComponent<TreeImpostorMarker>();
        int last = lods.Length - 1;
        marker.originalLastThreshold = lods[last].screenRelativeTransitionHeight;
        marker.originalFadeMode = group.fadeMode;

        // The last mesh LOD now ends at the impostor transition; the cards take over down to where the
        // tree used to be culled.
        float upper = last > 0 ? lods[last - 1].screenRelativeTransitionHeight : 1f;
        float handover = Mathf.Clamp(ImpostorTransition, marker.originalLastThreshold + 0.005f, upper - 0.005f);
        if (handover <= marker.originalLastThreshold)
        {
            Object.DestroyImmediate(child);
            return false;
        }
        lods[last].screenRelativeTransitionHeight = handover;

        var list = lods.ToList();
        list.Add(new LOD(marker.originalLastThreshold, new Renderer[] { renderer }));
        group.SetLODs(list.ToArray());
        return true;
    }

    private static int RemoveAll(bool save)
    {
        int removed = 0;
        foreach (TreeImpostorMarker marker in Object.FindObjectsByType<TreeImpostorMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var group = marker.GetComponentInParent<LODGroup>(true);
            if (group != null)
            {
                var renderer = marker.GetComponent<Renderer>();
                var lods = group.GetLODs().Where(l => !l.renderers.Contains(renderer)).ToArray();
                if (lods.Length > 0) lods[lods.Length - 1].screenRelativeTransitionHeight = marker.originalLastThreshold;
                group.SetLODs(lods);
                group.fadeMode = marker.originalFadeMode;
            }
            Object.DestroyImmediate(marker.gameObject);
            removed++;
        }

        if (removed > 0)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            if (save) EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }
        return removed;
    }
}
