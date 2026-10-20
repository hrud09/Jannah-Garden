using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// What a shop card's background colour is derived from. Category gives every collection its own
/// hue so the five tabs read apart; Tier varies the cards inside one tab as the quality band climbs.
/// </summary>
public enum ShopCardTintSource
{
    /// <summary>Leave the background on the sprite's authored colour.</summary>
    None,
    /// <summary>Colour of the collection the item belongs to (ShopTaxonomy.GetCategoryColor).</summary>
    Category,
    /// <summary>Colour of the item's quality band (ShopTaxonomy.GetTierColor), falling back to the
    /// category colour for items with no tier — coin packs, ad offers and treasure-box rewards.</summary>
    Tier
}

public class ShopItemUI : MonoBehaviour
{
    [Header("UI Component References")]
    public Image itemIcon;
    public TMP_Text itemNameText;
    public TMP_Text itemDescriptionText;
    public TMP_Text itemPriceText;
    public Image itemBackgroundImg;
    public Image itemIconBackgroundImg;
    public Button purchaseButton;
    [Tooltip("Label inside the purchase button. Reads 'Select' for normal items and 'Watch Ad' for rewarded-ad packs.")]
    public TMP_Text purchaseButtonLabel;

    [Header("State Visual References")]
    public GameObject[] lockedVisuals;
    public GameObject[] unlockedVisuals;

    [Header("Category & Tier Visuals")]
    [Tooltip("Reads the collection this item belongs to, e.g. 'Water of Garden'.")]
    public TMP_Text itemCategoryText;
    [Tooltip("Strip or icon tinted with the collection's accent colour.")]
    public Image itemCategoryAccentImg;
    [Tooltip("Reads the quality band, e.g. 'Tier 3 · Premium'.")]
    public TMP_Text itemTierText;
    [Tooltip("Badge behind the tier label. Tinted with the tier colour, and given the tier's sprite "
           + "when the shop supplies one.")]
    public Image itemTierBadgeImg;
    [Tooltip("Optional frame around the whole card, tinted with the tier colour.")]
    public Image itemTierFrameImg;
    [Tooltip("Optional per-tier decoration roots. Index 0 is Tier 1 — only the entry matching this "
           + "item's tier is left active. Leave empty if the card has no per-tier objects.")]
    public GameObject[] tierRoots;
    [Tooltip("Tint the tier badge, frame and per-tier roots with the tier colour from ShopTaxonomy. "
           + "Turn off when the sprites already carry their own colour.")]
    public bool tintTierVisuals = true;

    [Header("Card Background Colour")]
    [Tooltip("Where the card background's colour comes from. Tier gives each quality band inside a tab "
           + "its own hue; Category gives every collection one hue; None leaves the sprite untinted.")]
    public ShopCardTintSource backgroundTintSource = ShopCardTintSource.Tier;
    [Tooltip("How far the background is pushed from white toward the source colour. Low values keep the "
           + "card light so the icon and labels stay legible; 0 leaves it white.")]
    [Range(0f, 1f)]
    public float backgroundTintStrength = 0.3f;
    [Tooltip("Tint strength for the icon backing plate. Usually a little stronger than the card itself so "
           + "the icon sits in a visible well. Ignored when Icon Background Img is the same Image as the "
           + "card background.")]
    [Range(0f, 1f)]
    public float iconBackgroundTintStrength = 0.45f;
    [Tooltip("Colour the background is blended away from. White keeps the card art's own shading; a warm "
           + "off-white or a dark value suits a darker UI kit.")]
    public Color backgroundBaseColor = Color.white;

    [Header("Economy Visuals")]
    [Tooltip("Colour of the price label when the player CAN afford the item. Dark enough to read on "
           + "the cream price plate; the card art behind it is light, not the old green.")]
    public Color affordableColor = new Color(0.20f, 0.38f, 0.24f, 1f); // deep leaf green #33613D
    [Tooltip("Colour of the price label when the player CANNOT afford the item.")]
    public Color unaffordableColor = new Color(0.70f, 0.24f, 0.20f, 1f); // deep clay red #B33D33

