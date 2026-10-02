using UnityEngine;

/// <summary>
/// Chooses the rewarded-ad implementation at startup and hands it to <see cref="TreasureBoxManager"/>.
///
/// Every ad surface in the game — treasure boxes, the shop's "watch an ad" item, the interaction prompt —
/// is meant to go through <see cref="TreasureBoxManager.AdService"/>. Keeping the choice here means there
/// is one place that knows which network is live, instead of a per-platform #if at each call site (which
/// is what the treasure box panel used to do, and how it ended up able to play two ads for one box).
///
/// TURNING ON LEVELPLAY
/// --------------------
/// The SDK is not in the project yet, so this registers nothing and the game keeps
/// <see cref="NullAdService"/> — rewards are granted without an ad, which is the current dev behaviour.
/// To go live:
///   1. Package Manager → Unity Registry → "Ads Mediation" (`com.unity.services.levelplay`), install the
///      latest verified version. Accept the dependency-resolver prompt.
///   2. In its Integration Manager, install the Unity Ads and InMobi adapters, and nothing else.
///      Do NOT install the AdMob, AppLovin, or Meta adapters — AdMob breaks the Flutter host's build
///      outright, and all three are undeclared in the host app's privacy policy and Play Data safety
///      declaration. See the crash notes on <see cref="LevelPlayAdService"/> and MONETIZATION.md.
///   3. Add LEVELPLAY_ENABLED to the scripting define symbols for Android and iOS.
///   4. Make sure the Flutter host initialises LevelPlay and sends UPDATE_AD_SDK_STATE — the game
///      shows no ads until it does. See "CONSENT AND INITIALISATION" below.
///
/// The API used in <see cref="LevelPlayAdService"/> is checked against Ads Mediation 8.9. If you install
/// an older 8.x, note that 8.7 renamed the namespace — the using at the top of that file says which.
///
/// CONSENT AND INITIALISATION — OWNED BY THE HOST
/// ----------------------------------------------
/// The game ships inside the Amal Flutter app: one APK, one process, one LevelPlay app key. The host
/// initialises LevelPlay and answers the GDPR/UMP consent question; the game does neither. It waits for
/// <c>UPDATE_AD_SDK_STATE</c> over <see cref="FlutterIntegration.FlutterBridge"/> and then builds its own
/// rewarded ad on the SDK the host already has running, using the game's own ad unit so its revenue still
/// reports separately.
///
/// This replaced a hardcoded <c>consentGranted = true</c> that could overwrite a player's "no" from
/// Flutter's consent dialog, since both sides write to the same SDK singleton and the last writer wins.
/// </summary>
public class AdServiceBootstrap : MonoBehaviour
{
    /// <summary>
    /// The live rewarded-ad service, for callers that are not <see cref="TreasureBoxManager"/>.
    ///
    /// The manager keeps its own injectable <c>AdService</c> property — that seam exists so tests can
    /// swap the service, and removing it would be a step backwards. But the shop and the interaction
    /// prompt have no business reaching through the treasure box manager to find an ad, so they read
    /// this instead. Both are assigned from the same object below, so they cannot disagree.
    ///
    /// Never null: falls back to <see cref="NullAdService"/> until <see cref="Start"/> runs, so a caller
    /// that asks for an ad during another object's Awake gets the no-op rather than a crash.
    /// </summary>
    public static IAdService Service { get; private set; } = new NullAdService();

    [Tooltip("Turns on LevelPlay adapter debug logging and the integration check on device builds. " +
             "Their logcat output is what tells you why a network returns no fill — a missing adapter, " +
             "a wrong app key, or a device the LevelPlay dashboard has not been told is a test device. " +
             "Turn this off for release builds.")]
    [SerializeField] private bool enableAdTestMode = true;

    [Tooltip("Grant the reward anyway when no ad can be shown — SDK not initialised, no network fill, or " +
             "a display failure. Keeps an unsold or broken ad slot from locking players out of treasure " +
             "boxes. Does NOT apply when the player opens an ad and skips it; that still earns nothing. " +
             "Turn this off once ads fill reliably, or the game pays out for impressions it never served.")]
    [SerializeField] private bool grantRewardWhenAdUnavailable = true;

    [Tooltip("How many times a day the setting above may actually fire. Without a cap, a region or an " +
             "account that never gets fill opens every treasure box for free forever. With one, a player " +
             "is still never hard-blocked by a broken ad slot, but a permanent no-fill costs a bounded " +
             "amount. Set to 0 to refuse every no-ad reward without turning the bypass off outright.")]
    [Min(0)]
    [SerializeField] private int maxBypassGrantsPerDay = 3;

    // Start, not Awake: TreasureBoxManager assigns its Instance in its own Awake, and the relative order
    // of two Awakes is not defined. Every Awake runs before any Start, so by here the manager exists.
    private void Start()
    {
#if UNITY_EDITOR
        // LevelPlay's own Editor mock cannot be constructed in this project — see EditorFakeAdService for
        // the prefab-path reason. Initialising it here would throw out of Start() before Service was ever
        // assigned, leaving the reward-granting NullAdService behind and no ad on screen.
        Service = new EditorFakeAdService();
        Debug.Log("[AdServiceBootstrap] Editor session — rewarded ads use the fake ad panel. In a device "
            + $"build LevelPlay test mode would be {(enableAdTestMode ? "on" : "off")} and the "
            + $"no-ad reward bypass {(grantRewardWhenAdUnavailable ? "on" : "off")}, capped at "
            + $"{maxBypassGrantsPerDay} a day.");
#elif LEVELPLAY_ENABLED
        // Consent is deliberately not passed in any more. The Flutter host owns the GDPR/UMP flow and
        // sets the answer on the SDK before it initialises it. Setting it again from here would race the
        // host in the same process, and the game — which never asked the player anything — could
        // overwrite a "no" with a hardcoded "yes". Whoever asks the question owns the answer.
        LevelPlayAdService service = LevelPlayAdService.Instance;

        // Assign before initialising. Initialize() reaches into the native SDK, and anything it throws
        // would otherwise skip the assignment and silently leave NullAdService — which reports an ad as
        // ready and hands out the reward without showing one, making a broken SDK look like a working game.
        Service = service;
        service.GrantRewardWhenAdUnavailable = grantRewardWhenAdUnavailable;
        service.MaxBypassGrantsPerDay = maxBypassGrantsPerDay;

        try
        {
            service.Initialize(this, enableAdTestMode);
            Debug.Log("[AdServiceBootstrap] Rewarded ads are served by LevelPlay, on the SDK instance the "
                + "Flutter host initialises. Ads stay unavailable until the host announces it is up.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AdServiceBootstrap] LevelPlay failed to initialise: {e}");
        }
#else
        // Leave the NullAdService in place: it reports ads as ready and grants the reward, which keeps
        // treasure boxes openable in the Editor and in builds made before the SDK lands. Registering the
        // LevelPlay stub instead would report "not ready" and lock the boxes.
        Debug.Log("[AdServiceBootstrap] LEVELPLAY_ENABLED is not set — using the no-op ad service.");
#endif

        if (TreasureBoxManager.Instance != null)
        {
            TreasureBoxManager.Instance.AdService = Service;
        }
        else
        {
            Debug.LogWarning("[AdServiceBootstrap] No TreasureBoxManager in the scene — treasure boxes "
                + "will use their own default ad service.");
        }
    }
}
