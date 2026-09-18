using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
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

    // sourceItemId/sourceKind are the authoritative key used to resolve this item's prefab back to a
    // ShopItemData/TreasureBoxRewardItemData asset on load (see ResolveItemPrefabRef). prefabName is
    // kept for display/debugging only — it is no longer used for lookup.
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

    /// <summary>
    /// Fired when a placement's prefabs start downloading (true) and again when they finish, fail or are
    /// cancelled (false). Shop cards listen so they can lock themselves for the duration: only one
    /// placement can be in flight at a time, so a second tap supersedes the download the player is
    /// already waiting on and strands it (see <see cref="InternalPreparePlacement"/>). Fires only on an
    /// actual change of state — but note a cache hit still fires true then false within the same frame,
    /// because the load resolves synchronously.
    /// </summary>
    public static event System.Action<bool> OnDownloadStateChanged;

    public RectTransform crosshairRect;
    public TerrainCollider terrainCollider;
    public Button placeButton;

    [Header("Placement Radius")]
    [Tooltip("Max horizontal distance from the player the ghost can be positioned. Looking further than " +
             "this clamps the ghost to the radius edge along the same look direction, so an item can't " +
             "be dropped somewhere unreachable (across a lake, through a mountain, etc). Looking inside " +
             "the radius has no effect - the ghost tracks the crosshair/terrain intersection directly.")]
    public float placementRadius = 15f;

    [Header("Grid Placement")]
    [Tooltip("The lattice items snap to. Left empty, one is created on this object at startup and " +
             "anchored to the placement terrain.")]
    public GardenGrid grid;

    [Tooltip("Draws the lattice and the footprint highlight on the ground. Left empty, one is created " +
             "as a child at startup.")]
    public PlacementGridView gridView;

    [Tooltip("Optional. Freely rotates the item being placed - the slider's 0-1 range maps to a full " +
             "0-360 degree turn. Hidden and inert while nothing is being placed.")]
    public Slider rotationSlider;

    [Tooltip("Snap placements to the grid and show the overlay. Turning this off restores the old " +
             "free-form behaviour wholesale — an escape hatch, not a gameplay option.")]
    public bool useGrid = true;

    [Tooltip("Refuse to confirm a placement that overlaps another item, runs off the terrain or sits " +
             "on ground too steep for it. Turned off, the highlight still turns red but the player " +
             "can place anyway.")]
    public bool blockInvalidPlacements = true;

    [Tooltip("Optional. Abandons the placement in progress. For a relocation the item goes back where " +
             "it was; for a fresh purchase the item is handed back the same way a return would.")]
    public Button cancelPlacementButton;

    [Header("Shop/Prefab References")]
    public InGameShopManager shopManager;

    [Header("Memory")]
    [Tooltip("How many distinct downloaded item prefabs AddressableItemLoader keeps cached beyond " +
             "what the garden/current placement actually needs. Item meshes run tens of MB each, and " +
             "this process also carries the Flutter engine embedding it — a low cap trades a brief " +
             "reload toast for a bounded memory footprint instead of an OS-level OOM kill after a few " +
             "minutes of browsing the shop. Items already placed in the garden are never evicted.")]
    [Range(0, 20)]
    public int maxCachedUnusedItemPrefabs = 6;

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

    // ── Async prefab loading (Addressables) ──────────────────────────────────────
    // True while a placement's real/preview prefabs are downloading, i.e. between InternalPreparePlacement
    // starting and FinishPreparingPlacement/HandlePlacementLoadFailure ending it. currentPlacedObject alone
    // can no longer stand in for "a placement is in progress" — the ghost doesn't exist yet during the load.
    private bool _isPreparingPlacement;
    // Bumped every time a new placement starts or the pending one is cleared, so a load callback from a
    // superseded request (the player picked a different item before the first one finished downloading)
    // can recognize it's stale and no-op instead of resurrecting an abandoned ghost.
    private int _placementRequestVersion;
    // True while RebuildGardenAsync is reconstructing the garden from a save (cold start or a cloud
    // adopt) — guards SaveEverything from persisting a partially-populated garden mid-rebuild.
    private bool _isRebuildingGarden;

    // Saved items whose Addressable bundle failed to download during the last RebuildGardenAsync (no
    // network / not cached yet on a new device). Kept here and re-merged into every snapshot by
    // BuildCurrentState/ToPayload so they aren't dropped from the save for good — the next successful
    // load gets another chance to resolve them. Items whose reference is genuinely invalid (the source
    // ShopItemData/TreasureBoxRewardItemData asset no longer exists) are not kept here and stay dropped.
    private readonly List<PlacedItemSaveData> _unresolvedItems = new List<PlacedItemSaveData>();

    // Caller-supplied hooks for the placement request currently in flight (typically the shop), so it
    // can find out when to stop waiting: onReady once the ghost is actually up, onFailed if the download
    // failed or this request got superseded by a newer one before it could finish. See InternalPreparePlacement.
    private System.Action _pendingPlacementReadyCallback;
    private System.Action _pendingPlacementFailedCallback;

    // Live download progress reporting for the request in flight — polled once a frame in Update() rather
    // than pushed, since AddressableItemLoader/Addressables only expose PercentComplete as a snapshot, not
    // an event. The two refs are whatever InternalPreparePlacement is currently downloading; progress is
    // the average of both since the real and preview prefabs download concurrently.
    private System.Action<float> _pendingPlacementProgressCallback;
    private AssetReferenceGameObject _pendingProgressPrefabRef;
    private AssetReferenceGameObject _pendingProgressPreviewRef;
    private float _lastReportedPlacementProgress = -1f;

    // ── Grid placement state ──────────────────────────────────────────────────
    // The footprint is measured from the *real* prefab, never the ghost: the ghost is spawned at 0.2x
    // and items keep growing for as long as their timer runs, so a footprint taken from what is on
    // screen would have a sapling claim a fifth of the ground the grown tree needs.
    private Vector2Int _pendingFootprint = Vector2Int.one;
    private Vector2Int _pendingFootprintOverride;

    // Continuous yaw offset applied on top of _ghostBaseRotation, in degrees. The slider's centre (0.5)
    // is 0 degrees - the model's authored facing - so dragging left/right turns it either way from
    // there; see OnRotationSliderChanged for the (sliderValue - 0.5) * 360 mapping. Grid occupancy still
    // needs an axis-aligned rectangle, so footprint claims are derived by rounding this to the nearest
    // quarter-turn - see RefreshPendingFootprint.
    private float _pendingRotationDegrees;

    // The preview prefab's authored rotation. Placement yaw is applied on top of it rather than
    // replacing it, because several models are authored pre-rotated.
    private Quaternion _ghostBaseRotation = Quaternion.identity;

    private Vector2Int _snapAnchor;
    private bool _hasSnapAnchor;
    private RectInt _pendingArea;
    private PlacementValidity _pendingValidity = PlacementValidity.Valid;

    // ── Relocation state ──────────────────────────────────────────────────────
    // A relocation is a placement that reuses an existing item's identity and growth progress instead
    // of minting a new one, and that must not charge the player or award XP a second time.
    private bool _isRelocating;
    private string _relocateUniqueId;
    private float _relocateRemainingDuration;
    private Vector3 _relocateOriginalPosition;
    private Quaternion _relocateOriginalRotation;
    private RectInt _relocateOriginalArea;
    private bool _relocateHadArea;

    private List<PlaceableItem> activePlacedItems = new List<PlaceableItem>();
    private const string SAVE_KEY = "PlacedItemsData";

    // ── Cloud state ───────────────────────────────────────────────────────────
    private int _revision;
    private double _lastSavedAtUnix;
    private Coroutine _cloudPushRoutine;
    private bool _cloudPushPending;
    private GardenStatePayload _deferredCloudState;

    /// <summary>
    /// True while an item is following the crosshair and waiting to be confirmed, or while its prefab is
    /// still downloading (before the ghost even exists).
    /// </summary>
    public bool IsPlacing => currentPlacedObject != null || _isPreparingPlacement;

    /// <summary>
    /// True only for the download half of <see cref="IsPlacing"/> — a placement's prefabs are on the wire
    /// and the ghost does not exist yet. Lets a card that spawns or re-enables mid-download catch up on a
    /// state change it wasn't around to hear; see <see cref="OnDownloadStateChanged"/>.
    /// </summary>
    public bool IsDownloadingItem => _isPreparingPlacement;

    /// <summary>True when the placement in progress is moving an item that is already in the garden.</summary>
    public bool IsRelocating => _isRelocating;

    /// <summary>True once the grid is anchored and snapping is switched on.</summary>
    private bool UseGrid => useGrid && grid != null && grid.IsReady;

    /// <summary>Everything currently wrong with where the ghost is standing.</summary>
    public PlacementValidity CurrentValidity => _pendingValidity;

    /// <summary>
    /// Whether the Place button should do anything. Distinct from <see cref="CurrentValidity"/>: the
    /// highlight always shows the truth, but only <see cref="blockInvalidPlacements"/> makes it binding.
    /// </summary>
    public bool CanConfirmPlacement =>
        !UseGrid || !blockInvalidPlacements || _pendingValidity == PlacementValidity.Valid;

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

        // Before LoadPlacedItems: the rebuild claims cells as it goes, so the lattice has to exist first.
        SetupGrid();

        // Load previously placed items on startup
        LoadPlacedItems();

        BeginCloudSync();

        Application.lowMemory += HandleLowMemory;
    }

    private void OnDestroy()
    {
        FlutterBridge.OnGardenStateReceived -= HandleCloudGardenState;
        Application.lowMemory -= HandleLowMemory;

        if (placeButton != null) placeButton.onClick.RemoveListener(HandlePlaceButtonClick);
        if (cancelPlacementButton != null) cancelPlacementButton.onClick.RemoveListener(CancelPlacement);
        if (rotationSlider != null) rotationSlider.onValueChanged.RemoveListener(OnRotationSliderChanged);

        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// The OS is warning this process is close to being killed for memory — iOS's
    /// <c>didReceiveMemoryWarning</c> and Android's <c>onTrimMemory</c> both surface here as the same
    /// event. Neither platform gives a size budget with the warning, so there's nothing to tune: drop
    /// every downloaded item prefab this process isn't actively using, right now, rather than waiting for
    /// the next placement/return to trigger the normal bounded trim (see <see cref="TrimAddressableCache"/>).
    /// iOS in particular has no "ask for more headroom" escape hatch the way Android's largeHeap manifest
    /// flag does, so this is the only lever available there once the OS is already unhappy.
    /// </summary>
    private void HandleLowMemory()
    {
        Debug.LogWarning("[ItemPlacementManager] OS low-memory warning — releasing every unused item prefab.");
        AddressableItemLoader.TrimCache(0, ComputeInUseAddressableKeys());
        ItemFootprint.ClearCache();
        Resources.UnloadUnusedAssets();
    }

    private void Update()
    {
        if (currentPlacedObject != null)
        {
            UpdatePlacementPosition();

            // Keeps the overlay centred as the player walks. Cheap: it only re-samples terrain heights
            // once the player has actually moved a metre.
            if (gridView != null && UseGrid) gridView.SetCenter(transform.position, placementRadius);
        }

        if (_isPreparingPlacement && _pendingPlacementProgressCallback != null)
        {
            float progress = (AddressableItemLoader.GetProgress(_pendingProgressPrefabRef)
                + AddressableItemLoader.GetProgress(_pendingProgressPreviewRef)) * 0.5f;

            // PercentComplete jitters slightly frame to frame; only push an update when it actually moves,
            // so the UI isn't rebuilding its label text every single frame for no visible change.
            if (!Mathf.Approximately(progress, _lastReportedPlacementProgress))
            {
                _lastReportedPlacementProgress = progress;
                _pendingPlacementProgressCallback.Invoke(progress);
            }
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
        // The garden is mid-reconstruction (cold start / cloud adopt) — activePlacedItems is only
        // partially populated right now. Writing it out would overwrite the save file (and the cloud
        // mirror) with an incomplete garden; the on-disk/cloud copy this is rebuilding from is already
        // the correct source of truth, so there is nothing to gain by saving mid-rebuild.
        if (_isRebuildingGarden) return;

        if (_isRelocating) CancelPlacement();

        SavePlacedItems();
        FlushCloudPush();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PLACEMENT
    // ═══════════════════════════════════════════════════════════════════════════

    /// <param name="onReady">Called once the ghost preview is actually up — a cache hit resolves this
    /// synchronously, before <see cref="PreparePlacement(ShopItemData, System.Action, System.Action, System.Action{float})"/> even returns.</param>
    /// <param name="onFailed">Called if the download fails, or if this request is superseded by a newer
    /// one before it can finish (never both — exactly one of onReady/onFailed fires per call).</param>
    /// <param name="onProgress">Called roughly once a frame with a 0–1 estimate while downloading. Never
    /// called at all for a cache hit, since there is nothing to wait on.</param>
    public void PreparePlacement(ShopItemData itemData, System.Action onReady = null, System.Action onFailed = null,
        System.Action<float> onProgress = null)
    {
        if (itemData == null || itemData.itemPrefabRef == null || !itemData.itemPrefabRef.RuntimeKeyIsValid())
        {
            onFailed?.Invoke();
            return;
        }

        // Buying something in the middle of a move would overwrite the relocation state and lose the
        // item being carried. Put it back first.
        if (_isRelocating) CancelPlacement();

        _pendingRequiredXPLevel = itemData.requiredXPLevel;
        _pendingRewardItemData = null;
        _pendingSourceKind = PlacedItemSource.ShopItem;
        _pendingSourceItemId = itemData.itemID;

        InternalPreparePlacement(itemData.itemPrefabRef, itemData.itemPlacementModelPrefabRef, itemData.placementTimerDuration, onReady, onFailed, onProgress);
    }

    /// <param name="onReady">Called once the ghost preview is actually up — a cache hit resolves this
    /// synchronously, before <see cref="PreparePlacement(TreasureBoxRewardItemData, System.Action, System.Action, System.Action{float})"/> even returns.</param>
    /// <param name="onFailed">Called if the download fails, or if this request is superseded by a newer
    /// one before it can finish (never both — exactly one of onReady/onFailed fires per call).</param>
    /// <param name="onProgress">Called roughly once a frame with a 0–1 estimate while downloading. Never
    /// called at all for a cache hit, since there is nothing to wait on.</param>
    public void PreparePlacement(TreasureBoxRewardItemData itemData, System.Action onReady = null, System.Action onFailed = null,
        System.Action<float> onProgress = null)
    {
        if (itemData == null || itemData.itemPrefabRef == null || !itemData.itemPrefabRef.RuntimeKeyIsValid())
        {
            onFailed?.Invoke();
            return;
        }

        // As above: never let a new placement swallow the item currently being moved.
        if (_isRelocating) CancelPlacement();

        _pendingRequiredXPLevel = 0; // No XP reward for treasure box items
        _pendingRewardItemData = itemData;
        _pendingSourceKind = PlacedItemSource.InventoryItem;
        _pendingSourceItemId = itemData.itemID;

        InternalPreparePlacement(itemData.itemPrefabRef, itemData.itemPlacementModelPrefabRef, itemData.placementTimerDuration, onReady, onFailed, onProgress);
    }

    /// <summary>
    /// The single write point for <see cref="_isPreparingPlacement"/>, so <see cref="OnDownloadStateChanged"/>
    /// cannot drift out of sync with it — every path that starts, finishes, fails or cancels a load goes
    /// through here. Idempotent, so the paths that unwind through more than one of those (a failure that
    /// then clears the pending placement) still announce the state change exactly once.
    /// </summary>
    private void SetPreparingPlacement(bool preparing)
    {
        if (_isPreparingPlacement == preparing) return;

        _isPreparingPlacement = preparing;
        OnDownloadStateChanged?.Invoke(preparing);
    }

    /// <summary>
    /// Starts (or restarts) a placement: downloads the real and preview prefabs — a no-op if both are
    /// already cached from a previous download this session — then hands off to
    /// <see cref="FinishPreparingPlacement"/> once both resolve, or <see cref="HandlePlacementLoadFailure"/>
    /// if either fails. <paramref name="previewRef"/> may be an unassigned reference; it falls back to
    /// <paramref name="prefabRef"/>.
    /// </summary>
    private void InternalPreparePlacement(AssetReferenceGameObject prefabRef, AssetReferenceGameObject previewRef, float duration,
        System.Action onReady = null, System.Action onFailed = null, System.Action<float> onProgress = null)
    {
        // If there's an existing preview being placed, destroy it
        if (currentPlacedObject != null)
        {
            Objectpool.Instance.Despawn(currentPlacedObject);
            currentPlacedObject = null;
        }

        // This request supersedes whatever was in flight (e.g. BeginRelocate interrupting a shop
        // download). That old request's loaded-callbacks will now see a stale requestVersion and quietly
        // no-op instead of calling FinishPreparingPlacement/HandlePlacementLoadFailure — so its caller
        // would otherwise be stuck waiting forever. Tell it to give up.
        System.Action supersededFailed = _pendingPlacementFailedCallback;
        _pendingPlacementReadyCallback = onReady;
        _pendingPlacementFailedCallback = onFailed;
        _pendingPlacementProgressCallback = onProgress;
        _pendingProgressPrefabRef = prefabRef;
        _lastReportedPlacementProgress = -1f;
        supersededFailed?.Invoke();

        _pendingItemPrefab = null;
        _pendingDuration = duration;
        SetPreparingPlacement(true);
        int requestVersion = ++_placementRequestVersion;

        AssetReferenceGameObject effectivePreviewRef =
            previewRef != null && previewRef.RuntimeKeyIsValid() ? previewRef : prefabRef;
        _pendingProgressPreviewRef = effectivePreviewRef;

        GameObject resolvedReal = null;
        GameObject resolvedPreview = null;
        bool realReady = false;
        bool previewReady = false;

        StartCoroutine(ShowSlowLoadToastAfterDelay(requestVersion));

        void TryFinish()
        {
            if (requestVersion != _placementRequestVersion) return; // superseded by a newer request
            if (!realReady || !previewReady) return;

            if (resolvedReal == null || resolvedPreview == null)
            {
                HandlePlacementLoadFailure();
                return;
            }

            FinishPreparingPlacement(resolvedReal, resolvedPreview, duration);
        }

        AddressableItemLoader.LoadAsync(prefabRef, loaded =>
        {
            if (requestVersion != _placementRequestVersion) return;
            resolvedReal = loaded;
            realReady = true;
            TryFinish();
        });

        AddressableItemLoader.LoadAsync(effectivePreviewRef, loaded =>
        {
            if (requestVersion != _placementRequestVersion) return;
            resolvedPreview = loaded;
            previewReady = true;
            TryFinish();
        });
    }

    /// <summary>Shows a "downloading" toast if a placement is still loading after a short delay — never
    /// fires for a cache hit, since those resolve synchronously before this coroutine's first yield.</summary>
    private IEnumerator ShowSlowLoadToastAfterDelay(int requestVersion)
    {
        yield return new WaitForSecondsRealtime(0.3f);

        if (requestVersion == _placementRequestVersion && _isPreparingPlacement && ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("placement.downloading_item"));
        }
    }

    /// <summary>The tail of the old synchronous InternalPreparePlacement — spawns the ghost once both
    /// the real and preview prefabs have actually been resolved.</summary>
    private void FinishPreparingPlacement(GameObject prefab, GameObject previewPrefab, float duration)
    {
        SetPreparingPlacement(false);
        _pendingItemPrefab = prefab;
        _pendingDuration = duration;

        currentPlacedObject = Objectpool.Instance.Spawn(previewPrefab);

        // Placement yaw sits on top of whatever rotation the model was authored with, so the authored
        // one has to be captured before any yaw is applied.
        _ghostBaseRotation = currentPlacedObject.transform.rotation;
        _pendingFootprintOverride = ResolveFootprintOverride(_pendingSourceKind, _pendingSourceItemId);

        // A relocated item keeps the facing it already had; a fresh one starts unrotated.
        _pendingRotationDegrees = _isRelocating
            ? ItemFootprint.YawDegreesFromRotation(_relocateOriginalRotation, _ghostBaseRotation)
            : 0f;

        RefreshPendingFootprint();

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

        if (UseGrid && gridView != null) gridView.Show(transform.position, placementRadius);

        if (rotationSlider != null)
        {
            rotationSlider.gameObject.SetActive(true);
            rotationSlider.SetValueWithoutNotify(0.5f + _pendingRotationDegrees / 360f);
        }

        UpdatePlacementPosition();

        // Activate the placement button
        if (placeButton != null)
        {
            placeButton.gameObject.SetActive(true);
            var btnText = placeButton.GetComponentInChildren<TMPro.TMP_Text>();
            if (btnText != null) btnText.text = LocalizationManager.Instance.Get(_isRelocating ? "placement.button_move_here" : "placement.button_place");
        }

        if (cancelPlacementButton != null)
        {
            cancelPlacementButton.gameObject.SetActive(true);
        }

        System.Action readyCallback = _pendingPlacementReadyCallback;
        _pendingPlacementReadyCallback = null;
        _pendingPlacementFailedCallback = null;
        _pendingPlacementProgressCallback = null;
        readyCallback?.Invoke();
    }

    /// <summary>
    /// The real or preview prefab failed to download. Unwinds exactly like a cancelled placement — refund
    /// a fresh purchase, or (in the unreachable-in-v1 case a relocate's already-cached prefab somehow
    /// fails to reload) log it, since the live instance is already gone by this point.
    /// </summary>
    private void HandlePlacementLoadFailure()
    {
        SetPreparingPlacement(false);

        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("placement.download_failed"));
        }

        if (_isRelocating)
        {
            Debug.LogError("[ItemPlacementManager] Relocate failed to reload an already-cached prefab — item lost.");
        }
        else
        {
            RefundPendingPurchase();
        }

        ClearPendingPlacement();
        ApplyDeferredCloudState();

        System.Action failedCallback = _pendingPlacementFailedCallback;
        _pendingPlacementReadyCallback = null;
        _pendingPlacementFailedCallback = null;
        _pendingPlacementProgressCallback = null;
        failedCallback?.Invoke();
    }

    /// <summary>
    /// Handles the placeButton click.
    /// Immediately confirms and finalizes placement.
    /// </summary>
    public void HandlePlaceButtonClick()
    {
        if (currentPlacedObject == null) return;

        if (!CanConfirmPlacement)
        {
            ShowBlockedToast();
            return;
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ItemPlace);

        // Finalize placement immediately
        PlaceItem();
    }

    /// <summary>
    /// Projects a ray from the camera through the crosshair onto the terrain collider, snaps the
    /// result to the grid and updates the preview object, the footprint highlight and the Place
    /// button's enabled state.
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
        if (!terrainCollider.Raycast(ray, out hit, 1000f)) return;

        currentPlacedObject.transform.rotation = YawFor(_pendingRotationDegrees) * _ghostBaseRotation;

        if (!UseGrid)
        {
            currentPlacedObject.transform.position = ClampToPlacementRadius(hit.point, placementRadius);
            return;
        }

        // An item has to fit inside the reachable bubble whole, not just have its pivot inside it, so
        // the clamp works against a radius shrunk by the block's half-diagonal. Doing it here rather
        // than only in the validity check means looking into the distance slides the ghost to the
        // furthest spot it can legally stand, instead of stranding it somewhere permanently red.
        float halfDiagonal = new Vector2(_pendingFootprint.x, _pendingFootprint.y).magnitude * grid.CellSize * 0.5f;
        Vector3 target = ClampToPlacementRadius(hit.point, Mathf.Max(0f, placementRadius - halfDiagonal));

        Vector3 snapped = grid.Snap(target, _pendingFootprint, ref _snapAnchor, ref _hasSnapAnchor);

        // Ground height has to come from the snapped spot, not the raw hit: snapping moves the point
        // by up to a cell diagonally, and on any slope that is the difference between an item standing
        // on the ground and one hovering above or buried in it.
        snapped.y = grid.SampleHeight(snapped);

        _pendingArea = new RectInt(_snapAnchor, _pendingFootprint);

        // Occupancy is always evaluated, even when blockInvalidPlacements is off, so the highlight
        // tells the truth about the spot regardless of whether the rule is being enforced.
        _pendingValidity = grid.Evaluate(_pendingArea, transform.position, placementRadius, checkOccupancy: true);

        currentPlacedObject.transform.position = snapped;

        if (gridView != null) gridView.SetFootprint(_pendingArea, _pendingValidity == PlacementValidity.Valid);
        if (placeButton != null) placeButton.interactable = CanConfirmPlacement;
    }

    /// <summary>The yaw applied on top of the model's authored rotation, in degrees.</summary>
    private static Quaternion YawFor(float degrees) => Quaternion.Euler(0f, degrees, 0f);

    /// <summary>
    /// Live callback for rotationSlider - centred at 0.5 (the authored facing), dragging out to either
    /// end turns the item a full 180 degrees that way. Rectangular footprints only swap axes at
    /// quarter-turns, so the snap anchor is dropped and re-derived on the next frame whenever that
    /// snapped footprint changes - keeping the old anchor would swing the block away from the crosshair
    /// instead of turning it in place.
    /// </summary>
    public void OnRotationSliderChanged(float sliderValue)
    {
        if (currentPlacedObject == null) return;

        _pendingRotationDegrees = (sliderValue - 0.5f) * 360f;
        RefreshPendingFootprint();
        UpdatePlacementPosition();
    }

    /// <summary>Re-measures the cells the pending item claims at its current rotation. Grid occupancy
    /// is axis-aligned, so the free rotation is rounded to the nearest quarter-turn for this purpose.</summary>
    private void RefreshPendingFootprint()
    {
        if (grid == null || _pendingItemPrefab == null) return;

        int footprintSteps = (((Mathf.RoundToInt(_pendingRotationDegrees / 90f)) % 4) + 4) % 4;
        _pendingFootprint = ItemFootprint.Compute(
            _pendingItemPrefab, footprintSteps, grid.CellSize, _pendingFootprintOverride);

        _hasSnapAnchor = false;
    }

    /// <summary>The designer-authored footprint override for an item, if its source asset carries one.</summary>
    private Vector2Int ResolveFootprintOverride(PlacedItemSource kind, string itemId)
    {
        if (kind == PlacedItemSource.InventoryItem)
        {
            TreasureBoxRewardItemData reward = FindInventoryItemData(itemId);
            return reward != null ? reward.gridFootprintOverride : Vector2Int.zero;
        }

        ShopItemData shopData = FindShopItemData(itemId);
        return shopData != null ? shopData.gridFootprintOverride : Vector2Int.zero;
    }

    /// <summary>Explains the first real reason the current spot was refused.</summary>
    private void ShowBlockedToast()
    {
        if (ToastMessageManager.Instance == null || LocalizationManager.Instance == null) return;

        string key =
            (_pendingValidity & PlacementValidity.Occupied) != 0 ? "placement.blocked_occupied" :
            (_pendingValidity & PlacementValidity.TooSteep) != 0 ? "placement.blocked_steep" :
            (_pendingValidity & PlacementValidity.OutOfRadius) != 0 ? "placement.blocked_too_far" :
            "placement.blocked_off_terrain";

        ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get(key));
    }

    /// <summary>
    /// Creates the lattice and its overlay if the scene does not already provide them, and anchors the
    /// lattice to the placement terrain.
    ///
    /// The anchor is deliberately the Terrain's own transform rather than anything computed at runtime:
    /// placements travel to the player's other devices through Firebase as raw world positions, and a
    /// lattice that could land differently per device would let a garden that was flush on a phone come
    /// back a cell out on a tablet.
    /// </summary>
    private void SetupGrid()
    {
        Terrain resolved = terrainCollider != null ? terrainCollider.GetComponent<Terrain>() : null;
        if (resolved == null) resolved = Terrain.activeTerrain;

        if (grid == null)
        {
            grid = GardenGrid.Instance != null ? GardenGrid.Instance : gameObject.AddComponent<GardenGrid>();
        }

        grid.Bind(resolved);

        if (!grid.IsReady)
        {
            Debug.LogWarning("[ItemPlacementManager] No terrain to anchor the placement grid to - "
                + "falling back to free-form placement.");
        }

        if (gridView == null)
        {
            var viewObject = new GameObject("PlacementGridView");
            viewObject.transform.SetParent(transform, false);
            gridView = viewObject.AddComponent<PlacementGridView>();
        }

        gridView.Bind(grid);
        gridView.Hide();

        if (rotationSlider != null)
        {
            rotationSlider.onValueChanged.AddListener(OnRotationSliderChanged);
            rotationSlider.gameObject.SetActive(false);
        }
    }

    /// <summary>Claims the cells a newly confirmed item stands on.</summary>
    private void RegisterGridArea(PlaceableItem placeable, Vector3 worldPosition)
    {
        if (grid == null || !grid.IsReady || placeable == null) return;

        RectInt area = UseGrid ? _pendingArea : grid.AreaCovering(worldPosition, _pendingFootprint);
        placeable.SetGridArea(area);
        grid.Occupy(area, placeable.uniqueId);
    }

    /// <summary>
    /// Keeps the ghost within <paramref name="radius"/> of the player. A hit point inside the radius
    /// is used as-is (the ghost tracks the crosshair/terrain intersection directly); a hit point beyond
    /// it is pulled back along the same look direction to the radius edge, then re-sampled against the
    /// terrain so it still sits on the ground at that clamped spot rather than at the original (likely
    /// very different) hit height.
    /// </summary>
    private Vector3 ClampToPlacementRadius(Vector3 hitPoint, float radius)
    {
        Vector3 playerPos = transform.position;
        Vector3 flatOffset = hitPoint - playerPos;
        flatOffset.y = 0f;

        float flatDistance = flatOffset.magnitude;
        if (flatDistance <= radius || flatDistance < 0.0001f)
        {
            return hitPoint;
        }

        Vector3 direction = flatOffset / flatDistance;
        Vector3 clampedXZ = playerPos + direction * radius;

        Terrain terrain = terrainCollider != null ? terrainCollider.GetComponent<Terrain>() : null;
        if (terrain != null)
        {
            float terrainY = terrain.SampleHeight(clampedXZ) + terrain.transform.position.y;
            return new Vector3(clampedXZ.x, terrainY, clampedXZ.z);
        }

        // No Terrain component to resample height from - fall back to the original hit height rather
        // than leaving the ghost floating or buried at the wrong elevation.
        return new Vector3(clampedXZ.x, hitPoint.y, clampedXZ.z);
    }

    /// <summary>
    /// Confirms placement of the current item, sets up placement time tracking, and saves the game state.
    /// </summary>
    public void PlaceItem()
    {
        if (currentPlacedObject == null) return;

        // Guarded here as well as on the button: relocation and tutorial flows can drive placement
        // without going through the HUD.
        if (!CanConfirmPlacement)
        {
            ShowBlockedToast();
            return;
        }

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
        RegisterGridArea(placeable, confirmedPosition);
        SavePlacedItems();

        OnItemPlaced?.Invoke(placeable);

        if (_isRelocating)
        {
            // The player already paid for this item the first time round: no XP, no stock consumed.
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("placement.moved"));
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
        TrimAddressableCache();
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
        TrimAddressableCache();
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
        RestoreRelocatedGridArea(placeable);
        SavePlacedItems();

        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("placement.move_cancelled"));
        }
    }

    /// <summary>
    /// Re-claims the cells a relocated item held before it was picked up. Items placed before the grid
    /// existed have no recorded area, so theirs is derived from where they stood.
    /// </summary>
    private void RestoreRelocatedGridArea(PlaceableItem placeable)
    {
        if (grid == null || !grid.IsReady || placeable == null) return;

        RectInt area = _relocateHadArea
            ? _relocateOriginalArea
            : grid.AreaCovering(_relocateOriginalPosition, _pendingFootprint);

        placeable.SetGridArea(area);
        grid.Occupy(area, placeable.uniqueId);
    }

    /// <summary>Gives back whatever a cancelled first-time placement was bought with.</summary>
    private void RefundPendingPurchase()
    {
        if (_pendingSourceKind == PlacedItemSource.InventoryItem && _pendingRewardItemData != null)
        {
            // Stock was not consumed yet — PlaceItem does that — so there is nothing to give back.
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("placement.placement_cancelled"));
            }
            return;
        }

        ShopItemData shopData = FindShopItemData(_pendingSourceItemId);
        int refund = CalculateCoinRefund(shopData);

        if (refund > 0 && NoorCoinManager.Instance != null)
        {
            NoorCoinManager.Instance.Earn(refund);
        }
        else if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("placement.placement_cancelled"));
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
        _relocateHadArea = false;

        _pendingRotationDegrees = 0f;
        _pendingFootprint = Vector2Int.one;
        _pendingFootprintOverride = Vector2Int.zero;
        _pendingValidity = PlacementValidity.Valid;
        _hasSnapAnchor = false;
        _ghostBaseRotation = Quaternion.identity;

        SetPreparingPlacement(false);
        _placementRequestVersion++; // invalidate any in-flight load callbacks tied to the placement just cleared

        if (placeButton != null)
        {
            placeButton.gameObject.SetActive(false);
            placeButton.interactable = true;
        }

        if (cancelPlacementButton != null) cancelPlacementButton.gameObject.SetActive(false);
        if (rotationSlider != null)
        {
            rotationSlider.gameObject.SetActive(false);
            rotationSlider.SetValueWithoutNotify(0.5f);
        }
        if (gridView != null) gridView.Hide();
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
                ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("shop.finish_placing_first"));
            }
            return false;
        }

        AssetReferenceGameObject prefabRef = ResolveItemPrefabRef(item.sourceKind, item.sourceItemId);

        // The prefab standing in as `item` right now was already downloaded to spawn it, so this is
        // always a cache hit in v1 (nothing evicts AddressableItemLoader's cache) — checked rather than
        // assumed so a hypothetical miss fails safely, leaving the live item exactly where it is instead
        // of despawning it ahead of a load that might not succeed.
        if (prefabRef == null || !prefabRef.RuntimeKeyIsValid() || !AddressableItemLoader.TryGetCached(prefabRef, out _))
        {
            Debug.LogWarning($"[ItemPlacementManager] Cannot relocate item (sourceItemId='{item.sourceItemId}') — its prefab is not reachable.");
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("placement.cannot_move"));
            }
            return false;
        }

        // Remember everything needed to put it back before the object goes away.
        _isRelocating = true;
        _relocateUniqueId = item.uniqueId;
        _relocateRemainingDuration = item.remainingDuration;
        _relocateOriginalPosition = item.transform.position;
        _relocateOriginalRotation = item.transform.rotation;
        _relocateHadArea = item.hasGridArea;
        _relocateOriginalArea = item.gridArea;

        _pendingRequiredXPLevel = 0;
        _pendingRewardItemData = null;
        _pendingSourceKind = item.sourceKind;
        _pendingSourceItemId = item.sourceItemId;

        float totalDuration = item.placementDuration;
        AssetReferenceGameObject previewRef = ResolvePreviewPrefabRef(item.sourceKind, item.sourceItemId);

        // Take the real item out of the world — the ghost under the crosshair stands in for it until the
        // player puts it down. Nothing is saved yet on purpose: if the app dies mid-move, the last save
        // still has the item where it was, and pause/quit restores it (see OnApplicationPause).
        RemoveFromGarden(item);
        Objectpool.Instance.Despawn(item.gameObject);

        InternalPreparePlacement(prefabRef, previewRef, totalDuration);

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ItemInteract);
        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("placement.aim_move_here"));
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
                ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("shop.finish_placing_first"));
            }
            return false;
        }

        PlacedItemSource kind = item.sourceKind;
        string sourceItemId = item.sourceItemId;

        RemoveFromGarden(item);
        Objectpool.Instance.Despawn(item.gameObject);
        SavePlacedItems();
        TrimAddressableCache();

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ItemInteract);

        // ── Treasure box reward → back on the shelf as one item ───────────────
        TreasureBoxRewardItemData rewardData = kind == PlacedItemSource.ShopItem
            ? null
            : FindInventoryItemData(sourceItemId);

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
                ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("placement.returned_to_inventory", rewardData.LocalizedName));
            }

            return true;
        }

        // ── Shop purchase → Noor Coins back ───────────────────────────────────
        ShopItemData shopData = FindShopItemData(sourceItemId);
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
            ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("placement.returned_to_store"));
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
        if (grid != null) grid.Release(item.uniqueId);
        item.SetHighlight(false);
        OnItemRemoved?.Invoke(item);
    }

    /// <summary>The real item prefab an already-placed item came from, resolved by source id — used to
    /// re-download it for a relocate or a garden rebuild.</summary>
    private AssetReferenceGameObject ResolveItemPrefabRef(PlacedItemSource kind, string itemId)
    {
        if (kind != PlacedItemSource.InventoryItem)
        {
            ShopItemData shopData = FindShopItemData(itemId);
            if (shopData != null && shopData.itemPrefabRef != null && shopData.itemPrefabRef.RuntimeKeyIsValid())
            {
                return shopData.itemPrefabRef;
            }
        }

        if (kind != PlacedItemSource.ShopItem)
        {
            TreasureBoxRewardItemData rewardData = FindInventoryItemData(itemId);
            if (rewardData != null && rewardData.itemPrefabRef != null && rewardData.itemPrefabRef.RuntimeKeyIsValid())
            {
                return rewardData.itemPrefabRef;
            }
        }

        return null;
    }

    /// <summary>The lightweight ghost model for an item. May return null/invalid — InternalPreparePlacement
    /// falls back to the real prefab ref in that case.</summary>
    private AssetReferenceGameObject ResolvePreviewPrefabRef(PlacedItemSource kind, string itemId)
    {
        if (kind != PlacedItemSource.InventoryItem)
        {
            ShopItemData shopData = FindShopItemData(itemId);
            if (shopData != null) return shopData.itemPlacementModelPrefabRef;
        }

        if (kind != PlacedItemSource.ShopItem)
        {
            TreasureBoxRewardItemData rewardData = FindInventoryItemData(itemId);
            if (rewardData != null) return rewardData.itemPlacementModelPrefabRef;
        }

        return null;
    }

    /// <summary>
    /// Releases whatever <see cref="AddressableItemLoader"/> is holding for items the garden no longer
    /// needs, down to <see cref="maxCachedUnusedItemPrefabs"/> beyond what is actually in use. Call this
    /// after any change that could leave a previously-downloaded prefab unused: a placement finalized,
    /// cancelled or returned, or the garden freshly rebuilt.
    /// </summary>
    private void TrimAddressableCache()
    {
        AddressableItemLoader.TrimCache(maxCachedUnusedItemPrefabs, ComputeInUseAddressableKeys());
    }

    /// <summary>The AssetGUIDs <see cref="AddressableItemLoader"/> must not evict right now: every item
    /// standing in the garden (real + preview model), plus whatever the current placement is mid-load
    /// or mid-carry with.</summary>
    private HashSet<string> ComputeInUseAddressableKeys()
    {
        var keys = new HashSet<string>();

        void AddKey(AssetReferenceGameObject reference)
        {
            if (reference != null && reference.RuntimeKeyIsValid()) keys.Add(reference.AssetGUID);
        }

        foreach (PlaceableItem item in activePlacedItems)
        {
            if (item == null) continue;
            AddKey(ResolveItemPrefabRef(item.sourceKind, item.sourceItemId));
            AddKey(ResolvePreviewPrefabRef(item.sourceKind, item.sourceItemId));
        }

        AddKey(_pendingProgressPrefabRef);
        AddKey(_pendingProgressPreviewRef);

        return keys;
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

        // Items whose bundle failed to download during the last rebuild — preserve them in the save too,
        // instead of letting this snapshot (built only from activePlacedItems) quietly erase them for
        // good; see RebuildGardenAsync.
        state.items.AddRange(_unresolvedItems);

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

        StartCoroutine(RebuildGardenAsync(state));
    }

    /// <summary>
    /// Clears the garden and rebuilds it from <paramref name="state"/>, deducting the time that passed
    /// since the snapshot was taken from every growth timer. Every saved item's prefab is resolved by
    /// <see cref="ResolveItemPrefabRef"/> (source id, not the legacy prefab-name lookup) and downloaded
    /// if it isn't already cached; all downloads for the batch fire concurrently rather than one at a
    /// time, so a garden full of items doesn't serialize into a chain of sequential round trips at cold
    /// start (Addressables' own request-concurrency cap still throttles the actual network use).
    /// </summary>
    private IEnumerator RebuildGardenAsync(SaveStateCollection state)
    {
        _isRebuildingGarden = true;
        _unresolvedItems.Clear();

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
        if (grid != null) grid.ClearAll();

        int count = state.items.Count;
        GameObject[] resolvedPrefabs = new GameObject[count];
        bool[] done = new bool[count];
        bool[] hadValidRef = new bool[count];

        for (int i = 0; i < count; i++)
        {
            PlacedItemSaveData itemData = state.items[i];
            AssetReferenceGameObject prefabRef = ResolveItemPrefabRef(itemData.sourceKind, itemData.sourceItemId);

            if (prefabRef == null || !prefabRef.RuntimeKeyIsValid())
            {
                Debug.LogWarning($"[ItemPlacementManager] Failed to resolve a prefab for saved item "
                    + $"(sourceItemId='{itemData.sourceItemId}', sourceKind={itemData.sourceKind}).");
                done[i] = true;
                continue;
            }

            hadValidRef[i] = true;
            int index = i; // capture by value for the closure — `i` itself keeps incrementing
            AddressableItemLoader.LoadAsync(prefabRef, loaded =>
            {
                resolvedPrefabs[index] = loaded;
                done[index] = true;
            });
        }

        while (System.Array.IndexOf(done, false) >= 0)
        {
            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = resolvedPrefabs[i];
            if (prefab == null)
            {
                // A validly-referenced item whose bundle failed to download (offline, first sync on a
                // new device, not cached yet) — keep it pending instead of dropping it for good.
                if (hadValidRef[i]) _unresolvedItems.Add(state.items[i]);
                continue;
            }

            PlacedItemSaveData itemData = state.items[i];
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
            RegisterLoadedGridArea(placeable, prefab, itemData);
        }

        _isRebuildingGarden = false;
        TrimAddressableCache();
    }

    /// <summary>
    /// Claims the cells an item restored from a save is standing on.
    ///
    /// Saved positions are used exactly as they are, never re-snapped. Gardens built before the grid
    /// existed sit at arbitrary offsets, and silently nudging every decoration on the next launch -
    /// then pushing that rewritten layout to Firebase for every player at once - is neither something
    /// anyone asked for nor something that could be undone afterwards. The lattice governs what
    /// happens next instead: as players rearrange, their gardens come into alignment on their own.
    /// </summary>
    private void RegisterLoadedGridArea(PlaceableItem placeable, GameObject prefab, PlacedItemSaveData itemData)
    {
        if (grid == null || !grid.IsReady || placeable == null || prefab == null) return;

        int steps = ItemFootprint.StepsFromRotation(itemData.rotation, prefab.transform.rotation);
        Vector2Int footprint = ItemFootprint.Compute(
            prefab, steps, grid.CellSize, ResolveFootprintOverride(itemData.sourceKind, itemData.sourceItemId));

        RectInt area = grid.AreaCovering(itemData.position, footprint);
        placeable.SetGridArea(area);
        grid.Occupy(area, placeable.uniqueId);
    }

    /// <summary>Finds the shop entry an item came from by its stable id.</summary>
    private ShopItemData FindShopItemData(string itemId)
    {
        if (shopManager == null || shopManager.shopItemDatas == null || string.IsNullOrEmpty(itemId)) return null;

        foreach (var data in shopManager.shopItemDatas)
        {
            if (data != null && data.itemID == itemId) return data;
        }

        return null;
    }

    /// <summary>The inventory (treasure box reward) equivalent of <see cref="FindShopItemData"/>.</summary>
    private TreasureBoxRewardItemData FindInventoryItemData(string itemId)
    {
        if (shopManager == null || shopManager.inventoryItemDatas == null || string.IsNullOrEmpty(itemId)) return null;

        foreach (var data in shopManager.inventoryItemDatas)
        {
            if (data != null && data.itemID == itemId) return data;
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

        StartCoroutine(RebuildGardenAsync(state));

        // Mirror it locally so the next cold start shows the right garden before Flutter answers. This
        // persists the incoming state itself, not activePlacedItems, so it doesn't need to wait for the
        // (now async) rebuild above to finish spawning anything.
        SaveSystem.Save(SAVE_KEY, state);

        Debug.Log($"[ItemPlacementManager] Restoring garden from Firebase — {state.items.Count} item(s).");
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

        var items = new List<GardenItemPayload>(activePlacedItems.Count + _unresolvedItems.Count);
        foreach (var item in activePlacedItems)
        {
            Vector3 position = item.transform.position;
            Quaternion rotation = item.transform.rotation;

            items.Add(new GardenItemPayload
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
            });
        }

        // Items whose bundle failed to download during the last rebuild — keep them in the payload too
        // (as they last stood) so a cloud sync doesn't erase them for good; see RebuildGardenAsync.
        foreach (var pending in _unresolvedItems)
        {
            items.Add(new GardenItemPayload
            {
                uniqueId = pending.uniqueId,
                prefabName = pending.prefabName,
                posX = pending.position.x,
                posY = pending.position.y,
                posZ = pending.position.z,
                rotX = pending.rotation.x,
                rotY = pending.rotation.y,
                rotZ = pending.rotation.z,
                rotW = pending.rotation.w,
                remainingDuration = pending.remainingDuration,
                totalDuration = pending.totalDuration,
                sourceItemId = pending.sourceItemId,
                sourceKind = (int)pending.sourceKind
            });
        }

        return new GardenStatePayload
        {
            hasData = true,
            savedAtUnix = (long)_lastSavedAtUnix,
            revision = _revision,
            items = items.ToArray()
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
