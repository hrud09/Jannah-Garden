using UnityEngine;
using System;
using System.Collections;
using FlutterIntegration;

/// <summary>
/// Real-money purchases for shop items (Noor Coin packs).
///
/// There is no storefront wired up yet. With <see cref="useDummyStore"/> enabled — the default — a
/// purchase is faked: it logs, waits <see cref="dummyPurchaseDelay"/> seconds to stand in for the store
/// sheet, then reports the result configured on <see cref="dummyPurchaseSucceeds"/>. The caller's
/// callback fires exactly as it would against a real store, so the shop flow is complete and testable
/// today and only this class changes when the backend lands.
///
/// The real path is sketched below: Jannah Garden is embedded in the Flutter app, so the purchase is
/// owned by Flutter (which holds the store plugin and the Firebase wallet). Unity asks for a purchase
/// and waits for Flutter to call <see cref="HandlePurchaseResult"/> back through
/// <see cref="FlutterBridge"/>. Granting the coins is the caller's job (InGameShopManager does it once
/// the purchase reports success), so it stays identical across both paths.
/// </summary>
public class IAPManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static IAPManager Instance { get; private set; }

    // ─── Events ───────────────────────────────────────────────────────────────

    public static event Action<ShopItemData> OnPurchaseSucceeded;
    public static event Action<ShopItemData, string> OnPurchaseFailed;

    // ─── Inspector Fields ─────────────────────────────────────────────────────

    [Header("Dummy Store (no storefront integrated yet)")]
    [Tooltip("Fake the purchase locally instead of asking Flutter to run the real store flow.")]
    [SerializeField] private bool useDummyStore = true;

    [Tooltip("Result the fake purchase reports. Untick to exercise the cancelled/failed path.")]
    [SerializeField] private bool dummyPurchaseSucceeds = true;

    [Tooltip("Seconds the fake purchase takes, standing in for the store sheet the player would see.")]
    [SerializeField] private float dummyPurchaseDelay = 1f;

    // ─── Private State ────────────────────────────────────────────────────────

    private ShopItemData _pendingItem;
    private Action<bool> _pendingCallback;

    // ─── Properties ───────────────────────────────────────────────────────────

    /// <summary>True while a purchase is in flight. Only one may run at a time.</summary>
    public bool IsPurchasePending => _pendingItem != null;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the real-money purchase flow for <paramref name="data"/>.
    /// <paramref name="onComplete"/> receives <c>true</c> if the purchase succeeded, <c>false</c> if it
    /// was cancelled or failed. It is always invoked exactly once.
    /// </summary>
    public void PurchaseProduct(ShopItemData data, Action<bool> onComplete)
    {
        if (data == null)
        {
            Debug.LogError("[IAPManager] PurchaseProduct called with null item data.");
            onComplete?.Invoke(false);
            return;
        }

        if (string.IsNullOrEmpty(data.iapProductId))
        {
            Debug.LogError($"[IAPManager] '{data.itemName}' is an InAppPurchase item but has no iapProductId set.");
            onComplete?.Invoke(false);
            return;
        }

        if (IsPurchasePending)
        {
            Debug.LogWarning($"[IAPManager] A purchase for '{_pendingItem.itemName}' is already in flight. "
                + $"Ignoring request for '{data.itemName}'.");
            onComplete?.Invoke(false);
            return;
        }

        _pendingItem = data;
        _pendingCallback = onComplete;

        if (useDummyStore)
        {
            StartCoroutine(DummyPurchaseRoutine(data));
            return;
        }

        RequestPurchaseFromFlutter(data);
    }

    /// <summary>
    /// Resolves the in-flight purchase. Flutter calls this through
    /// <see cref="FlutterBridge"/> once the store sheet closes.
    /// </summary>
    /// <param name="productId">Must match the pending item's <see cref="ShopItemData.iapProductId"/>.</param>
    /// <param name="success">Whether the player actually paid.</param>
    /// <param name="message">Optional store message, shown to the player on failure.</param>
    public void HandlePurchaseResult(string productId, bool success, string message = null)
    {
        if (_pendingItem == null)
        {
            Debug.LogWarning($"[IAPManager] Purchase result for '{productId}' arrived with no purchase in flight. Ignored.");
            return;
        }

        if (_pendingItem.iapProductId != productId)
        {
            Debug.LogWarning($"[IAPManager] Purchase result for '{productId}' does not match the pending "
                + $"product '{_pendingItem.iapProductId}'. Ignored.");
            return;
        }

        CompletePendingPurchase(success, message);
    }

    // ─── Dummy Store ──────────────────────────────────────────────────────────

    private IEnumerator DummyPurchaseRoutine(ShopItemData data)
    {
        Debug.Log($"[IAPManager] DUMMY PURCHASE START — product '{data.iapProductId}' "
            + $"('{data.itemName}', {data.realMoneyPriceLabel}) → grants {data.noorCoinReward} Noor Coins. "
            + "No real money is charged; no storefront is integrated yet.");

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, dummyPurchaseDelay));

        string message = dummyPurchaseSucceeds ? null : "Purchase cancelled";
        Debug.Log($"[IAPManager] DUMMY PURCHASE {(dummyPurchaseSucceeds ? "SUCCEEDED" : "FAILED")} "
            + $"for product '{data.iapProductId}'.");

        CompletePendingPurchase(dummyPurchaseSucceeds, message);
    }

    // ─── Real Store (via Flutter) ─────────────────────────────────────────────

    /// <summary>
    /// Hands the purchase off to Flutter, which owns the store plugin and the Firebase wallet.
    /// Flutter is expected to answer with <see cref="FlutterCommands.IAPPurchaseResult"/>, which
    /// <see cref="FlutterBridge"/> routes back into <see cref="HandlePurchaseResult"/>.
    /// </summary>
    private void RequestPurchaseFromFlutter(ShopItemData data)
    {
        if (FlutterBridge.Instance == null)
        {
            Debug.LogError("[IAPManager] useDummyStore is off but there is no FlutterBridge in the scene — "
                + "the purchase cannot be sent anywhere. Failing it.");
            CompletePendingPurchase(false, "Store unavailable");
            return;
        }

        var payload = new IAPPurchaseRequestPayload
        {
            productId = data.iapProductId,
            noorCoinReward = data.noorCoinReward
        };

        FlutterBridge.Instance.SendMessageToFlutterApp(FlutterCommands.RequestIAPPurchase, payload);

        Debug.Log($"[IAPManager] Purchase for '{data.iapProductId}' sent to Flutter. Awaiting result.");

        // No timeout: the purchase stays pending until Flutter answers. If Flutter can drop a purchase
        // silently, add one here so the shop button does not stay stuck on "Purchasing...".
    }

    // ─── Internal Helpers ─────────────────────────────────────────────────────

    private void CompletePendingPurchase(bool success, string message)
    {
        ShopItemData item = _pendingItem;
        Action<bool> callback = _pendingCallback;

        // Clear before invoking, so a callback that starts another purchase is not blocked by this one.
        _pendingItem = null;
        _pendingCallback = null;

        if (success)
        {
            OnPurchaseSucceeded?.Invoke(item);
        }
        else
        {
            Debug.Log($"[IAPManager] Purchase of '{item.itemName}' did not complete: {message ?? "cancelled"}.");
            OnPurchaseFailed?.Invoke(item, message);
        }

        callback?.Invoke(success);
    }
}
