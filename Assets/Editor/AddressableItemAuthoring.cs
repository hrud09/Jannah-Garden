using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Shared editor-time plumbing for moving shop/treasure-box item prefabs into the Remote Addressables
/// group, so they download on demand at runtime instead of shipping in the app build. Used by the
/// one-time migration tool (Tools/Addressables/Migrate Item Prefabs To Addressables) and by every
/// shop-item generator tool that assigns a prefab onto a ShopItemData/TreasureBoxRewardItemData asset.
/// </summary>
public static class AddressableItemAuthoring
{
    public const string RemoteGroupName = "Remote Item Prefabs";

    /// <summary>Finds the shared remote item-prefab group, creating it with the right schema settings if missing.</summary>
    public static AddressableAssetGroup GetOrCreateRemoteGroup(AddressableAssetSettings settings)
    {
        AddressableAssetGroup group = settings.FindGroup(RemoteGroupName);
        if (group == null)
        {
            group = settings.CreateGroup(
                RemoteGroupName,
                setAsDefaultGroup: false,
                readOnly: false,
                postEvent: false,
                schemasToCopy: null,
                typeof(BundledAssetGroupSchema),
                typeof(ContentUpdateGroupSchema));
        }

        var schema = group.GetSchema<BundledAssetGroupSchema>();
        if (schema != null)
        {
            schema.BuildPath.SetVariableByName(settings, "Remote.BuildPath");
            schema.LoadPath.SetVariableByName(settings, "Remote.LoadPath");

            // PackSeparately, not the project default PackTogether — otherwise every item prefab lands
            // in one shared bundle and unlocking a single item downloads all of them, defeating the
            // point of moving them out of the build in the first place.
            schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            schema.IncludeInBuild = true;
            EditorUtility.SetDirty(schema);
        }

        EditorUtility.SetDirty(group);
        return group;
    }

    /// <summary>
    /// Ensures <paramref name="prefab"/> has an Addressables entry in <paramref name="remoteGroup"/> and
    /// points <paramref name="assetRef"/> at it. Safe to call repeatedly — CreateOrMoveEntry is idempotent
    /// per-GUID, so two items sharing the same prefab correctly collapse onto one bundle entry.
    /// </summary>
    public static bool AssignPrefab(AssetReferenceGameObject assetRef, GameObject prefab,
        AddressableAssetSettings settings, AddressableAssetGroup remoteGroup)
    {
        if (assetRef == null || prefab == null || settings == null || remoteGroup == null) return false;

        string assetPath = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(assetPath)) return false;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return false;

        settings.CreateOrMoveEntry(guid, remoteGroup);
        return assetRef.SetEditorAsset(prefab);
    }
}
