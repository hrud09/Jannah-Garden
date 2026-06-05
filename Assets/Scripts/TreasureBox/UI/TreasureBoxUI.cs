using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Controls the Treasure Box panel's overall layout: shows/hides the panel,
/// subscribes to manager events, displays reward popups, and manages the IAP
/// "unlock all 3" button per tier.
///
/// Attach to the root panel GameObject in your UI hierarchy.
/// Wire up all slot UIs and per-tier IAP buttons in the Inspector.
/// </summary>
public class TreasureBoxUI : MonoBehaviour
{
    // ─── Panel References ─────────────────────────────────────────────────────

    [Header("Panel Root")]
    [Tooltip("The root RectTransform of the treasure box panel.")]
    public RectTransform panelRoot;

    [Tooltip("Open panel button — e.g., a chest icon in the HUD.")]
    public Button openPanelButton;

    [Tooltip("Close / back button inside the panel.")]
    public Button closePanelButton;

    [Header("Panel Animation")]
    [Tooltip("Duration of the panel slide-in/out animation in seconds.")]
    public float panelAnimDuration = 0.35f;
    public AnimationCurve panelAnimCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // ─── Slot UI Arrays ───────────────────────────────────────────────────────

    [Header("Slot UIs — Silver (slots 0‑2)")]
    public TreasureBoxSlotUI[] silverSlots = new TreasureBoxSlotUI[3];

    [Header("Slot UIs — Gold (slots 0‑2)")]
    public TreasureBoxSlotUI[] goldSlots = new TreasureBoxSlotUI[3];

    [Header("Slot UIs — Platinum (slots 0‑2)")]
    public TreasureBoxSlotUI[] platinumSlots = new TreasureBoxSlotUI[3];

    [Header("Slot UIs — Diamond (slots 0‑2)")]
    public TreasureBoxSlotUI[] diamondSlots = new TreasureBoxSlotUI[3];

    // ─── Per-Tier IAP Buttons ─────────────────────────────────────────────────

    [Header("IAP Unlock-All Buttons (one per tier)")]
    [Tooltip("Button that triggers the IAP purchase to unlock all 3 Silver boxes at once.")]
    public Button silverIAPButton;
    public Button goldIAPButton;
    public Button platinumIAPButton;
    public Button diamondIAPButton;

    // ─── Tier Lock Overlays ───────────────────────────────────────────────────

    [Header("Per-Tier Lock Overlays")]
    [Tooltip("Full-tier overlay shown when the tier is locked (lower tiers incomplete). " +
             "One per tier row in the panel layout.")]
    public GameObject silverTierLockOverlay;
    public GameObject goldTierLockOverlay;
    public GameObject platinumTierLockOverlay;
    public GameObject diamondTierLockOverlay;

    // ─── Reward Popup ─────────────────────────────────────────────────────────

    [Header("Reward Popup")]
    [Tooltip("Root GameObject of the reward popup (hidden by default).")]
    public GameObject rewardPopupRoot;

    [Tooltip("Icon displayed in the reward popup.")]
    public Image rewardPopupIcon;

    [Tooltip("Title text in the reward popup, e.g., 'Silver Set Complete!'")]
    public TMP_Text rewardPopupTitle;

    [Tooltip("Description of the reward, e.g., 'Reward: Crystal Fountain'")]
    public TMP_Text rewardPopupDescription;

    [Tooltip("Button to dismiss the reward popup.")]
    public Button rewardPopupDismissButton;

    // ─── Badge / Notification Dot ─────────────────────────────────────────────

    [Header("HUD Badge")]
    [Tooltip("A small notification dot/badge on the HUD button showing available boxes.")]
    public GameObject availableBadge;

    [Tooltip("Text inside the badge showing the available box count.")]
    public TMP_Text availableBadgeCount;

    // ─── Private State ────────────────────────────────────────────────────────

    private bool      _isOpen;
    private Vector2   _hiddenAnchoredPos;
    private Vector2   _shownAnchoredPos;
    private Coroutine _panelCoroutine;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Cache panel positions (assumes the panel starts at its SHOWN position)
        if (panelRoot != null)
        {
            _shownAnchoredPos  = panelRoot.anchoredPosition;
            // Slide off-screen to the right by the panel's own width
            _hiddenAnchoredPos = _shownAnchoredPos + new Vector2(panelRoot.rect.width + 50f, 0f);
        }

        if (openPanelButton  != null) openPanelButton.onClick.AddListener(OpenPanel);
        if (closePanelButton != null) closePanelButton.onClick.AddListener(ClosePanel);

        if (rewardPopupDismissButton != null)
            rewardPopupDismissButton.onClick.AddListener(HideRewardPopup);

        WireIAPButton(silverIAPButton,   TreasureBoxTier.Silver);
        WireIAPButton(goldIAPButton,     TreasureBoxTier.Gold);
        WireIAPButton(platinumIAPButton, TreasureBoxTier.Platinum);
        WireIAPButton(diamondIAPButton,  TreasureBoxTier.Diamond);

