using System;
using System.Collections.Generic;
using System.Text;
using GameAnalyticsSDK;
using UnityEngine;

/// <summary>
/// The game's one door into GameAnalytics. Gameplay code calls the typed methods here
/// (<see cref="DhikrCompleted"/>, <see cref="ItemPlaced"/>, …) and never the SDK directly, so that:
///
/// • every event id is built and sanitised in one place — GameAnalytics silently drops any id part
///   that is empty, longer than 64 characters, or contains anything outside
///   <c>[A-Za-z0-9 -_.()!?]</c>, and a dropped event is invisible until someone wonders why a
///   dashboard is empty;
/// • the resource currencies and item types the SDK requires are registered before it starts
///   (an unregistered one is rejected — which is why the old balance-diff coin events never landed);
/// • no analytics call can throw into gameplay.
///
/// Naming: design ids are <c>Area:Action[:Detail…]</c>, PascalCase, at most five parts. Free-form
/// detail (question ids, item ids, timings) goes in custom fields rather than the id, so the number
/// of distinct ids stays small enough for the dashboard to aggregate.
///
/// Turn on <see cref="LogToConsole"/> (on by default in the Editor) to watch every event go out.
/// </summary>
public static class GameEvents
{
    // ─── Registered resource types ────────────────────────────────────────────
    // GameAnalytics rejects any resource event whose currency or item type is not in these lists at
    // initialisation. EnsureInitialized merges them into the SDK settings, so adding a value here is
    // all a new coin source needs.

    public const string CurrencyNoorCoin = "NoorCoin";

    public static class CoinSource
    {
        public const string Quiz = "Quiz";
        public const string Dhikr = "Dhikr";
        public const string TreasureBox = "TreasureBox";
        public const string ShopReward = "ShopReward";
        public const string Refund = "Refund";
        public const string Other = "Other";
    }

    public static class CoinSink
    {
        public const string ShopPurchase = "ShopPurchase";
        public const string Other = "Other";
    }

    private static readonly string[] Currencies = { CurrencyNoorCoin };

    private static readonly string[] ItemTypes =
    {
        CoinSource.Quiz, CoinSource.Dhikr, CoinSource.TreasureBox, CoinSource.ShopReward,
        CoinSource.Refund, CoinSource.Other, CoinSink.ShopPurchase,
    };

    // ─── Ad placements ────────────────────────────────────────────────────────

    public static class AdPlacement
    {
        public const string TreasureBox = "treasure_box";
        public const string ShopItem = "shop_item";
        public const string Unknown = "unknown";
    }

    public static class AdSdk
    {
        public const string LevelPlay = "levelplay";
        public const string HouseAd = "house_ad";
        public const string Flutter = "flutter";
        public const string FakeAd = "fake_ad";
    }

    /// <summary>Echo every event to the Unity console. On in the Editor so a play session can be audited.</summary>
    public static bool LogToConsole = Application.isEditor;

    private static bool _initialized;

    /// <summary>
    /// Starts the SDK if nothing has yet. Safe to call repeatedly and from any scene, so an event
    /// fired before <see cref="GameAnalyticsManager"/> wakes (the Init scene, the loading screen) is
    /// not lost.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        if (!Application.isPlaying) return;

