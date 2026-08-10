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

        /// <summary>
        /// Flutter's answer to <see cref="RequestRewardedAd"/>, sent once the ad closes for any reason.
        /// Payload: <see cref="RewardedAdResultPayload"/>. Carries no coin amount by design — the reward
        /// arrives separately as <see cref="UpdateCoins"/> once the server has credited the wallet.
        /// </summary>
        public const string RewardedAdResult = "REWARDED_AD_RESULT";

        /// <summary>
        /// Flutter tells the game whether a rewarded ad is loaded and showable. Pushed unprompted
        /// whenever the ad's state changes, so "Watch Ad" buttons can be enabled/disabled ahead of time.
        /// Payload: <see cref="AdAvailabilityPayload"/>.
        /// </summary>
        public const string UpdateAdAvailability = "UPDATE_AD_AVAILABILITY";

        /// <summary>
        /// Flutter hands over the garden it has stored in Firebase — every asset the player has placed,
        /// where it stands and how far along its growth timer is. Sent in answer to
        /// <see cref="RequestGardenState"/>, and again whenever the stored garden changes.
        /// Payload: <see cref="GardenStatePayload"/>. This is what carries a player's Jannah Garden onto
        /// a new device.
        /// </summary>
        public const string UpdateGardenState = "UPDATE_GARDEN_STATE";

        /// <summary>
        /// Flutter's answer to <see cref="RequestSharePhoto"/> / <see cref="RequestSavePhoto"/>, sent once
        /// the share sheet closes or the gallery write finishes. Payload:
        /// <see cref="PhotoActionResultPayload"/>. Optional — the game only uses it to tell the player
        /// what happened, and says nothing if it never arrives.
        /// </summary>
        public const string PhotoActionResult = "PHOTO_ACTION_RESULT";

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

        /// <summary>
        /// Asks Flutter to show a rewarded ad. Flutter owns the ad SDK and the consent (UMP) flow, so
        /// the game only says *when* an ad should play — it never loads or renders one itself.
        /// Payload: <see cref="RewardedAdRequestPayload"/>. Answered with <see cref="RewardedAdResult"/>.
        /// </summary>
        public const string RequestRewardedAd = "REQUEST_REWARDED_AD";

        /// <summary>
        /// Asks Flutter for the garden stored in Firebase. Sent once when the placement manager comes up
        /// with nothing cached. Payload: <see cref="EmptyPayload"/>. Answered with
        /// <see cref="UpdateGardenState"/> — including when the account has no garden yet, in which case
        /// <see cref="GardenStatePayload.hasData"/> is false and the game seeds Firebase from this device.
        /// </summary>
        public const string RequestGardenState = "REQUEST_GARDEN_STATE";

        /// <summary>
        /// The player's garden changed — write it to Firebase under their account. Sent a few seconds
        /// after any placement, move or return (so a burst of edits is one write), and immediately when
        /// the game is paused or closed. Payload: <see cref="GardenStatePayload"/>, always with
        /// <see cref="GardenStatePayload.hasData"/> true; an empty <c>items</c> array means the player
        /// really has emptied their garden and Firebase should record that.
        /// </summary>
        public const string SaveGardenState = "SAVE_GARDEN_STATE";

        /// <summary>
        /// The player tapped Share on a photo they took of their garden — open the native share sheet.
        /// Payload: <see cref="PhotoSharePayload"/>.
        ///
        /// The image travels as a <b>file path</b>, never as image data: the game writes a PNG into its
        /// own <c>persistentDataPath</c> (readable by the Flutter side of the same app) and sends where
        /// it put it. A full-screen screenshot is megabytes, and base64 through the message channel
        /// would stall both sides.
        /// </summary>
        public const string RequestSharePhoto = "REQUEST_SHARE_PHOTO";

        /// <summary>
        /// The player tapped Save on a photo — write it to the device's photo gallery. Same file-path
        /// contract as <see cref="RequestSharePhoto"/>. Payload: <see cref="PhotoSharePayload"/>.
        /// Flutter owns the gallery permission prompt; the game never asks for one.
        /// </summary>
        public const string RequestSavePhoto = "REQUEST_SAVE_PHOTO";
    }

    /// <summary>The action values <see cref="PhotoActionResultPayload.action"/> can carry.</summary>
    public static class PhotoAction
    {
        public const string Share = "share";
        public const string Save = "save";
    }

    /// <summary>The outcome values <see cref="RewardedAdResultPayload.status"/> can carry.</summary>
    public static class RewardedAdStatus
    {
        /// <summary>The player watched the ad through — grant whatever it was paying for.</summary>
        public const string Rewarded = "rewarded";

        /// <summary>The player closed the ad early. Not an error: no reward, but no complaint either.</summary>
        public const string Dismissed = "dismissed";

        /// <summary>The ad was loaded but failed to present.</summary>
        public const string Failed = "failed";

        /// <summary>No ad was loaded when the request arrived.</summary>
        public const string Unavailable = "unavailable";
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

    /// <summary>
    /// Unity → Flutter: please show a rewarded ad. <paramref name="source"/> says what the player is
    /// paying for (e.g. "treasure_box", "shop_item") so Flutter can attribute the impression.
    /// </summary>
    [Serializable]
    public class RewardedAdRequestPayload
    {
        public string source;
    }

    /// <summary>
    /// Flutter → Unity: how the rewarded ad ended. <see cref="status"/> is one of
    /// <see cref="RewardedAdStatus"/>; <see cref="source"/> echoes back the request's source.
    /// </summary>
    [Serializable]
    public class RewardedAdResultPayload
    {
        public string status;
        public string source;
    }

    /// <summary>Flutter → Unity: whether a rewarded ad is loaded and ready to show right now.</summary>
    [Serializable]
    public class AdAvailabilityPayload
    {
        public bool rewardedReady;
    }

    /// <summary>
    /// One asset standing in the player's garden.
    ///
    /// Position and rotation are flat floats rather than nested Vector3/Quaternion objects: this maps
    /// straight onto a Firestore document without a custom converter on the Flutter side, and it keeps
    /// JsonUtility (which is fussy about nesting) out of trouble in both directions.
    /// </summary>
    [Serializable]
    public class GardenItemPayload
    {
        /// <summary>Stable id for this placement, so a move updates a row instead of adding one.</summary>
        public string uniqueId;

        /// <summary>Name of the prefab to respawn. Must match a prefab the game can reach — do not rewrite it.</summary>
        public string prefabName;

        public float posX;
        public float posY;
        public float posZ;

        public float rotX;
        public float rotY;
        public float rotZ;
        public float rotW;

        /// <summary>Seconds of growth left when the snapshot was taken. The game ages it by the time since.</summary>
        public float remainingDuration;

        /// <summary>How long this item takes to fully grow, start to finish.</summary>
        public float totalDuration;

        /// <summary>The shop/inventory item this was placed from, used when the player returns it.</summary>
        public string sourceItemId;

        /// <summary>0 = unknown, 1 = shop item, 2 = inventory (treasure box) item. See <c>PlacedItemSource</c>.</summary>
        public int sourceKind;
    }

    /// <summary>
    /// The whole garden, in both directions: Unity → Flutter to be written to Firebase
    /// (<see cref="FlutterCommands.SaveGardenState"/>), and Flutter → Unity to be restored
    /// (<see cref="FlutterCommands.UpdateGardenState"/>).
    /// </summary>
    [Serializable]
    public class GardenStatePayload
    {
        /// <summary>
        /// False only from Flutter, meaning "this account has no garden stored yet". The game then keeps
        /// whatever is on the device and seeds Firebase from it. An account whose garden is genuinely
        /// empty should send true with an empty <see cref="items"/> array instead.
        /// </summary>
        public bool hasData;

        /// <summary>
        /// When this snapshot was taken (Unix seconds, UTC). Drives two things: how much growth to
        /// fast-forward while the player was away, and which of the device's and Firebase's copies is
        /// newer. Flutter should replace it with a server timestamp when it can — the value written here
        /// is the device's own clock, and a badly-set clock otherwise always wins the comparison.
        /// </summary>
        public long savedAtUnix;

        /// <summary>Save counter, used only to break a tie when both copies report the same second.</summary>
        public int revision;

        public GardenItemPayload[] items;
    }

    /// <summary>
    /// Unity → Flutter: a photo the player took of their garden, ready to share or save.
    /// </summary>
    [Serializable]
    public class PhotoSharePayload
    {
        /// <summary>
        /// Absolute path to a PNG inside the app's own storage. Already written and closed by the time
        /// this arrives, so it can be read immediately.
        /// </summary>
        public string filePath;

        /// <summary>Suggested text for the share sheet. Ignored when saving to the gallery.</summary>
        public string caption;

        /// <summary>What prompted it, e.g. "photo_mode", for analytics.</summary>
        public string source;

        /// <summary>Pixel size of the image, in case the host wants to resize before sharing.</summary>
        public int width;
        public int height;
    }

    /// <summary>Flutter → Unity: how a share or save ended, so the game can tell the player.</summary>
    [Serializable]
    public class PhotoActionResultPayload
    {
        /// <summary>One of <see cref="PhotoAction"/>.</summary>
        public string action;

        public bool success;

        /// <summary>Optional message shown to the player when <see cref="success"/> is false.</summary>
        public string message;
    }

    /// <summary>Placeholder for commands that carry no data (JsonUtility cannot serialize null).</summary>
    [Serializable]
    public class EmptyPayload
    {
    }
}
