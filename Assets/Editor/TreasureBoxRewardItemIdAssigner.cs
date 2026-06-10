#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public static class TreasureBoxRewardItemIdAssigner
{
    [MenuItem("Tools/Assign Treasure Box Reward Item IDs")]
    public static void AssignIdsToAllRewardItems()
    {
        string[] guids = AssetDatabase.FindAssets("t:TreasureBoxRewardItemData");
        int assigned = 0;
        int updated = 0;

        HashSet<string> usedIds = new HashSet<string>();

        // First collect existing IDs to avoid duplicates
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null) continue;
            SerializedObject so = new SerializedObject(obj);
            SerializedProperty prop = so.FindProperty("itemID");
            if (prop != null && !string.IsNullOrEmpty(prop.stringValue))
            {
                usedIds.Add(prop.stringValue);
            }
        }

        // Assign/Update IDs
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null) continue;

            SerializedObject so = new SerializedObject(obj);
            SerializedProperty prop = so.FindProperty("itemID");
            if (prop == null)
            {
                assigned++;
                continue;
            }

            bool needSave = false;
            string current = prop.stringValue;

            // If empty or duplicate, generate a new random ID
            if (string.IsNullOrEmpty(current) || usedIds.Contains(current))
            {
                // Remove the current one if it was added to usedIds as a duplicate check
                if (!string.IsNullOrEmpty(current))
                {
                    usedIds.Remove(current);
                }

                string newId;
                do
                {
                    newId = System.Guid.NewGuid().ToString("N");
                } while (usedIds.Contains(newId));

                prop.stringValue = newId;
                usedIds.Add(newId);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(obj);
                needSave = true;
            }
            else
            {
                // Ensure current ID is registered as used
                usedIds.Add(current);
            }

            if (needSave)
            {
                updated++;
            }

            assigned++;
        }

        if (updated > 0)
        {
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[TreasureBoxRewardItemIdAssigner] Processed {assigned} TreasureBoxRewardItemData assets. Assigned/Updated IDs on {updated} assets.");
        EditorUtility.DisplayDialog(
            "Assign IDs",
            $"Processed {assigned} TreasureBoxRewardItemData assets. Assigned/Updated IDs on {updated} assets.",
            "OK"
        );
    }
}
#endif
