using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the garden lake's water depth from the terrain heightmap into a small texture and
/// applies the Jannah/Environment/Lake Water material to Environment/Water/Lake.
///
/// The mobile pipeline has no camera depth texture, so the lake shader reads this baked map for
/// depth colour, shoreline foam and the soft shore edge instead. Re-run after sculpting the
/// lakebed or moving/resizing the lake plane.
/// </summary>
public static class LakeWaterTools
{
    const string LakePath = "Environment/Water/Lake";
    const string ShaderName = "Jannah/Environment/Lake Water";
    const string OutDir = "Assets/Materials/Water";
    const string DepthTexPath = OutDir + "/LakeDepth.png";
    const string MaterialPath = OutDir + "/LakeWater.mat";
    const string NormalA = "Assets/3D Assets/Idyllic Fantasy Nature/Textures/Water/Water_Normal_01.png";
    const string NormalB = "Assets/3D Assets/Idyllic Fantasy Nature/Textures/Water/Water_Normal_02.png";
    const int Resolution = 256;
    const float MaxDepth = 5f;
    const int BlurRadius = 3;

    static Color32[] BoxBlur(Color32[] src, int n, int r)
    {
        var tmp = new float[src.Length];
        var dst = new Color32[src.Length];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float s = 0; int c = 0;
            for (int k = -r; k <= r; k++) { int xx = Mathf.Clamp(x + k, 0, n - 1); s += src[y * n + xx].r; c++; }
            tmp[y * n + x] = s / c;
        }
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float s = 0; int c = 0;
            for (int k = -r; k <= r; k++) { int yy = Mathf.Clamp(y + k, 0, n - 1); s += tmp[yy * n + x]; c++; }
            // Never push the shoreline outward: dry texels (0) stay dry, so the edge fade and
            // foam keep hugging the real bank.
            byte orig = src[y * n + x].r;
            byte v = orig == 0 ? (byte)0 : (byte)Mathf.Clamp(Mathf.RoundToInt(s / c), 1, 255);
            dst[y * n + x] = new Color32(v, v, v, 255);
        }
        return dst;
    }

    [MenuItem("Jannah/Environment/Bake Lake Water")]
    public static void BakeAndApply()
    {
        var lake = GameObject.Find(LakePath);
        if (lake == null) { Debug.LogError("[LakeWater] " + LakePath + " not found in the open scene."); return; }
        var renderer = lake.GetComponent<MeshRenderer>();
        var terrain = Terrain.activeTerrain;
        if (terrain == null) { Debug.LogError("[LakeWater] No active terrain to bake depth from."); return; }

        Directory.CreateDirectory(OutDir);

        // Square world rect around the lake bounds, padded a little so bilinear sampling at the
        // plane's edge reads real data.
        Bounds b = renderer.bounds;
        float size = Mathf.Max(b.size.x, b.size.z) + 4f;
        Vector2 min = new Vector2(b.center.x - size * 0.5f, b.center.z - size * 0.5f);
        float waterY = b.center.y;

        var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true);
        var px = new Color32[Resolution * Resolution];
        float tY = terrain.transform.position.y;
        for (int y = 0; y < Resolution; y++)
        for (int x = 0; x < Resolution; x++)
        {
            float wx = min.x + (x + 0.5f) / Resolution * size;
            float wz = min.y + (y + 0.5f) / Resolution * size;
            float h = terrain.SampleHeight(new Vector3(wx, 0, wz)) + tY;
            float d = Mathf.Clamp01((waterY - h) / MaxDepth);
            byte v = (byte)Mathf.RoundToInt(d * 255f);
            px[y * Resolution + x] = new Color32(v, v, v, 255);
        }
        // Blur so the colour bands follow soft curves rather than the terrain's sculpt steps
        // (the lakebed floor is a flat box with steep walls).
        px = BoxBlur(px, Resolution, BlurRadius);
        px = BoxBlur(px, Resolution, BlurRadius);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(DepthTexPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(DepthTexPath, ImportAssetOptions.ForceUpdate);

        var ti = (TextureImporter)AssetImporter.GetAtPath(DepthTexPath);
        ti.textureType = TextureImporterType.SingleChannel;
        ti.textureShape = TextureImporterShape.Texture2D;
        ti.sRGBTexture = false;
        ti.mipmapEnabled = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.maxTextureSize = Resolution;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        var single = ti.GetDefaultPlatformTextureSettings();
        single.format = TextureImporterFormat.R8;
        ti.SetPlatformTextureSettings(single);
        ti.SaveAndReimport();

        var shader = Shader.Find(ShaderName);
        if (shader == null) { Debug.LogError("[LakeWater] Shader " + ShaderName + " not found (compile error?)."); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "LakeWater" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        else mat.shader = shader;

        mat.SetTexture("_NormalA", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalA));
        mat.SetTexture("_NormalB", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalB));
        mat.SetTexture("_DepthMap", AssetDatabase.LoadAssetAtPath<Texture2D>(DepthTexPath));
        mat.SetVector("_DepthRect", new Vector4(min.x, min.y, 1f / size, 1f / size));
        mat.SetFloat("_DepthMax", MaxDepth);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(renderer, "Apply Lake Water");
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        EditorUtility.SetDirty(renderer);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lake.scene);

        Debug.Log("[LakeWater] Baked " + Resolution + "² depth over " + size.ToString("F1") + " m and applied " + MaterialPath + " to " + LakePath + ".");
    }
}
