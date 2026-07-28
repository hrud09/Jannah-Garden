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
        /// Asks Flutter for the player's authoritative Noor Coin balance. Sent on every game start —
        /// including each time the player re-enters the embedded game, since Unity is paused rather than
        /// torn down — and repeated until Flutter answers. Flutter should reply with
        /// <see cref="UpdateCoins"/> (or <see cref="UpdateUserProfile"/>, which also carries the balance).
        /// Payload: <see cref="EmptyPayload"/>.
        /// </summary>
        public const string RequestCoinBalance = "REQUEST_COIN_BALANCE";

        /// <summary>
        /// Asks Flutter to run the real-money purchase flow for a Noor Coin pack, since Flutter owns
        /// the store plugin and the Firebase wallet. Payload: <see cref="IAPPurchaseRequestPayload"/>.
        /// </summary>
        public const string RequestIAPPurchase = "REQUEST_IAP_PURCHASE";

        /// <summary>
        /// Asks Flutter to leave the Unity game and open the app's subscription page. Fired when the
        /// player taps "Subscribe" on the treasure box panel. Flutter owns the subscription flow, so
        /// its job is to pop/dismiss the Unity widget and navigate to the subscribe screen.
        /// Payload: <see cref="SubscribeRequestPayload"/> (carries the context that prompted it).
        /// </summary>
        public const string RequestSubscribe = "REQUEST_SUBSCRIBE";

        /// <summary>
        /// Asks Flutter to leave the Unity game and return the player to the app. Fired when the player
        /// confirms exit on the exit panel. Unity must NOT call <c>Application.Quit()</c> for this: the
        /// game runs inside the Flutter host process, so quitting would close the whole app. Flutter's
        /// job is to pop/dismiss the Unity widget and show its own screen.
        /// Payload: <see cref="ExitGameRequestPayload"/> (carries the context that prompted it).
        /// </summary>
        public const string RequestExitGame = "REQUEST_EXIT_GAME";
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

    /// <summary>
    /// Unity → Flutter: the player asked to subscribe. <paramref name="source"/> tells Flutter where the
    /// request came from (e.g. "treasure_box") so it can tailor the subscribe screen or analytics.
    /// </summary>
    [Serializable]
    public class SubscribeRequestPayload
    {
        public string source;
    }

    /// <summary>
    /// Unity → Flutter: the player asked to leave the game. <paramref name="source"/> tells Flutter what
    /// triggered it (e.g. "exit_panel") so it can decide where to land the player or log analytics.
    /// </summary>
    [Serializable]
    public class ExitGameRequestPayload
    {
        public string source;
    }

    /// <summary>Placeholder for commands that carry no data (JsonUtility cannot serialize null).</summary>
    [Serializable]
    public class EmptyPayload
    {
    }
}
