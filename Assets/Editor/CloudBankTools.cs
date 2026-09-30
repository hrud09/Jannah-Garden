using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Authoring side of <see cref="CloudBank"/>: generates the tiling density texture the cloud
/// shader samples, builds the shared material, and drops a configured bank into the open scene
/// centred on the terrain.
///
/// The noise is generated rather than shipped as art because it has to be seamlessly tileable in
/// both channels at two different frequencies - hand-painting that is fiddly, and an untileable
/// texture shows as a hard repeat seam straight across the horizon wall.
/// </summary>
public static class CloudBankTools
{
    private const string TextureFolder = "Assets/Textures/Environment";
    private const string TexturePath = TextureFolder + "/CloudBankNoise.png";
    private const string MaterialFolder = "Assets/Materials/Environment";
    private const string MaterialPath = MaterialFolder + "/CloudBank.mat";
    private const string MistMaterialPath = MaterialFolder + "/GardenMist.mat";
    private const string ShaderName = "Jannah/Environment/Volumetric Cloud Bank";

    private const int TextureSize = 256;

    [MenuItem("Tools/Environment/Cloud Bank/Create In Scene", priority = 20)]
    public static void CreateInScene()
    {
        Material material = GetOrCreateMaterial();

        var existing = Object.FindFirstObjectByType<CloudBank>();
        if (existing != null)
        {
            existing.cloudMaterial = material;
            AssignMistMaterial(existing);
            existing.Rebuild();
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("[CloudBank] Scene already has a cloud bank - refreshed it instead.", existing);
            return;
        }

        var go = new GameObject("Cloud Bank");
        var bank = go.AddComponent<CloudBank>();
        bank.cloudMaterial = material;
        AssignMistMaterial(bank);

        // Centre on the terrain so the ring is concentric with the playable area; the default
        // layer radii are chosen against the 200x200 terrain's ~141m corner reach.
        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
        {
            Vector3 size = terrain.terrainData.size;
            Vector3 origin = terrain.transform.position;
            go.transform.position = new Vector3(origin.x + size.x * 0.5f, origin.y, origin.z + size.z * 0.5f);
        }

        // Keep it beside the rest of the scene dressing.
        GameObject environment = GameObject.Find("Environment");
        if (environment != null)
        {
            go.transform.SetParent(environment.transform, true);
        }

        bank.Rebuild();

        Undo.RegisterCreatedObjectUndo(go, "Create Cloud Bank");
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log($"[CloudBank] Created at {go.transform.position}.", go);
    }

