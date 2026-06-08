#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ShopItemIdAssigner
{
    [MenuItem("Tools/Assign Shop Item IDs")]
    public static void AssignIdsToAllShopItems()
    {
        string[] guids = AssetDatabase.FindAssets("t:ShopItemData");
        int assigned = 0;
        int updated = 0;

        System.Collections.Generic.HashSet<string> usedIds = new System.Collections.Generic.HashSet<string>();

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

        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null) continue;

            SerializedObject so = new SerializedObject(obj);
            SerializedProperty prop = so.FindProperty("itemID");
            if (prop == null)
            {
                // No itemID property found on this asset
                assigned++;
                continue;
            }

            bool needSave = false;
            string current = prop.stringValue;

            if (string.IsNullOrEmpty(current) || usedIds.Contains(current))
            {
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

        Debug.Log($"[ShopItemIdAssigner] Processed {assigned} ShopItemData assets. Assigned/Updated IDs on {updated} assets.");
    }
}
#endif