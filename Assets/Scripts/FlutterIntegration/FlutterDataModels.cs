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
        /// Flutter tells the game which language to display. Payload: <see cref="LocalePayload"/>, whose
        /// <c>localeCode</c> is a short code ("en", "ar", "bn") — longer forms like "ar-SA" are also
        /// accepted and normalised. Sent whenever the app's language changes, and ideally once right after
        /// <see cref="UnityReady"/> so the game doesn't briefly show its cached/default language first.
        /// LocalizationManager falls back to English for any locale it doesn't recognise.
        /// </summary>
        public const string SetLocale                 = "SET_LOCALE";

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
        /// Flutter reports whether the LevelPlay SDK is initialised in this process.
        ///
        /// The game and the Flutter host share one APK, one process, and one LevelPlay app key
        /// (<c>285c611cd</c> on Android). LevelPlay may only be initialised <b>once</b> per process, and
        /// the host owns that call — it runs first and it owns the GDPR/UMP consent flow whose answer has
        /// to be set before init. The game therefore never calls <c>LevelPlay.Init</c>; it waits for this
        /// message and only then creates its own rewarded ad object, on its own ad unit.
        ///
        /// Push this unprompted as soon as init succeeds or fails, and answer
        /// <see cref="RequestAdSdkState"/> with it. Payload: <see cref="AdSdkStatePayload"/>.
        /// </summary>
        public const string UpdateAdSdkState = "UPDATE_AD_SDK_STATE";

        /// <summary>
        /// Flutter hands over the signed-in player's Firebase login — uid and ID token — so the game can
        /// read and write their saves in Firestore itself (see <c>CloudSave</c>). Send it in answer to
        /// <see cref="RequestAuthToken"/>, and push it unprompted whenever the token refreshes or the
        /// account changes (FirebaseAuth's <c>idTokenChanges()</c> stream does both). Empty fields mean
        /// nobody is signed in. Payload: <see cref="AuthTokenPayload"/>.
        /// </summary>
        public const string UpdateAuthToken = "UPDATE_AUTH_TOKEN";

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
        /// Asks Flutter whether LevelPlay is initialised yet. Sent once when the game's ad service starts.
        ///
        /// Needed because the host almost always finishes initialising before the Unity scene loads, so
        /// the unprompted <see cref="UpdateAdSdkState"/> would have been broadcast while nothing was
        /// listening. Without this the game would wait forever for an event that already happened.
        /// Payload: <see cref="EmptyPayload"/>. Answered with <see cref="UpdateAdSdkState"/>.
        /// </summary>
        public const string RequestAdSdkState = "REQUEST_AD_SDK_STATE";

        /// <summary>
        /// Asks Flutter for the signed-in player's Firebase uid and a fresh ID token. Sent when the game
        /// starts or resumes, and whenever the current token is about to expire (they last an hour).
        /// Payload: <see cref="EmptyPayload"/>. Answered with <see cref="UpdateAuthToken"/>.
        /// </summary>
        public const string RequestAuthToken = "REQUEST_AUTH_TOKEN";

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

    /// <summary>Flutter → Unity: the app's active language. See <see cref="FlutterCommands.SetLocale"/>.</summary>
    [Serializable]
    public class LocalePayload
    {
        public string localeCode;
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

        /// <summary>
        /// How many fellows the player really has. Flutter caps the roster it sends (the server returns
        /// at most 500 members per call), so <c>fellows.Length</c> is a page, not a total. 0 means
        /// "not sent" — the game then treats the array length as the total.
        /// </summary>
        public int totalCount;

        /// <summary>How many of them the player invited personally (level 1). 0 when not sent.</summary>
        public int directCount;

        /// <summary>True when <see cref="fellows"/> was cut short by the server's cap.</summary>
        public bool truncated;
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
    /// Flutter → Unity: whether the host has finished initialising LevelPlay in this process.
    /// <see cref="message"/> is optional and only used for logging when <see cref="initialized"/> is
    /// false, so a failed host init shows up in the game's logs instead of looking like a silent hang.
    /// </summary>
    [Serializable]
    public class AdSdkStatePayload
    {
        public bool initialized;
        public string message;
    }

    /// <summary>
    /// Flutter → Unity: who is signed in. See <see cref="FlutterCommands.UpdateAuthToken"/>.
    /// </summary>
    [Serializable]
    public class AuthTokenPayload
    {
        /// <summary>Firebase Auth uid. Empty when nobody is signed in.</summary>
        public string uid;

        /// <summary>
        /// A current Firebase ID token for that user (<c>user.getIdToken()</c>). The game sends it with
        /// every Firestore request, so the database's security rules see the player as themselves.
        /// </summary>
        public string idToken;
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