    [MenuItem("Tools/Environment/Cloud Bank/Regenerate Noise Texture", priority = 21)]
    public static void RegenerateNoiseTexture()
    {
        string path = GenerateNoiseTexture();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        Material material = GetOrCreateMaterial();
        material.SetTexture("_CloudNoise", texture);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CloudBank] Regenerated {path}.", texture);
    }

    /// <summary>
    /// Points every Disc layer without an explicit material at the shared mist material, so a
    /// freshly created bank works without the designer wiring it up by hand.
    /// </summary>
    private static void AssignMistMaterial(CloudBank bank)
    {
        if (bank.layers == null)
        {
            return;
        }

        Material mist = null;

        foreach (CloudBank.Layer layer in bank.layers)
        {
            if (layer == null || layer.placement != CloudBank.Placement.Disc || layer.material != null)
            {
                continue;
            }

            mist = mist != null ? mist : GetOrCreateMistMaterial();
            layer.material = mist;
        }
    }

    /// <summary>
    /// Mist variant of the cloud material: turbulence and near-fade on, softer and faster moving.
    /// Deliberately a separate material from the horizon bank - the turbulence keyword costs an
    /// extra texture fetch per pixel, and the horizon layers cover far more of the screen while
    /// being too distant for the churn to read.
    /// </summary>
    private static Material GetOrCreateMistMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MistMaterialPath);
        bool created = false;

        if (material == null)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[CloudBank] Shader '{ShaderName}' not found.");
                return null;
            }

            EnsureFolder(MaterialFolder);
            material = new Material(shader) { name = "GardenMist" };
            created = true;
        }

        material.SetTexture("_CloudNoise", LoadOrCreateNoiseTexture());

        // Churning smoke rather than a sliding decal.
        material.EnableKeyword("CLOUD_TURBULENCE");
        material.SetFloat("_TurbulenceOn", 1f);
        material.SetFloat("_Turbulence", 0.16f);
        material.SetFloat("_TurbulenceSpeed", 0.4f);
        material.SetVector("_WindScroll", new Vector4(0.013f, 0.005f, -0.019f, 0.008f));
        material.SetVector("_Sway", new Vector4(1.2f, 0.16f, 3.2f, 0.09f));
        material.SetVector("_Spin", new Vector4(0.26f, 0.22f, 0f, 0f));

        // Thin, soft and low contrast - this is haze, not cumulus.
        material.SetFloat("_Coverage", 0.1f);
        material.SetFloat("_DensityScale", 1.9f);
        material.SetFloat("_SoftEdge", 1.35f);
        material.SetFloat("_Opacity", 1f);
        material.SetFloat("_NoiseTiling", 0.55f);
        material.SetFloat("_DetailTiling", 1.5f);
        material.SetFloat("_Absorption", 1.4f);
        material.SetFloat("_SilverLining", 0.25f);
        material.SetColor("_ShadowTint", new Color(0.8f, 0.85f, 0.93f, 1f));

        // Dissolve before the player can walk inside a card, and feather the underside where it
        // passes over a hillside. Both stand in for the soft-particle fade this pipeline cannot do.
        material.SetVector("_NearFade", new Vector4(4f, 14f, 0f, 0f));
        material.SetFloat("_BottomFade", 0.35f);

        // Mist sits inside the fog's range, so it should take the fog more fully than the bank.
        material.SetFloat("_FogInfluence", 0.7f);

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;

        if (created)
        {
            AssetDatabase.CreateAsset(material, MistMaterialPath);
        }

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static Material GetOrCreateMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
        {
            if (material.GetTexture("_CloudNoise") == null)
            {
                material.SetTexture("_CloudNoise", LoadOrCreateNoiseTexture());
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }

            return material;
        }

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[CloudBank] Shader '{ShaderName}' not found. Is the project still compiling shaders?");
            return null;
        }

        EnsureFolder(MaterialFolder);

        material = new Material(shader) { name = "CloudBank" };
        material.SetTexture("_CloudNoise", LoadOrCreateNoiseTexture());
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static Texture2D LoadOrCreateNoiseTexture()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (texture != null)
        {
            return texture;
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(GenerateNoiseTexture());
    }

    private static string GenerateNoiseTexture()
    {
        EnsureFolder(TextureFolder);

        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, true);
        var pixels = new Color32[TextureSize * TextureSize];

        var shapeField = new float[TextureSize * TextureSize];
        var detailField = new float[TextureSize * TextureSize];

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float u = x / (float)TextureSize;
                float v = y / (float)TextureSize;

                // R drives the puff's large-scale silhouette, G erodes it with finer billows.
                // Different base periods keep the two channels from correlating into banding.
                int index = y * TextureSize + x;
                shapeField[index] = Fbm(u, v, basePeriod: 3, octaves: 4, gain: 0.5f);
                detailField[index] = Fbm(u + 0.37f, v + 0.61f, basePeriod: 6, octaves: 3, gain: 0.55f);
            }
        }

        // Stretch each channel over the full 0..1 range. A raw fbm clusters hard around its mean,
        // which after quantisation to 8 bits leaves the shader almost no contrast to carve a
        // cloud edge out of - the visible symptom is a uniform grey mottle instead of lobes.
        Normalise(shapeField);
        Normalise(detailField);

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color32(
                (byte)(shapeField[i] * 255f),
                (byte)(detailField[i] * 255f),
                0, 255);
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.textureType = TextureImporterType.Default;
        // Density values, not colour: sRGB would gamma-bend the Beer-Lambert maths.
        importer.sRGBTexture = false;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        // Uncompressed on purpose. The shader magnifies a 0.45 window of this texture across a
        // whole puff, so DXT/ETC's 4x4 blocks land on screen as visible rectangles inside every
        // cloud. At 256x256 that costs ~256KB, which is cheaper than the artefact.
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        return TexturePath;
    }

    private static void Normalise(float[] field)
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int i = 0; i < field.Length; i++)
        {
            min = Mathf.Min(min, field[i]);
            max = Mathf.Max(max, field[i]);
        }

        float range = Mathf.Max(0.0001f, max - min);
        for (int i = 0; i < field.Length; i++)
        {
            field[i] = Mathf.Clamp01((field[i] - min) / range);
        }
    }

    /// <summary>
    /// Fractal sum of tileable *billowed* value noise. Every octave wraps on its own period, so
    /// the result is seamless across the 0..1 domain and the shader can tile it without a seam.
    ///
    /// The billow remap (1 - |2n-1|, the rounded ridges that read as cumulus rather than as fog)
    /// has to be applied per octave and then summed. Applying it to the finished sum instead
    /// pushes the whole field toward 1 - because the sum already concentrates around its 0.5 mean -
    /// and every puff comes out uniformly opaque.
    /// </summary>
    private static float Fbm(float x, float y, int basePeriod, int octaves, float gain)
    {
        float sum = 0f;
        float amplitude = 1f;
        float total = 0f;
        int period = basePeriod;

        for (int i = 0; i < octaves; i++)
        {
            float n = ValueNoise(x * period, y * period, period);
            n = 1f - Mathf.Abs(n * 2f - 1f);

            sum += n * amplitude;
            total += amplitude;
            amplitude *= gain;
            period *= 2;
        }

        return sum / Mathf.Max(0.0001f, total);
    }

    private static float ValueNoise(float x, float y, int period)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float fx = x - x0;
        float fy = y - y0;

        // Quintic fade - C2 continuous, so no visible lattice creases after the billow remap.
        float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        float uy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);

        float a = Hash(x0, y0, period);
        float b = Hash(x0 + 1, y0, period);
        float c = Hash(x0, y0 + 1, period);
        float d = Hash(x0 + 1, y0 + 1, period);

        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    private static float Hash(int x, int y, int period)
    {
        // Wrapping the lattice index by the octave period is what makes the noise tileable.
        x = ((x % period) + period) % period;
        y = ((y % period) + period) % period;

        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string[] parts = folder.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}

