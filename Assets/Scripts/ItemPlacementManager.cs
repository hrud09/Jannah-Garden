using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using FlutterIntegration;

[System.Serializable]
public class PlacedItemSaveData
{
    public string uniqueId;
    public string prefabName;
    public SerializableVector3 position;
    public SerializableQuaternion rotation;
    public float remainingDuration;
    public float totalDuration;

    // Added after the first release. BinaryFormatter leaves fields it does not find in an older save
    // at their defaults, so old save files still load — they just come back as Unknown/empty and fall
    // back to a prefab-name lookup when the player returns the item to the store.
    public string sourceItemId;
    public PlacedItemSource sourceKind;
}

[System.Serializable]
public class SaveStateCollection
{
    public double gameClosedTimeUnix;

    /// <summary>
    /// Bumped on every save. Used only to break ties when two devices report the same save second —
    /// see <see cref="ItemPlacementManager.IsCloudStateNewer"/>.
    /// </summary>
    public int revision;

    public List<PlacedItemSaveData> items = new List<PlacedItemSaveData>();
}

public class ItemPlacementManager : MonoBehaviour
{
    public static ItemPlacementManager Instance { get; private set; }

    public static event System.Action<PlaceableItem> OnItemPlaced;

    /// <summary>Fired when an item is taken out of the garden, whether returned to the store or picked up to be moved.</summary>
    public static event System.Action<PlaceableItem> OnItemRemoved;

    public RectTransform crosshairRect;
    public TerrainCollider terrainCollider;
    public Button placeButton;

    [Tooltip("Optional. Abandons the placement in progress. For a relocation the item goes back where " +
             "it was; for a fresh purchase the item is handed back the same way a return would.")]
    public Button cancelPlacementButton;

    [Header("Shop/Prefab References")]
    public InGameShopManager shopManager;
    [Tooltip("Explicit array of prefabs for quick loading. Fallback searches shopManager.")]
    public GameObject[] placeablePrefabs;

    [Header("Return To Asset Store")]
    [Tooltip("Share of the Noor Coin price refunded when a shop item is returned to the store. " +
             "1 = the full price back, 0.5 = half. Treasure box rewards always come back as one whole item.")]
    [Range(0f, 1f)]
    public float storeRefundRate = 1f;

    [Header("Cloud Save")]
    [Tooltip("Mirror every change to Firebase through the Flutter bridge, so the garden follows the " +
             "player onto a new device. Turn off to keep the garden device-local.")]
    public bool syncToCloud = true;

    [Tooltip("Seconds to wait after a change before pushing to Firebase. Collapses a burst of edits " +
             "(placing three trees in a row) into a single write.")]
    [Range(0.5f, 30f)]
    public float cloudPushDelay = 3f;

    private GameObject currentPlacedObject;
    private GameObject _pendingItemPrefab;
    private float _pendingDuration;
    private int _pendingRequiredXPLevel;
    private TreasureBoxRewardItemData _pendingRewardItemData;
    private PlacedItemSource _pendingSourceKind = PlacedItemSource.Unknown;
    private string _pendingSourceItemId;

    // ── Relocation state ──────────────────────────────────────────────────────
    // A relocation is a placement that reuses an existing item's identity and growth progress instead
    // of minting a new one, and that must not charge the player or award XP a second time.
    private bool _isRelocating;
    private string _relocateUniqueId;
    private float _relocateRemainingDuration;
    private Vector3 _relocateOriginalPosition;
    private Quaternion _relocateOriginalRotation;

    private List<PlaceableItem> activePlacedItems = new List<PlaceableItem>();
    private const string SAVE_KEY = "PlacedItemsData";

    // ── Cloud state ───────────────────────────────────────────────────────────
    private int _revision;
    private double _lastSavedAtUnix;
    private Coroutine _cloudPushRoutine;
    private bool _cloudPushPending;
    private GardenStatePayload _deferredCloudState;

    /// <summary>True while an item is following the crosshair and waiting to be confirmed.</summary>
    public bool IsPlacing => currentPlacedObject != null;

    /// <summary>True when the placement in progress is moving an item that is already in the garden.</summary>
    public bool IsRelocating => _isRelocating;

    /// <summary>Every item currently standing in the garden.</summary>
    public IReadOnlyList<PlaceableItem> ActivePlacedItems => activePlacedItems;

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

