using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Re-bakes the BaseColor textures of the optimized Jannah Garden models from the original Meshy
/// texture, so the optimized LODs keep the colors the original material showed.
///
/// Why this exists: the external optimizer baked BaseColor from a PBR source in which metal areas
/// are dark (their color lives in the metallic response). OmniShade isn't a metallic PBR shader, so
/// gold and metal items came out dark and grey. Geometry, UVs, normal maps and LODs are fine; only
/// BaseColor needs redoing.
///
/// How: every texel of the optimized mesh's UV layout is mapped to its 3D point and normal, moved
/// into the original mesh's space, and a short ray along the normal finds the matching spot on the
/// original high-poly surface. That spot's UV samples the original texture. Several samples per
/// texel are averaged, and the result is dilated past UV island edges so mipmaps don't bleed black.
///
/// Overwrites &lt;Name&gt;_BaseColor.png, _LOD1_BaseColor.png and _LOD2_BaseColor.png in
/// _Optimized_Unity/&lt;Name&gt;/. The previous versions are in git (commit d2fc402a7).
/// </summary>
public static class OptimizedTextureRebaker
{
    const string JG = "Assets/3D Assets/Jannah Garden Assets/";
    const string OPT = JG + "_Optimized_Unity/";

    public struct Stats { public string name; public int lod; public int texels; public float hitRate; public float seconds; }

    /// <summary>Re-bakes all three LOD BaseColor textures for one optimized asset (e.g. "Ornate_Golden_Goblet").</summary>
    public static List<Stats> Rebake(string name)
    {
        var stats = new List<Stats>();
        string folder = OPT + name + "/";

        // Locate the original Meshy source via the optimizer's report.
        string rep = File.ReadAllText(OPT + "_optimize_report.json");
        int k = rep.IndexOf("\"name\": \"" + name + "\"");
        if (k < 0) throw new System.Exception("Not in report: " + name);
        int sf = rep.IndexOf("\"source_folder\": \"", k) + 18;
        string src = JG + rep.Substring(sf, rep.IndexOf('"', sf) - sf) + "/";

        var origMesh = AssetDatabase.LoadAllAssetsAtPath(src + "Meshy_AI_model.fbx").OfType<Mesh>().First();
        var origMat = AssetDatabase.FindAssets("t:Material", new[] { src.TrimEnd('/') })
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).First();
        var origTex = LoadCpu(AssetDatabase.GetAssetPath(origMat.GetTexture("_MainTex")));

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(folder + name + ".fbx");
        var lodMeshes = new Mesh[3];
        foreach (var f in fbx.GetComponentsInChildren<MeshFilter>(true))
            for (int i = 0; i < 3; i++) if (f.name.EndsWith("_LOD" + i)) lodMeshes[i] = f.sharedMesh;

        // The prefabs show the original mesh rotated -90° about X; the optimized mesh is Y-up with its
        // pivot at the bottom centre and ~1/scale the size. Work out the mapping from optimized space
        // into original-mesh space: uniform scale, then align X/Z centres and the bottoms.
        Quaternion rot = Quaternion.Euler(-90f, 0f, 0f);
        Bounds bo = RotatedBounds(origMesh.bounds, rot);
        Bounds bn = lodMeshes[0].bounds;
        float scale = MaxC(bo.size) / MaxC(bn.size);
        Vector3 offset = new Vector3(bo.center.x - bn.center.x * scale, bo.min.y - bn.min.y * scale, bo.center.z - bn.center.z * scale);
        Quaternion inv = Quaternion.Inverse(rot);
        float reach = MaxC(bo.size) * 0.02f;   // how far from the decimated surface to search for the original

        var uvs = origMesh.uv;
        var tris = origMesh.triangles;

