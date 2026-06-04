using System;

/// <summary>
/// Abstraction over an in-app purchase service.
/// Implement with Unity IAP or any storefront SDK and register with
/// <see cref="TreasureBoxManager.IAPService"/>.
///
/// A no-op stub (<see cref="NullIAPService"/>) is used by default.
/// </summary>
public interface IIAPService
{
    /// <summary>
    /// Initiates the real-money purchase flow that unlocks all three treasure
    /// boxes of the specified tier at once, bypassing the standard progression
    /// order for free users.
    /// </summary>
    /// <param name="tier">The tier whose three boxes should be unlocked.</param>
    /// <param name="onComplete">
    /// Callback invoked when the purchase flow concludes.
    /// <c>true</c> = purchase succeeded; <c>false</c> = cancelled or failed.
    /// </param>
    void PurchaseTierUnlock(TreasureBoxTier tier, Action<bool> onComplete);
}

// ─────────────────────────────────────────────────────────────────────────────
// Default no-op stub
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Null-object implementation of <see cref="IIAPService"/>.
/// Always reports purchase as failed (so no boxes are accidentally unlocked
/// without real IAP logic). Replace with a real implementation at integration time.
/// </summary>
public class NullIAPService : IIAPService
{
    public void PurchaseTierUnlock(TreasureBoxTier tier, Action<bool> onComplete)
    {
        UnityEngine.Debug.LogWarning($"[NullIAPService] PurchaseTierUnlock({tier}) called — no real IAP SDK. Purchase rejected.");
        onComplete?.Invoke(false);
    }
}
