using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds one OmniShade/Standard URP material per Meshy asset folder under
/// "Assets/3D Assets/Jannah Garden Assets", wires up that folder's own maps, and
/// remaps the folder's FBX to use it.
///
/// Texture routing (OmniShade has no metallic/roughness workflow of its own):
///   _MainTex     <- Meshy_AI_&lt;Name&gt;_&lt;stamp&gt;_texture.png   (base colour)
///   _NormalTex   <- Meshy_AI_texture_0_normal.png
///   _EmissiveTex <- Meshy_AI_texture_0_emission.png       (only where non-black)
///   _SpecularTex <- Meshy_AI_texture_0_spec.png           (pre-packed from metallic+roughness:
///                                                          RGB = max(1-rough, metal), A = 1-rough,
///                                                          matching the shader's specTex.r /
///                                                          specTex.a usage)
///
/// Per-asset scalars come from <see cref="JannahGardenAssetTable"/>.
/// Safe to re-run: existing materials are updated in place, keeping their GUIDs
/// and any references from scenes or prefabs.
/// </summary>
public static class JannahGardenMaterialGenerator
{
    const string AssetsRoot = "Assets/3D Assets/Jannah Garden Assets";
    const string ShaderName = "OmniShade/Standard URP";
    const string FbxFile = "Meshy_AI_model.fbx";
    const string NormalFile = "Meshy_AI_texture_0_normal.png";
    const string EmissionFile = "Meshy_AI_texture_0_emission.png";
    const string SpecFile = "Meshy_AI_texture_0_spec.png";
    const int SpecMaxSize = 1024;

