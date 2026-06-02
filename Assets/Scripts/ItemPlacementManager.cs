using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[System.Serializable]
public class PlacedItemSaveData
{
    public string uniqueId;
    public string prefabName;
    public Vector3 position;
    public Quaternion rotation;
    public float remainingDuration;
    public float totalDuration;
}

[System.Serializable]
public class SaveStateCollection
{
    public double gameClosedTimeUnix;
    public List<PlacedItemSaveData> items = new List<PlacedItemSaveData>();
}

public class ItemPlacementManager : MonoBehaviour
{
    public RectTransform crosshairRect;
    public TerrainCollider terrainCollider;
    public Button placeButton;

    [Header("Shop/Prefab References")]
    public InGameShopManager shopManager;
    [Tooltip("Explicit array of prefabs for quick loading. Fallback searches shopManager.")]
    public GameObject[] placeablePrefabs;

    private GameObject currentPlacedObject;
    private ShopItemData pendingItemData;
    private List<PlaceableItem> activePlacedItems = new List<PlaceableItem>();
    private const string SAVE_KEY = "PlacedItemsData";

    private void Start()
    {
        if (placeButton != null)
        {
            placeButton.onClick.AddListener(HandlePlaceButtonClick);
            placeButton.gameObject.SetActive(false); // Hide the place button by default
        }

        // Load previously placed items on startup
        LoadPlacedItems();
    }

    private void Update()
    {
        if (currentPlacedObject != null)
        {
            UpdatePlacementPosition();
        }
    }

