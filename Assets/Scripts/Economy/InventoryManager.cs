using UnityEngine;
using System.Collections.Generic;
using System;

[Serializable]
public class InventorySaveData
{
    public Dictionary<string, int> itemCounts = new Dictionary<string, int>();
}

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

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

        LoadInventory();
    }

    private void LoadInventory()
    {
        if (SaveSystem.Exists(SAVE_KEY))
        {
            _saveData = SaveSystem.Load<InventorySaveData>(SAVE_KEY);
        }

        if (_saveData == null)
        {
            _saveData = new InventorySaveData();
            if (_saveData.itemCounts == null) _saveData.itemCounts = new Dictionary<string, int>();
        }
    }

    public void SaveInventory()
    {
        if (_saveData != null)
        {
            SaveSystem.Save(SAVE_KEY, _saveData);
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
