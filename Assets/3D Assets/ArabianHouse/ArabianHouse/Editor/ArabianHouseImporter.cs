using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace JannahGarden.EditorTools
{
    /// <summary>
    /// Import pipeline for everything under .../JannahGarden/ArabianHouse/ :
    ///  - textures: normal map / linear AO flags, 2048 max, ASTC on Android + iOS
    ///  - model: real (weighted) normals + Blender tangents, no animation/cameras/lights, scale 1
    ///  - UCX_* meshes become BoxColliders (convex MeshCollider for the stair ramp); their renderers are removed
    ///  - LODGroup: LOD0 = exterior + interior + door leaves, LOD1, LOD2, then culled
    ///  - SM_Door_* get the ArabianDoor component, swinging into the house
    ///  - materials M_ArabianHouse / M_ArabianHouseInterior are created in Materials/ (URP Simple Lit or
    ///    Built-in Standard, whichever pipeline the project uses) and remapped onto the model
    /// </summary>
    public class ArabianHouseImporter : AssetPostprocessor
    {
        // The package was authored for Assets/JannahGarden/ArabianHouse/, but in this project it lives under
        // Assets/3D Assets/ArabianHouse/ArabianHouse/. Match on the asset folder itself so the pipeline runs
        // wherever the folder is moved to (it never ran before, so UCX_ collider boxes were being rendered).
        const string Marker = "/ArabianHouse/";
        const float Smoothness = 0.15f;   // Blender roughness 0.85

        // screen-relative height at which each LOD stops being used (mobile-friendly)
        const float Lod0Until = 0.30f, Lod1Until = 0.12f, Lod2Until = 0.02f;

        static readonly (string mat, string tex)[] Materials =
        {
            ("M_ArabianHouse", "T_ArabianHouse"),
            ("M_ArabianHouseInterior", "T_ArabianHouseInterior"),
        };

        static bool Ours(string path)
        {
            path = path.Replace('\\', '/');
            if (!path.Contains(Marker)) return false;
            string file = Path.GetFileName(path);
            return file.StartsWith("SM_ArabianHouse") || file.StartsWith("T_ArabianHouse");
        }

        public override uint GetVersion() => 2;

        // ------------------------------------------------------------------ textures
        void OnPreprocessTexture()
        {
            if (!Ours(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath);
            bool normal = n.EndsWith("_Normal");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !(normal || n.EndsWith("_AO"));
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;         // atlases
            ti.anisoLevel = 2;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Compressed;
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var s = ti.GetPlatformTextureSettings(platform);
                s.overridden = true;
                s.maxTextureSize = 2048;
                s.format = normal ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(s);
            }
        }

        // ------------------------------------------------------------------ model
        void OnPreprocessModel()
        {
            if (!Ours(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = false;
            mi.importNormals = ModelImporterNormals.Import;      // keep the weighted normals from Blender
            mi.importTangents = ModelImporterTangents.Import;    // tangents match the baked normal map
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;   // compression wobbles atlas UVs
            mi.isReadable = false;
            mi.optimizeMeshPolygons = true;
            mi.optimizeMeshVertices = true;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;                      // UV0 is a non-overlapping atlas already
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        readonly Dictionary<string, float> _openAngles = new Dictionary<string, float>();

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            if (!Ours(assetPath)) return;
            for (int i = 0; i < names.Length; i++)
                if (names[i] == "open_angle_deg")
                {
                    try { _openAngles[go.name] = System.Convert.ToSingle(values[i]); }
                    catch { /* keep default */ }
                }
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!Ours(assetPath)) return;
            var all = root.GetComponentsInChildren<Transform>(true);

            // -- collision
            foreach (var t in all.Where(x => x.name.StartsWith("UCX_")))
            {
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                if (IsAxisAlignedBox(mesh))
                {
                    var bc = t.gameObject.AddComponent<BoxCollider>();
                    bc.center = mesh.bounds.center;
                    bc.size = mesh.bounds.size;
                }
                else
                {
                    var mc = t.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    mc.convex = true;
                }
                var mr = t.GetComponent<MeshRenderer>();
                if (mr) Object.DestroyImmediate(mr);
                Object.DestroyImmediate(mf);
            }

            // -- doors
            var doorRenderers = new List<Renderer>();
            foreach (var t in all.Where(x => x.name.StartsWith("SM_Door_")))
            {
                var door = t.gameObject.AddComponent<JannahGarden.ArabianDoor>();
                float a = Mathf.Abs(_openAngles.TryGetValue(t.name, out var v) ? v : 90f);
                var mf = t.GetComponent<MeshFilter>();
                Vector3 c = mf && mf.sharedMesh ? mf.sharedMesh.bounds.center : Vector3.zero;
                // pick the swing direction that moves the leaf towards the house centre (= inward)
                Vector3 local = t.localRotation * c;
                float Dist(float ang)
                {
                    Vector3 p = Quaternion.AngleAxis(ang, Vector3.up) * local + t.localPosition;
                    return new Vector2(p.x, p.z).magnitude;
                }
                door.openAngle = Dist(a) < Dist(-a) ? a : -a;
                var rb = t.gameObject.AddComponent<Rigidbody>();    // moving colliders want a kinematic body
                rb.isKinematic = true;
                rb.useGravity = false;
                var r = t.GetComponent<Renderer>();
                if (r) doorRenderers.Add(r);
            }

            // -- static flags for the non-moving parts
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                        StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            foreach (var t in all)
            {
                bool moving = t.GetComponentInParent<JannahGarden.ArabianDoor>() != null;
                if (!moving) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }

            // -- LOD group
            Renderer R(string n) => all.FirstOrDefault(x => x.name == n)?.GetComponent<Renderer>();
            var lod0 = new[] { R("SM_ArabianHouse_LOD0"), R("SM_ArabianHouse_Interior") }
                .Concat(doorRenderers).Where(r => r != null).ToArray();
            var lod1 = R("SM_ArabianHouse_LOD1");
            var lod2 = R("SM_ArabianHouse_LOD2");
            if (lod0.Length > 0 && lod1 && lod2)
            {
                var group = root.GetComponent<LODGroup>();      // Unity may already add one for *_LODn names
                if (group == null) group = root.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.None;
                group.SetLODs(new[]
                {
                    new LOD(Lod0Until, lod0),
                    new LOD(Lod1Until, new[] { lod1 }),
                    new LOD(Lod2Until, new[] { lod2 }),
                });
                group.RecalculateBounds();
            }
        }

        static bool IsAxisAlignedBox(Mesh mesh)
        {
            // a wedge (the stair ramp) also has every vertex on the bounds extremes, so require a box's 12 triangles
            if (mesh.triangles.Length != 36) return false;
            var b = mesh.bounds;
            const float eps = 1e-3f;
            bool On(float v, float lo, float hi) => Mathf.Abs(v - lo) < eps || Mathf.Abs(v - hi) < eps;
            foreach (var p in mesh.vertices)
                if (!On(p.x, b.min.x, b.max.x) || !On(p.y, b.min.y, b.max.y) || !On(p.z, b.min.z, b.max.z))
                    return false;
            return true;
        }

        // ------------------------------------------------------------------ materials
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
            {
                if (!Ours(p) || !p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                string path = p;
                EditorApplication.delayCall += () => SetupMaterials(path, false);
            }
        }

        [MenuItem("Tools/Jannah Garden/Rebuild Arabian House Materials")]
        static void RebuildMenu()
        {
            foreach (var guid in AssetDatabase.FindAssets("SM_ArabianHouse t:Model"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Ours(path)) SetupMaterials(path, true);
            }
        }

        static void SetupMaterials(string fbxPath, bool rebuild)
        {
            var mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (mi == null) return;
            string baseDir = Path.GetDirectoryName(Path.GetDirectoryName(fbxPath)).Replace('\\', '/');
            string texDir = baseDir + "/Textures";
            string matDir = baseDir + "/Materials";
            var map = mi.GetExternalObjectMap();
            bool remapped = false;
            foreach (var (matName, texPrefix) in Materials)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), matName);
                // keep a hand-assigned material (e.g. the OmniShade ones in Textures/) unless rebuilding
                if (!rebuild && map.TryGetValue(id, out var existing) && existing != null) continue;
                if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(baseDir, "Materials");
                string matPath = $"{matDir}/{matName}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = BuildMaterial(null, texDir, texPrefix);
                    if (mat == null) return;
                    mat.name = matName;
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else if (rebuild)
                {
                    BuildMaterial(mat, texDir, texPrefix);
                    EditorUtility.SetDirty(mat);
                }
                if (!map.TryGetValue(id, out var current) || current != mat)
                {
                    mi.AddRemap(id, mat);
                    remapped = true;
                }
            }
            AssetDatabase.SaveAssets();
            if (remapped) mi.SaveAndReimport();
        }

        static Material BuildMaterial(Material mat, string texDir, string prefix)
        {
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{texDir}/{prefix}_BaseColorAO.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{texDir}/{prefix}_Normal.png");

            Shader shader = null;
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
            {
                shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    Debug.LogWarning($"[JannahGarden] Render pipeline '{pipeline.name}' is not URP; falling back to Standard.");
            }
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("[JannahGarden] No suitable shader found for the Arabian house materials.");
                return null;
            }

            if (mat == null) mat = new Material(shader);
            else mat.shader = shader;

            foreach (var prop in new[] { "_BaseMap", "_MainTex" })
                if (mat.HasProperty(prop)) mat.SetTexture(prop, albedo);
            foreach (var prop in new[] { "_BaseColor", "_Color" })
                if (mat.HasProperty(prop)) mat.SetColor(prop, Color.white);
            if (mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            foreach (var prop in new[] { "_Smoothness", "_Glossiness" })
                if (mat.HasProperty(prop)) mat.SetFloat(prop, Smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;
            return mat;
        }
    }
}