    [Header("Acquisition Labels")]
    [Tooltip("Button label shown on a rewarded-ad item that is ready to claim.")]
    public string watchAdLabel = "Watch Ad";
    [Tooltip("Button label shown on every non-ad item.")]
    public string selectLabel = "Select";
    [Tooltip("Button label shown while a real-money purchase is in flight.")]
    public string purchasePendingLabel = "Purchasing...";
    [Tooltip("Button label shown while the item's prefab is downloading after purchase, before placement mode "
           + "starts. {0} is replaced with the live progress percentage. It replaces the Select/Watch Ad "
           + "label, never the price — the cost stays on the card throughout. Kept to the number alone "
           + "because the button is a narrow pill and a translated \"Downloading...\" wraps out of it; the "
           + "word itself is said once, in the toast that goes up when the download starts.")]
    public string downloadPendingLabel = "{0}%";

    [Header("Download Progress (optional)")]
    [Tooltip("Optional fill image (Image Type = Filled) driven 0→1 with live download progress. Safe to "
           + "leave unassigned — the purchase button always shows the percentage as text too.")]
    public Image downloadProgressFillImg;
    [Tooltip("Optional root (e.g. a progress bar container) shown only while a download is in progress.")]
    public GameObject downloadProgressRoot;

    [Tooltip("Alpha applied to this card while some OTHER item is downloading, so it reads as "
           + "temporarily unavailable. Set to 1 to disable the dimming and rely on the button alone.")]
    [Range(0.2f, 1f)]
    public float blockedCardAlpha = 0.5f;


    public ShopItemData ItemData { get; private set; }
    public TreasureBoxRewardItemData RewardItemData { get; private set; }

    private CanvasGroup canvasGroup;

    // True while IAPManager has a real-money purchase in flight for this card.
    private bool isPurchasePending;

    // True from the moment this item is paid for until ItemPlacementManager finishes downloading its
    // prefab — see InGameShopManager.CompleteAcquisition/SelectAndUseInventoryItem. The shop stays open
    // for this whole window, so the card needs its own visual to show something is happening.
    private bool isDownloadPending;

    // 0–1 progress within the current download, driven by ItemPlacementManager's onProgress callback.
    private float downloadProgress01;

    // True while an item is downloading somewhere — usually a different card, but also a treasure box
    // reward or a relocate, neither of which has a card at all. Only one placement can be in flight at
    // a time, so anything clickable here would just supersede the download already running. Kept apart
    // from isDownloadPending so the card actually downloading keeps its own progress label and stays
    // legible while every other card dims.
    private bool isInteractionBlocked;

    // Throttles the daily-offer countdown to one refresh per second.
    private float countdownTimer;

    // The purchase button label's authored sizing, captured the first time a download shrinks it.
    private bool hasCachedLabelSizing;
    private bool cachedAutoSizing;
    private float cachedFontSize;
    private float cachedFontSizeMin;
    private float cachedFontSizeMax;