        try
        {
            if (!GameAnalytics.Initialized)
            {
                RegisterResourceTypes();
                GameAnalytics.Initialize();
            }
            _initialized = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameEvents] GameAnalytics failed to initialise: {e}");
        }
    }

    private static void RegisterResourceTypes()
    {
        var settings = GameAnalytics.SettingsGA;
        if (settings == null) return;

        foreach (string c in Currencies)
            if (!settings.ResourceCurrencies.Contains(c)) settings.ResourceCurrencies.Add(c);

        foreach (string t in ItemTypes)
            if (!settings.ResourceItemTypes.Contains(t)) settings.ResourceItemTypes.Add(t);
    }

    /// <summary>
    /// Fields GameAnalytics stamps onto every later event — the player's context at the time, so any
    /// event can be split by level, language or subscriber status without each call site passing it.
    /// </summary>
    public static void SetGlobalContext(int xpLevel, string locale, bool isSubscriber)
    {
        EnsureInitialized();
        Safe(() => GameAnalytics.SetGlobalCustomEventFields(new Dictionary<string, object>
        {
            { "xp_level", xpLevel },
            { "locale", locale ?? "unknown" },
            { "subscriber", isSubscriber ? 1 : 0 },
        }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Session & time
    // ═════════════════════════════════════════════════════════════════════════

    public static void GameStarted(bool firstLaunch, float coldStartSeconds)
    {
        Design(new[] { "Game", "Start", firstLaunch ? "FirstLaunch" : "Returning" }, coldStartSeconds);
    }

    /// <summary>Foreground time since the last pause, sent each time the app goes to the background.</summary>
    public static void SessionSegment(float seconds, float lifetimeMinutes)
    {
        Design(new[] { "Session", "ForegroundSeconds" }, seconds,
            Fields("lifetime_minutes", Mathf.RoundToInt(lifetimeMinutes)));
    }

    public static void PlaytimeMilestone(int minutes)
    {
        Design(new[] { "Playtime", "Milestone", $"{minutes}min" }, minutes);
    }

    public static void GamePaused() => Design(new[] { "Game", "Pause" });
    public static void GameResumed(float secondsAway) => Design(new[] { "Game", "Resume" }, secondsAway);
    public static void GameQuit(float sessionSeconds) => Design(new[] { "Game", "Quit" }, sessionSeconds);

    public static void SceneLoaded(string sceneName, float secondsSinceLaunch)
    {
        Design(new[] { "Scene", "Loaded", sceneName }, secondsSinceLaunch);
    }

    /// <summary>How long the player spent inside a feature (shop, photo mode, settings, …).</summary>
    public static void FeatureTime(string feature, float seconds)
    {
        Design(new[] { "Time", feature }, seconds);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Rewarded ads
    // ═════════════════════════════════════════════════════════════════════════

    private static string _adPlacement = AdPlacement.Unknown;
    private static float _adShownAt = -1f;

    /// <summary>
    /// The player asked for a rewarded ad. Records the placement so the SDK-level events that follow
    /// (show, reward, failure) — which fire deep in the ad service with no idea why the ad was asked
    /// for — are attributed to it.
    /// </summary>
    public static void AdRequested(string placement, string sdk = AdSdk.LevelPlay)
    {
        _adPlacement = string.IsNullOrEmpty(placement) ? AdPlacement.Unknown : placement;
        Ad(GAAdAction.Request, sdk);
    }

    public static void AdLoaded(string sdk = AdSdk.LevelPlay, string network = null)
    {
        EnsureInitialized();
        Safe(() => GameAnalytics.NewAdEvent(GAAdAction.Loaded, GAAdType.RewardedVideo, sdk, "preload",
            Fields("network", network ?? "unknown")));
        LogDesign($"Ad:Loaded ({sdk}, {network})");
    }

    public static void AdLoadFailed(string sdk, string error, int consecutiveFailures)
    {
        Design(new[] { "Ads", "LoadFailed", sdk }, consecutiveFailures, Fields("error", Truncate(error, 200)));
    }

    /// <param name="revenue">Impression revenue in USD as reported by mediation, when it reports one.</param>
    public static void AdShown(string sdk = AdSdk.LevelPlay, string network = null, double? revenue = null)
    {
        _adShownAt = Time.realtimeSinceStartup;
        var fields = Fields("network", network ?? "unknown");
        if (revenue.HasValue) fields["revenue_usd"] = revenue.Value;
        Ad(GAAdAction.Show, sdk, fields);
    }

    public static void AdClicked(string sdk = AdSdk.LevelPlay, string network = null)
    {
        Ad(GAAdAction.Clicked, sdk, Fields("network", network ?? "unknown"));
    }

    public static void AdRewarded(string sdk = AdSdk.LevelPlay)
    {
        long ms = _adShownAt >= 0f ? (long)((Time.realtimeSinceStartup - _adShownAt) * 1000f) : 0;
        EnsureInitialized();
        string placement = _adPlacement;
        Safe(() => GameAnalytics.NewAdEvent(GAAdAction.RewardReceived, GAAdType.RewardedVideo, sdk, placement, ms));
        LogDesign($"Ad:RewardReceived ({sdk}, {placement}, {ms}ms)");
        _adShownAt = -1f;
    }

    /// <summary>The player closed an ad they were shown before earning the reward.</summary>
    public static void AdSkipped(string sdk = AdSdk.LevelPlay)
    {
        float watched = _adShownAt >= 0f ? Time.realtimeSinceStartup - _adShownAt : 0f;
        Design(new[] { "Ads", "Skipped", _adPlacement }, watched, Fields("sdk", sdk));
        _adShownAt = -1f;
    }

    /// <summary>We could not put an ad on screen.</summary>
    public static void AdFailedToShow(string sdk, GAAdError reason, string detail = null)
    {
        EnsureInitialized();
        string placement = _adPlacement;
        Safe(() => GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.RewardedVideo, sdk, placement, reason,
            Fields("detail", Truncate(detail ?? "", 200))));
        LogDesign($"Ad:FailedShow ({sdk}, {placement}, {reason}, {detail})");
    }

    /// <summary>No network ad, so the "we could not show you an ad" path ran. <paramref name="outcome"/>: HouseAd / BypassGranted / BypassDenied.</summary>
    public static void AdFallback(string outcome)
    {
        Design(new[] { "Ads", "Fallback", outcome, _adPlacement });
    }

    public static void AdTimedOut(string sdk) => Design(new[] { "Ads", "Timeout", sdk, _adPlacement });

    private static void Ad(GAAdAction action, string sdk, IDictionary<string, object> fields = null)
    {
        EnsureInitialized();
        string placement = _adPlacement;
        Safe(() =>
        {
            if (fields != null) GameAnalytics.NewAdEvent(action, GAAdType.RewardedVideo, sdk, placement, fields);
            else GameAnalytics.NewAdEvent(action, GAAdType.RewardedVideo, sdk, placement);
        });
        LogDesign($"Ad:{action} ({sdk}, {placement})");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Dhikr
    // ═════════════════════════════════════════════════════════════════════════

    public static void DhikrStarted(int dhikrIndex, int targetCount, bool fromOrb)
    {
        Design(new[] { "Dhikr", "Started" }, targetCount,
            Fields("dhikr_index", dhikrIndex, "target_count", targetCount, "from_orb", fromOrb ? 1 : 0));
    }

    public static void DhikrCompleted(int dhikrIndex, int targetCount, float seconds, int lifetimeCompleted)
    {
        Design(new[] { "Dhikr", "Completed" }, seconds,
            Fields("dhikr_index", dhikrIndex, "target_count", targetCount, "lifetime_completed", lifetimeCompleted));
        // Total recitations, so "how many dhikr did players say today" is one sum in the dashboard.
        Design(new[] { "Dhikr", "Recitations" }, targetCount);
    }

    public static void DhikrAbandoned(int dhikrIndex, int count, int targetCount, float seconds)
    {
        Design(new[] { "Dhikr", "Abandoned" }, targetCount > 0 ? (float)count / targetCount : 0f,
            Fields("dhikr_index", dhikrIndex, "count", count, "target_count", targetCount, "seconds", Round1(seconds)));
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Quiz (MCQ)
    // ═════════════════════════════════════════════════════════════════════════

    public static void QuizStarted(string questionId, int questionLevel, string category, bool fromOrb)
    {
        Design(new[] { "Quiz", "Started", $"Level_{questionLevel}" }, questionLevel,
            Fields("question_id", questionId ?? "", "category", category ?? "", "from_orb", fromOrb ? 1 : 0));
    }

    public static void QuizAnswered(bool correct, int attempt, string questionId, int questionLevel,
        string category, float secondsToAnswer)
    {
        Design(new[] { "Quiz", "Answer", correct ? "Correct" : "Wrong", $"Level_{questionLevel}" }, attempt,
            Fields("question_id", questionId ?? "", "category", category ?? "", "seconds", Round1(secondsToAnswer)));
    }

    public static void QuizCompleted(int attempts, string questionId, int questionLevel, float seconds, int lifetimeCorrect)
    {
        Design(new[] { "Quiz", "Completed", attempts == 1 ? "FirstTry" : "Retry" }, seconds,
            Fields("question_id", questionId ?? "", "level", questionLevel, "attempts", attempts,
                "lifetime_correct", lifetimeCorrect));
    }

    public static void QuizAbandoned(int attempts, string questionId, int questionLevel, float seconds)
    {
        Design(new[] { "Quiz", "Abandoned" }, seconds,
            Fields("question_id", questionId ?? "", "level", questionLevel, "attempts", attempts));
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Placement & garden
    // ═════════════════════════════════════════════════════════════════════════

    public static void PlacementStarted(string source, string itemId, string category)
    {
        Design(new[] { "Placement", "Started", source }, Fields("item_id", itemId ?? "", "category", category ?? ""));
    }

    public static void PlacementDownloadFailed(string itemId)
    {
        Design(new[] { "Placement", "DownloadFailed" }, Fields("item_id", itemId ?? ""));
    }

    /// <summary>A new item went into the garden. <paramref name="gardenTotal"/> is the item count afterwards.</summary>
    public static void ItemPlaced(string source, string itemId, string category, int gardenTotal, int lifetimePlaced)
    {
        Design(new[] { "Placement", "Placed", source, category ?? "Unknown" }, gardenTotal,
            Fields("item_id", itemId ?? "", "lifetime_placed", lifetimePlaced));
    }

    public static void ItemMoved(string itemId) => Design(new[] { "Placement", "Moved" }, Fields("item_id", itemId ?? ""));
    public static void PlacementCancelled(bool wasRelocation, string itemId) =>
        Design(new[] { "Placement", "Cancelled", wasRelocation ? "Relocation" : "New" }, Fields("item_id", itemId ?? ""));

    public static void ItemReturnedToStore(string source, string itemId, int refund, int gardenTotal)
    {
        Design(new[] { "Placement", "Returned", source }, gardenTotal, Fields("item_id", itemId ?? "", "refund", refund));
    }

    /// <summary>Snapshot of the garden at session start.</summary>
    public static void GardenSnapshot(int itemCount)
    {
        Design(new[] { "Garden", "ItemCount" }, itemCount);
    }

    public static void AreaUnlocked(int sequence, string areaId)
    {
        Design(new[] { "Garden", "AreaUnlocked", $"Area_{sequence}" }, sequence, Fields("area_id", areaId ?? ""));
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Shop & economy
    // ═════════════════════════════════════════════════════════════════════════

    public static void ShopOpened() => Design(new[] { "Shop", "Opened" });

    public static void ShopCategorySelected(string category) => Design(new[] { "Shop", "Category", category });

    /// <summary><paramref name="method"/>: Coins / Ad / IAP / Free / Inventory.</summary>
    public static void ShopItemAcquired(string method, string itemId, string category, int price)
    {
        Design(new[] { "Shop", "Acquired", method, category ?? "Unknown" }, price, Fields("item_id", itemId ?? ""));
    }

    /// <summary><paramref name="reason"/>: LevelLocked / NotEnoughCoins / AdUnavailable / OnCooldown / Busy.</summary>
    public static void ShopBlocked(string reason, string itemId)
    {
        Design(new[] { "Shop", "Blocked", reason }, Fields("item_id", itemId ?? ""));
    }

    public static void CoinStoreOpened(bool fromUnaffordableItem)
    {
        Design(new[] { "Shop", "CoinStoreOpened", fromUnaffordableItem ? "Unaffordable" : "Manual" });
    }

    public static void CoinsEarned(int amount, string source, string itemId)
    {
        Resource(GAResourceFlowType.Source, amount, source, itemId);
    }

    public static void CoinsSpent(int amount, string sink, string itemId)
    {
        Resource(GAResourceFlowType.Sink, amount, sink, itemId);
    }

    /// <summary>
    /// Real-money purchases are charged by the Flutter host, which owns the receipt and should send the
    /// GameAnalytics business event. These design events cover the funnel from Unity's side.
    /// </summary>
    public static void IapStarted(string productId) => Design(new[] { "IAP", "Started" }, Fields("product_id", productId ?? ""));
    public static void IapSucceeded(string productId) => Design(new[] { "IAP", "Succeeded" }, Fields("product_id", productId ?? ""));
    public static void IapFailed(string productId, string message) =>
        Design(new[] { "IAP", "Failed" }, Fields("product_id", productId ?? "", "message", Truncate(message ?? "", 200)));

    // ═════════════════════════════════════════════════════════════════════════
    //  XP & progression
    // ═════════════════════════════════════════════════════════════════════════

    public static void XpGained(string task, float amount) => Design(new[] { "XP", "Gained", task }, amount);

    public static void LevelReached(int previousLevel, int newLevel)
    {
        EnsureInitialized();
        Safe(() =>
        {
            if (previousLevel > 0)
                GameAnalytics.NewProgressionEvent(GAProgressionStatus.Complete, "PlayerLevel", $"Level_{previousLevel:00}");
            GameAnalytics.NewProgressionEvent(GAProgressionStatus.Start, "PlayerLevel", $"Level_{newLevel:00}");
        });
        LogDesign($"Progression:PlayerLevel {previousLevel} -> {newLevel}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Treasure boxes
    // ═════════════════════════════════════════════════════════════════════════

    public static void TreasureBoxOpenRequested(string tier, int slot, bool subscriber)
    {
        Design(new[] { "TreasureBox", "OpenRequested", tier, subscriber ? "Subscriber" : "Free" }, slot);
    }

    public static void TreasureBoxOpened(string tier, int slot)
    {
        EnsureInitialized();
        Safe(() => GameAnalytics.NewProgressionEvent(GAProgressionStatus.Complete, "TreasureBox", tier, $"Slot_{slot + 1}"));
        Design(new[] { "TreasureBox", "Opened", tier }, slot + 1);
    }

    public static void TreasureBoxOpenFailed(string tier, string reason)
    {
        Design(new[] { "TreasureBox", "OpenFailed", tier }, Fields("reason", Truncate(reason ?? "", 200)));
    }

    public static void TreasureBoxSetCompleted(string tier) => Design(new[] { "TreasureBox", "SetCompleted", tier });

    public static void TreasureBoxTierPurchase(string tier, bool success) =>
        Design(new[] { "TreasureBox", "TierPurchase", tier, success ? "Success" : "Failed" });

    // ═════════════════════════════════════════════════════════════════════════
    //  Tutorial
    // ═════════════════════════════════════════════════════════════════════════

    public static void TutorialFlowStarted(string flow)
    {
        EnsureInitialized();
        Safe(() => GameAnalytics.NewProgressionEvent(GAProgressionStatus.Start, "Tutorial", Part(flow)));
        LogDesign($"Progression:Tutorial:{flow} Start");
    }

    public static void TutorialFlowCompleted(string flow, bool skipped)
    {
        EnsureInitialized();
        Safe(() => GameAnalytics.NewProgressionEvent(
            skipped ? GAProgressionStatus.Fail : GAProgressionStatus.Complete, "Tutorial", Part(flow)));
        LogDesign($"Progression:Tutorial:{flow} {(skipped ? "Skipped" : "Complete")}");
        if (skipped) Design(new[] { "Tutorial", "Skipped", flow });
    }

    public static void TutorialStep(string flow, string step, bool replay)
    {
        Design(new[] { "Tutorial", replay ? "ReplayStep" : "Step", flow, step });
    }

    public static void TutorialReplayStarted() => Design(new[] { "Tutorial", "ReplayStarted" });

    // ═════════════════════════════════════════════════════════════════════════
    //  Photo mode, navigation, settings, social
    // ═════════════════════════════════════════════════════════════════════════

    public static void PhotoTaken() => Design(new[] { "Photo", "Taken" });

    /// <summary><paramref name="action"/>: Share / Save.</summary>
    public static void PhotoAction(string action, bool? success = null)
    {
        string outcome = success == null ? "Requested" : (success.Value ? "Success" : "Failed");
        Design(new[] { "Photo", action, outcome });
    }

    public static void AutoWalkStarted(string destination) => Design(new[] { "Navigation", "AutoWalk", destination, "Started" });

    public static void AutoWalkEnded(string destination, string reason) =>
        Design(new[] { "Navigation", "AutoWalk", destination, reason });

    public static void InspectorModeToggled(bool on) => Design(new[] { "Navigation", "InspectorMode", on ? "On" : "Off" });

    public static void QuickActionDrawerToggled(bool open) => Design(new[] { "UI", "QuickActions", open ? "Open" : "Close" });

    public static void SettingChanged(string setting, string value) => Design(new[] { "Settings", setting, value });

    public static void LanguageChanged(string locale) => Design(new[] { "Settings", "Language", locale });

    public static void FellowsShown(int count) => Design(new[] { "Fellowship", "Shown" }, count);

    public static void IntroVideo(string outcome) => Design(new[] { "Intro", outcome });

    // ═════════════════════════════════════════════════════════════════════════
    //  Core
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Sends a design event built from <paramref name="parts"/> (each sanitised, at most five).</summary>
    public static void Design(string[] parts, IDictionary<string, object> fields = null) => Design(parts, null, fields);

    public static void Design(string[] parts, float? value, IDictionary<string, object> fields = null)
    {
        EnsureInitialized();
        string id = BuildEventId(parts);

        Safe(() =>
        {
            if (value.HasValue)
            {
                if (fields != null) GameAnalytics.NewDesignEvent(id, value.Value, fields);
                else GameAnalytics.NewDesignEvent(id, value.Value);
            }
            else
            {
                if (fields != null) GameAnalytics.NewDesignEvent(id, fields);
                else GameAnalytics.NewDesignEvent(id);
            }
        });

        if (LogToConsole) LogDesign(value.HasValue ? $"{id} = {value.Value:0.##}{FieldsSuffix(fields)}" : id + FieldsSuffix(fields));
    }

    private static void Resource(GAResourceFlowType flow, int amount, string itemType, string itemId)
    {
        if (amount <= 0) return;
        EnsureInitialized();

        string type = Array.IndexOf(ItemTypes, itemType) >= 0 ? itemType : CoinSource.Other;
        string id = Part(itemId);

        Safe(() => GameAnalytics.NewResourceEvent(flow, CurrencyNoorCoin, amount, type, id));
        if (LogToConsole) LogDesign($"Resource:{flow}:{CurrencyNoorCoin} {amount} ({type}/{id})");
    }

    internal static string BuildEventId(string[] parts)
    {
        var sb = new StringBuilder();
        int n = Mathf.Min(parts.Length, 5);
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(':');
            sb.Append(Part(parts[i]));
        }
        return sb.ToString();
    }

    /// <summary>Makes one id part acceptable to GameAnalytics: allowed characters only, 1–64 long.</summary>
    internal static string Part(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Unknown";

        var sb = new StringBuilder(Mathf.Min(raw.Length, 64));
        foreach (char ch in raw)
        {
            if (sb.Length >= 64) break;
            bool ok = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')
                      || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '(' || ch == ')'
                      || ch == '!' || ch == '?';
            sb.Append(ok ? ch : '_');
        }

        string s = sb.ToString().Trim();
        return s.Length == 0 ? "Unknown" : s;
    }

    private static IDictionary<string, object> Fields(params object[] kv)
    {
        var d = new Dictionary<string, object>(kv.Length / 2);
        for (int i = 0; i + 1 < kv.Length; i += 2)
        {
            object v = kv[i + 1];
            // GameAnalytics accepts strings and numbers only.
            if (v is bool b) v = b ? 1 : 0;
            else if (v is string str) v = Truncate(str, 256);
            d[(string)kv[i]] = v;
        }
        return d;
    }

    private static string FieldsSuffix(IDictionary<string, object> fields)
    {
        if (fields == null || fields.Count == 0) return "";
        var sb = new StringBuilder(" {");
        bool first = true;
        foreach (var kv in fields)
        {
            if (!first) sb.Append(", ");
            sb.Append(kv.Key).Append('=').Append(kv.Value);
            first = false;
        }
        return sb.Append('}').ToString();
    }

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);

    private static float Round1(float f) => Mathf.Round(f * 10f) / 10f;

    private static void LogDesign(string msg)
    {
        if (LogToConsole) Debug.Log($"[GameEvents] {msg}");
    }

    /// <summary>Analytics must never break gameplay: anything the SDK throws stops here.</summary>
    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Debug.LogWarning($"[GameEvents] Analytics call failed: {e.Message}"); }
    }
}
