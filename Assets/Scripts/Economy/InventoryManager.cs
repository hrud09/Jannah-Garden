using UnityEngine;
using System.Collections.Generic;
using System;
using System.Runtime.Serialization;

[Serializable]
public class InventorySaveData : ISerializationCallbackReceiver
{
    /// <summary>
    /// The live counts. JsonUtility skips dictionaries, so they travel as the two parallel lists below;
    /// the field itself stays for the old BinaryFormatter saves, which stored it directly.
    /// </summary>
    public Dictionary<string, int> itemCounts = new Dictionary<string, int>();

    [OptionalField] public List<string> ids;
    [OptionalField] public List<int> counts;

    public void OnBeforeSerialize()
    {
        ids = new List<string>();
        counts = new List<int>();
        if (itemCounts == null) return;

        // Sorted so the same inventory always produces the same JSON — CloudSave compares the text to
        // skip writes that change nothing.
        var keys = new List<string>(itemCounts.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (string id in keys)
        {
            ids.Add(id);
            counts.Add(itemCounts[id]);
        }
    }

    public void OnAfterDeserialize()
    {
        itemCounts = new Dictionary<string, int>();
        if (ids == null || counts == null) return;

        for (int i = 0; i < ids.Count && i < counts.Count; i++)
        {
            if (!string.IsNullOrEmpty(ids[i])) itemCounts[ids[i]] = counts[i];
        }
    }
}

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    /// <summary>Fires when the inventory is replaced wholesale — a newer copy arrived from the cloud.</summary>
    public static event Action OnInventoryReloaded;

    private const string SAVE_KEY = "PlayerInventory";
    private InventorySaveData _saveData;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // Optional: DontDestroyOnLoad(gameObject);
        // Assuming it's placed in a manager scene or main scene.

        // Before loading, so a cloud copy that lands from here on is handed to us rather than missed.
        CloudSave.Register(SAVE_KEY, HandleCloudData);
        LoadInventory();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        CloudSave.Unregister(SAVE_KEY, HandleCloudData);
        Instance = null;
    }

    private void LoadInventory()
    {
        if (SaveSystem.Exists(SAVE_KEY))
        {
            _saveData = SaveSystem.Load<InventorySaveData>(SAVE_KEY);
        }

        EnsureData();
    }

    private void EnsureData()
    {
        if (_saveData == null) _saveData = new InventorySaveData();
        if (_saveData.itemCounts == null) _saveData.itemCounts = new Dictionary<string, int>();
    }

    /// <summary>A newer inventory arrived from Firebase (or null: this account has none).</summary>
    private void HandleCloudData(string json)
    {
        _saveData = json != null ? SaveSystem.FromJson<InventorySaveData>(json, SAVE_KEY) : null;
        EnsureData();

        Debug.Log($"[InventoryManager] Inventory replaced from the cloud — {_saveData.itemCounts.Count} item type(s).");
        OnInventoryReloaded?.Invoke();
    }

    public void SaveInventory()
    {
        if (_saveData != null)
        {
            CloudSave.Save(SAVE_KEY, _saveData);
            Debug.Log("[InventoryManager] Inventory saved.");
        }
    }

    public void AddInventoryItem(string itemID, int amount = 1)
    {
        if (string.IsNullOrEmpty(itemID))
        {
            Debug.LogWarning("[InventoryManager] Cannot add item with empty or null ID.");
            return;
        }

        if (_saveData.itemCounts.ContainsKey(itemID))
        {
            _saveData.itemCounts[itemID] += amount;
        }
        else
        {
            _saveData.itemCounts[itemID] = amount;
        }

        SaveInventory();
        Debug.Log($"[InventoryManager] Added {amount} of item {itemID}. Total: {_saveData.itemCounts[itemID]}");
    }

    public int GetItemQuantity(string itemID)
    {
        if (string.IsNullOrEmpty(itemID) || _saveData == null || _saveData.itemCounts == null) return 0;

        if (_saveData.itemCounts.TryGetValue(itemID, out int count))
        {
            return count;
        }
        return 0;
    }

    public bool ConsumeInventoryItem(string itemID, int amount = 1)
    {
        if (string.IsNullOrEmpty(itemID))
        {
            Debug.LogWarning("[InventoryManager] Cannot consume item with empty or null ID.");
            return false;
        }

        if (_saveData == null || _saveData.itemCounts == null) return false;

        if (_saveData.itemCounts.ContainsKey(itemID))
        {
            if (_saveData.itemCounts[itemID] >= amount)
            {
                _saveData.itemCounts[itemID] -= amount;
                SaveInventory();
                Debug.Log($"[InventoryManager] Consumed {amount} of item {itemID}. Remaining: {_saveData.itemCounts[itemID]}");
                return true;
            }
        }

        Debug.LogWarning($"[InventoryManager] Cannot consume {amount} of item {itemID}: insufficient quantity.");
        return false;
    }
}
