using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "get more Noor Coins" popup. Opened from <see cref="InGameShopManager"/> when the player taps
/// an item they cannot afford: rather than a toast that tells them no and leaves them where they
/// started, the shop hands them the one screen that fixes it — the Noor Coins collection, whose
/// offers pay out coins for a rewarded ad (or real money).
///
/// The cards are ordinary <see cref="ShopItemUI"/> instances wired to the same
/// <see cref="InGameShopManager.SelectAndUseItem"/> entry point as the shop's own cards, so ad
/// gating, daily-offer cooldowns and coin granting all behave exactly as they do in the Noor Coins
/// tab. This panel is only a second place to show them.
/// </summary>
public class NoorCoinStorePopup : MonoBehaviour
{
    public static NoorCoinStorePopup Instance { get; private set; }

    [Header("Panel")]
    [Tooltip("Root toggled on and off. Leave empty to toggle this GameObject.")]
    public GameObject panelRoot;
    [Tooltip("Faded in on open. Optional — without it the panel just pops.")]
    public CanvasGroup canvasGroup;
    [Tooltip("Scaled up on open for the same bounce the shop panel uses. Optional.")]
    public RectTransform window;

    [Header("Content")]
    [Tooltip("Parent the coin offer cards are spawned under — the Content of this popup's scroll view.")]
    public Transform contentParent;
    [Tooltip("Card prefab to spawn. Leave empty to borrow InGameShopManager's own shop item prefab.")]
    public GameObject shopItemUIPrefab;

    [Header("Labels")]
    public TMP_Text titleText;
    [Tooltip("Reads how many more coins the player needs for the item they just tried to buy.")]
    public TMP_Text messageText;
    [Tooltip("Optional — reads the live balance while the popup is open.")]
    public TMP_Text balanceText;

    [Header("Buttons")]
    public Button closeButton;
    [Tooltip("Label inside the close button, localized on open. Optional.")]
    public TMP_Text closeButtonLabel;
    [Tooltip("Full-screen dim behind the window; tapping it closes the popup like the close button does.")]
    public Button backdropButton;

    [Header("Behaviour")]
    [Tooltip("Reparent this popup to its Canvas on Awake and stretch it over the whole screen. The "
           + "shop prefab it is authored inside has a zero-sized root rect (its panels carry their own "
           + "sizes), so a popup left there would have nothing to fill. Turn this off only if the panel "
           + "is moved somewhere that is already full-screen.")]
    public bool fillCanvasOnAwake = true;
    [Tooltip("Close the popup automatically once the player has earned enough for the item that opened "
           + "it, so they land back on that item instead of having to dismiss this first.")]
    public bool closeWhenAffordable = true;
    [Tooltip("Seconds the 'you can afford it now' message stays up before the popup closes itself.")]
    public float affordableCloseDelay = 1.2f;
    public float openDuration = 0.35f;

    // The item whose price opened this popup. Null when the popup was opened on its own.
    private ShopItemData blockedItem;

    private readonly List<ShopItemUI> spawnedCards = new List<ShopItemUI>();
    private bool hasSpawnedCards;
    private Coroutine autoCloseRoutine;

    public bool IsOpen => Root.activeSelf;

    private GameObject Root => panelRoot != null ? panelRoot : gameObject;

    private void Awake()
    {
        Instance = this;

        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (backdropButton != null) backdropButton.onClick.AddListener(Close);

        if (fillCanvasOnAwake) FillCanvas();

        Root.SetActive(false);
    }

    /// <summary>
    /// Lifts the popup out of the shop's own (zero-sized) rect onto the canvas it lives under, as the
    /// last sibling so it draws over the shop panel, and stretches it edge to edge.
    /// </summary>
    private void FillCanvas()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        Transform canvasRoot = canvas.isRootCanvas ? canvas.transform : canvas.rootCanvas.transform;
        if (transform.parent != canvasRoot) transform.SetParent(canvasRoot, false);
        transform.SetAsLastSibling();

        var rect = transform as RectTransform;
        if (rect == null) return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (backdropButton != null) backdropButton.onClick.RemoveListener(Close);

