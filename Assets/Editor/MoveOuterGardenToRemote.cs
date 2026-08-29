using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// Moves the "Outer Garden" scene entry out of Default Local Group — where its ~150MB bundle ships
/// embedded in StreamingAssets with every install — into a remote group served from Firebase
/// Hosting like the Shop Item prefabs. The entry's address doesn't change, so
/// LoadingScreenManager/JannahGardenManager keep loading it the same way; it just downloads on
/// first visit (behind the existing loading screen) instead of padding the app download.
///
/// The new group copies its schemas from "Remote Item Prefabs" so build/load paths and compression
/// stay consistent with the remote content that is already known to work.
/// </summary>
public static class MoveOuterGardenToRemote
{
    private const string EntryAddress = "Outer Garden";
    private const string SourceGroupName = "Remote Item Prefabs";
    private const string TargetGroupName = "Remote Scenes";

    [MenuItem("Tools/Addressables/Move Outer Garden Scene To Remote Group")]
    public static void Move()
    {
        MoveInternal();
    }

    public static bool MoveInternal()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[MoveOuterGardenToRemote] No AddressableAssetSettings found.");
            return false;
        }

        AddressableAssetEntry entry = FindEntryByAddress(settings, EntryAddress);
        if (entry == null)
        {
            Debug.LogError($"[MoveOuterGardenToRemote] No entry with address '{EntryAddress}' found.");
            return false;
        }

        AddressableAssetGroup targetGroup = settings.FindGroup(TargetGroupName);
        if (targetGroup == null)
        {
            AddressableAssetGroup template = settings.FindGroup(SourceGroupName);
            if (template == null)
            {
                Debug.LogError($"[MoveOuterGardenToRemote] Template group '{SourceGroupName}' not found — cannot copy remote schema settings.");
                return false;
            }

            targetGroup = settings.CreateGroup(TargetGroupName, false, false, true,
                new List<AddressableAssetGroupSchema>(template.Schemas));

            var bundleSchema = targetGroup.GetSchema<BundledAssetGroupSchema>();
            if (bundleSchema == null || bundleSchema.LoadPath.GetName(settings) != AddressableAssetSettings.kRemoteLoadPath)
            {
                Debug.LogError($"[MoveOuterGardenToRemote] '{TargetGroupName}' did not inherit the remote load path from '{SourceGroupName}' — aborting before moving the entry.");
                return false;
            }
        }

        if (entry.parentGroup == targetGroup)
        {
            Debug.Log($"[MoveOuterGardenToRemote] MOVE_OK (already in '{TargetGroupName}')");
            return true;
        }

        settings.MoveEntry(entry, targetGroup);
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"[MoveOuterGardenToRemote] MOVE_OK entry='{EntryAddress}' group='{TargetGroupName}'");
        return true;
    }

    private static AddressableAssetEntry FindEntryByAddress(AddressableAssetSettings settings, string address)
    {
        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null) continue;
            foreach (AddressableAssetEntry entry in group.entries)
            {
                if (entry.address == address) return entry;
            }
        }
        return null;
    }
}
