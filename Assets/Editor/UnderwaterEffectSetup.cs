using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Creates the underwater materials and puts an <see cref="UnderwaterEffect"/> in the open scene, wired to
/// them. The materials are referenced from the scene on purpose: that is what keeps their shaders in the
/// player build (a Shader.Find at runtime would come back null once the build strips them).
/// </summary>
public static class UnderwaterEffectSetup
{
    private const string MaterialFolder = "Assets/Materials/Underwater";

    [MenuItem("Jannah/Environment/Set Up Underwater Effect")]
    public static void SetUp()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Underwater");

        Material underside = GetOrCreate("Lake Underside", "Jannah/Environment/Lake Underside");
        Material caustics = GetOrCreate("Underwater Caustics", "Jannah/Environment/Underwater Caustics");
        Material overlay = GetOrCreate("Underwater Overlay", "Jannah/Environment/Underwater Overlay");
        Material motes = GetOrCreate("Underwater Motes", "Jannah/Environment/Underwater Motes");
        if (underside == null || caustics == null || overlay == null || motes == null) return;

        UnderwaterEffect effect = Object.FindFirstObjectByType<UnderwaterEffect>(FindObjectsInactive.Include);
        if (effect == null)
        {
            GameObject water = GameObject.Find("Environment/Water");
            var go = new GameObject("Underwater Effect");
            if (water != null) go.transform.SetParent(water.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Add Underwater Effect");
            effect = go.AddComponent<UnderwaterEffect>();
        }

        Undo.RecordObject(effect, "Wire Underwater Effect");
        effect.undersideMaterial = underside;
        effect.causticsMaterial = caustics;
        effect.overlayMaterial = overlay;
        effect.motesMaterial = motes;

        if (effect.lakeSurface == null)
        {
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                Material m = r.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name == "Jannah/Environment/Lake Water")
                {
                    effect.lakeSurface = r;
                    break;
                }
            }
        }

        EditorUtility.SetDirty(effect);
        EditorSceneManager.MarkSceneDirty(effect.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Underwater] Effect set up on '{effect.name}', lake = {(effect.lakeSurface != null ? effect.lakeSurface.name : "auto")}.");
    }

    private static Material GetOrCreate(string name, string shaderName)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[Underwater] Shader '{shaderName}' not found — did it compile?");
            return null;
        }

        mat = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
