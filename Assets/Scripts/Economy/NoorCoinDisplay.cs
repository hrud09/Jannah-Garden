using UnityEngine;
using TMPro;

/// <summary>
/// Attach to any UI GameObject that should display the player's Noor Coin balance.
/// Subscribes to <see cref="NoorCoinManager.OnBalanceChanged"/> and refreshes
/// the label automatically on every balance change.
/// Numbers are formatted compactly: 999 → "999", 1000 → "1k", 1500 → "1.5k",
/// 1,000,000 → "1m", 1,000,000,000 → "1b", etc.
///
/// The label does NOT have to live on this GameObject — in Jannah Garden this component sits on the
/// Pop Up Canvas while the label it drives is in the HUD canvas. That is why the subscription lasts for
/// the component's whole lifetime (Awake → OnDestroy) instead of its enabled state: disabling the popup
/// canvas must not freeze a label that is still on screen.
/// </summary>
public class NoorCoinDisplay : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The TMP label that shows the current Noor Coin balance.")]
    public TMP_Text balanceLabel;

    [Tooltip("Text shown in place of the balance while the initial Flutter/Firebase sync is still in " +
             "progress, so the player never sees an unsynced 0 and mistakes it for their real balance.")]
    [SerializeField] private string loadingText = "...";

    [Header("Motion")]
    [Tooltip("What punches when the balance changes (usually the whole coin pill). Defaults to the label.")]
    public Transform punchTarget;

    [Tooltip("Where the floating +N / -N text appears. Defaults to the label's parent.")]
    public RectTransform floatingTextAnchor;

    [Tooltip("Show a floating +N / -N beside the balance when it changes.")]
    public bool showFloatingDelta = true;

    [Tooltip("Where reward coins fly into (UIFlyToTarget) - a rect over the coin on the pill art. Leave " +
             "empty on displays that shouldn't receive flying coins; only one display should set it.")]
    public RectTransform flyTarget;

    [Tooltip("Sprite the flying coins use.")]
    public Sprite flySprite;

    // Balance shown when a reward hold began; the final +N is measured from here, not from the last
    // landing, so the floating text reports the whole reward.
    private int? _heldFrom;

    // Last balance painted as a real number; null while the label shows the loading text, so the
    // first real balance appears instantly instead of rolling up from 0.
    private int? _lastShown;

    /// <summary>The live balance, or 0 when no manager exists yet — never the label's authored text.</summary>
    private static int CurrentBalance =>
        NoorCoinManager.Instance != null ? NoorCoinManager.Instance.Balance : 0;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (balanceLabel == null)
        {
            Debug.LogWarning($"[NoorCoinDisplay] No balanceLabel assigned on '{name}' — the balance has nowhere to show.");
        }

        NoorCoinManager.OnBalanceChanged += Refresh;
        UIRewardHold.Landed += OnRewardLanded;
        UIRewardHold.Released += OnRewardReleased;
        if (flyTarget != null)
            UIFlyToTarget.RegisterTarget(RewardChannel.Coins, flyTarget, flySprite);

        // Paint a real number straight away once synced. Without this the label keeps whatever
        // placeholder the designer typed (the HUD one says "200"), so a player with no coins appears to
        // have 200. Before the sync completes, show the loading text instead of the unsynced 0 — the
        // first OnBalanceChanged fired once Flutter answers will replace it with the real balance.
        ShowCurrentState();
    }

    private void OnEnable()
    {
        // Re-show the current balance whenever this object comes back on; the subscription itself is
        // already alive, so nothing is re-registered here.
        ShowCurrentState();
    }

    private void OnDestroy()
    {
        NoorCoinManager.OnBalanceChanged -= Refresh;
        UIRewardHold.Landed -= OnRewardLanded;
        UIRewardHold.Released -= OnRewardReleased;
        if (flyTarget != null) UIFlyToTarget.UnregisterTarget(RewardChannel.Coins, flyTarget);
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    /// <summary>Shows the loading text while the initial sync is pending, otherwise the real balance.</summary>
    private void ShowCurrentState()
    {
        if (NoorCoinManager.IsBalanceSynced)
        {
            Refresh(CurrentBalance);
        }
        else if (balanceLabel != null)
        {
            balanceLabel.text = loadingText;
            _lastShown = null;
        }
    }

    private void Refresh(int newBalance)
    {
        if (balanceLabel == null) return;

        UICountTo counter = Counter();
        bool canAnimate = _lastShown.HasValue && balanceLabel.isActiveAndEnabled;

        // A reward is flying in: leave the number where it is and let the landings move it.
        if (canAnimate && UIRewardHold.IsHeld(RewardChannel.Coins))
        {
            _heldFrom ??= _lastShown;
            return;
        }

        int previous = _heldFrom ?? _lastShown ?? newBalance;
        _heldFrom = null;
        _lastShown = newBalance;

        if (!canAnimate)
        {
            counter.SetImmediate(newBalance, FormatCoins);
            return;
        }

        int delta = newBalance - previous;
        counter.RollTo(newBalance, FormatCoins);

        if (delta != 0 && showFloatingDelta)
        {
            UIMotionTokens t = UIMotion.Tokens;
            RectTransform anchor = floatingTextAnchor != null ? floatingTextAnchor : balanceLabel.rectTransform.parent as RectTransform;
            UIFloatingText.Spawn(anchor, (delta > 0 ? "+" : "") + delta, delta > 0 ? t.gainFlash : t.spendFlash,
                // The pill sits right under the XP bar, so the delta drifts down into open space.
                balanceLabel, new Vector2(-30f, -55f), -35f);
        }
    }

    /// <summary>One flying coin landed: tick towards the real balance by its share, never past it.</summary>
    private void OnRewardLanded(RewardChannel channel, int amount)
    {
        if (channel != RewardChannel.Coins || balanceLabel == null || !_lastShown.HasValue) return;
        if (!balanceLabel.isActiveAndEnabled) return;

        _heldFrom ??= _lastShown;
        int target = CurrentBalance;
        int shown = _lastShown.Value;
        int next = target >= shown ? Mathf.Min(shown + amount, target) : Mathf.Max(shown - amount, target);
        if (next == shown) return;

        _lastShown = next;
        Counter().RollTo(next, FormatCoins);
    }

    private void OnRewardReleased(RewardChannel channel)
    {
        if (channel != RewardChannel.Coins) return;
        if (_heldFrom.HasValue || _lastShown != CurrentBalance) Refresh(CurrentBalance);
    }

    private UICountTo Counter()
    {
        if (!balanceLabel.TryGetComponent(out UICountTo counter))
        {
            counter = balanceLabel.gameObject.AddComponent<UICountTo>();
            counter.punchTarget = punchTarget;
        }
        return counter;
    }

    /// <summary>
    /// Converts a coin amount into a compact, human-readable string.
    /// <list type="bullet">
    ///   <item>0–999      → "999"</item>
    ///   <item>1000–999 999  → "1k" / "1.5k"</item>
    ///   <item>1 000 000–999 999 999 → "1m" / "2.3m"</item>
    ///   <item>≥ 1 000 000 000 → "1b" / "4.7b"</item>
    /// </list>
    /// Trailing ".0" is stripped so "1.0k" becomes "1k".
    /// </summary>
    private static string FormatCoins(int amount)
    {
        if (amount >= 1_000_000_000)
        {
            return StripTrailingZero(amount / 1_000_000_000f, 1) + "b";
        }

        if (amount >= 1_000_000)
        {
            return StripTrailingZero(amount / 1_000_000f, 1) + "m";
        }

        if (amount >= 1_000)
        {
            return StripTrailingZero(amount / 1_000f, 1) + "k";
        }

        return amount.ToString();
    }

    /// <summary>
    /// Formats <paramref name="value"/> to <paramref name="decimals"/> decimal places,
    /// then strips a trailing ".0" so "1.0" becomes "1" but "1.5" stays "1.5".
    /// </summary>
    private static string StripTrailingZero(float value, int decimals)
    {
        string formatted = value.ToString("F" + decimals); // e.g. "1.0", "1.5"
        if (formatted.EndsWith(".0"))
        {
            formatted = formatted.Substring(0, formatted.Length - 2);
        }
        return formatted;
    }
}