[CustomEditor(typeof(CloudBank))]
public class CloudBankEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var bank = (CloudBank)target;

        EditorGUILayout.Space();

        if (bank.cloudMaterial == null)
        {
            EditorGUILayout.HelpBox(
                "No cloud material assigned. Use Tools > Environment > Cloud Bank > Create In Scene " +
                "to generate the material and noise texture.", MessageType.Warning);
        }

        EditorGUILayout.HelpBox(BuildStats(bank), MessageType.Info);

        if (GUILayout.Button("Rebuild", GUILayout.Height(26)))
        {
            bank.Rebuild();
            SceneView.RepaintAll();
        }
    }

    private static string BuildStats(CloudBank bank)
    {
        int puffs = 0;
        int renderers = 0;
        int enabledLayers = 0;

        if (bank.layers != null)
        {
            foreach (CloudBank.Layer layer in bank.layers)
            {
                if (layer == null || !layer.enabled)
                {
                    continue;
                }

                enabledLayers++;

                int layerPuffs;
                if (layer.placement == CloudBank.Placement.Disc)
                {
                    layerPuffs = Mathf.Max(1, Mathf.RoundToInt(layer.discCount * bank.qualityScale));
                }
                else
                {
                    // Mirrors the count derivation in CloudBank.GenerateRingPuffs.
                    float averageWidth = Mathf.Max(0.01f, (layer.minWidth + layer.maxWidth) * 0.5f);
                    float step = Mathf.Max(0.01f, averageWidth * layer.angularSpacing);
                    int perRow = Mathf.CeilToInt(2f * Mathf.PI * layer.radius / step);
                    perRow = Mathf.Max(8, Mathf.RoundToInt(perRow * bank.qualityScale));
                    layerPuffs = perRow * layer.rows;
                }

                puffs += layerPuffs;
                renderers += Mathf.Clamp(bank.sectors, 1, layerPuffs);
            }
        }

        // 8 verts / 6 tris per octagonal puff.
        return $"{enabledLayers} layer(s), {puffs} puffs, {puffs * 8} verts, {puffs * 6} tris.\n" +
               $"{renderers} renderers total; roughly {Mathf.CeilToInt(renderers / 3f)} drawn per frame " +
               "after frustum culling.";
    }
}
