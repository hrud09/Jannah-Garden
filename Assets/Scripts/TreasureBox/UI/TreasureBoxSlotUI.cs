using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Drives a single treasure box slot in the UI panel.
///
/// Attach this component to each of the 12 slot GameObjects in the Treasure Box
/// panel (3 slots × 4 tiers). Wire up the public references in the Inspector,
/// and the slot will automatically poll <see cref="TreasureBoxManager"/> to
/// keep its visuals, countdown timer, and button state up to date.
///
/// Tier + slot index must be configured in the Inspector.
/// </summary>
public class TreasureBoxSlotUI : MonoBehaviour
{
    // ─── Inspector Configuration ──────────────────────────────────────────────

    [Header("Slot Identity")]
    [Tooltip("Which tier this slot represents.")]
    public TreasureBoxTier tier;

    [Tooltip("Slot index within the tier (0, 1, or 2).")]
    [Range(0, TreasureBoxManager.SLOTS_PER_TIER - 1)]
    public int slotIndex;

    // ─── UI References ────────────────────────────────────────────────────────

    [Header("UI References")]
    [Tooltip("The Open button the player taps to open this box.")]
    public Button openButton;

    [Tooltip("Image that displays the box sprite (closed / opened).")]
    public Image boxImage;

    [Tooltip("Glow / rim light image tinted with the tier's theme color.")]
    public Image glowImage;

    [Tooltip("Shows 'Ready!' or a hh:mm:ss countdown, or 'Opened'.")]
    public TMP_Text statusText;

    [Tooltip("Shows the slot number label, e.g., 'Box 1'.")]
    public TMP_Text slotLabel;

    [Header("State Overlays")]
    [Tooltip("Root GameObject shown when this slot is locked (wrong tier).")]
    public GameObject lockedOverlay;

    [Tooltip("Root GameObject shown when this slot has been opened.")]
    public GameObject openedOverlay;

    [Tooltip("Root GameObject shown when the slot is on cooldown (not ready yet).")]
    public GameObject cooldownOverlay;

    [Header("Animator")]
    [Tooltip("Animator on the glow/shimmer loop. Trigger 'Open' to play the open burst.")]
    public Animator glowAnimator;

    // ─── String Trigger Names ─────────────────────────────────────────────────

    private static readonly int ANIM_OPEN      = Animator.StringToHash("Open");
    private static readonly int ANIM_AVAILABLE = Animator.StringToHash("Available");
    private static readonly int ANIM_IDLE      = Animator.StringToHash("Idle");

    // ─── Private State ────────────────────────────────────────────────────────

    private TreasureBoxRewardData _rewardData;
    private Coroutine             _countdownCoroutine;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (openButton != null)
            openButton.onClick.AddListener(OnOpenButtonClicked);

