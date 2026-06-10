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

    [MenuItem("Tools/Assign Random Shop Item Unlock Levels")]
    public static void AssignRandomUnlockLevelsToAllShopItems()
    {
        string[] guids = AssetDatabase.FindAssets("t:ShopItemData");
        int processed = 0;

        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null) continue;

            SerializedObject so = new SerializedObject(obj);
            SerializedProperty prop = so.FindProperty("requiredXPLevel");
            if (prop != null)
            {
                // Generate a random required XP level between 1 and 15 (inclusive)
                int randomLevel = UnityEngine.Random.Range(1, 16);
                prop.intValue = randomLevel;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(obj);
                processed++;
            }
            else
            {
                Debug.LogWarning($"[ShopItemIdAssigner] requiredXPLevel property not found on asset: {path}");
            }
        }

        if (processed > 0)
        {
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[ShopItemIdAssigner] Assigned random requiredXPLevel values (1-15) to {processed} ShopItemData assets.");
    }
}
#endif