        Scene scene = EditorSceneManager.NewPreviewScene();
        bool oldBackfaces = Physics.queriesHitBackfaces;
        try
        {
            Physics.queriesHitBackfaces = false;   // see Probe: only same-facing original faces count
            var colGo = new GameObject("orig");
            SceneManager.MoveGameObjectToScene(colGo, scene);
            var col = colGo.AddComponent<MeshCollider>();
            // No cleaning/welding: those can drop or reorder triangles, and the bake relies on
            // RaycastHit.triangleIndex matching the source mesh's triangle list to look up UVs.
            // Fast midphase is also off - it can miss hits on meshes over ~2M triangles.
            col.cookingOptions = MeshColliderCookingOptions.None;
            col.sharedMesh = origMesh;
            PhysicsScene ps = scene.GetPhysicsScene();

            for (int lod = 0; lod < 3; lod++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string outPath = folder + name + (lod == 0 ? "" : "_LOD" + lod) + "_BaseColor.png";
                var existing = LoadCpu(outPath);
                int res = existing.width;
                var existingPx = existing.GetPixels32();
                Object.DestroyImmediate(existing);
                int ss = Mathf.Clamp(origTex.width / res, 1, 4);  // supersamples per axis

                var result = BakeOne(lodMeshes[lod], res, ss, p => inv * (p * scale + offset), n => inv * n,
                                     ps, reach, uvs, tris, origTex, existingPx, out int texels, out float hitRate);
                var outTex = new Texture2D(res, res, TextureFormat.RGB24, false, true);
                outTex.SetPixels32(result);
                outTex.Apply();
                File.WriteAllBytes(outPath, outTex.EncodeToPNG());
                Object.DestroyImmediate(outTex);
                stats.Add(new Stats { name = name, lod = lod, texels = texels, hitRate = hitRate, seconds = (float)sw.Elapsed.TotalSeconds });
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = oldBackfaces;
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(origTex);
        }

        for (int lod = 0; lod < 3; lod++)
            AssetDatabase.ImportAsset(folder + name + (lod == 0 ? "" : "_LOD" + lod) + "_BaseColor.png", ImportAssetOptions.ForceUpdate);
        return stats;
    }