        if (slotLabel != null)
            slotLabel.text = $"Box {slotIndex + 1}";
    }

    private void OnEnable()
    {
        TreasureBoxManager.OnStateChanged += Refresh;
        TreasureBoxManager.OnBoxOpened    += OnBoxOpenedHandler;
        Refresh();
    }

    private void OnDisable()
    {
        TreasureBoxManager.OnStateChanged -= Refresh;
        TreasureBoxManager.OnBoxOpened    -= OnBoxOpenedHandler;
        StopCountdown();
    }

    // ─── Refresh ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads current state from <see cref="TreasureBoxManager"/> and updates
    /// every visual element of this slot. Called automatically when state changes.
    /// </summary>
    public void Refresh()
    {
        if (TreasureBoxManager.Instance == null) return;

        _rewardData = TreasureBoxManager.Instance.GetRewardData(tier);

        // Apply tier glow color
        if (_rewardData != null && glowImage != null)
            glowImage.color = _rewardData.glowColor;

        TreasureBoxTierState state = TreasureBoxManager.Instance.GetTierState(tier);
        bool tierUnlocked  = TreasureBoxManager.Instance.IsTierUnlocked(tier);
        bool slotAlreadyOpened = state.slotOpened[slotIndex];
        bool slotAvailable = TreasureBoxManager.Instance.IsSlotAvailable(tier, slotIndex);

        StopCountdown();

        // ── Opened state ──────────────────────────────────────────────────────
        if (slotAlreadyOpened)
        {
            SetOverlays(locked: false, opened: true, cooldown: false);
            SetBoxSprite(opened: true);
            SetStatus("Opened", Color.gray);
            SetButtonInteractable(false);
            SetAnimatorTrigger(ANIM_IDLE);
            return;
        }

        // ── Tier locked state ─────────────────────────────────────────────────
        if (!tierUnlocked)
        {
            SetOverlays(locked: true, opened: false, cooldown: false);
            SetBoxSprite(opened: false);
            SetStatus("Locked", new Color(0.6f, 0.6f, 0.6f));
            SetButtonInteractable(false);
            SetAnimatorTrigger(ANIM_IDLE);
            return;
        }

        // ── Slot available state ──────────────────────────────────────────────
        if (slotAvailable)
        {
            SetOverlays(locked: false, opened: false, cooldown: false);
            SetBoxSprite(opened: false);
            SetStatus("Ready!", new Color(0.2f, 0.9f, 0.4f));
            SetButtonInteractable(true);
            SetAnimatorTrigger(ANIM_AVAILABLE);
            return;
        }

        // ── Cooldown state ────────────────────────────────────────────────────
        SetOverlays(locked: false, opened: false, cooldown: true);
        SetBoxSprite(opened: false);
        SetButtonInteractable(false);
        SetAnimatorTrigger(ANIM_IDLE);
        _countdownCoroutine = StartCoroutine(CountdownCoroutine());
    }

    // ─── Countdown ────────────────────────────────────────────────────────────

    private IEnumerator CountdownCoroutine()
    {
        while (true)
        {
            System.DateTime readyAt = TreasureBoxManager.Instance.GetSlotAvailableAt(tier, slotIndex);

            if (readyAt == System.DateTime.MinValue)
            {
                // Slot became available while counting down — trigger a full refresh
                Refresh();
                yield break;
            }

            System.TimeSpan remaining = readyAt - System.DateTime.UtcNow;
            if (remaining <= System.TimeSpan.Zero)
            {
                Refresh();
                yield break;
            }

            string display = remaining.Hours > 0
                ? $"{remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
                : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";

            SetStatus(display, new Color(1f, 0.75f, 0.2f));
            yield return new WaitForSeconds(1f);
        }
    }

    private void StopCountdown()
    {
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }
    }

    // ─── Button Handler ───────────────────────────────────────────────────────

    private void OnOpenButtonClicked()
    {
        if (TreasureBoxManager.Instance == null) return;

        SetButtonInteractable(false); // Prevent double-tap during ad

        TreasureBoxManager.Instance.TryOpenBox(tier, slotIndex, (success, reason) =>
        {
            if (!success)
            {
                Debug.Log($"[TreasureBoxSlotUI] Open failed: {reason}");
                SetButtonInteractable(true);
            }
            // On success, OnStateChanged event will trigger Refresh() automatically
        });
    }

    // ─── Event Handlers ───────────────────────────────────────────────────────

    private void OnBoxOpenedHandler(TreasureBoxTier openedTier, int openedSlot, TreasureBoxRewardData data)
    {
        if (openedTier == tier && openedSlot == slotIndex)
        {
            // Play open animation on this slot
            SetAnimatorTrigger(ANIM_OPEN);
        }
    }

    // ─── Visual Helpers ───────────────────────────────────────────────────────

    private void SetOverlays(bool locked, bool opened, bool cooldown)
    {
        if (lockedOverlay  != null) lockedOverlay.SetActive(locked);
        if (openedOverlay  != null) openedOverlay.SetActive(opened);
        if (cooldownOverlay != null) cooldownOverlay.SetActive(cooldown);
    }

    private void SetBoxSprite(bool opened)
    {
        if (boxImage == null || _rewardData == null) return;
        Sprite sprite = opened ? _rewardData.openedBoxSprite : _rewardData.closedBoxSprite;
        if (sprite != null) boxImage.sprite = sprite;
    }

    private void SetStatus(string text, Color color)
    {
        if (statusText == null) return;
        statusText.text  = text;
        statusText.color = color;
    }

    private void SetButtonInteractable(bool interactable)
    {
        if (openButton != null) openButton.interactable = interactable;
    }

    private void SetAnimatorTrigger(int triggerHash)
    {
        if (glowAnimator == null) return;
        glowAnimator.ResetTrigger(ANIM_OPEN);
        glowAnimator.ResetTrigger(ANIM_AVAILABLE);
        glowAnimator.ResetTrigger(ANIM_IDLE);
        glowAnimator.SetTrigger(triggerHash);
    }
}