    private void OnApplicationQuit()
    {
        SavePlacedItems();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            SavePlacedItems();
        }
    }

    /// <summary>
    /// Prepares placement for an item but does not spawn it yet.
    /// Activates the Place button and sets its label to "Place".
    /// </summary>
    public void PreparePlacement(ShopItemData itemData)
    {
        if (itemData == null || itemData.itemPrefab == null) return;

        // If there's an existing preview being placed, destroy it
        if (currentPlacedObject != null)
        {
            Destroy(currentPlacedObject);
            currentPlacedObject = null;
        }

        pendingItemData = itemData;

        // Activate the placement button
        if (placeButton != null)
        {
            placeButton.gameObject.SetActive(true);
            var btnText = placeButton.GetComponentInChildren<TMPro.TMP_Text>();
            if (btnText != null) btnText.text = "Place";
        }
    }

    /// <summary>
    /// Handles the placeButton click.
    /// If no item is currently being previewed (following the crosshair), starts placement.
    /// If an item is being previewed, finalizes its placement.
    /// </summary>
    public void HandlePlaceButtonClick()
    {
        if (currentPlacedObject == null)
        {
            // Start placement: spawn the item and let it follow the crosshair
            if (pendingItemData != null && pendingItemData.itemPrefab != null)
            {
                currentPlacedObject = Instantiate(pendingItemData.itemPrefab);

                PlaceableItem placeable = currentPlacedObject.GetComponent<PlaceableItem>();
                if (placeable != null)
                {
                    placeable.enabled = false;
                }

                UpdatePlacementPosition();

                // Change button text to "Confirm"
                if (placeButton != null)
                {
                    var btnText = placeButton.GetComponentInChildren<TMPro.TMP_Text>();
                    if (btnText != null) btnText.text = "Confirm";
                }
            }
        }
        else
        {
            // Finalize placement
            PlaceItem();
        }
    }

    /// <summary>
    /// Projects a ray from the camera through the crosshair onto the terrain collider,
    /// updating the position of the preview object.
    /// </summary>
    private void UpdatePlacementPosition()
    {
        if (currentPlacedObject == null || terrainCollider == null || crosshairRect == null) return;

        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        // Cast a ray from camera through crosshair screen space position
        Ray ray = mainCam.ScreenPointToRay(crosshairRect.position);
        RaycastHit hit;

        // Raycast specifically against the TerrainCollider
        if (terrainCollider.Raycast(ray, out hit, 1000f))
        {
            currentPlacedObject.transform.position = hit.point;
        }
    }

    /// <summary>
    /// Confirms placement of the current item, sets up placement time tracking, and saves the game state.
    /// </summary>
    public void PlaceItem()
    {
        if (currentPlacedObject == null) return;

        // Enable and initialize the PlaceableItem component on the placed object
        PlaceableItem placeable = currentPlacedObject.GetComponent<PlaceableItem>();
        if (placeable == null)
        {
            placeable = currentPlacedObject.AddComponent<PlaceableItem>();
        }

        placeable.enabled = true;

        // Initialize tracking with unique ID and total duration
        string uniqueId = System.Guid.NewGuid().ToString();
        placeable.Initialize(uniqueId, placeable.placementDuration, placeable.placementDuration);

        // Strip "(Clone)" suffix from the spawned gameobject name to find it when loading
        string prefabName = currentPlacedObject.name.Replace("(Clone)", "").Trim();
        placeable.prefabName = prefabName;

        // Add to tracking list and save state
        activePlacedItems.Add(placeable);
        SavePlacedItems();

        // Clear preview control references and hide placement button
        currentPlacedObject = null;
        pendingItemData = null; // Reset pending state
        if (placeButton != null)
        {
            placeButton.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Saves the current list of placed items and the shutdown timestamp to PlayerPrefs.
    /// </summary>
    public void SavePlacedItems()
    {
        SaveStateCollection state = new SaveStateCollection();
        state.gameClosedTimeUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Remove any deleted items from active tracking list
        activePlacedItems.RemoveAll(item => item == null);

        foreach (var item in activePlacedItems)
        {
            PlacedItemSaveData data = new PlacedItemSaveData
            {
                uniqueId = item.uniqueId,
                prefabName = item.prefabName,
                position = item.transform.position,
                rotation = item.transform.rotation,
                remainingDuration = item.remainingDuration,
                totalDuration = item.placementDuration
            };
            state.items.Add(data);
        }

        string json = JsonUtility.ToJson(state);
        PlayerPrefs.SetString(SAVE_KEY, json);
        PlayerPrefs.Save();
        Debug.Log("Placed items successfully saved to PlayerPrefs.");
    }

    /// <summary>
    /// Loads placed items from PlayerPrefs and offsets remaining times by offline elapsed duration.
    /// </summary>
    private void LoadPlacedItems()
    {
        if (!PlayerPrefs.HasKey(SAVE_KEY)) return;

        string json = PlayerPrefs.GetString(SAVE_KEY);
        SaveStateCollection state = JsonUtility.FromJson<SaveStateCollection>(json);
        if (state == null || state.items == null) return;

        double currentUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        double elapsedOffline = 0;

        if (state.gameClosedTimeUnix > 0)
        {
            elapsedOffline = currentUnix - state.gameClosedTimeUnix;
        }

        // Clean existing active items list
        activePlacedItems.Clear();

        foreach (var itemData in state.items)
        {
            GameObject prefab = GetPrefabByName(itemData.prefabName);
            if (prefab != null)
            {
                GameObject spawned = Instantiate(prefab, itemData.position, itemData.rotation);
                
                PlaceableItem placeable = spawned.GetComponent<PlaceableItem>();
                if (placeable == null)
                {
                    placeable = spawned.AddComponent<PlaceableItem>();
                }
                
                placeable.enabled = true;
                placeable.prefabName = itemData.prefabName;

                // Deduct the elapsed offline time from the remaining duration
                float newRemaining = itemData.remainingDuration - (float)elapsedOffline;
                if (newRemaining < 0) newRemaining = 0;

                placeable.Initialize(itemData.uniqueId, itemData.totalDuration, newRemaining);
                activePlacedItems.Add(placeable);
            }
            else
            {
                Debug.LogWarning("Failed to find placeable prefab named: " + itemData.prefabName);
            }
        }
    }

    /// <summary>
    /// Searches for a placeable prefab matching the provided name.
    /// </summary>
    private GameObject GetPrefabByName(string prefabName)
    {
        // 1. Search custom prefabs list
        if (placeablePrefabs != null)
        {
            foreach (var prefab in placeablePrefabs)
            {
                if (prefab != null && prefab.name == prefabName)
                {
                    return prefab;
                }
            }
        }

        // 2. Search shop manager's data sources
        if (shopManager != null && shopManager.shopItemDatas != null)
        {
            foreach (var data in shopManager.shopItemDatas)
            {
                if (data != null && data.itemPrefab != null && data.itemPrefab.name == prefabName)
                {
                    return data.itemPrefab;
                }
            }
        }

        return null;
    }
}