    [MenuItem("Tools/Shop/Generate Jannah Garden Materials")]
    public static void Generate()
    {
        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[JannahGarden] Shader \"{ShaderName}\" not found. Is the OmniShade package present?");
            return;
        }

        // The packed spec maps may have been dropped in from outside the editor;
        // import them before anything tries to reference them.
        AssetDatabase.Refresh();

        var entries = JannahGardenAssetTable.Entries;
        var materials = new Material[entries.Length];
        int texturesFixed = 0, created = 0, updated = 0, remapped = 0;
        var problems = new List<string>();

        try
        {
            // ---- Phase 1: importer settings + materials -------------------
            // Batched so the 64 texture reimports resolve in one pass.
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    string dir = $"{AssetsRoot}/{e.Folder}";
                    if (!AssetDatabase.IsValidFolder(dir))
                    {
                        problems.Add($"missing folder: {e.Folder}");
                        continue;
                    }

                    Report("Configuring textures", e.MaterialName, i, entries.Length);

                    string albedoPath = $"{dir}/{e.AlbedoFile}";
                    string normalPath = $"{dir}/{NormalFile}";
                    string emissionPath = $"{dir}/{EmissionFile}";
                    string specPath = $"{dir}/{SpecFile}";

                    if (ConfigureTexture(albedoPath, TextureImporterType.Default, sRGB: true)) texturesFixed++;
                    if (ConfigureTexture(normalPath, TextureImporterType.NormalMap, sRGB: false)) texturesFixed++;
                    if (ConfigureTexture(specPath, TextureImporterType.Default, sRGB: false,
                                         maxSize: SpecMaxSize, keepAlpha: true)) texturesFixed++;
                    if (e.HasEmission &&
                        ConfigureTexture(emissionPath, TextureImporterType.Default, sRGB: true)) texturesFixed++;

                    string matPath = $"{dir}/{e.MaterialName}.mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    bool isNew = mat == null;
                    if (isNew) mat = new Material(shader);
                    else if (mat.shader != shader) mat.shader = shader;

                    Configure(mat, e, albedoPath, normalPath, emissionPath, specPath, problems);

                    if (isNew) { AssetDatabase.CreateAsset(mat, matPath); created++; }
                    else { EditorUtility.SetDirty(mat); updated++; }

                    materials[i] = mat;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // Materials must be real, GUID-bearing assets before an importer can
            // reference them, so the remap pass runs only after this settles.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ---- Phase 2: point each FBX at its material ------------------
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (materials[i] == null) continue;
                    var e = entries[i];
                    Report("Remapping meshes", e.MaterialName, i, entries.Length);

                    string fbxPath = $"{AssetsRoot}/{e.Folder}/{FbxFile}";
                    if (RemapFbx(fbxPath, materials[i], problems)) remapped++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[JannahGarden] {created} material(s) created, {updated} updated, " +
                  $"{remapped} FBX remapped, {texturesFixed} texture importer(s) corrected.");
        if (problems.Count > 0)
            Debug.LogWarning("[JannahGarden] Issues:\n  " + string.Join("\n  ", problems));
    }

    static void Report(string stage, string name, int i, int total)
    {
        EditorUtility.DisplayProgressBar($"{stage} ({i + 1}/{total})", name, (float)i / total);
    }

    // ------------------------------------------------------------------ material

    static void Configure(Material mat, JannahGardenAssetTable.Entry e, string albedoPath,
                          string normalPath, string emissionPath, string specPath,
                          List<string> problems)
    {
        mat.name = e.MaterialName;

        SetTexture(mat, "_MainTex", albedoPath, e.MaterialName, problems);
        SetTexture(mat, "_NormalTex", normalPath, e.MaterialName, problems);
        SetTexture(mat, "_SpecularTex", specPath, e.MaterialName, problems);

        // Meshy albedos are RGB with no alpha channel; ignoring it keeps the
        // surface opaque regardless of what the importer synthesises.
        mat.SetFloat("_IgnoreMainTexAlpha", 1f);

        // Specular comes from the packed map: .r drives brightness, .a drives
        // highlight tightness. The scalars below set each asset's overall level.
        mat.SetFloat("_Specular", 1f);
        mat.SetFloat("_SpecularBrightness", e.SpecBrightness);
        mat.SetFloat("_SpecularSmoothness", e.SpecSmoothness);
        mat.SetColor("_SpecularColor", e.SpecColor);

        if (e.HasEmission)
        {
            SetTexture(mat, "_EmissiveTex", emissionPath, e.MaterialName, problems);
            mat.SetColor("_Emissive", Color.white);   // map supplies the colour
        }
        else
        {
            mat.SetTexture("_EmissiveTex", null);
            mat.SetColor("_Emissive", new Color(0f, 0f, 0f, 0f));
        }

        SyncKeywords(mat);
    }

    static void SetTexture(Material mat, string prop, string path, string owner, List<string> problems)
    {
        if (!mat.HasProperty(prop)) { problems.Add($"{owner}: shader has no {prop}"); return; }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null) { problems.Add($"{owner}: missing texture {path}"); return; }
        mat.SetTexture(prop, tex);
    }

    // ------------------------------------------------------------------ keywords

    // Keywords OmniShade turns on from a property's *value* rather than a
    // [Toggle], mirroring OmniShadeGUI.AutoEnableShaderKeywords so a generated
    // material matches one configured by hand in the inspector.
    static readonly (string keyword, string prop, bool isTexture, float offValue)[] ValueDrivenKeywords =
    {
        ("SPECULAR_MAP",               "_SpecularTex",              true,  0f),
        ("NORMAL_MAP",                 "_NormalTex",                true,  0f),
        ("NORMAL_MAP2",                "_NormalTex2",               true,  0f),
        ("LIGHT_MAP",                  "_LightmapTex",              true,  0f),
        ("EMISSIVE_MAP",               "_EmissiveTex",              true,  0f),
        ("REFLECTION_TEX",             "_ReflectionTex",            true,  0f),
        ("MATCAP",                     "_MatCapTex",                true,  0f),
        ("DETAIL",                     "_DetailTex",                true,  0f),
        ("LAYER1",                     "_Layer1Tex",                true,  0f),
        ("LAYER2",                     "_Layer2Tex",                true,  0f),
        ("LAYER3",                     "_Layer3Tex",                true,  0f),
        ("TRANSPARENCY_MASK",          "_TransparencyMaskTex",      true,  0f),
        ("HEIGHT_COLORS_TEX",          "_HeightColorsTex",          true,  0f),
        ("SHADOW_OVERLAY",             "_ShadowOverlayTex",         true,  0f),
        ("TOP_TEX",                    "_TopTex",                   true,  0f),
        ("NORMAL_MAP_TOP",             "_NormalTopTex",             true,  0f),
        ("BASE_CONTRAST",              "_Contrast",                 false, 1f),
        ("BASE_SATURATION",            "_Saturation",               false, 1f),
        ("MATCAP_CONTRAST",            "_MatCapContrast",           false, 1f),
        ("VERTEX_COLORS_CONTRAST",     "_VertexColorsContrast",     false, 1f),
        ("DETAIL_CONTRAST",            "_DetailContrast",           false, 1f),
        ("TRANSPARENCY_MASK_CONTRAST", "_TransparencyMaskContrast", false, 1f),
        ("TRIPLANAR_SHARPNESS",        "_TriplanarSharpness",       false, 1f),
        ("AMBIENT",                    "_AmbientBrightness",        false, 0f),
        ("ANIME_SOFT",                 "_AnimeSoftness",            false, 0f),
        ("ZOFFSET",                    "_ZOffset",                  false, 0f),
    };

    /// <summary>
    /// Brings the material's keyword set in line with its property values, the
    /// same way the OmniShade inspector would. Without this the maps are
    /// assigned but never sampled.
    /// </summary>
    static void SyncKeywords(Material mat)
    {
        var shader = mat.shader;

        // [Toggle] / [Toggle(KEYWORD)] and [KeywordEnum(...)] declare their
        // keywords in the shader itself, so derive them rather than hard-coding.
        int count = shader.GetPropertyCount();
        for (int i = 0; i < count; i++)
        {
            string prop = shader.GetPropertyName(i);
            if (shader.GetPropertyType(i) != ShaderPropertyType.Float &&
                shader.GetPropertyType(i) != ShaderPropertyType.Range)
                continue;

            foreach (string raw in shader.GetPropertyAttributes(i))
            {
                string attr = raw.Trim().TrimStart('[').TrimEnd(']');
                string name = attr, arg = null;
                int paren = attr.IndexOf('(');
                if (paren >= 0 && attr.EndsWith(")"))
                {
                    name = attr.Substring(0, paren).Trim();
                    arg = attr.Substring(paren + 1, attr.Length - paren - 2);
                }

                if (name == "Toggle")
                {
                    string kw = string.IsNullOrWhiteSpace(arg)
                        ? prop.ToUpperInvariant() + "_ON"
                        : arg.Trim();
                    SetKeyword(mat, kw, mat.GetFloat(prop) == 1f);
                }
                else if (name == "KeywordEnum" && arg != null)
                {
                    var options = arg.Split(',').Select(o => o.Trim()).ToArray();
                    int selected = Mathf.RoundToInt(mat.GetFloat(prop));
                    for (int o = 0; o < options.Length; o++)
                    {
                        string kw = prop.ToUpperInvariant() + "_" +
                                    options[o].ToUpperInvariant().Replace(' ', '_');
                        SetKeyword(mat, kw, o == selected);
                    }
                }
            }
        }

        foreach (var v in ValueDrivenKeywords)
        {
            if (!mat.HasProperty(v.prop)) continue;
            bool on = v.isTexture
                ? mat.GetTexture(v.prop) != null
                : mat.GetFloat(v.prop) != v.offValue;
            SetKeyword(mat, v.keyword, on);
        }

        // The lighting keywords above are inferred from [Toggle(...)] attributes.
        // These two decide whether the surface is lit and whether the packed
        // specular map is sampled at all, so set them outright rather than
        // relying on that inference.
        SetKeyword(mat, "DIFFUSE", mat.GetFloat("_Diffuse") == 1f);
        SetKeyword(mat, "SPECULAR", mat.GetFloat("_Specular") == 1f);

        // The outline pass is off, so skip drawing it entirely.
        bool outline = mat.HasProperty("_Outline") && mat.GetFloat("_Outline") == 1f;
        SetKeyword(mat, "OUTLINE_PASS_DISABLED", !outline);
        mat.SetShaderPassEnabled("SRPDefaultUnlit", outline);

        mat.globalIlluminationFlags = mat.GetColor("_Emissive") != new Color(0f, 0f, 0f, 0f)
            ? MaterialGlobalIlluminationFlags.BakedEmissive
            : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
    }

    static void SetKeyword(Material mat, string keyword, bool on)
    {
        if (on) mat.EnableKeyword(keyword);
        else mat.DisableKeyword(keyword);
    }

    // ------------------------------------------------------------------ importers

    /// <returns>true if the importer had to be changed and reimported.</returns>
    static bool ConfigureTexture(string path, TextureImporterType type, bool sRGB,
                                 int maxSize = 0, bool keepAlpha = false)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return false;

        bool changed = false;
        if (imp.textureType != type) { imp.textureType = type; changed = true; }

        // NormalMap forces linear sampling itself; setting sRGB there is a no-op
        // that would still dirty the importer on every run.
        if (type != TextureImporterType.NormalMap && imp.sRGBTexture != sRGB)
        {
            imp.sRGBTexture = sRGB;
            changed = true;
        }

        if (keepAlpha)
        {
            if (imp.alphaSource != TextureImporterAlphaSource.FromInput)
            {
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                changed = true;
            }
            if (imp.alphaIsTransparency) { imp.alphaIsTransparency = false; changed = true; }
        }

        if (maxSize > 0 && imp.maxTextureSize != maxSize)
        {
            imp.maxTextureSize = maxSize;
            changed = true;
        }

        if (changed) imp.SaveAndReimport();
        return changed;
    }

    // ------------------------------------------------------------------ fbx remap

    static bool RemapFbx(string fbxPath, Material mat, List<string> problems)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) { problems.Add($"not a model: {fbxPath}"); return false; }

        // Collect the FBX's material slot names. On a first run they show up as
        // embedded sub-assets; once remapped they no longer do, so the existing
        // remap table is the source of truth on any later run.
        var names = new HashSet<string>();
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (obj is Material embedded && !string.IsNullOrEmpty(embedded.name))
                names.Add(embedded.name);

        foreach (var kv in importer.GetExternalObjectMap())
            if (kv.Key.type == typeof(Material))
                names.Add(kv.Key.name);

        if (names.Count == 0)
        {
            problems.Add($"no material slot found on {fbxPath}");
            return false;
        }

        foreach (string n in names)
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mat);

        importer.SaveAndReimport();
        return true;
    }
}
