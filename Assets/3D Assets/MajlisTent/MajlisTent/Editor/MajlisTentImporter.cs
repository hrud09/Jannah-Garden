// Auto-configures the Majlis Tent asset for mobile when it is imported into Unity.
// Keep this file inside an "Editor" folder (it already is if you copy the whole MajlisTent folder).
//
//  * Textures  (T_Majlis_*.png): mipmaps, max 1024, ASTC 6x6 on Android + iOS,
//                                coverage-preserving mips for the tassel cut-out.
//  * Model     (SM_MajlisTent*.fbx): no cameras/lights/animation, imported normals,
//                                no tangents (no normal maps), lightmap UVs generated.
//  * Materials: URP "Simple Lit" when URP is active, otherwise built-in "Mobile/Diffuse";
//               M_Majlis_Fringe is set up as alpha-clip (cut-out).
//  * LOD Group (auto-created by Unity from the _LOD0/_LOD1 names):
//               LOD0 above 25% screen height, LOD1 down to 2%, culled below that.
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

public class MajlisTentImporter : AssetPostprocessor
{
    const float Lod0ScreenHeight = 0.25f; // switch to far LOD below this
    const float CullScreenHeight = 0.02f; // hide completely below this

    static bool IsTentTexture(string path) => Path.GetFileName(path).StartsWith("T_Majlis_");
    static bool IsTentModel(string path) => Path.GetFileName(path).StartsWith("SM_MajlisTent");

    void OnPreprocessTexture()
    {
        if (!IsTentTexture(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        bool cutout = assetPath.Contains("Fringe");

        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.filterMode = FilterMode.Bilinear;
        ti.anisoLevel = 1;
        ti.maxTextureSize = 1024;
        ti.alphaSource = cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.alphaIsTransparency = cutout;
        if (cutout)
        {
            ti.mipMapsPreserveCoverage = true;   // tassels don't vanish at distance
            ti.alphaTestReferenceValue = 0.5f;
        }

        foreach (var platform in new[] { "Android", "iPhone" })
        {
            var s = ti.GetPlatformTextureSettings(platform);
            s.overridden = true;
            s.maxTextureSize = 1024;
            s.format = TextureImporterFormat.ASTC_6x6;
            s.compressionQuality = 50;
            ti.SetPlatformTextureSettings(s);
        }
    }

    void OnPreprocessModel()
    {
        if (!IsTentModel(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importBlendShapes = false;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.isReadable = false;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.None;
        mi.generateSecondaryUV = true;          // for baked lightmaps
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
    }

    void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        if (!IsTentModel(assetPath)) return;

        string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        string texName = description.materialName.Replace("M_Majlis_", "T_Majlis_");
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/Textures/" + texName + ".png");
        bool cutout = description.materialName.Contains("Fringe");

        Shader urp = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (GraphicsSettings.currentRenderPipeline != null && urp != null)
        {
            material.shader = urp;
            if (tex != null) material.SetTexture("_BaseMap", tex);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.1f);
            if (cutout)
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.5f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
        }
        else
        {
            material.shader = Shader.Find(cutout ? "Legacy Shaders/Transparent/Cutout/Diffuse" : "Mobile/Diffuse");
            if (tex != null) material.mainTexture = tex;
            if (cutout) material.SetFloat("_Cutoff", 0.5f);
        }
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!IsTentModel(assetPath)) return;

        var lg = root.GetComponent<LODGroup>();
        if (lg != null)
        {
            var lods = lg.GetLODs();
            if (lods.Length >= 2)
            {
                lods[0].screenRelativeTransitionHeight = Lod0ScreenHeight;
                lods[1].screenRelativeTransitionHeight = CullScreenHeight;
                lg.SetLODs(lods);
                lg.RecalculateBounds();
            }
        }

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic);
    }

    // If the textures finish importing after the model, re-import the model so its materials pick them up.
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var p in imported)
        {
            if (!IsTentTexture(p)) continue;
            string modelDir = Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
            foreach (var guid in AssetDatabase.FindAssets("SM_MajlisTent t:Model", new[] { modelDir }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            break;
        }
    }
}
