using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace JannahGarden.JungleOptimized
{
    /// <summary>
    /// Builds the mobile-optimized jungle in "Assets/Jungle Optimized" from the "Environment/Jungle"
    /// hierarchy of the open scene. Source packs (SNB, Synty, ToonScapes) are never modified - every
    /// optimized prefab, material and mesh is a new asset inside the folder.
    ///
    /// Tools > Jungle Optimized > Build From Open Scene   regenerates everything (safe to re-run; GUIDs are kept)
    /// Tools > Jungle Optimized > Swap Into Open Scene    disables the old jungle and drops the optimized prefab in its place
    /// </summary>
    public static class JungleOptimizedBuilder
    {
        public const string Root = "Assets/Jungle Optimized";
        const string PrefabDir = Root + "/Prefabs";
        const string MaterialDir = Root + "/Materials";
        const string MeshDir = Root + "/Meshes";
        const string ShaderPath = Root + "/Shaders/SNB_Nature_URP_Mobile.shader";
        public const string LayoutPrefabPath = Root + "/Jungle (Optimized).prefab";
        const string SourceShaderName = "JannahGarden/SNB_Nature_URP";
        const string SourceJunglePath = "Environment/Jungle";

        // ------------------------------------------------------------------ tuning
        // Cull distances are what you get on the Mobile quality level (LOD bias below, 60 deg FOV).
        const float MobileLodBias = 0.6f;
        const float ReferenceFov = 60f;
        const float TreeCullDistance = 115f;
        const float PropCullDistance = 45f;
        const float SmallPropCullDistance = 30f;
        const float ChunkCullDistance = 50f;
        const float ChunkSize = 16f;
        const int HeavyLod0Triangles = 5000;     // trees whose LOD0 is heavier than this lose LOD0 (LOD1 becomes the top level)
        const int TreeShadowLodCount = 2;        // only the first N LOD levels of a tree cast shadows
        const int MaxParticles = 150;

        static readonly string[] CombineGroups = { "Undergrowth", "Dense Forest Undergrowth" };

        enum Kind { Tree, Prop, SmallProp, Fx }

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/Jungle Optimized/Build From Open Scene")]
        public static void BuildMenu()
        {
            Debug.Log(Build());
        }

        [MenuItem("Tools/Jungle Optimized/Swap Into Open Scene")]
        public static void SwapMenu()
        {
            Debug.Log(SwapIntoOpenScene());
        }

        // ------------------------------------------------------------------ state for one build

        class BuildContext
        {
            public Shader mobileShader;
            public readonly Dictionary<string, GameObject> prefabBySource = new Dictionary<string, GameObject>();
            public readonly Dictionary<string, string> usedPrefabNames = new Dictionary<string, string>();
            public readonly Dictionary<(Material, bool), Material> materials = new Dictionary<(Material, bool), Material>();
            public readonly StringBuilder log = new StringBuilder();
            public int srcRenderers, outRenderers, combinedSourceRenderers, chunks, droppedLod0;
        }

        // ------------------------------------------------------------------ build

        public static string Build()
        {
            GameObject jungle = GameObject.Find(SourceJunglePath);
            if (jungle == null) return "[Jungle Optimized] No '" + SourceJunglePath + "' in the open scene - open the scene that contains the original jungle.";

            var ctx = new BuildContext
            {
                mobileShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath),
            };
            if (ctx.mobileShader == null) return "[Jungle Optimized] Missing shader " + ShaderPath;

            EnsureFolder(PrefabDir);
            EnsureFolder(MaterialDir);
            EnsureFolder(MeshDir);

            ctx.srcRenderers = jungle.GetComponentsInChildren<Renderer>(false).Length;

            var layout = new GameObject("Jungle (Optimized)");
            layout.transform.SetPositionAndRotation(jungle.transform.position, jungle.transform.rotation);
            layout.transform.localScale = jungle.transform.lossyScale;

            try
            {
                int index = 0, count = jungle.transform.childCount;
                foreach (Transform group in jungle.transform)
                {
                    EditorUtility.DisplayProgressBar("Jungle Optimized", "Building " + group.name, (float)index++ / count);
                    BuildGroup(ctx, group, layout.transform);
                }

                // Runtime layer: nearest-N lantern lights, ambient particle pausing, fog/far clip, minimap throttle.
                var opt = new GameObject("Mobile Environment Optimizer");
                opt.transform.SetParent(layout.transform, false);
                var component = opt.AddComponent<MobileEnvironmentOptimizer>();
                component.cullRoots = new[] { layout.transform };

                ctx.outRenderers = layout.GetComponentsInChildren<Renderer>(false).Length;
                PrefabUtility.SaveAsPrefabAsset(layout, LayoutPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(layout);
                EditorUtility.ClearProgressBar();
            }

            int stale = DeleteStalePrefabs(ctx);
            if (stale > 0) ctx.log.AppendLine("  removed " + stale + " stale prefab(s) from a previous build");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ctx.log.Insert(0, string.Format(
                "[Jungle Optimized] Built {0}\n  renderers: {1} -> {2}  ({3} undergrowth renderers baked into {4} chunks)\n" +
                "  optimized prefabs: {5}, mobile materials: {6}, heavy trees that dropped LOD0: {7}\n",
                LayoutPrefabPath, ctx.srcRenderers, ctx.outRenderers, ctx.combinedSourceRenderers, ctx.chunks,
                ctx.prefabBySource.Count, ctx.materials.Count, ctx.droppedLod0));
            return ctx.log.ToString();
        }

        static void BuildGroup(BuildContext ctx, Transform group, Transform layoutRoot)
        {
            // A group that is itself a component carrier (e.g. the local mood Volume) is copied as-is.
            if (group.GetComponents<Component>().Length > 1)
            {
                GameObject copy = Object.Instantiate(group.gameObject, layoutRoot);
                copy.name = group.name;
                CopyLocal(group, copy.transform);
                return;
            }

            var g = new GameObject(group.name);
            g.transform.SetParent(layoutRoot, false);
            CopyLocal(group, g.transform);
            g.SetActive(group.gameObject.activeSelf);

            if (CombineGroups.Contains(group.name))
            {
                BuildChunks(ctx, group, g.transform);
                return;
            }

            foreach (Transform child in group) PlaceOptimized(ctx, child, g.transform);
        }

        /// <summary>Re-creates one scene object under <paramref name="parent"/> using its optimized prefab.</summary>
        static void PlaceOptimized(BuildContext ctx, Transform child, Transform parent)
        {
            if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
            {
                string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                GameObject optimized = GetOptimizedPrefab(ctx, src);
                if (optimized != null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(optimized, parent);
                    inst.name = child.name;
                    CopyLocal(child, inst.transform);
                    inst.SetActive(child.gameObject.activeSelf);
                    return;
                }
            }

            // Plain scene objects (e.g. the "Lights" group of lantern point lights) are copied and tidied.
            GameObject copy = Object.Instantiate(child.gameObject, parent);
            copy.name = child.name;
            CopyLocal(child, copy.transform);
            foreach (Light l in copy.GetComponentsInChildren<Light>(true)) l.shadows = LightShadows.None;
            OptimizeParticles(copy);
        }

        // ------------------------------------------------------------------ optimized prefabs

        static GameObject GetOptimizedPrefab(BuildContext ctx, string sourcePath)
        {
            if (ctx.prefabBySource.TryGetValue(sourcePath, out GameObject cached)) return cached;

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) { ctx.prefabBySource[sourcePath] = null; return null; }

            var temp = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(temp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            temp.name = source.name;
            temp.transform.position = Vector3.zero;

            Kind kind = Classify(temp);
            OptimizeLods(ctx, temp, kind);
            OptimizeShadows(temp, kind);
            OptimizeParticles(temp);
            RemapMaterials(ctx, temp, false);
            foreach (Transform t in temp.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, kind == Kind.Fx ? 0 : StaticEditorFlags.OccludeeStatic);

            string folder = PrefabDir + "/" + CategoryFolder(source.name, kind);
            EnsureFolder(folder);
            string fileName = source.name;
            // Two different sources with the same name (different packs) must not overwrite each other.
            if (ctx.usedPrefabNames.TryGetValue(folder + "/" + fileName, out string owner) && owner != sourcePath)
                fileName += "_" + AssetDatabase.AssetPathToGUID(sourcePath).Substring(0, 6);
            ctx.usedPrefabNames[folder + "/" + fileName] = sourcePath;

            string path = folder + "/" + fileName + ".prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);

            ctx.prefabBySource[sourcePath] = saved;
            return saved;
        }

        /// <summary>Folder for the developer's benefit only - the optimization itself is driven by <see cref="Kind"/>.</summary>
        static string CategoryFolder(string name, Kind k)
        {
            if (k == Kind.Fx) return "FX";
            if (name.Contains("Rock") || name.Contains("Stone")) return "Rocks";
            if (name.Contains("Mushroom")) return "Mushrooms";
            if (name.Contains("Lamp") || name.Contains("Lantern")) return "Lanterns";
            if (k == Kind.Tree) return "Trees";
            return "Props";
        }

        /// <summary>Removes optimized prefabs a previous build made that this build no longer uses.</summary>
        static int DeleteStalePrefabs(BuildContext ctx)
        {
            var produced = new HashSet<string>(ctx.prefabBySource.Values.Where(p => p != null).Select(AssetDatabase.GetAssetPath));
            int deleted = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!produced.Contains(path) && AssetDatabase.DeleteAsset(path)) deleted++;
            }
            foreach (string dir in AssetDatabase.GetSubFolders(PrefabDir))
                if (AssetDatabase.FindAssets("", new[] { dir }).Length == 0) AssetDatabase.DeleteAsset(dir);
            return deleted;
        }

        static Kind Classify(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (renderers.Length == 0) return Kind.Fx;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            if (b.size.y >= 3.5f) return Kind.Tree;
            if (b.extents.magnitude <= 0.6f) return Kind.SmallProp;
            return Kind.Prop;
        }

        static float CullDistance(Kind k)
        {
            switch (k)
            {
                case Kind.Tree: return TreeCullDistance;
                case Kind.SmallProp: return SmallPropCullDistance;
                default: return PropCullDistance;
            }
        }

        /// <summary>Screen-relative height at which an object of <paramref name="size"/> metres is culled at <paramref name="distance"/> on the Mobile tier.</summary>
        static float CullHeight(float size, float distance)
        {
            float tanHalf = Mathf.Tan(ReferenceFov * 0.5f * Mathf.Deg2Rad);
            return size * MobileLodBias / (2f * tanHalf * distance);
        }

        static int Triangles(LOD lod)
        {
            int n = 0;
            foreach (Renderer r in lod.renderers)
            {
                if (r == null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) n += (int)(mf.sharedMesh.GetIndexCount(0) / 3);
            }
            return n;
        }

        static void OptimizeLods(BuildContext ctx, GameObject go, Kind kind)
        {
            foreach (LODGroup lg in go.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lg.GetLODs().ToList();

                // Heavy trees (e.g. Synty 23k-triangle LOD0): LOD1 becomes the closest level.
                if (kind == Kind.Tree && lods.Count >= 3 && Triangles(lods[0]) >= HeavyLod0Triangles)
                {
                    var keep = new HashSet<Renderer>(lods.Skip(1).SelectMany(l => l.renderers).Where(r => r != null));
                    foreach (Renderer r in lods[0].renderers)
                    {
                        if (r == null || keep.Contains(r)) continue;
                        bool plainChild = r.gameObject != lg.gameObject && r.transform.childCount == 0 && r.GetComponents<Component>().Length <= 3;
                        if (plainChild) Object.DestroyImmediate(r.gameObject);
                        else Object.DestroyImmediate(r);
                    }
                    lods.RemoveAt(0);
                    ctx.droppedLod0++;
                }

                for (int i = 0; i < lods.Count; i++)
                {
                    LOD l = lods[i];
                    l.renderers = l.renderers.Where(r => r != null).ToArray();
                    lods[i] = l;
                }

                lg.fadeMode = LODFadeMode.None;
                lg.animateCrossFading = false;
                lg.SetLODs(lods.ToArray());
                lg.RecalculateBounds();

                Vector3 s = lg.transform.lossyScale;
                float size = lg.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                LOD[] arr = lg.GetLODs();
                if (arr.Length == 0) continue;

                int last = arr.Length - 1;
                arr[last].screenRelativeTransitionHeight = Mathf.Clamp(
                    Mathf.Max(arr[last].screenRelativeTransitionHeight, CullHeight(size, CullDistance(kind))), 0.001f, 0.9f);
                // Keep the levels strictly decreasing after raising the cull height.
                for (int i = last - 1; i >= 0; i--)
                {
                    float min = arr[i + 1].screenRelativeTransitionHeight + 0.01f;
                    arr[i].screenRelativeTransitionHeight = Mathf.Min(Mathf.Max(arr[i].screenRelativeTransitionHeight, min), 0.99f - 0.01f * i);
                }
                lg.SetLODs(arr);
            }
        }

        static void OptimizeShadows(GameObject go, Kind kind)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (r is ParticleSystemRenderer) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; continue; }
                if (kind != Kind.Tree) r.shadowCastingMode = ShadowCastingMode.Off;
            }

            if (kind != Kind.Tree) return;
            foreach (LODGroup lg in go.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = lg.GetLODs();
                for (int i = TreeShadowLodCount; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static void OptimizeParticles(GameObject go)
        {
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                // Cap the pool to what the emitter can actually have alive (+20%), never below what is visible.
                float alive = ps.emission.rateOverTime.constantMax * main.startLifetime.constantMax;
                int cap = Mathf.Max(MaxParticles, Mathf.CeilToInt(alive * 1.2f));
                if (main.maxParticles > cap) main.maxParticles = cap;
                if (main.cullingMode == ParticleSystemCullingMode.AlwaysSimulate)
                    main.cullingMode = main.loop ? ParticleSystemCullingMode.Pause : ParticleSystemCullingMode.Automatic;
                var psr = ps.GetComponent<ParticleSystemRenderer>();
                if (psr != null) { psr.shadowCastingMode = ShadowCastingMode.Off; psr.receiveShadows = false; }
            }
        }

        // ------------------------------------------------------------------ materials

        static void RemapMaterials(BuildContext ctx, GameObject go, bool combined)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = GetMobileMaterial(ctx, mats[i], combined);
                    if (m != mats[i]) { mats[i] = m; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>Mobile copy of an SNB material (other shaders are returned unchanged).</summary>
        static Material GetMobileMaterial(BuildContext ctx, Material source, bool combined)
        {
            if (source == null || source.shader == null || source.shader.name != SourceShaderName) return source;
            if (ctx.materials.TryGetValue((source, combined), out Material cached)) return cached;

            var fresh = new Material(source) { shader = ctx.mobileShader };
            fresh.name = source.name + (combined ? "_Combined" : "") + "_Mobile";
            fresh.shaderKeywords = new string[0];

            bool alphaClip = source.IsKeywordEnabled("_ALPHATEST_ON") || (source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0.5f);
            SetToggle(fresh, "_AlphaClip", "_ALPHATEST_ON", alphaClip);
            SetToggle(fresh, "_UseNormalMap", "_NORMALMAP", source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null);
            SetToggle(fresh, "_CombinedMesh", "_COMBINED_MESH", combined);
            fresh.enableInstancing = !combined;
            fresh.renderQueue = source.renderQueue;

            string path = MaterialDir + "/" + fresh.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material result;
            if (existing != null)
            {
                EditorUtility.CopySerialized(fresh, existing);   // keeps the GUID so references survive a rebuild
                Object.DestroyImmediate(fresh);
                result = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(fresh, path);
                result = fresh;
            }
            ctx.materials[(source, combined)] = result;
            return result;
        }

        static void SetToggle(Material m, string property, string keyword, bool on)
        {
            if (m.HasProperty(property)) m.SetFloat(property, on ? 1f : 0f);
            if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
        }

        // ------------------------------------------------------------------ undergrowth chunks

        struct Piece
        {
            public Renderer renderer;
            public Mesh mesh;
            public int subMesh;
        }

        static void BuildChunks(BuildContext ctx, Transform group, Transform parent)
        {
            var buckets = new Dictionary<(int, int, Material), List<Piece>>();

            foreach (Transform child in group)
            {
                if (!child.gameObject.activeInHierarchy) continue;

                // Only the closest LOD of a plant is baked; its lower LODs are dropped.
                LODGroup lg = child.GetComponentInChildren<LODGroup>();
                Renderer[] renderers = lg != null && lg.lodCount > 0
                    ? lg.GetLODs()[0].renderers
                    : child.GetComponentsInChildren<MeshRenderer>(false);

                bool combinable = renderers.Length > 0 && child.GetComponentInChildren<Collider>() == null;
                foreach (Renderer r in renderers)
                {
                    if (r == null) { combinable = false; break; }
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) { combinable = false; break; }
                    if (r.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name != SourceShaderName)) { combinable = false; break; }
                }

                if (!combinable)
                {
                    PlaceOptimized(ctx, child, parent);
                    continue;
                }

                foreach (Renderer r in renderers)
                {
                    if (!r.enabled) continue;
                    Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    Vector3 c = r.bounds.center;
                    int cx = Mathf.FloorToInt(c.x / ChunkSize), cz = Mathf.FloorToInt(c.z / ChunkSize);
                    Material[] mats = r.sharedMaterials;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        Material mobile = GetMobileMaterial(ctx, mats[Mathf.Min(s, mats.Length - 1)], true);
                        var key = (cx, cz, mobile);
                        if (!buckets.TryGetValue(key, out List<Piece> list)) buckets[key] = list = new List<Piece>();
                        list.Add(new Piece { renderer = r, mesh = mesh, subMesh = s });
                    }
                    ctx.combinedSourceRenderers++;
                }
            }

            // All chunk meshes of this group live in one asset file.
            string meshPath = MeshDir + "/" + group.name + " Chunks.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
            Mesh container = null;

            foreach (var kv in buckets.OrderBy(k => k.Key.Item1).ThenBy(k => k.Key.Item2).ThenBy(k => k.Key.Item3.name))
            {
                (int cx, int cz, Material mat) = kv.Key;
                List<Piece> pieces = kv.Value;

                float minY = pieces.Min(p => p.renderer.bounds.min.y);
                var chunk = new GameObject(string.Format("Chunk {0},{1} ({2})", cx, cz, mat.name.Replace("_Combined_Mobile", "")));
                chunk.transform.SetParent(parent, false);
                chunk.transform.position = new Vector3((cx + 0.5f) * ChunkSize, minY, (cz + 0.5f) * ChunkSize);
                chunk.transform.rotation = Quaternion.identity;
                GameObjectUtility.SetStaticEditorFlags(chunk, StaticEditorFlags.OccludeeStatic);

                Mesh mesh = Combine(pieces, chunk.transform.worldToLocalMatrix);
                mesh.name = group.name + " " + chunk.name;
                if (container == null) { AssetDatabase.CreateAsset(mesh, meshPath); container = mesh; }
                else AssetDatabase.AddObjectToAsset(mesh, container);
                // No CPU-side copy in the player: saves memory (the game shares its process with Flutter).
                var so = new SerializedObject(mesh);
                so.FindProperty("m_IsReadable").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();

                chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = chunk.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.lightProbeUsage = LightProbeUsage.Off;

                // Native distance culling of the whole chunk.
                var lg = chunk.AddComponent<LODGroup>();
                lg.fadeMode = LODFadeMode.None;
                lg.SetLODs(new[] { new LOD(0.5f, new Renderer[] { mr }) });
                lg.RecalculateBounds();
                lg.SetLODs(new[] { new LOD(Mathf.Clamp(CullHeight(lg.size, ChunkCullDistance), 0.01f, 0.9f), new Renderer[] { mr }) });

                ctx.chunks++;
            }
        }

        static Mesh Combine(List<Piece> pieces, Matrix4x4 worldToChunk)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var colors = new List<Color>();
            var indices = new List<int>();

            var srcPos = new List<Vector3>();
            var srcNrm = new List<Vector3>();
            var srcTan = new List<Vector4>();
            var srcUv = new List<Vector2>();
            var srcCol = new List<Color>();
            var remap = new Dictionary<int, int>();

            foreach (Piece p in pieces)
            {
                Matrix4x4 m = worldToChunk * p.renderer.localToWorldMatrix;
                Matrix4x4 nm = m.inverse.transpose;
                float flip = m.determinant < 0f ? -1f : 1f;

                Mesh src = p.mesh;
                src.GetVertices(srcPos);
                src.GetNormals(srcNrm);
                src.GetTangents(srcTan);
                src.GetUVs(0, srcUv);
                src.GetColors(srcCol);
                int[] tris = src.GetTriangles(p.subMesh);

                remap.Clear();
                for (int i = 0; i < tris.Length; i++)
                {
                    int v = tris[i];
                    if (!remap.TryGetValue(v, out int dst))
                    {
                        dst = positions.Count;
                        remap[v] = dst;
                        positions.Add(m.MultiplyPoint3x4(srcPos[v]));
                        normals.Add(srcNrm.Count > v ? nm.MultiplyVector(srcNrm[v]).normalized : Vector3.up);
                        if (srcTan.Count > v)
                        {
                            Vector3 t = m.MultiplyVector(new Vector3(srcTan[v].x, srcTan[v].y, srcTan[v].z)).normalized;
                            tangents.Add(new Vector4(t.x, t.y, t.z, srcTan[v].w * flip));
                        }
                        else tangents.Add(new Vector4(1, 0, 0, 1));
                        uv0.Add(srcUv.Count > v ? srcUv[v] : Vector2.zero);
                        colors.Add(srcCol.Count > v ? srcCol[v] : Color.white);
                        // The plant's own object-space height, for the shader's height gradient.
                        uv1.Add(new Vector2(srcPos[v].y, 0f));
                    }
                    indices.Add(dst);
                }

                if (flip < 0f)
                    for (int i = indices.Count - tris.Length; i < indices.Count; i += 3)
                    { int a = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = a; }
            }

            // Compact vertex layout: 40 bytes/vertex instead of Unity's default 72 (half-float normals/tangents/UVs,
            // 8-bit colours). All formats are core in GLES3/Vulkan.
            int count = positions.Count;
            var packed = new PackedVertex[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 n = normals[i];
                Vector4 t = tangents[i];
                Color32 c = colors[i];
                packed[i] = new PackedVertex
                {
                    position = positions[i],
                    nx = H(n.x), ny = H(n.y), nz = H(n.z), nw = H(0f),
                    tx = H(t.x), ty = H(t.y), tz = H(t.z), tw = H(t.w),
                    r = c.r, g = c.g, b = c.b, a = c.a,
                    u0 = H(uv0[i].x), v0 = H(uv0[i].y),
                    u1 = H(uv1[i].x), v1 = H(uv1[i].y),
                };
            }

            var mesh = new Mesh();
            mesh.SetVertexBufferParams(count,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 2));
            mesh.SetVertexBufferData(packed, 0, 0, count);

            IndexFormat indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetIndexBufferParams(indices.Count, indexFormat);
            if (indexFormat == IndexFormat.UInt16) mesh.SetIndexBufferData(indices.Select(i => (ushort)i).ToArray(), 0, 0, indices.Count);
            else mesh.SetIndexBufferData(indices.ToArray(), 0, 0, indices.Count);

            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Count, MeshTopology.Triangles));
            mesh.RecalculateBounds();
            MeshUtility.Optimize(mesh);
            return mesh;
        }

        static ushort H(float f) => Mathf.FloatToHalf(f);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct PackedVertex
        {
            public Vector3 position;
            public ushort nx, ny, nz, nw;
            public ushort tx, ty, tz, tw;
            public byte r, g, b, a;
            public ushort u0, v0;
            public ushort u1, v1;
        }

        // ------------------------------------------------------------------ swap into scene

        public static string SwapIntoOpenScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LayoutPrefabPath);
            if (prefab == null) return "[Jungle Optimized] Build the prefab first (Tools > Jungle Optimized > Build From Open Scene).";

            GameObject old = GameObject.Find(SourceJunglePath);
            Transform parent = old != null ? old.transform.parent : null;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent != null ? parent.gameObject.scene : EditorSceneManager.GetActiveScene());
            Undo.RegisterCreatedObjectUndo(inst, "Swap in optimized jungle");
            if (parent != null) inst.transform.SetParent(parent, true);
            if (old != null)
            {
                inst.transform.SetSiblingIndex(old.transform.GetSiblingIndex() + 1);
                Undo.RecordObject(old, "Disable original jungle");
                old.SetActive(false);
            }
            EditorSceneManager.MarkSceneDirty(inst.scene);
            Selection.activeGameObject = inst;
            return "[Jungle Optimized] Placed '" + inst.name + "'" + (old != null ? " and disabled the original 'Jungle' (delete it once you are happy)." : ".");
        }

        // ------------------------------------------------------------------ helpers

        static void CopyLocal(Transform from, Transform to)
        {
            to.localPosition = from.localPosition;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
