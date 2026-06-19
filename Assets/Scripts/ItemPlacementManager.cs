using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[System.Serializable]
public class PlacedItemSaveData
{
    public string uniqueId;
    public string prefabName;
    public SerializableVector3 position;
    public SerializableQuaternion rotation;
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
    public static ItemPlacementManager Instance { get; private set; }

    public RectTransform crosshairRect;
    public TerrainCollider terrainCollider;
    public Button placeButton;

    [Header("Shop/Prefab References")]
    public InGameShopManager shopManager;
    [Tooltip("Explicit array of prefabs for quick loading. Fallback searches shopManager.")]
    public GameObject[] placeablePrefabs;

    private GameObject currentPlacedObject;
    private GameObject _pendingItemPrefab;
    private float _pendingDuration;
    private int _pendingRequiredXPLevel;
    private TreasureBoxRewardItemData _pendingRewardItemData;
    private List<PlaceableItem> activePlacedItems = new List<PlaceableItem>();
    private const string SAVE_KEY = "PlacedItemsData";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

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

    public void PreparePlacement(ShopItemData itemData)
    {
        if (itemData == null || itemData.itemPrefab == null) return;
        _pendingRequiredXPLevel = itemData.requiredXPLevel;
        _pendingRewardItemData = null;
        InternalPreparePlacement(
            itemData.itemPrefab, 
            itemData.itemPrefab, 
            itemData.placementTimerDuration
        );
    }
    
    public void PreparePlacement(TreasureBoxRewardItemData itemData)
    {
        if (itemData == null || itemData.itemPrefab == null) return;
        _pendingRequiredXPLevel = 0; // No XP reward for treasure box items
        _pendingRewardItemData = itemData;
        InternalPreparePlacement(
            itemData.itemPrefab, 
            itemData.itemPrefab, 
            itemData.placementTimerDuration
        );
    }

    private void InternalPreparePlacement(GameObject prefab, GameObject previewPrefab, float duration)
    {
        // If there's an existing preview being placed, destroy it
        if (currentPlacedObject != null)
        {
            Objectpool.Instance.Despawn(currentPlacedObject);
            currentPlacedObject = null;
        }

        _pendingItemPrefab = prefab;
        _pendingDuration = duration;

        currentPlacedObject = Objectpool.Instance.Spawn(previewPrefab);

        // Disable PlaceableItem on the ghost so the countdown doesn't start yet
        PlaceableItem placeable = currentPlacedObject.GetComponent<PlaceableItem>();
        if (placeable != null)
        {
            placeable.enabled = false;

            // Make sure the preview starts unsaturated
            placeable.UpdateSaturation(0f, -1);
            // Apply starting scale of 0.2x to the preview model to match the initial placement scale
            placeable.SetScaleMultiplier(0.2f);

            // Show the timer label immediately so the player can see the
            // duration before confirming placement.
            placeable.PreviewTimer(duration);
        }

        UpdatePlacementPosition();

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
    /// Immediately confirms and finalizes placement.
    /// </summary>
    public void HandlePlaceButtonClick()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ItemPlace);
        if (currentPlacedObject != null)
        {
            // Finalize placement immediately
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

        // ── Swap ghost → real prefab ──────────────────────────────────────────
        // Record where the ghost ended up, then destroy it.
        Vector3 confirmedPosition = currentPlacedObject.transform.position;
        Quaternion confirmedRotation = currentPlacedObject.transform.rotation;
        Objectpool.Instance.Despawn(currentPlacedObject);
        currentPlacedObject = null;

        // Spawn the real item prefab at the confirmed position.
        if (_pendingItemPrefab == null)
        {
            Debug.LogWarning("[ItemPlacementManager] PlaceItem: no pending item prefab available.");
            if (placeButton != null) placeButton.gameObject.SetActive(false);
            return;
        }

        GameObject realObject = Objectpool.Instance.Spawn(_pendingItemPrefab, confirmedPosition, confirmedRotation);
        // ─────────────────────────────────────────────────────────────────────

        // Enable and initialize the PlaceableItem component on the real object
        PlaceableItem placeable = realObject.GetComponent<PlaceableItem>();
        if (placeable == null)
        {
            placeable = realObject.AddComponent<PlaceableItem>();
        }

        placeable.enabled = true;

        // Use the duration defined in the data asset as the authoritative source.
        float totalDuration = _pendingDuration;

        string uniqueId = System.Guid.NewGuid().ToString();
        placeable.Initialize(uniqueId, totalDuration, totalDuration);

        // Strip "(Clone)" suffix so we can find the prefab by name when loading
        string prefabName = realObject.name.Replace("(Clone)", "").Trim();
        placeable.prefabName = prefabName;

        // Add to tracking list and save state
        activePlacedItems.Add(placeable);
        SavePlacedItems();

        // Award XP if applicable
        if (_pendingRequiredXPLevel > 0 && PlayerXPManager.Instance != null)
        {
            PlayerXPManager.Instance.AddXPForPlacingShopItem(_pendingRequiredXPLevel);
        }

        if (_pendingRewardItemData != null)
        {
            _pendingRewardItemData.quantity++;
            if (shopManager != null)
            {
                shopManager.UpdateInventoryUI(_pendingRewardItemData);
            }
            _pendingRewardItemData = null; // Clear after use
        }

        // Clear preview control references and hide placement button
        currentPlacedObject = null;
        _pendingItemPrefab = null; // Reset pending state
        _pendingRequiredXPLevel = 0;
        if (placeButton != null)
        {
            placeButton.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Saves the current list of placed items and the shutdown timestamp to binary save file.
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

        SaveSystem.Save(SAVE_KEY, state);
        Debug.Log("Placed items successfully saved to binary using SaveSystem.");
    }

    /// <summary>
    /// Loads placed items from binary save file and offsets remaining times by offline elapsed duration.
    /// </summary>
    private void LoadPlacedItems()
    {
        if (!SaveSystem.Exists(SAVE_KEY)) return;

        SaveStateCollection state = SaveSystem.Load<SaveStateCollection>(SAVE_KEY);
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
                GameObject spawned = Objectpool.Instance.Spawn(prefab, itemData.position, itemData.rotation);
                
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