    /// <summary>
    /// Gets the CanvasGroup on this GameObject, adding one dynamically if missing.
    /// </summary>
    public CanvasGroup CanvasGroup
    {
        get
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }
            return canvasGroup;
        }
    }

    /// <summary>
    /// Initializes the UI components with the values from a ShopItemData asset, the tier styling for
    /// its quality band, and background overrides.
    /// </summary>
    public void Initialize(ShopItemData data, ShopTierVisuals tierVisuals = null,
                           Sprite customBackground = null, Sprite customIconBackground = null)
    {
        if (data == null) return;
        ItemData = data;
        isPurchasePending = false;
        isDownloadPending = false;
        downloadProgress01 = 0f;

        // A tier entry can carry its own card art, which wins over the plain overrides.
        if (tierVisuals != null)
        {
            if (tierVisuals.cardBackground != null) customBackground = tierVisuals.cardBackground;
            if (tierVisuals.iconBackground != null) customIconBackground = tierVisuals.iconBackground;
        }

        if (itemIcon != null && data.itemIcon != null)
        {
            itemIcon.sprite = data.itemIcon;
        }

        ApplyNameAndDescription(data.LocalizedName, data.LocalizedDescription);

        bool isLevelLocked = PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevel < data.requiredXPLevel;

        if (itemBackgroundImg != null && customBackground != null)
        {
            itemBackgroundImg.sprite = customBackground;
        }

        if (itemIconBackgroundImg != null && customIconBackground != null)
        {
            itemIconBackgroundImg.sprite = customIconBackground;
        }

        // Keep locked visuals active when locked, inactive otherwise
        bool isLocked = isLevelLocked;
        if (lockedVisuals != null)
        {
            foreach (var go in lockedVisuals)
            {
                if (go != null) go.SetActive(isLocked);
            }
        }
        // Keep unlocked visuals active when unlocked, inactive otherwise
        bool isUnlocked = !isLocked;
        if (unlockedVisuals != null)
        {
            foreach (var go in unlockedVisuals)
            {
                if (go != null) go.SetActive(isUnlocked);
            }
        }

        ApplyTaxonomyVisuals(data.itemCategory, data.itemTier, tierVisuals);
        ApplyBackgroundTint(data.itemCategory, data.itemTier);

        // Price label + affordability tint (both derived from the acquisition type)
        RefreshAffordabilityVisual();
    }

    /// <summary>
    /// Paints the collection and tier badges. Every reference here is optional, so a card prefab can
    /// opt into as much or as little of this as its layout has room for. Items with no tier (coin
    /// packs, ad offers) hide the tier widgets entirely rather than showing an empty badge.
    /// </summary>
    private void ApplyTaxonomyVisuals(ShopItemCategory category, ShopItemTier tier, ShopTierVisuals tierVisuals)
    {
        // ── Category ──────────────────────────────────────────────────────
        Color categoryColor = ShopTaxonomy.GetCategoryColor(category);

        if (itemCategoryText != null)
        {
            itemCategoryText.color = categoryColor;
            LocalizedRendering.SetText(itemCategoryText, ShopTaxonomy.GetCategoryName(category));
        }

        if (itemCategoryAccentImg != null)
        {
            // Preserve the authored alpha so a soft accent strip stays soft.
            categoryColor.a = itemCategoryAccentImg.color.a;
            itemCategoryAccentImg.color = categoryColor;
        }

        // ── Tier ──────────────────────────────────────────────────────────
        bool hasTier = tier != ShopItemTier.None;
        Color tierColor = ShopTaxonomy.GetTierColor(tier);

        if (itemTierText != null)
        {
            itemTierText.gameObject.SetActive(hasTier);
            if (hasTier)
            {
                if (tintTierVisuals) itemTierText.color = tierColor;
                LocalizedRendering.SetText(itemTierText, ShopTaxonomy.GetTierLabel(tier));
            }
        }

        if (itemTierBadgeImg != null)
        {
            itemTierBadgeImg.gameObject.SetActive(hasTier);
            if (hasTier)
            {
                if (tierVisuals != null && tierVisuals.tierBadge != null)
                {
                    itemTierBadgeImg.sprite = tierVisuals.tierBadge;
                }
                if (tintTierVisuals)
                {
                    tierColor.a = itemTierBadgeImg.color.a;
                    itemTierBadgeImg.color = tierColor;
                }
            }
        }

        if (itemTierFrameImg != null && hasTier && tintTierVisuals)
        {
            Color frameColor = ShopTaxonomy.GetTierColor(tier);
            frameColor.a = itemTierFrameImg.color.a;
            itemTierFrameImg.color = frameColor;
        }

        // Only the root matching this item's tier stays on; Tier 1 is index 0.
        if (tierRoots != null && tierRoots.Length > 0)
        {
            int activeIndex = hasTier ? (int)tier - 1 : -1;
            for (int i = 0; i < tierRoots.Length; i++)
            {
                if (tierRoots[i] != null) tierRoots[i].SetActive(i == activeIndex);
            }
        }
    }

    /// <summary>
    /// Colours the card background (and the icon backing plate, when it is a separate Image) from the
    /// item's tier or category, so a panel of cards no longer reads as one flat block. The source colour
    /// is blended away from <see cref="backgroundBaseColor"/> rather than applied raw: a shop card is
    /// mostly a backdrop for an icon and three labels, and a full-strength accent swallows them. The
    /// authored alpha is preserved so a translucent card stays translucent.
    /// </summary>
    private void ApplyBackgroundTint(ShopItemCategory category, ShopItemTier tier)
    {
        if (backgroundTintSource == ShopCardTintSource.None) return;

        // Tier mode falls back to the category colour for anything unranked — coin packs, ad offers and
        // treasure-box rewards all arrive here as Tier.None and would otherwise come out plain white.
        Color source = backgroundTintSource == ShopCardTintSource.Category || tier == ShopItemTier.None
            ? ShopTaxonomy.GetCategoryColor(category)
            : ShopTaxonomy.GetTierColor(tier);

        ApplyTintTo(itemBackgroundImg, source, backgroundTintStrength);

        // Both references point at the same Image on the stock card prefab; tinting it twice would
        // apply the icon strength over the card strength and lose the card's own setting.
        if (itemIconBackgroundImg != itemBackgroundImg)
        {
            ApplyTintTo(itemIconBackgroundImg, source, iconBackgroundTintStrength);
        }
    }

    /// <summary>Blends one Image from <see cref="backgroundBaseColor"/> toward <paramref name="source"/>,
    /// keeping whatever alpha the prefab authored.</summary>
    private void ApplyTintTo(Image img, Color source, float strength)
    {
        if (img == null) return;

        Color tint = Color.Lerp(backgroundBaseColor, source, Mathf.Clamp01(strength));
        tint.a = img.color.a;
        img.color = tint;
    }

    // Affordability Visuals

    private void OnEnable()
    {
        NoorCoinManager.OnBalanceChanged += OnBalanceChanged;
        DailyOfferManager.OnOffersChanged += OnOffersChanged;
        ItemPlacementManager.OnDownloadStateChanged += SetInteractionBlocked;
        LocalizationManager.OnLocaleChanged += RefreshLocalizedLabels;

        // Catch up on a download that started before this card existed. Categories build their cards the
        // first time they are opened, so a player who buys an item and then switches tabs mid-download
        // would otherwise be handed a panel full of live buttons.
        SetInteractionBlocked(ItemPlacementManager.Instance != null
            && ItemPlacementManager.Instance.IsDownloadingItem);

        RefreshLocalizedLabels();
        RefreshAffordabilityVisual();
    }

    private void OnDisable()
    {
        NoorCoinManager.OnBalanceChanged -= OnBalanceChanged;
        DailyOfferManager.OnOffersChanged -= OnOffersChanged;
        ItemPlacementManager.OnDownloadStateChanged -= SetInteractionBlocked;
        LocalizationManager.OnLocaleChanged -= RefreshLocalizedLabels;
    }

    private void OnBalanceChanged(int _) => RefreshAffordabilityVisual();

    private void OnOffersChanged() => RefreshAffordabilityVisual();

    /// <summary>
    /// Re-applies the bound item's localized name/description — called on enable and whenever the active
    /// locale changes, so a card already on screen updates immediately instead of only on its next
    /// Initialize() call.
    /// </summary>
    private void RefreshLocalizedLabels()
    {
        if (ItemData != null) ApplyNameAndDescription(ItemData.LocalizedName, ItemData.LocalizedDescription);
        else if (RewardItemData != null) ApplyNameAndDescription(RewardItemData.LocalizedName, RewardItemData.LocalizedDescription);

        // These acquisition-label fields are Inspector-configurable (see their [Tooltip]s above), but every
        // instance ships with the plain English default — refresh them from the active locale so a card
        // doesn't get stuck showing English after a locale change.
        LocalizationManager loc = LocalizationManager.Instance;
        if (loc == null) return;
        watchAdLabel = loc.Get("shop.label_watch_ad");
        selectLabel = loc.Get("shop.label_select");
        purchasePendingLabel = loc.Get("shop.label_purchasing");
        downloadPendingLabel = loc.Get("shop.label_downloading_percent");
    }

    /// <summary>Shapes and assigns the name/description labels for the active locale (RTL mirroring,
    /// Arabic/Urdu joining, Bengali HarfBuzz rendering) instead of a raw <c>.text =</c> assignment.</summary>
    private void ApplyNameAndDescription(string name, string description)
    {
        if (itemNameText != null && !string.IsNullOrEmpty(name)) LocalizedRendering.SetText(itemNameText, name);
        if (itemDescriptionText != null && !string.IsNullOrEmpty(description)) LocalizedRendering.SetText(itemDescriptionText, description);
    }

    private void Update()
    {
        // A daily offer on cooldown shows a live countdown, so it has to retick itself. Everything else
        // is event-driven and needs no per-frame work.
        if (!IsDailyOfferOnCooldown()) return;

        countdownTimer -= Time.unscaledDeltaTime;
        if (countdownTimer > 0f) return;

        countdownTimer = 1f;
        RefreshAffordabilityVisual();
    }

    /// <summary>
    /// Called by <see cref="InGameShopManager"/> while a real-money purchase is in flight, so the card
    /// cannot be clicked twice.
    /// </summary>
    public void SetPurchasePending(bool pending)
    {
        isPurchasePending = pending;
        RefreshAffordabilityVisual();
    }

    /// <summary>
    /// Called by <see cref="InGameShopManager"/> from the moment this item is paid for until its prefab
    /// has finished downloading and placement mode takes over. Keeps the card from being clicked again
    /// while the shop stays open waiting for the download.
    /// </summary>
    public void SetDownloadPending(bool pending)
    {
        isDownloadPending = pending;
        downloadProgress01 = 0f;

        if (downloadProgressFillImg != null) downloadProgressFillImg.fillAmount = 0f;
        if (downloadProgressRoot != null) downloadProgressRoot.SetActive(pending);

        // Even a bare "42%" can be wider than the shortest Select label at some font sizes, so let the
        // label shrink for the duration rather than wrapping inside the button pill.
        SetButtonLabelAutoSizing(pending);

        // This card just became (or stopped being) the one downloading, which flips whether the shop-wide
        // lock should be dimming it.
        ApplyBlockedAlpha();

        RefreshAffordabilityVisual();
    }

    /// <summary>
    /// Locks this card while any item's prefab is downloading, driven by
    /// <see cref="ItemPlacementManager.OnDownloadStateChanged"/>. Only one placement can be in flight at
    /// a time, so leaving the rest of the shop live means a second tap supersedes the download the player
    /// is already waiting on — the old request is abandoned and its card is left stranded. Purely a UI
    /// lock: the card keeps its own price and affordability label, it just cannot be clicked.
    /// </summary>
    public void SetInteractionBlocked(bool blocked)
    {
        isInteractionBlocked = blocked;

        ApplyBlockedAlpha();
        RefreshAffordabilityVisual();
    }

    /// <summary>
    /// Dims this card while the shop-wide download lock is on — except on the card that is actually
    /// downloading, which stays at full opacity so its live progress label remains readable.
    /// </summary>
    private void ApplyBlockedAlpha()
    {
        CanvasGroup.alpha = isInteractionBlocked && !isDownloadPending ? blockedCardAlpha : 1f;
    }

    /// <summary>
    /// Live download progress (0–1) for the item currently downloading on this card, pushed roughly once
    /// a frame by ItemPlacementManager. Ignored once the card has moved on from the downloading state
    /// (e.g. a stray update arriving right after SetDownloadPending(false)).
    /// </summary>
    public void SetDownloadProgress(float progress01)
    {
        if (!isDownloadPending) return;

        downloadProgress01 = Mathf.Clamp01(progress01);
        if (downloadProgressFillImg != null) downloadProgressFillImg.fillAmount = downloadProgress01;

        // Only the button label changes as the bytes land — the price label is unaffected by progress,
        // and re-shaping it every frame would run HarfBuzz on the Bengali/Arabic path for nothing.
        SetProgressButtonLabel();
    }

    /// <summary>
    /// Rebuilds the card's button label and its price label, and enables or disables the purchase
    /// button. The two are deliberately independent: the button says what tapping the card does —
    /// "Select", "Watch Ad", "Purchasing...", "Downloading... 42%" — while the price label only ever
    /// says what the item costs or pays out, so the cost never disappears from under the player mid
    /// download.
    /// </summary>
    public void RefreshAffordabilityVisual()
    {
        // ── Pending states: they take over the BUTTON, not the price ──────────
        if (isDownloadPending)
        {
            SetProgressButtonLabel();
            SetButtonInteractable(false);
            RefreshPriceLabel();
            return;
        }

        if (isPurchasePending)
        {
            SetButtonLabel(purchasePendingLabel);
            SetButtonInteractable(false);
            RefreshPriceLabel();
            return;
        }

        // ── Idle: the button says how the item is acquired ────────────────────
        if (RewardItemData != null)
        {
            SetButtonLabel(selectLabel);
            SetButtonInteractable(true);
            RefreshPriceLabel();
            return;
        }

        if (ItemData == null)
        {
            SetButtonInteractable(true);
            return;
        }

        SetButtonLabel(ItemData.acquisitionType == ShopAcquisitionType.RewardedAd ? watchAdLabel : selectLabel);

        // A claimed daily offer is the only idle state that locks the card — an item the player cannot
        // currently afford stays tappable, because tapping it is now what opens the Noor Coin store.
        SetButtonInteractable(!IsDailyOfferOnCooldown());

        RefreshPriceLabel();
    }

    /// <summary>
    /// Writes the price/payout label and its affordability tint. Never touches the button — see
    /// <see cref="RefreshAffordabilityVisual"/> for why the two are kept apart.
    /// </summary>
    private void RefreshPriceLabel()
    {
        if (itemPriceText == null) return;

        if (RewardItemData != null)
        {
            // The text itself ("Owned") is written once in Initialize.
            itemPriceText.color = affordableColor;
            return;
        }

        if (ItemData == null) return;

        // Locked items advertise the level they need, and don't need affordability tinting.
        bool isLevelLocked = PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevel < ItemData.requiredXPLevel;
        if (isLevelLocked)
        {
            LocalizedRendering.SetText(itemPriceText, LocalizationManager.Instance.Get("shop.level_badge", ItemData.requiredXPLevel));
            return;
        }

        switch (ItemData.acquisitionType)
        {
            case ShopAcquisitionType.InAppPurchase:
            {
                // Real money: always "affordable" — the storefront, not the wallet, decides.
                LocalizedRendering.SetText(itemPriceText, string.IsNullOrEmpty(ItemData.realMoneyPriceLabel)
                    ? LocalizationManager.Instance.Get("shop.label_buy")
                    : ItemData.realMoneyPriceLabel);
                itemPriceText.color = affordableColor;
                break;
            }

            case ShopAcquisitionType.RewardedAd:
            {
                // A daily offer that has been claimed counts down to its next refresh; anything else is
                // one ad away.
                if (IsDailyOfferOnCooldown())
                {
                    System.TimeSpan remaining = DailyOfferManager.Instance.GetTimeUntilAvailable(ItemData);
                    LocalizedRendering.SetText(itemPriceText, DailyOfferManager.FormatCooldown(remaining));
                    itemPriceText.color = unaffordableColor;
                }
                else if (ItemData.noorCoinReward > 0)
                {
                    // Pure digits — no translated words, so this skips shaping. No inline coin glyph: the
                    // card's Item Cost UI already draws the coin icon beside this label, and "⧟" is in
                    // neither the UI font nor the Bengali one, so it rendered as a tofu box everywhere.
                    LocalizedRendering.SetPlainText(itemPriceText, $"+{ItemData.noorCoinReward}");
                    itemPriceText.color = affordableColor;
                }
                else
                {
                    // The price slot advertises the payout instead of a cost — the button already says
                    // it costs an ad. A prefab-granting offer has no coin payout, so it falls back to
                    // the ad label.
                    LocalizedRendering.SetText(itemPriceText, watchAdLabel);
                    itemPriceText.color = affordableColor;
                }
                break;
            }

            default:
            {
                if (ItemData.noorCoinCost == 0)
                {
                    LocalizedRendering.SetText(itemPriceText, LocalizationManager.Instance.Get("shop.label_free"));
                }
                else
                {
                    // Pure digits — see the reward branch above for why there is no inline coin glyph.
                    LocalizedRendering.SetPlainText(itemPriceText, $"{ItemData.noorCoinCost}");
                }

                // Free items are always "affordable"
                if (ItemData.noorCoinCost <= 0)
                {
                    itemPriceText.color = affordableColor;
                    break;
                }

                bool canAfford = NoorCoinManager.Instance != null &&
                                 NoorCoinManager.Instance.CanAfford(ItemData.noorCoinCost);

                itemPriceText.color = canAfford ? affordableColor : unaffordableColor;
                break;
            }
        }
    }

    /// <summary>True only for a daily-offer ad item whose cooldown has not yet elapsed.</summary>
    private bool IsDailyOfferOnCooldown()
    {
        return ItemData != null
            && ItemData.acquisitionType == ShopAcquisitionType.RewardedAd
            && ItemData.isDailyOffer
            && DailyOfferManager.Instance != null
            && !DailyOfferManager.Instance.IsOfferAvailable(ItemData);
    }

    /// <summary>
    /// The one place the purchase button's interactable state is set. A download in flight anywhere
    /// always wins over whatever this card would otherwise allow, so every caller below can go on
    /// reasoning about its own state alone.
    /// </summary>
    private void SetButtonInteractable(bool interactable)
    {
        if (purchaseButton != null) purchaseButton.interactable = interactable && !isInteractionBlocked;
    }

    /// <summary>
    /// Writes the live percentage into the purchase button. Digits and a percent sign only, so this
    /// takes the plain path rather than the shaping one — there is no script to join or mirror, and
    /// this runs roughly once a frame for as long as the download lasts.
    /// </summary>
    private void SetProgressButtonLabel()
    {
        if (purchaseButtonLabel == null) return;

        LocalizedRendering.SetPlainText(purchaseButtonLabel,
            string.Format(downloadPendingLabel, Mathf.RoundToInt(downloadProgress01 * 100f)));
    }

    private void SetButtonLabel(string label)
    {
        if (purchaseButtonLabel != null) LocalizedRendering.SetText(purchaseButtonLabel, label);
    }

    /// <summary>
    /// Turns TMP auto-sizing on for the button label while a long status string is in it, and puts the
    /// prefab's authored sizing back afterwards. The authored values are captured the first time this
    /// runs, so a card that is never used for a download keeps its label untouched.
    /// </summary>
    private void SetButtonLabelAutoSizing(bool enable)
    {
        if (purchaseButtonLabel == null) return;

        if (!hasCachedLabelSizing)
        {
            cachedAutoSizing = purchaseButtonLabel.enableAutoSizing;
            cachedFontSize = purchaseButtonLabel.fontSize;
            cachedFontSizeMin = purchaseButtonLabel.fontSizeMin;
            cachedFontSizeMax = purchaseButtonLabel.fontSizeMax;
            hasCachedLabelSizing = true;
        }

        if (enable)
        {
            purchaseButtonLabel.enableAutoSizing = true;
            purchaseButtonLabel.fontSizeMin = Mathf.Max(8f, cachedFontSize * 0.55f);
            purchaseButtonLabel.fontSizeMax = cachedFontSize;
        }
        else
        {
            purchaseButtonLabel.enableAutoSizing = cachedAutoSizing;
            purchaseButtonLabel.fontSizeMin = cachedFontSizeMin;
            purchaseButtonLabel.fontSizeMax = cachedFontSizeMax;
            purchaseButtonLabel.fontSize = cachedFontSize;
        }
    }

    /// <summary>
    /// Initializes the UI components with the values from a TreasureBoxRewardItemData asset and background overrides.
    /// </summary>
    public void Initialize(TreasureBoxRewardItemData data, Sprite customBackground = null, Sprite customIconBackground = null)
    {
        if (data == null) return;
        RewardItemData = data;
        isDownloadPending = false;
        downloadProgress01 = 0f;

        if (itemIcon != null && data.itemIcon != null)
        {
            itemIcon.sprite = data.itemIcon;
        }

        ApplyNameAndDescription(data.LocalizedName, data.LocalizedDescription);

        if (itemPriceText != null)
        {
            // Since there is no isUnlocked, assume they own what is shown in inventory
            LocalizedRendering.SetText(itemPriceText, LocalizationManager.Instance.Get("shop.owned_label"));
        }

        if (itemBackgroundImg != null && customBackground != null)
        {
            itemBackgroundImg.sprite = customBackground;
        }

        if (itemIconBackgroundImg != null && customIconBackground != null)
        {
            itemIconBackgroundImg.sprite = customIconBackground;
        }

        // Treasure box rewards are ranked by their own ItemRarity, not by a shop tier, so the tier
        // widgets stay hidden here.
        ApplyTaxonomyVisuals(data.itemCategory, ShopItemTier.None, null);
        ApplyBackgroundTint(data.itemCategory, ShopItemTier.None);

        bool isLocked = false;
        if (lockedVisuals != null)
        {
            foreach (var go in lockedVisuals)
            {
                if (go != null) go.SetActive(isLocked);
            }
        }

        bool isUnlocked = !isLocked;
        if (unlockedVisuals != null)
        {
            foreach (var go in unlockedVisuals)
            {
                if (go != null) go.SetActive(isUnlocked);
            }
        }

        RefreshAffordabilityVisual();
    }
}
