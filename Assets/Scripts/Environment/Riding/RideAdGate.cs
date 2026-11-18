using System;
using UnityEngine;

/// <summary>
/// Decides whether climbing onto a camel or pegasus asks for a rewarded ad first. Rides run in cycles of
/// four, counted across both animals and kept between sessions:
/// <list type="number">
/// <item>No popup and no ad: straight on.</item>
/// <item>and 3. A popup offering an ad, with "Ride Now" right beside it. Either way the player rides.</item>
/// <item>The ad is required. Closing the popup, or an ad that doesn't finish, means no ride (and the
/// next try is still the fourth).</item>
/// </list>
/// The fifth ride starts the cycle again. Only rides that actually start move the count on.
/// </summary>
public static class RideAdGate
{
    private const string CountKey = "RideAdGate.Count";
    private const int CycleLength = 4;

    /// <summary>Where the next ride falls in the cycle: 0 free, 1–2 ad offered, 3 ad required.</summary>
    public static int NextStep => Mathf.Abs(PlayerPrefs.GetInt(CountKey, 0)) % CycleLength;

    /// <summary>
    /// Runs the gate for riding <paramref name="animal"/>, then <paramref name="mount"/> if the ride goes
    /// ahead. <paramref name="mount"/> returns whether the player actually got on.
    /// </summary>
    public static void Request(GrazingAnimal animal, Func<bool> mount)
    {
        if (animal == null || mount == null) return;

        int step = NextStep;
        RidePromptPanel panel = RideFeatures.Instance != null ? RideFeatures.Instance.promptPanel : null;

        // No popup in this scene: never block riding on UI that isn't there.
        if (step == 0 || panel == null)
        {
            Ride(mount);
            return;
        }

        Sprite icon = RideFeatures.Instance.IconFor(animal);
        string name = RideFeatures.NameFor(animal);

        if (step < CycleLength - 1)
        {
            panel.Show(
                RideFeatures.Loc("ride.ad_optional_title"),
                RideFeatures.Loc("ride.ad_optional_body", name),
                RideFeatures.Loc("ride.ad_optional_note"),
                icon,
                RideFeatures.Loc("ride.watch_ad"), true,
                // An optional ad that doesn't finish still lets them ride: it was a favour, not a fee.
                () => ShowAd(_ => Ride(mount)),
                RideFeatures.Loc("ride.ride_now"), () => Ride(mount),
                null);
            return;
        }

        panel.Show(
            RideFeatures.Loc("ride.ad_required_title"),
            RideFeatures.Loc("ride.ad_required_body", name),
            RideFeatures.Loc("ride.ad_required_note"),
            icon,
            RideFeatures.Loc("ride.watch_ad"), true,
            () => ShowAd(earned =>
            {
                if (earned) Ride(mount);
                else RideFeatures.Toast(RideFeatures.Loc("ride.ad_failed"), ToastTone.Warning);
            }),
            null, null,
            null);
    }

    private static void Ride(Func<bool> mount)
    {
        if (!mount()) return;
        PlayerPrefs.SetInt(CountKey, (NextStep + 1) % CycleLength);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Plays a rewarded ad through the game's ad service (LevelPlay on device, the fake ad panel in the
    /// Editor). <paramref name="done"/> hears whether it was watched through.
    /// </summary>
    public static void ShowAd(Action<bool> done)
    {
        IAdService ads = AdServiceBootstrap.Service;
        if (ads == null)
        {
            done?.Invoke(true);
            return;
        }
        ads.ShowRewardedAd(earned => done?.Invoke(earned));
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Jannah/Animals/Reset Ride Ad Cycle")]
    private static void ResetCycle()
    {
        PlayerPrefs.DeleteKey(CountKey);
        Debug.Log("[RideAdGate] Ride count reset: the next ride is free.");
    }
#endif
}
