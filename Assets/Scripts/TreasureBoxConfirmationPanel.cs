using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FlutterIntegration;

public class TreasureBoxConfirmationPanel : MonoBehaviour
{
    private static TreasureBoxConfirmationPanel _instance;
    public static TreasureBoxConfirmationPanel Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<TreasureBoxConfirmationPanel>(true);
            }
            return _instance;
        }
    }

    [Header("UI Fields")]
    public TMP_Text nameText;
    public TMP_Text descriptionText;
    public Image rewardIcon;
    public Image rarityBgImage;
    public TMP_Text rarityText;

    [Header("Buttons")]
    public Button watchAdButton;
    public Button subscribeButton;
    public Button closeButton;

    [Header("Visual Roots")]
    [Tooltip("The fullscreen dim behind the panel. Toggled instead of this GameObject so the " +
             "panel root stays active and Instance stays resolvable.")]
    public GameObject darkBgImage;

    [Tooltip("The panel body (window + content). Toggled instead of this GameObject.")]
    public GameObject bgImage;

    private TreasureBoxTier _tier;
    private int _slotIndex;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        ResolveVisualRoots();

        // Setup button listeners
        if (watchAdButton != null)
        {
            watchAdButton.onClick.AddListener(OnWatchAdClicked);
        }
        if (subscribeButton != null)
        {
            subscribeButton.onClick.AddListener(OnSubscribeClicked);
        }
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseClicked);
        }

        // Initially hide the panel visuals — the root itself stays active.
        SetVisualsActive(false);
    }

    /// <summary>
    /// Falls back to looking the visual roots up by name so an un-wired prefab
    /// instance still hides correctly.
    /// </summary>
    private void ResolveVisualRoots()
    {
        if (darkBgImage == null)
        {
            Transform t = transform.Find("Dark BG Image");
            if (t != null) darkBgImage = t.gameObject;
        }
        if (bgImage == null)
        {
            Transform t = transform.Find("BG Image");
            if (t != null) bgImage = t.gameObject;
        }
    }

    /// <summary>
    /// Shows/hides only the panel's visual children. The panel GameObject itself is
    /// never deactivated, so <see cref="Instance"/> and any coroutines on it survive.
    /// </summary>
    private void SetVisualsActive(bool active)
    {
        if (darkBgImage != null) darkBgImage.SetActive(active);
        if (bgImage != null) bgImage.SetActive(active);
    }

    public void Show(TreasureBoxTier tier, int slotIndex)
    {
        _tier = tier;
        _slotIndex = slotIndex;

        if (TreasureBoxManager.Instance != null)
        {
            TreasureBoxRewardItemData reward = TreasureBoxManager.Instance.GetCurrentCycleReward(tier);
            if (reward != null)
            {
                if (nameText != null) LocalizedRendering.SetText(nameText, reward.LocalizedName);
                if (descriptionText != null) LocalizedRendering.SetText(descriptionText, reward.LocalizedDescription);
                if (rewardIcon != null)
                {
                    rewardIcon.sprite = reward.itemIcon;
                    rewardIcon.gameObject.SetActive(reward.itemIcon != null);
                }
                if (rarityBgImage != null)
                {
                    rarityBgImage.color = reward.GetRarityColor();
                    rarityBgImage.gameObject.SetActive(true);
                }
                if (rarityText != null)
                {
                    LocalizedRendering.SetText(rarityText, ItemRarityTaxonomy.GetName(reward.GetRarity()));
                    rarityText.gameObject.SetActive(true);
                }
            }
            else
            {
                if (nameText != null) LocalizedRendering.SetText(nameText, TreasureBoxData.GetLocalizedTierBoxFallback(tier));
                if (descriptionText != null) LocalizedRendering.SetText(descriptionText, LocalizationManager.Instance.Get("treasurebox.open_prompt"));
                if (rewardIcon != null) rewardIcon.gameObject.SetActive(false);
                if (rarityBgImage != null) rarityBgImage.gameObject.SetActive(false);
                if (rarityText != null) rarityText.gameObject.SetActive(false);
            }
        }

        ResolveVisualRoots();
        SetVisualsActive(true);
    }

    private void OnWatchAdClicked()
    {
        SetVisualsActive(false);
        if (AdsManager.Instance != null)
        {
            AdsManager.Instance.ShowRewardedAd(() =>
            {
                if (TreasureBoxManager.Instance != null)
                {
                    TreasureBoxManager.Instance.TryOpenBox(_tier, _slotIndex);
                }
            }, "treasure_box");
        }
        else
        {
            if (TreasureBoxManager.Instance != null)
            {
                TreasureBoxManager.Instance.TryOpenBox(_tier, _slotIndex);
            }
        }
    }

    private void OnSubscribeClicked()
    {
        SetVisualsActive(false);

        // Subscriptions are owned by the Flutter app, not the game. Ask Flutter to leave Unity and open
        // its subscribe page — Flutter pops the Unity widget and handles navigation on its side.
        if (FlutterBridge.Instance != null)
        {
            FlutterBridge.Instance.SendMessageToFlutterApp(
                FlutterCommands.RequestSubscribe,
                new SubscribeRequestPayload { source = "treasure_box" });
        }
        else
        {
            Debug.LogWarning("[TreasureBoxConfirmationPanel] No FlutterBridge — cannot open subscribe page.");
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast(LocalizationManager.Instance.Get("treasurebox.need_subscribe"));
            }
        }
    }

    private void OnCloseClicked()
    {
        SetVisualsActive(false);
    }
}
