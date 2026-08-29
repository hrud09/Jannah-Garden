using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Converts EnvironmentGeneratorWindow's baked "Environment Generated" scene instances into a
/// <see cref="PrePlacedLayoutData"/> asset plus a <see cref="PrePlacedAssetSpawner"/>, then deletes
/// the instances from the scene. The point: a scene's hard prefab references serialize every mesh
/// and texture into the player build (sharedassets), duplicating the Remote Addressables bundles
/// those same Shop Item prefabs already ship in — recording only GUID + transform and re-spawning
/// at runtime removes that duplication from the app download entirely.
///
/// Only instances whose prefab is a registered Addressables entry are baked; anything else under
/// the generated root is left in the scene and reported, never silently dropped.
/// </summary>
public static class PrePlacedLayoutBaker
{
    private const string GeneratedRootName = "Environment Generated";
    private const string SpawnerName = "Pre-Placed Asset Spawner";
    private const string LayoutFolder = "Assets/Data";
    private const string LayoutAssetPath = LayoutFolder + "/PrePlacedLayout.asset";

    [MenuItem("Tools/Environment/Bake Pre-Placed Layout And Strip Scene Instances")]
    public static void Bake()
    {
        BakeActiveScene();
    }

    /// <summary>Batchmode-friendly entry: opens the given scene first, then bakes it.</summary>
    public static bool BakeScenePath(string scenePath)
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.path != scenePath)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }
        return BakeActiveScene();
    }

    public static bool BakeActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();

        GameObject root = FindSceneRoot(scene, GeneratedRootName);
        if (root == null)
        {
            Debug.LogError($"[PrePlacedLayoutBaker] No '{GeneratedRootName}' root in scene '{scene.name}' — nothing to bake.");
            return false;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[PrePlacedLayoutBaker] No AddressableAssetSettings — cannot verify entries are addressable.");
            return false;
        }

        var entries = new List<PrePlacedLayoutData.Entry>();
        var toDelete = new List<GameObject>();
        int kept = 0;

        foreach (Transform chunk in root.transform)
        {
            // Chunks hold item instances directly; anything deeper belongs to the prefabs themselves.
            foreach (Transform item in chunk)
            {
                string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item.gameObject);
                if (string.IsNullOrEmpty(prefabPath))
                {
                    Debug.LogWarning($"[PrePlacedLayoutBaker] '{item.name}' under {chunk.name} is not a prefab instance — left in scene.");
                    kept++;
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(prefabPath);
                if (settings.FindAssetEntry(guid) == null)
                {
                    Debug.LogWarning($"[PrePlacedLayoutBaker] '{prefabPath}' is not an Addressables entry — '{item.name}' left in scene. " +
                                     "Add it to the Remote Item Prefabs group and re-run to bake it.");
                    kept++;
                    continue;
                }

                entries.Add(new PrePlacedLayoutData.Entry
                {
                    assetGuid = guid,
                    position = item.position,
                    rotation = item.rotation,
                    scale = item.lossyScale,
                });
                toDelete.Add(item.gameObject);
            }
        }

        if (entries.Count == 0)
        {
            Debug.LogError("[PrePlacedLayoutBaker] Found no bakeable instances — scene left untouched.");
            return false;
        }

        PrePlacedLayoutData layout = LoadOrCreateLayoutAsset();
        layout.entries = entries;
        EditorUtility.SetDirty(layout);
        AssetDatabase.SaveAssets();

        foreach (GameObject go in toDelete)
        {
            Object.DestroyImmediate(go);
        }

        // The empty chunk scaffolding only matters while it still shelters unbaked stragglers.
        if (kept == 0)
        {
            Object.DestroyImmediate(root);
        }

        GameObject spawnerGo = FindSceneRoot(scene, SpawnerName) ?? new GameObject(SpawnerName);
        PrePlacedAssetSpawner spawner = spawnerGo.GetComponent<PrePlacedAssetSpawner>();
        if (spawner == null) spawner = spawnerGo.AddComponent<PrePlacedAssetSpawner>();
        spawner.layout = layout;
        EditorUtility.SetDirty(spawnerGo);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[PrePlacedLayoutBaker] BAKE_OK baked={entries.Count} keptInScene={kept} layout={LayoutAssetPath} scene={scene.name}");
        return true;
    }

    private static PrePlacedLayoutData LoadOrCreateLayoutAsset()
    {
        var layout = AssetDatabase.LoadAssetAtPath<PrePlacedLayoutData>(LayoutAssetPath);
        if (layout != null) return layout;

        if (!AssetDatabase.IsValidFolder(LayoutFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Data");
        }

        layout = ScriptableObject.CreateInstance<PrePlacedLayoutData>();
        AssetDatabase.CreateAsset(layout, LayoutAssetPath);
        return layout;
    }

    /// <summary>Root lookup that, unlike GameObject.Find, also sees inactive roots.</summary>
    private static GameObject FindSceneRoot(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == name) return go;
        }
        return null;
    }
}
