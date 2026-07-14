using System;

namespace FlutterIntegration
{
    // Extend this file with new classes as your payload types grow

    /// <summary>
    /// The command strings shared by both sides of the bridge. Flutter must send exactly these values
    /// in <see cref="FlutterMessage.command"/>. Keep this list in sync with the Flutter-side constants
    /// (see flutter_bridge_guide.md at the repo root).
    /// </summary>
    public static class FlutterCommands
    {
        // ─── Flutter → Unity ──────────────────────────────────────────────────
        public const string UpdateUserProfile         = "UPDATE_USER_PROFILE";
        public const string UpdateCoins               = "UPDATE_COINS";
        public const string UpdateFellowshipProfiles  = "UPDATE_FELLOWSHIP_PROFILES";

        /// <summary>
        /// Flutter's answer to <see cref="RequestIAPPurchase"/>, sent once the store sheet closes.
        /// Payload: <see cref="IAPPurchaseResultPayload"/>. Not implemented on the Flutter side yet —
        /// until it is, IAPManager runs in dummy mode.
        /// </summary>
        public const string IAPPurchaseResult = "IAP_PURCHASE_RESULT";

        // ─── Unity → Flutter ──────────────────────────────────────────────────
        /// <summary>Sent once the bridge is alive, so Flutter knows it is safe to push data.</summary>
        public const string UnityReady = "UNITY_READY";

        /// <summary>Sent when a scene needs fellowship data that has not been pushed yet.</summary>
        public const string RequestFellowshipProfiles = "REQUEST_FELLOWSHIP_PROFILES";

        /// <summary>
        /// Asks Flutter to run the real-money purchase flow for a Noor Coin pack, since Flutter owns
        /// the store plugin and the Firebase wallet. Payload: <see cref="IAPPurchaseRequestPayload"/>.
        /// </summary>
        public const string RequestIAPPurchase = "REQUEST_IAP_PURCHASE";
    }

    [Serializable]
    public class UserProfilePayload
    {
        public string userName;
        public int noorCoins;
        public string profileImagePath; // Use local path or URL, avoid Base64
    }

    [Serializable]
    public class CoinUpdatePayload
    {
        public int newBalance;
    }

    /// <summary>
    /// The fellowship roster: the other users whose profile cards are spawned around the garden.
    /// Field-for-field the same shape as the dummy JSON in Resources/fellow_profiles.txt, so the same
    /// view layer renders both.
    /// </summary>
    [Serializable]
    public class FellowshipProfilesPayload
    {
        public FellowProfileData[] fellows;
    }

    /// <summary>Unity → Flutter: please charge the player for this product.</summary>
    [Serializable]
    public class IAPPurchaseRequestPayload
    {
        public string productId;

        /// <summary>Noor Coins this product grants, so Flutter can credit the wallet server-side.</summary>
        public int noorCoinReward;
    }

    /// <summary>Flutter → Unity: the outcome of a purchase Unity asked for.</summary>
    [Serializable]
    public class IAPPurchaseResultPayload
    {
        public string productId;
        public bool success;

        /// <summary>Optional store message, shown to the player when the purchase fails.</summary>
        public string message;
    }

    /// <summary>Placeholder for commands that carry no data (JsonUtility cannot serialize null).</summary>
    [Serializable]
    public class EmptyPayload
    {
    }
}