        NoorCoinManager.OnBalanceChanged -= OnBalanceChanged;
    }

    /// <summary>
    /// Opens the popup because <paramref name="unaffordable"/> costs more than the player has. Pass
    /// null to open it as a plain coin storefront.
    /// </summary>
    public void Open(ShopItemData unaffordable)
    {
        blockedItem = unaffordable;

        EnsureCardsSpawned();

        if (autoCloseRoutine != null)
        {
            StopCoroutine(autoCloseRoutine);
            autoCloseRoutine = null;
        }

        Root.SetActive(true);

        NoorCoinManager.OnBalanceChanged -= OnBalanceChanged;
        NoorCoinManager.OnBalanceChanged += OnBalanceChanged;

        RefreshLabels();
        foreach (var card in spawnedCards)
        {
            if (card != null) card.RefreshAffordabilityVisual();
        }

        PlayOpenAnimation();
    }

    /// <summary>Opens the popup with no particular item behind it.</summary>
    public void Open() => Open(null);

    public void Close()
    {
        if (autoCloseRoutine != null)
        {
            StopCoroutine(autoCloseRoutine);
            autoCloseRoutine = null;
        }

        NoorCoinManager.OnBalanceChanged -= OnBalanceChanged;

        if (window != null) window.DOKill();
        if (canvasGroup != null) canvasGroup.DOKill();

        blockedItem = null;
        Root.SetActive(false);
    }

    private void PlayOpenAnimation()
    {
        if (canvasGroup != null)
        {
            canvasGroup.DOKill();
            canvasGroup.alpha = 0f;
            canvasGroup.DOFade(1f, openDuration * 0.6f).SetUpdate(true);
        }

        if (window != null)
        {
            window.DOKill();
            window.localScale = Vector3.one * 0.85f;
            window.DOScale(1f, openDuration).SetEase(Ease.OutBack).SetUpdate(true);
        }
    }

    /// <summary>
    /// Instantiates one card per Noor Coins offer, in the same order the shop's own Noor Coins tab
    /// shows them. Run once — the offers themselves are static, and each card keeps itself up to date
    /// from the balance and daily-offer events it already listens to.
    /// </summary>
    private void EnsureCardsSpawned()
    {
        if (hasSpawnedCards) return;

        // Instance is set in the shop's Awake, which may not have run yet if something opens this
        // popup during startup — fall back to a scene search rather than coming up empty.
        InGameShopManager shop = InGameShopManager.Instance != null
            ? InGameShopManager.Instance
            : FindObjectOfType<InGameShopManager>(true);

        if (shop == null)
        {
            Debug.LogError("[NoorCoinStorePopup] No InGameShopManager in the scene — cannot build the "
                + "coin offers.");
            return;
        }

        GameObject prefab = shopItemUIPrefab != null ? shopItemUIPrefab : shop.ShopItemUIPrefab;
        if (prefab == null || contentParent == null)
        {
            Debug.LogError("[NoorCoinStorePopup] Content Parent or the card prefab is unassigned — "
                + "run Tools/Shop/Build Noor Coin Popup to wire this panel up.");
            return;
        }

        hasSpawnedCards = true;

        List<ShopItemData> items = shop.shopItemDatas;
        if (items == null) return;

        var offers = items
            .Where(d => d != null && d.itemCategory == ShopItemCategory.NoorCoins)
            .OrderBy(d => d.sortOrder)
            .ThenByDescending(d => d.noorCoinReward);

        foreach (ShopItemData data in offers)
        {
            // One bad card must not cost the player the whole storefront — this is the screen they are
            // on precisely because they are short of coins.
            try
            {
                GameObject spawned = Instantiate(prefab, contentParent);
                ShopItemUI card = spawned.GetComponent<ShopItemUI>();
                if (card == null) continue;

                card.Initialize(data, shop.GetVisualsForTier(data.itemTier));

                if (card.purchaseButton != null)
                {
                    ShopItemUI captured = card;
                    card.purchaseButton.onClick.AddListener(() => shop.SelectAndUseItem(captured));
                }

                spawnedCards.Add(card);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[NoorCoinStorePopup] Failed to spawn the coin offer card for "
                    + $"'{data.itemName}'; skipping it: {e}");
            }
        }
    }

    private void OnBalanceChanged(int balance)
    {
        RefreshLabels();

        if (!closeWhenAffordable || blockedItem == null || autoCloseRoutine != null) return;
        if (NoorCoinManager.Instance == null || !NoorCoinManager.Instance.CanAfford(blockedItem.noorCoinCost)) return;

        autoCloseRoutine = StartCoroutine(CloseAfterAffordable());
    }

    /// <summary>
    /// The ad paid out enough — say so, then get out of the way so the player is looking at the item
    /// they actually wanted when they can finally buy it.
    /// </summary>
    private IEnumerator CloseAfterAffordable()
    {
        if (messageText != null)
        {
            LocalizedRendering.SetText(messageText, LocalizationManager.Instance.Get("shop.coin_popup_now_affordable"));
        }

        yield return new WaitForSecondsRealtime(affordableCloseDelay);

        autoCloseRoutine = null;
        Close();
    }

    private void RefreshLabels()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (loc == null) return;

        int balance = NoorCoinManager.Instance != null ? NoorCoinManager.Instance.Balance : 0;

        if (titleText != null) LocalizedRendering.SetText(titleText, loc.Get("shop.coin_popup_title"));

        if (closeButtonLabel != null) LocalizedRendering.SetText(closeButtonLabel, loc.Get("shop.coin_popup_close"));

        if (balanceText != null) LocalizedRendering.SetPlainText(balanceText, balance.ToString());

        if (messageText == null) return;

        if (blockedItem == null)
        {
            LocalizedRendering.SetText(messageText, loc.Get("shop.coin_popup_generic"));
            return;
        }

        int shortfall = Mathf.Max(0, blockedItem.noorCoinCost - balance);
        LocalizedRendering.SetText(messageText, shortfall > 0
            ? loc.Get("shop.coin_popup_shortfall", shortfall)
            : loc.Get("shop.coin_popup_now_affordable"));
    }
}