        // Start hidden
        SetPanelImmediate(false);
        HideRewardPopup();
    }

    private void OnEnable()
    {
        TreasureBoxManager.OnStateChanged += RefreshAll;
        TreasureBoxManager.OnSetCompleted  += ShowRewardPopup;
        RefreshAll();
    }

    private void OnDisable()
    {
        TreasureBoxManager.OnStateChanged -= RefreshAll;
        TreasureBoxManager.OnSetCompleted  -= ShowRewardPopup;
    }

    // ─── Panel Open / Close ───────────────────────────────────────────────────

    public void OpenPanel()
    {
        if (_isOpen) return;
        _isOpen = true;

        if (_panelCoroutine != null) StopCoroutine(_panelCoroutine);
        _panelCoroutine = StartCoroutine(AnimatePanel(_hiddenAnchoredPos, _shownAnchoredPos));

        RefreshAll();
    }

    public void ClosePanel()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (_panelCoroutine != null) StopCoroutine(_panelCoroutine);
        _panelCoroutine = StartCoroutine(AnimatePanel(_shownAnchoredPos, _hiddenAnchoredPos));
    }

    public void TogglePanel()
    {
        if (_isOpen) ClosePanel(); else OpenPanel();
    }

    private void SetPanelImmediate(bool open)
    {
        _isOpen = open;
        if (panelRoot != null)
            panelRoot.anchoredPosition = open ? _shownAnchoredPos : _hiddenAnchoredPos;
    }

    private IEnumerator AnimatePanel(Vector2 from, Vector2 to)
    {
        if (panelRoot == null) yield break;

        float elapsed = 0f;
        while (elapsed < panelAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / panelAnimDuration);
            float curveT = panelAnimCurve.Evaluate(t);
            panelRoot.anchoredPosition = Vector2.Lerp(from, to, curveT);
            yield return null;
        }
        panelRoot.anchoredPosition = to;
    }

    // ─── Refresh ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes the HUD badge and all per-tier lock overlays.
    /// Individual slot UIs subscribe to OnStateChanged themselves and self-refresh.
    /// </summary>
    public void RefreshAll()
    {
        if (TreasureBoxManager.Instance == null) return;

        RefreshTierLockOverlay(TreasureBoxTier.Silver,   silverTierLockOverlay);
        RefreshTierLockOverlay(TreasureBoxTier.Gold,     goldTierLockOverlay);
        RefreshTierLockOverlay(TreasureBoxTier.Platinum, platinumTierLockOverlay);
        RefreshTierLockOverlay(TreasureBoxTier.Diamond,  diamondTierLockOverlay);

        RefreshBadge();
    }

    private void RefreshTierLockOverlay(TreasureBoxTier tier, GameObject overlay)
    {
        if (overlay == null) return;
        bool locked = !TreasureBoxManager.Instance.IsTierUnlocked(tier);
        overlay.SetActive(locked);
    }

    private void RefreshBadge()
    {
        if (TreasureBoxManager.Instance == null) return;

        int available = TreasureBoxManager.Instance.GetTotalAvailableBoxCount();

        if (availableBadge != null)
            availableBadge.SetActive(available > 0);

        if (availableBadgeCount != null)
            availableBadgeCount.text = available > 0 ? available.ToString() : string.Empty;
    }

    // ─── Reward Popup ─────────────────────────────────────────────────────────

    private void ShowRewardPopup(TreasureBoxTier tier, TreasureBoxData rewardData)
    {
        if (rewardPopupRoot == null || rewardData == null) return;

        rewardPopupRoot.SetActive(true);

        if (rewardPopupTitle != null)
            rewardPopupTitle.text = $"{rewardData.tierDisplayName} Set Complete!";

        TreasureBoxRewardItemData reward = rewardData.exclusiveRewardItem;

        if (rewardPopupIcon != null && reward != null && reward.itemIcon != null)
            rewardPopupIcon.sprite = reward.itemIcon;

        if (rewardPopupDescription != null)
        {
            if (reward != null && reward.isUnlocked)
                rewardPopupDescription.text = $"Already owned! Received {rewardData.noorCoinEquivalent} Noor Coins.";
            else if (reward != null)
                rewardPopupDescription.text = $"Unlocked: {reward.itemName}";
            else
                rewardPopupDescription.text = $"Received {rewardData.noorCoinEquivalent} Noor Coins!";
        }
    }

    private void HideRewardPopup()
    {
        if (rewardPopupRoot != null)
            rewardPopupRoot.SetActive(false);
    }

    // ─── IAP Wiring ───────────────────────────────────────────────────────────

    private void WireIAPButton(Button button, TreasureBoxTier tier)
    {
        if (button == null) return;
        button.onClick.AddListener(() =>
        {
            if (TreasureBoxManager.Instance == null) return;

            button.interactable = false;
            TreasureBoxManager.Instance.PurchaseTierUnlock(tier, (success, reason) =>
            {
                button.interactable = true;
                if (!success)
                    Debug.Log($"[TreasureBoxUI] IAP failed for {tier}: {reason}");
            });
        });
    }
}
