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

        // Paint a real number straight away. Without this the label keeps whatever placeholder the
        // designer typed (the HUD one says "200"), so a player with no coins appears to have 200.
        Refresh(CurrentBalance);
    }

    private void OnEnable()
    {
        // Re-show the current balance whenever this object comes back on; the subscription itself is
        // already alive, so nothing is re-registered here.
        Refresh(CurrentBalance);
    }

    private void OnDestroy()
    {
        NoorCoinManager.OnBalanceChanged -= Refresh;
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    private void Refresh(int newBalance)
    {
        if (balanceLabel == null) return;

        balanceLabel.text = FormatCoins(newBalance);
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