        if (cancelPlacementButton != null)
        {
            cancelPlacementButton.onClick.AddListener(CancelPlacement);
            cancelPlacementButton.gameObject.SetActive(false);
        }

        // Load previously placed items on startup
        LoadPlacedItems();

        BeginCloudSync();
    }

    private void OnDestroy()
    {
        FlutterBridge.OnGardenStateReceived -= HandleCloudGardenState;

        if (placeButton != null) placeButton.onClick.RemoveListener(HandlePlaceButtonClick);
        if (cancelPlacementButton != null) cancelPlacementButton.onClick.RemoveListener(CancelPlacement);

        if (Instance == this) Instance = null;
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
        SaveEverything();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            SaveEverything();
        }
    }

    /// <summary>
    /// Writes the garden out both locally and to Firebase. An item being moved is not in the garden
    /// right now — it is a ghost following the crosshair — so put it back first, or leaving the game
    /// mid-move would save a garden without it and lose it for good.
    /// </summary>
    private void SaveEverything()
    {
        if (_isRelocating) CancelPlacement();

        SavePlacedItems();
        FlushCloudPush();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PLACEMENT
    // ═══════════════════════════════════════════════════════════════════════════

    public void PreparePlacement(ShopItemData itemData)
    {
        if (itemData == null || itemData.itemPrefab == null) return;

        // Buying something in the middle of a move would overwrite the relocation state and lose the
        // item being carried. Put it back first.
        if (_isRelocating) CancelPlacement();

        _pendingRequiredXPLevel = itemData.requiredXPLevel;
        _pendingRewardItemData = null;
        _pendingSourceKind = PlacedItemSource.ShopItem;
        _pendingSourceItemId = itemData.itemID;

        GameObject preview = itemData.itemPlacementModelPrefab != null
            ? itemData.itemPlacementModelPrefab
            : itemData.itemPrefab;

        InternalPreparePlacement(
            itemData.itemPrefab,
            preview,
            itemData.placementTimerDuration
        );
    }

    public void PreparePlacement(TreasureBoxRewardItemData itemData)
    {
        if (itemData == null || itemData.itemPrefab == null) return;

        // As above: never let a new placement swallow the item currently being moved.
        if (_isRelocating) CancelPlacement();

        _pendingRequiredXPLevel = 0; // No XP reward for treasure box items
        _pendingRewardItemData = itemData;
        _pendingSourceKind = PlacedItemSource.InventoryItem;
        _pendingSourceItemId = itemData.itemID;

        GameObject preview = itemData.itemPlacementModelPrefab != null
            ? itemData.itemPlacementModelPrefab
            : itemData.itemPrefab;

        InternalPreparePlacement(
            itemData.itemPrefab,
            preview,
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
            placeable.PreviewTimer(_isRelocating ? _relocateRemainingDuration : duration);
        }

        UpdatePlacementPosition();

        // Activate the placement button
        if (placeButton != null)
        {
            placeButton.gameObject.SetActive(true);
            var btnText = placeButton.GetComponentInChildren<TMPro.TMP_Text>();
            if (btnText != null) btnText.text = _isRelocating ? "Move Here" : "Place";
        }

        if (cancelPlacementButton != null)
        {
            cancelPlacementButton.gameObject.SetActive(true);
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
            ClearPendingPlacement();
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

        // A relocation keeps the item's identity and its growth progress — moving a half-grown tree
        // must not restart its timer, and must not mint a second item in the save file.
        string uniqueId = _isRelocating ? _relocateUniqueId : System.Guid.NewGuid().ToString();
        float remainingDuration = _isRelocating ? _relocateRemainingDuration : totalDuration;

        placeable.Initialize(uniqueId, totalDuration, remainingDuration);
        placeable.SetSource(_pendingSourceKind, _pendingSourceItemId);

        // Strip "(Clone)" suffix so we can find the prefab by name when loading
        string prefabName = realObject.name.Replace("(Clone)", "").Trim();
        placeable.prefabName = prefabName;

        // Add to tracking list and save state
        activePlacedItems.Add(placeable);
        SavePlacedItems();

        OnItemPlaced?.Invoke(placeable);

        if (_isRelocating)
        {
            // The player already paid for this item the first time round: no XP, no stock consumed.
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("Moved");
            }
        }
        else
        {
            // Award XP if applicable
            if (_pendingRequiredXPLevel > 0 && PlayerXPManager.Instance != null)
            {
                PlayerXPManager.Instance.AddXPForPlacingShopItem(_pendingRequiredXPLevel);
            }

            if (_pendingRewardItemData != null)
            {
                if (InventoryManager.Instance != null)
                {
                    InventoryManager.Instance.ConsumeInventoryItem(_pendingRewardItemData.itemID, 1);
                    _pendingRewardItemData.quantity = InventoryManager.Instance.GetItemQuantity(_pendingRewardItemData.itemID);
                }
                else
                {
                    if (_pendingRewardItemData.quantity > 0) _pendingRewardItemData.quantity--;
                }

                if (shopManager != null)
                {
                    shopManager.UpdateInventoryUI(_pendingRewardItemData);
                }
            }
        }

        ClearPendingPlacement();
        ApplyDeferredCloudState();
    }

    /// <summary>
    /// Abandons the placement in progress. A relocated item goes back exactly where it stood; a freshly
    /// bought one is handed back through the same route a return uses, so the player is never charged
    /// for an item that never made it into the garden.
    /// </summary>
    public void CancelPlacement()
    {
        if (currentPlacedObject == null) return;

        Objectpool.Instance.Despawn(currentPlacedObject);
        currentPlacedObject = null;

        if (_isRelocating && _pendingItemPrefab != null)
        {
            RestoreRelocatedItem();
        }
        else
        {
            RefundPendingPurchase();
        }

        ClearPendingPlacement();
        ApplyDeferredCloudState();
    }

    /// <summary>Puts a relocated item back at the position it was picked up from.</summary>
    private void RestoreRelocatedItem()
    {
        GameObject restored = Objectpool.Instance.Spawn(
            _pendingItemPrefab, _relocateOriginalPosition, _relocateOriginalRotation);

        PlaceableItem placeable = restored.GetComponent<PlaceableItem>();
        if (placeable == null)
        {
            placeable = restored.AddComponent<PlaceableItem>();
        }

        placeable.enabled = true;
        placeable.prefabName = restored.name.Replace("(Clone)", "").Trim();
        placeable.Initialize(_relocateUniqueId, _pendingDuration, _relocateRemainingDuration);
        placeable.SetSource(_pendingSourceKind, _pendingSourceItemId);

        activePlacedItems.Add(placeable);
        SavePlacedItems();

        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast("Move cancelled");
        }
    }

    /// <summary>Gives back whatever a cancelled first-time placement was bought with.</summary>
    private void RefundPendingPurchase()
    {
        if (_pendingSourceKind == PlacedItemSource.InventoryItem && _pendingRewardItemData != null)
        {
            // Stock was not consumed yet — PlaceItem does that — so there is nothing to give back.
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("Placement cancelled");
            }
            return;
        }

        ShopItemData shopData = FindShopItemData(_pendingSourceItemId, _pendingItemPrefab != null ? _pendingItemPrefab.name : null);
        int refund = CalculateCoinRefund(shopData);

        if (refund > 0 && NoorCoinManager.Instance != null)
        {
            NoorCoinManager.Instance.Earn(refund);
        }
        else if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast("Placement cancelled");
        }
    }

    private void ClearPendingPlacement()
    {
        currentPlacedObject = null;
        _pendingItemPrefab = null;
        _pendingRequiredXPLevel = 0;
        _pendingRewardItemData = null;
        _pendingSourceKind = PlacedItemSource.Unknown;
        _pendingSourceItemId = null;
        _isRelocating = false;
        _relocateUniqueId = null;
        _relocateRemainingDuration = 0f;

        if (placeButton != null) placeButton.gameObject.SetActive(false);
        if (cancelPlacementButton != null) cancelPlacementButton.gameObject.SetActive(false);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  RELOCATE / RETURN
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Picks an already-placed item back up and puts it under the crosshair, so the player can set it
    /// down somewhere else. Its growth timer, identity and source carry over untouched.
    /// </summary>
    /// <returns>False when the item cannot be moved (its prefab is missing, or a placement is already running).</returns>
    public bool BeginRelocate(PlaceableItem item)
    {
        if (item == null) return false;

        if (IsPlacing)
        {
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("Finish placing the current item first");
            }
            return false;
        }

        GameObject prefab = GetPrefabByName(item.prefabName);
        if (prefab == null)
        {
            Debug.LogWarning($"[ItemPlacementManager] Cannot relocate '{item.prefabName}' — its prefab is not reachable from this scene.");
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("This item cannot be moved");
            }
            return false;
        }

        // Remember everything needed to put it back before the object goes away.
        _isRelocating = true;
        _relocateUniqueId = item.uniqueId;
        _relocateRemainingDuration = item.remainingDuration;
        _relocateOriginalPosition = item.transform.position;
        _relocateOriginalRotation = item.transform.rotation;

        _pendingRequiredXPLevel = 0;
        _pendingRewardItemData = null;
        _pendingSourceKind = item.sourceKind;
        _pendingSourceItemId = item.sourceItemId;

        float totalDuration = item.placementDuration;
        PlacedItemSource kind = item.sourceKind;
        string sourceItemId = item.sourceItemId;

        // Take the real item out of the world — the ghost under the crosshair stands in for it until the
        // player puts it down. Nothing is saved yet on purpose: if the app dies mid-move, the last save
        // still has the item where it was, and pause/quit restores it (see OnApplicationPause).
        RemoveFromGarden(item);
        Objectpool.Instance.Despawn(item.gameObject);

        GameObject preview = GetPreviewPrefabFor(kind, sourceItemId, prefab);
        InternalPreparePlacement(prefab, preview, totalDuration);

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ItemInteract);
        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast("Aim where you want it, then tap Move Here");
        }

        return true;
    }

    /// <summary>
    /// Takes an item out of the garden and hands it back to the Asset Store: a treasure box reward
    /// returns to the inventory as one item, a shop purchase refunds
    /// <see cref="storeRefundRate"/> of its Noor Coin price.
    /// </summary>
    /// <returns>False when the item could not be returned.</returns>
    public bool ReturnToStore(PlaceableItem item)
    {
        if (item == null) return false;

        if (IsPlacing)
        {
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast("Finish placing the current item first");
            }
            return false;
        }

        string prefabName = item.prefabName;
        PlacedItemSource kind = item.sourceKind;
        string sourceItemId = item.sourceItemId;

        RemoveFromGarden(item);
        Objectpool.Instance.Despawn(item.gameObject);
        SavePlacedItems();

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ItemInteract);

        // ── Treasure box reward → back on the shelf as one item ───────────────
        TreasureBoxRewardItemData rewardData = kind == PlacedItemSource.ShopItem
            ? null
            : FindInventoryItemData(sourceItemId, prefabName);

        if (rewardData != null)
        {
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.AddInventoryItem(rewardData.itemID, 1);
                rewardData.quantity = InventoryManager.Instance.GetItemQuantity(rewardData.itemID);
            }
            else
            {
                rewardData.quantity++;
            }

            if (shopManager != null) shopManager.UpdateInventoryUI(rewardData);

            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast($"{rewardData.itemName} returned to your inventory");
            }

            return true;
        }

        // ── Shop purchase → Noor Coins back ───────────────────────────────────
        ShopItemData shopData = FindShopItemData(sourceItemId, prefabName);
        int refund = CalculateCoinRefund(shopData);

        if (refund > 0 && NoorCoinManager.Instance != null)
        {
            NoorCoinManager.Instance.Earn(refund);
            return true;
        }

        // Items bought with an ad or with real money have no coin price to give back. They still leave
        // the garden — the player asked for that — but say so rather than pretending they were paid.
        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast("Returned to the store");
        }

        return true;
    }

    /// <summary>What returning <paramref name="shopData"/> pays out, rounded to a whole coin.</summary>
    private int CalculateCoinRefund(ShopItemData shopData)
    {
        if (shopData == null || shopData.noorCoinCost <= 0) return 0;
        return Mathf.RoundToInt(shopData.noorCoinCost * storeRefundRate);
    }

    /// <summary>Drops an item from the tracking list without touching the object itself.</summary>
    private void RemoveFromGarden(PlaceableItem item)
    {
        activePlacedItems.Remove(item);
        item.SetHighlight(false);
        OnItemRemoved?.Invoke(item);
    }

    /// <summary>The lightweight ghost model for an item, falling back to the real prefab.</summary>
    private GameObject GetPreviewPrefabFor(PlacedItemSource kind, string itemId, GameObject fallback)
    {
        if (kind != PlacedItemSource.InventoryItem)
        {
            ShopItemData shopData = FindShopItemData(itemId, fallback != null ? fallback.name : null);
            if (shopData != null && shopData.itemPlacementModelPrefab != null) return shopData.itemPlacementModelPrefab;
        }

        if (kind != PlacedItemSource.ShopItem)
        {
            TreasureBoxRewardItemData rewardData = FindInventoryItemData(itemId, fallback != null ? fallback.name : null);
            if (rewardData != null && rewardData.itemPlacementModelPrefab != null) return rewardData.itemPlacementModelPrefab;
        }

        return fallback;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  LOCAL SAVE / LOAD
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Saves the current list of placed items and the shutdown timestamp to binary save file,
    /// then queues the same state for Firebase.
    /// </summary>
    public void SavePlacedItems()
    {
        SaveStateCollection state = BuildCurrentState();

        SaveSystem.Save(SAVE_KEY, state);
        Debug.Log("Placed items successfully saved to binary using SaveSystem.");

        QueueCloudPush();
    }

    /// <summary>Snapshots the garden as it stands right now.</summary>
    private SaveStateCollection BuildCurrentState()
    {
        // Remove any deleted items from active tracking list
        activePlacedItems.RemoveAll(item => item == null);

        _revision++;
        _lastSavedAtUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        SaveStateCollection state = new SaveStateCollection
        {
            gameClosedTimeUnix = _lastSavedAtUnix,
            revision = _revision
        };

        foreach (var item in activePlacedItems)
        {
            state.items.Add(new PlacedItemSaveData
            {
                uniqueId = item.uniqueId,
                prefabName = item.prefabName,
                position = item.transform.position,
                rotation = item.transform.rotation,
                remainingDuration = item.remainingDuration,
                totalDuration = item.placementDuration,
                sourceItemId = item.sourceItemId,
                sourceKind = item.sourceKind
            });
        }

        return state;
    }

    /// <summary>
    /// Loads placed items from binary save file and offsets remaining times by offline elapsed duration.
    /// </summary>
    private void LoadPlacedItems()
    {
        if (!SaveSystem.Exists(SAVE_KEY)) return;

        SaveStateCollection state = SaveSystem.Load<SaveStateCollection>(SAVE_KEY);
        if (state == null || state.items == null) return;

        _revision = state.revision;
        _lastSavedAtUnix = state.gameClosedTimeUnix;

        RebuildGarden(state);
    }

    /// <summary>
    /// Clears the garden and rebuilds it from <paramref name="state"/>, deducting the time that passed
    /// since the snapshot was taken from every growth timer.
    /// </summary>
    private void RebuildGarden(SaveStateCollection state)
    {
        double currentUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        double elapsedOffline = state.gameClosedTimeUnix > 0
            ? System.Math.Max(0, currentUnix - state.gameClosedTimeUnix)
            : 0;

        // Take down whatever is standing, so a cloud state that removed an item removes it here too.
        foreach (var existing in activePlacedItems)
        {
            if (existing == null) continue;
            existing.SetHighlight(false);
            Objectpool.Instance.Despawn(existing.gameObject);
        }
        activePlacedItems.Clear();

        foreach (var itemData in state.items)
        {
            GameObject prefab = GetPrefabByName(itemData.prefabName);
            if (prefab == null)
            {
                Debug.LogWarning("Failed to find placeable prefab named: " + itemData.prefabName);
                continue;
            }

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
            placeable.SetSource(itemData.sourceKind, itemData.sourceItemId);
            activePlacedItems.Add(placeable);
        }
    }

    /// <summary>
    /// Searches for a placeable prefab matching the provided name.
    /// </summary>
    private GameObject GetPrefabByName(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return null;

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

        if (shopManager == null) return null;

        // 2. Search the shop's catalogue
        if (shopManager.shopItemDatas != null)
        {
            foreach (var data in shopManager.shopItemDatas)
            {
                if (data != null && data.itemPrefab != null && data.itemPrefab.name == prefabName)
                {
                    return data.itemPrefab;
                }
            }
        }

        // 3. …and the treasure box rewards, which are placeable too.
        if (shopManager.inventoryItemDatas != null)
        {
            foreach (var data in shopManager.inventoryItemDatas)
            {
                if (data != null && data.itemPrefab != null && data.itemPrefab.name == prefabName)
                {
                    return data.itemPrefab;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the shop entry an item came from, by id when it has one and by prefab name otherwise —
    /// items placed before source tracking existed only have the name.
    /// </summary>
    private ShopItemData FindShopItemData(string itemId, string prefabName)
    {
        if (shopManager == null || shopManager.shopItemDatas == null) return null;

        if (!string.IsNullOrEmpty(itemId))
        {
            foreach (var data in shopManager.shopItemDatas)
            {
                if (data != null && data.itemID == itemId) return data;
            }
        }

        if (!string.IsNullOrEmpty(prefabName))
        {
            foreach (var data in shopManager.shopItemDatas)
            {
                if (data != null && data.itemPrefab != null && data.itemPrefab.name == prefabName) return data;
            }
        }

        return null;
    }

    /// <summary>The inventory (treasure box reward) equivalent of <see cref="FindShopItemData"/>.</summary>
    private TreasureBoxRewardItemData FindInventoryItemData(string itemId, string prefabName)
    {
        if (shopManager == null || shopManager.inventoryItemDatas == null) return null;

        if (!string.IsNullOrEmpty(itemId))
        {
            foreach (var data in shopManager.inventoryItemDatas)
            {
                if (data != null && data.itemID == itemId) return data;
            }
        }

        if (!string.IsNullOrEmpty(prefabName))
        {
            foreach (var data in shopManager.inventoryItemDatas)
            {
                if (data != null && data.itemPrefab != null && data.itemPrefab.name == prefabName) return data;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CLOUD SAVE (Firebase, via the Flutter bridge)
    //
    //  Unity has no Firebase SDK — Flutter owns the wallet, the profile and now the
    //  garden. The local binary save stays the fast path so the garden appears on the
    //  first frame; the cloud snapshot arrives a moment later and wins if it is newer,
    //  which is what carries the garden across to a new device.
    // ═══════════════════════════════════════════════════════════════════════════

    private void BeginCloudSync()
    {
        if (!syncToCloud) return;

        FlutterBridge.OnGardenStateReceived += HandleCloudGardenState;

        // Flutter may have pushed the garden while a loading scene was up.
        if (FlutterBridge.LatestGardenState != null)
        {
            HandleCloudGardenState(FlutterBridge.LatestGardenState);
        }
        else if (FlutterBridge.Instance != null)
        {
            FlutterBridge.Instance.RequestGardenState();
        }
    }

    /// <summary>
    /// Flutter answered with the garden stored in Firebase. Adopt it when it is newer than what this
    /// device has; otherwise push what we have, so the account ends up holding the latest garden either way.
    /// </summary>
    private void HandleCloudGardenState(GardenStatePayload payload)
    {
        if (!syncToCloud || payload == null) return;

        // hasData=false is the only "nothing stored for this account" signal. An empty items array with
        // hasData=true is a real, deliberately empty garden — the player cleared it on another device —
        // and has to be able to win the comparison below, or clearing a garden could never sync.
        if (!payload.hasData)
        {
            if (activePlacedItems.Count > 0)
            {
                Debug.Log("[ItemPlacementManager] No garden in Firebase yet — seeding it from this device.");
                QueueCloudPush();
            }
            return;
        }

        if (!IsCloudStateNewer(payload))
        {
            Debug.Log("[ItemPlacementManager] Local garden is at least as new as Firebase — pushing it up.");
            QueueCloudPush();
            return;
        }

        // Rebuilding mid-placement would yank the preview out from under the player. Hold it until they
        // have finished putting the current item down.
        if (IsPlacing)
        {
            Debug.Log("[ItemPlacementManager] Cloud garden held back until the current placement finishes.");
            _deferredCloudState = payload;
            return;
        }

        AdoptCloudState(payload);
    }

    private void AdoptCloudState(GardenStatePayload payload)
    {
        SaveStateCollection state = ToSaveState(payload);

        _revision = payload.revision;
        _lastSavedAtUnix = payload.savedAtUnix;

        RebuildGarden(state);

        // Mirror it locally so the next cold start shows the right garden before Flutter answers.
        SaveSystem.Save(SAVE_KEY, state);

        Debug.Log($"[ItemPlacementManager] Garden restored from Firebase — {state.items.Count} item(s).");
    }

    private void ApplyDeferredCloudState()
    {
        if (_deferredCloudState == null || IsPlacing) return;

        GardenStatePayload payload = _deferredCloudState;
        _deferredCloudState = null;

        // The player changed the garden while the cloud copy was waiting, so their edit is now the
        // newest thing there is — keep it and push it instead of overwriting it.
        if (!IsCloudStateNewer(payload))
        {
            QueueCloudPush();
            return;
        }

        AdoptCloudState(payload);
    }

    /// <summary>
    /// Whether the snapshot from Firebase is more recent than what this device holds. Both sides stamp
    /// their own wall clock, so a tie falls back to the revision counter.
    /// </summary>
    private bool IsCloudStateNewer(GardenStatePayload payload)
    {
        if (payload.savedAtUnix > _lastSavedAtUnix) return true;
        if (payload.savedAtUnix < _lastSavedAtUnix) return false;
        return payload.revision > _revision;
    }

    /// <summary>Schedules a push to Firebase, collapsing a burst of edits into one write.</summary>
    private void QueueCloudPush()
    {
        if (!syncToCloud) return;

        _cloudPushPending = true;

        // A disabled manager cannot run coroutines; the pending flag makes sure the push still happens
        // on the next pause/quit flush rather than being lost.
        if (!isActiveAndEnabled) return;

        if (_cloudPushRoutine != null) StopCoroutine(_cloudPushRoutine);
        _cloudPushRoutine = StartCoroutine(PushToCloudAfterDelay());
    }

    private IEnumerator PushToCloudAfterDelay()
    {
        // Realtime: the game sits at timeScale 0 behind panels and during ads, and the garden still
        // needs to reach Firebase.
        yield return new WaitForSecondsRealtime(cloudPushDelay);
        _cloudPushRoutine = null;
        PushToCloud();
    }

    /// <summary>Sends any pending push right now. Called when the game is being paused or closed.</summary>
    private void FlushCloudPush()
    {
        if (_cloudPushRoutine != null)
        {
            StopCoroutine(_cloudPushRoutine);
            _cloudPushRoutine = null;
        }

        if (_cloudPushPending) PushToCloud();
    }

    private void PushToCloud()
    {
        if (!syncToCloud) return;

        if (FlutterBridge.Instance == null)
        {
            Debug.LogWarning("[ItemPlacementManager] No FlutterBridge — the garden was saved locally only.");
            return;
        }

        FlutterBridge.Instance.SaveGardenState(ToPayload());
        _cloudPushPending = false;
    }

    /// <summary>The garden as Flutter expects it: flat, JsonUtility-friendly, ready for Firestore.</summary>
    private GardenStatePayload ToPayload()
    {
        activePlacedItems.RemoveAll(item => item == null);

        var items = new GardenItemPayload[activePlacedItems.Count];
        for (int i = 0; i < activePlacedItems.Count; i++)
        {
            PlaceableItem item = activePlacedItems[i];
            Vector3 position = item.transform.position;
            Quaternion rotation = item.transform.rotation;

            items[i] = new GardenItemPayload
            {
                uniqueId = item.uniqueId,
                prefabName = item.prefabName,
                posX = position.x,
                posY = position.y,
                posZ = position.z,
                rotX = rotation.x,
                rotY = rotation.y,
                rotZ = rotation.z,
                rotW = rotation.w,
                remainingDuration = item.remainingDuration,
                totalDuration = item.placementDuration,
                sourceItemId = item.sourceItemId,
                sourceKind = (int)item.sourceKind
            };
        }

        return new GardenStatePayload
        {
            hasData = true,
            savedAtUnix = (long)_lastSavedAtUnix,
            revision = _revision,
            items = items
        };
    }

    private static SaveStateCollection ToSaveState(GardenStatePayload payload)
    {
        SaveStateCollection state = new SaveStateCollection
        {
            gameClosedTimeUnix = payload.savedAtUnix,
            revision = payload.revision
        };

        if (payload.items == null) return state;

        foreach (var item in payload.items)
        {
            if (item == null) continue;

            state.items.Add(new PlacedItemSaveData
            {
                uniqueId = item.uniqueId,
                prefabName = item.prefabName,
                position = new Vector3(item.posX, item.posY, item.posZ),
                rotation = new Quaternion(item.rotX, item.rotY, item.rotZ, item.rotW),
                remainingDuration = item.remainingDuration,
                totalDuration = item.totalDuration,
                sourceItemId = item.sourceItemId,
                sourceKind = (PlacedItemSource)item.sourceKind
            });
        }

        return state;
    }
}