    /// <summary>
    /// Renders original (left), optimized LOD0 (middle) and LOD2 (right) side by side to a PNG, for
    /// checking a bake by eye. <paramref name="prefab"/> is the shop prefab that uses the asset.
    /// </summary>
    public static void RenderComparison(string name, GameObject prefab, string pngPath)
    {
        string rep = File.ReadAllText(OPT + "_optimize_report.json");
        int k = rep.IndexOf("\"name\": \"" + name + "\"");
        int sf = rep.IndexOf("\"source_folder\": \"", k) + 18;
        string src = JG + rep.Substring(sf, rep.IndexOf('"', sf) - sf);
        var origMat = AssetDatabase.FindAssets("t:Material", new[] { src })
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).First();
        var origMesh = AssetDatabase.LoadAllAssetsAtPath(src + "/Meshy_AI_model.fbx").OfType<Mesh>().First();

        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var lightGo = new GameObject("Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            SceneManager.MoveGameObjectToScene(lightGo, scene);

            var camGo = new GameObject("Camera");
            var cam = camGo.AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camGo, scene);
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.7f, 0.85f);
            var rt = new RenderTexture(1200, 480, 24);
            cam.targetTexture = rt;

            var old = new GameObject("old");
            SceneManager.MoveGameObjectToScene(old, scene);
            old.AddComponent<MeshFilter>().sharedMesh = origMesh;
            old.AddComponent<MeshRenderer>().sharedMaterial = origMat;
            old.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            var objs = new GameObject[3];
            objs[0] = old;
            for (int i = 1; i < 3; i++)
            {
                objs[i] = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var pi = objs[i].GetComponent<PlaceableItem>();
                if (pi.timerHolder) pi.timerHolder.SetActive(false);
                pi.itemGFX.GetComponent<LODGroup>().ForceLOD(i == 1 ? 0 : 2);
            }

            float w = 0f, h = 0f;
            var bs = new Bounds[3];
            for (int i = 0; i < 3; i++)
            {
                string suffix = i == 2 ? "_LOD2" : "_LOD0";
                var rs = objs[i].GetComponentsInChildren<Renderer>().Where(r => i == 0 || r.name.EndsWith(suffix)).ToArray();
                bs[i] = rs[0].bounds; foreach (var r in rs) bs[i].Encapsulate(r.bounds);
                w = Mathf.Max(w, bs[i].size.x); h = Mathf.Max(h, bs[i].size.y);
            }
            for (int i = 0; i < 3; i++)
                objs[i].transform.position += new Vector3((i - 1) * w * 1.15f - bs[i].center.x, -bs[i].min.y, -bs[i].center.z);

            float d = Mathf.Max(w * 2.55f, h * 1.9f);
            cam.transform.position = new Vector3(0f, h * 0.5f + d * 0.3f, -d);
            cam.transform.LookAt(new Vector3(0f, h * 0.45f, 0f));
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    /// <summary>The shop prefab whose LOD0 renderer uses &lt;name&gt;_LOD0, or null.</summary>
    public static GameObject FindShopPrefab(string name)
    {
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Shop Items" }))
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            var pi = p.GetComponent<PlaceableItem>();
            if (pi != null && pi.itemRenderers != null && pi.itemRenderers.Length == 3 && pi.itemRenderers[0] != null
                && pi.itemRenderers[0].sharedMaterial != null && pi.itemRenderers[0].sharedMaterial.name == name + "_LOD0")
                return p;
        }
        return null;
    }

    static Color32[] BakeOne(Mesh mesh, int res, int ss, System.Func<Vector3, Vector3> toOrig, System.Func<Vector3, Vector3> dirToOrig,
                             PhysicsScene ps, float reach, Vector2[] origUv, int[] origTris, Texture2D origTex,
                             Color32[] fallback, out int texelCount, out float hitRate)
    {
        var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var t = mesh.triangles;
        var acc = new Color[res * res];
        var cnt = new int[res * res];
        int rays = 0, hits = 0;
        float inv = 1f / ss;

        for (int ti = 0; ti < t.Length; ti += 3)
        {
            int a = t[ti], b = t[ti + 1], c = t[ti + 2];
            Vector2 A = uv[a] * res, B = uv[b] * res, C = uv[c] * res;
            float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
            if (Mathf.Abs(area) < 1e-12f) continue;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x))));
            int x1 = Mathf.Min(res - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y))));
            int y1 = Mathf.Min(res - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
            Vector3 faceN = Vector3.Cross(v[b] - v[a], v[c] - v[a]).normalized;

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
            {
                Vector2 P = new Vector2(x + (sx + 0.5f) * inv, y + (sy + 0.5f) * inv);
                float w1 = ((P.x - A.x) * (C.y - A.y) - (C.x - A.x) * (P.y - A.y)) / area;
                float w2 = ((B.x - A.x) * (P.y - A.y) - (P.x - A.x) * (B.y - A.y)) / area;
                float w0 = 1f - w1 - w2;
                if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;

                Vector3 pos = toOrig(v[a] * w0 + v[b] * w1 + v[c] * w2);
                Vector3 nrm = n.Length > 0 ? (n[a] * w0 + n[b] * w1 + n[c] * w2) : faceN;
                if (nrm.sqrMagnitude < 1e-8f) nrm = faceN;
                nrm = dirToOrig(nrm.normalized);

                rays++;
                if (!Probe(ps, pos, nrm, reach, out RaycastHit hit)) continue;
                hits++;
                int tb = hit.triangleIndex * 3;
                Vector3 bc = hit.barycentricCoordinate;
                Vector2 huv = origUv[origTris[tb]] * bc.x + origUv[origTris[tb + 1]] * bc.y + origUv[origTris[tb + 2]] * bc.z;
                int idx = y * res + x;
                acc[idx] += origTex.GetPixelBilinear(huv.x, huv.y);
                cnt[idx]++;
            }
        }

        var outPx = new Color32[res * res];
        var filled = new bool[res * res];
        texelCount = 0;
        for (int i = 0; i < outPx.Length; i++)
        {
            if (cnt[i] == 0) continue;
            outPx[i] = (Color32)(acc[i] / cnt[i]);
            filled[i] = true;
            texelCount++;
        }
        Dilate(outPx, filled, res, 8);
        // Anything still empty (texels no probe reached, far from any baked texel) keeps the
        // optimizer's colour rather than going black.
        for (int i = 0; i < outPx.Length; i++)
            if (!filled[i]) outPx[i] = fallback[i];
        hitRate = rays > 0 ? hits / (float)rays : 0f;
        return outPx;
    }

    /// <summary>
    /// Finds the original surface matching the optimized surface at <paramref name="p"/>.
    ///
    /// A ray runs from just outside to just inside along the normal with back faces ignored, so it
    /// only stops on original faces that face the same way as the optimized surface. That matters on
    /// thin parts (cup walls, petals, flutes): the opposite side of the wall is within reach, faces
    /// the other way, and is usually a differently coloured part of the texture. The ray keeps going
    /// past each hit, and the hit closest to <paramref name="p"/> wins. Widens once if nothing lands.
    /// </summary>
    static bool Probe(PhysicsScene ps, Vector3 p, Vector3 n, float reach, out RaycastHit best)
    {
        best = default;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            float r = attempt == 0 ? reach : reach * 4f;
            float travelled = 0f, bestErr = float.MaxValue;
            bool found = false;
            Vector3 origin = p + n * r;
            for (int step = 0; step < 6 && travelled < 2f * r; step++)
            {
                if (!ps.Raycast(origin, -n, out RaycastHit hit, 2f * r - travelled)) break;
                float along = travelled + hit.distance;          // distance from p + n*r
                float err = Mathf.Abs(along - r);                // distance from p
                if (err < bestErr) { bestErr = err; best = hit; found = true; }
                float advance = hit.distance + r * 0.001f;
                travelled += advance;
                origin += -n * advance;
            }
            if (found) return true;
        }

        // Fallback for foliage cards and other places where the decimated surface's facing doesn't
        // match the original: accept the nearest surface of either facing.
        Physics.queriesHitBackfaces = true;
        try
        {
            float r = reach * 4f;
            bool hOut = ps.Raycast(p + n * r, -n, out RaycastHit outHit, r);
            bool hIn = ps.Raycast(p - n * r, n, out RaycastHit inHit, r);
            if (hOut && hIn) { best = outHit.distance >= inHit.distance ? outHit : inHit; return true; }
            if (hOut) { best = outHit; return true; }
            if (hIn) { best = inHit; return true; }
        }
        finally { Physics.queriesHitBackfaces = false; }
        return false;
    }

    static void Dilate(Color32[] px, bool[] filled, int res, int passes)
    {
        var next = (bool[])filled.Clone();
        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                int i = y * res + x;
                if (filled[i]) continue;
                int r = 0, g = 0, b = 0, c = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= res || yy >= res) continue;
                    int j = yy * res + xx;
                    if (!filled[j]) continue;
                    r += px[j].r; g += px[j].g; b += px[j].b; c++;
                }
                if (c == 0) continue;
                px[i] = new Color32((byte)(r / c), (byte)(g / c), (byte)(b / c), 255);
                next[i] = true;
            }
            System.Array.Copy(next, filled, filled.Length);
        }
    }

    static Texture2D LoadCpu(string assetPath)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        t.LoadImage(File.ReadAllBytes(assetPath));
        return t;
    }

    static Bounds RotatedBounds(Bounds b, Quaternion r)
    {
        Vector3 mn = Vector3.one * float.MaxValue, mx = -mn;
        for (int i = 0; i < 8; i++)
        {
            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var w = r * c; mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
        }
        var o = new Bounds(); o.SetMinMax(mn, mx); return o;
    }

    static float MaxC(Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));
}
