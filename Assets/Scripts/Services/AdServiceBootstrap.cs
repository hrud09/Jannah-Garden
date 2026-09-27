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
///   2. In its Integration Manager, install the Unity Ads, Meta Audience Network, and InMobi adapters.
///      Do NOT install the AdMob adapter — see the crash notes on <see cref="LevelPlayAdService"/>.
///   3. Add LEVELPLAY_ENABLED to the scripting define symbols for Android and iOS.
///   4. Replace the consent value below with the host's real answer.
///
/// The API used in <see cref="LevelPlayAdService"/> is checked against Ads Mediation 8.9. If you install
/// an older 8.x, note that 8.7 renamed the namespace — the using at the top of that file says which.
///
/// OPEN: CONSENT
/// -------------
/// The Flutter host owns the GDPR/UMP consent flow today, and <c>FlutterCommands</c> has no command to
/// pass the outcome across. Until that wire exists this bootstrap has no real consent value to give the
/// SDK, so it assumes none was granted — the conservative reading, and worth lower revenue rather than
/// serving non-consented ads in the EU. Either add an UPDATE_CONSENT bridge command or move the consent
/// flow into Unity along with the SDK, then feed the answer in here.
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

    // Start, not Awake: TreasureBoxManager assigns its Instance in its own Awake, and the relative order
    // of two Awakes is not defined. Every Awake runs before any Start, so by here the manager exists.
    private void Start()
    {
#if LEVELPLAY_ENABLED
        // Placeholder: see "OPEN: CONSENT" above. This must become the host's real decision before
        // shipping to a GDPR region.
        const bool consentGranted = false;

        LevelPlayAdService service = LevelPlayAdService.Instance;
        service.Initialize(consentGranted, this);
        Service = service;

        Debug.Log("[AdServiceBootstrap] Rewarded ads are served by LevelPlay.");
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
