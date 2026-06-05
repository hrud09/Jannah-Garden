using UnityEngine;
using TMPro;

/// <summary>
/// Attach to any UI GameObject that should display the player's Noor Coin balance.
/// Subscribes to <see cref="NoorCoinManager.OnBalanceChanged"/> and refreshes
/// the label automatically on every balance change.
/// Numbers are formatted compactly: 999 → "999", 1000 → "1k", 1500 → "1.5k",
/// 1,000,000 → "1m", 1,000,000,000 → "1b", etc.
/// </summary>
public class NoorCoinDisplay : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The TMP label that shows the current Noor Coin balance.")]
    public TMP_Text balanceLabel;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        NoorCoinManager.OnBalanceChanged += Refresh;

        // Show the current balance immediately when this object becomes active
        if (NoorCoinManager.Instance != null)
        {
            Refresh(NoorCoinManager.Instance.Balance);
        }
    }

    private void OnDisable()
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
