using System.Collections.Generic;
using System.Text;
using FlutterIntegration;
using UnityEngine;

/// <summary>
/// Spawns fellow profile cards around the map at registered spawn points.
///
/// The roster comes from Flutter, via <see cref="FlutterBridge"/>. Because Flutter usually pushes the
/// data while the player is still in Jannah Garden — long before this scene loads — the bridge caches
/// the last roster it received. So this manager does not rely on catching the message live:
///
///   1. On Start, take whatever roster the bridge already has cached.
///   2. If the cache is empty, ask Flutter to send it, and render it when it lands.
///   3. If Flutter never answers (e.g. running the scene straight from the editor), optionally fall
///      back to the dummy JSON in Resources/fellow_profiles.txt.
///
/// Spawn points are registered the same way as <see cref="QuestionMarkOrbManager"/>: every child of
/// <see cref="spawnPointsParent"/> is one point, and each holds at most one profile.
/// </summary>
public class FellowshipVisualizationManager : MonoBehaviour
{
    public static FellowshipVisualizationManager Instance;

    [Header("Prefab & Spawn Points")]
    public GameObject fellowProfilePrefab;

    [Tooltip("Every child of this transform is registered as one spawn point.")]
    public Transform spawnPointsParent;

    [Header("Settings")]
    [Tooltip("Upper bound on profiles shown at once. The real count is also limited by the number of spawn points and available profiles.")]
    public int maxProfiles = 8;

    [Tooltip("Scatter the chosen profiles across random spawn points. Turn off to fill the points in order (useful for debugging layout). This no longer affects WHICH fellows are chosen - see 'Rank By Closeness'.")]
    public bool randomize = true;

    [Tooltip("Choose which fellows to show by how close they are to the player: the person who invited them first, then the people they invited personally, then the deeper chain. Turn off to take them in the order Flutter sent.")]
    public bool rankByCloseness = true;

    [Tooltip("Always keep the player's inviter on screen, even when the ranking is off and the roster is longer than the spawn points. A chain is two-way, and the one person who brought the player into the app should not be crowded out by their own referrals.")]
    public bool alwaysShowInviter = true;

    public bool spawnOnStart = true;

    [Header("Data Source")]
    [Tooltip("Use the roster pushed from the Flutter app. Turn off to always use the dummy JSON.")]
    public bool useFlutterData = true;

    [Tooltip("Show the dummy JSON when Flutter has sent nothing. Editor-only unless 'Allow Dummy Data In Builds' is also on.")]
    public bool fallbackToDummyData = true;

    [Tooltip("Let the dummy JSON show in a real build too. Leave OFF: a player would otherwise see invented strangers in their garden and believe they are real referrals.")]
    public bool allowDummyDataInBuilds = false;

    [Tooltip("How long to wait for Flutter's roster before giving up. Must exceed Flutter's own round trip - it calls the getReferralNetwork Cloud Function with an 8s timeout, so anything under ~10s gives up while the real answer is still in flight.")]
    public float flutterResponseTimeout = 12f;

    [Tooltip("Re-ask Flutter once after this long if the roster has not arrived. Covers the case where the first request landed before Flutter's listener was attached.")]
    public float flutterRetryAfter = 4f;

    [Header("Data")]
    [Tooltip("Dummy profile JSON. Falls back to Resources/fellow_profiles.txt when unassigned.")]
    public TextAsset fellowProfileFile;

    private readonly List<Transform> availableSpawnPoints = new List<Transform>();
    private readonly List<FellowProfileObject> activeProfiles = new List<FellowProfileObject>();

    /// <summary>Guards the dummy fallback from overwriting a roster that arrived while we waited.</summary>
    private bool hasRenderedFlutterRoster;

    /// <summary>
    /// Signature of the roster currently on screen. Flutter re-pushes the roster on every UNITY_READY,
    /// and the bridge's handshake announces UNITY_READY several times - without this the cards would be
    /// despawned and re-shuffled onto different spawn points each time, which reads as a glitch.
    /// </summary>
    private string renderedRosterSignature;

    /// <summary>
    /// How many fellows the player actually has, which is not the same as how many are on screen: the
    /// roster is capped by <see cref="maxProfiles"/>, by the spawn points, and by the server's own
    /// 500-member page. Exposed for a "+N more" label.
    /// </summary>
    public int TotalFellowCount { get; private set; }

    /// <summary>How many of them the player invited personally. 0 when Flutter does not send it.</summary>
    public int DirectFellowCount { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Static Instance would otherwise keep pointing at a destroyed manager after a scene change,
        // and the next scene's Start would see a stale (fake-null) reference.
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        // Static event, so this works even though the bridge lives in another scene.
        FlutterBridge.OnFellowshipProfilesReceived += HandleFlutterRoster;
    }

    private void OnDisable()
    {
        FlutterBridge.OnFellowshipProfilesReceived -= HandleFlutterRoster;
    }

    private void Start()
    {
        RegisterSpawnPoints();

        if (!spawnOnStart) return;

        if (!useFlutterData)
        {
            ShowDummyFellows();
            return;
        }

        // The roster was very likely pushed before this scene loaded — the bridge kept it for us. Show
        // it straight away so the garden is never empty, then ask for a fresh copy anyway: the cache can
        // be minutes old, and new fellows joining is the whole point of the feature.
        if (FlutterBridge.HasFellowshipProfiles)
        {
            HandleFlutterRoster(FlutterBridge.LatestFellowshipProfiles);
            RequestRoster();
            return;
        }

        // Nothing cached: ask Flutter for it, and fall back if the answer never comes.
        RequestRoster();

        // Re-ask once. The first request can land before Flutter has attached its listener (the game and
        // the host start together), and a dropped request is never retried by either side.
        Invoke(nameof(RetryRosterRequest), flutterRetryAfter);

        if (ShouldUseDummyData)
        {
            Invoke(nameof(FallBackToDummyData), flutterResponseTimeout);
        }
    }

    // ─── Flutter data ─────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the dummy JSON may be shown at all. Off in a player build unless explicitly allowed:
    /// invented fellows standing in a real player's garden look exactly like real referrals, and the
    /// player has no way to tell they are fake.
    /// </summary>
    private bool ShouldUseDummyData => fallbackToDummyData && (Application.isEditor || allowDummyDataInBuilds);

    private void RequestRoster()
    {
        if (FlutterBridge.Instance == null)
        {
            Debug.LogWarning("[FellowshipVisualization] No FlutterBridge yet — cannot request the roster.", this);
            return;
        }

        FlutterBridge.Instance.RequestFellowshipProfiles();
    }

    private void RetryRosterRequest()
    {
        if (hasRenderedFlutterRoster) return;

        Debug.Log($"[FellowshipVisualization] No roster after {flutterRetryAfter}s — asking Flutter again.", this);
        RequestRoster();
    }

    private void HandleFlutterRoster(FellowshipProfilesPayload roster)
    {
        // A null 'fellows' array means the payload was malformed, which is not an answer — keep waiting.
        // An EMPTY array *is* an answer: this player has invited nobody yet. Treating the two the same is
        // what used to put dummy strangers in the garden of every player with no referrals.
        if (roster?.fellows == null)
        {
            Debug.LogWarning("[FellowshipVisualization] Flutter roster had no 'fellows' array — ignoring.", this);
            return;
        }

        hasRenderedFlutterRoster = true;
        CancelInvoke(nameof(FallBackToDummyData));
        CancelInvoke(nameof(RetryRosterRequest));

        TotalFellowCount = roster.totalCount > 0 ? roster.totalCount : roster.fellows.Length;
        DirectFellowCount = roster.directCount;

        if (roster.fellows.Length == 0)
        {
            Debug.Log("[FellowshipVisualization] Flutter reports the player has no fellows yet — garden left empty.", this);
            renderedRosterSignature = string.Empty;
            ClearFellows();
            return;
        }

        string signature = BuildSignature(roster.fellows);
        if (signature == renderedRosterSignature)
        {
            // Same roster as what is already standing in the garden - re-spawning it would only move the
            // cards around. Flutter re-pushes on every UNITY_READY, and the bridge sends several.
            return;
        }

        renderedRosterSignature = signature;

        Debug.Log(
            $"[FellowshipVisualization] Showing {roster.fellows.Length} fellow(s) from Flutter" +
            (roster.truncated ? $" (of {TotalFellowCount} total)." : "."),
            this);

        ShowFellows(roster.fellows);
    }

    /// <summary>
    /// Cheap identity for a roster: who is in it and what their cards would say. Coins are part of it on
    /// purpose — a balance change should refresh the cards.
    /// </summary>
    private static string BuildSignature(IList<FellowProfileData> fellows)
    {
        StringBuilder sb = new StringBuilder(fellows.Count * 24);

        foreach (FellowProfileData f in fellows)
        {
            if (f == null) continue;
            sb.Append(f.userId).Append('|').Append(f.noorCoins).Append('|').Append(f.userName).Append(';');
        }

        return sb.ToString();
    }

    private void FallBackToDummyData()
    {
        if (hasRenderedFlutterRoster) return;

        Debug.LogWarning(
            $"[FellowshipVisualization] Flutter sent no roster within {flutterResponseTimeout}s — showing dummy profiles. " +
            "In a build this would mean the app never answered REQUEST_FELLOWSHIP_PROFILES.",
            this);

        ShowDummyFellows();
    }

    private void ShowDummyFellows()
    {
        List<FellowProfileData> fellows = LoadDummyFellows();
        if (fellows != null) ShowFellows(fellows);
    }

    // ─── Spawn points ─────────────────────────────────────────────────────────

    /// <summary>Rebuilds the available-point list from the children of <see cref="spawnPointsParent"/>.</summary>
    public void RegisterSpawnPoints()
    {
        availableSpawnPoints.Clear();

        if (spawnPointsParent == null)
        {
            Debug.LogWarning("[FellowshipVisualization] No spawnPointsParent assigned — nothing to spawn onto.", this);
            return;
        }

        foreach (Transform child in spawnPointsParent)
        {
            availableSpawnPoints.Add(child);
        }
    }

    // ─── Data ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the dummy JSON. Returns null (and logs) when the file is missing or unparseable, so the
    /// caller does not spawn a garden full of blank cards.
    /// </summary>
    private List<FellowProfileData> LoadDummyFellows()
    {
        if (fellowProfileFile == null)
            fellowProfileFile = Resources.Load<TextAsset>("fellow_profiles");

        if (fellowProfileFile == null)
        {
            Debug.LogError("[FellowshipVisualization] Could not find Resources/fellow_profiles.txt.", this);
            return null;
        }

        FellowProfileList parsed = JsonUtility.FromJson<FellowProfileList>(fellowProfileFile.text);

        if (parsed?.fellows == null || parsed.fellows.Length == 0)
        {
            Debug.LogError("[FellowshipVisualization] fellow_profiles.txt contained no fellows.", this);
            return null;
        }

        return new List<FellowProfileData>(parsed.fellows);
    }

    // ─── Spawning ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears any existing cards and spawns one per fellow onto a free spawn point.
    /// This is the entry point the real profile system should call.
    /// </summary>
    public void ShowFellows(IList<FellowProfileData> fellows)
    {
        ClearFellows();

        if (fellowProfilePrefab == null)
        {
            Debug.LogError("[FellowshipVisualization] No fellowProfilePrefab assigned.", this);
            return;
        }

        if (fellows == null || fellows.Count == 0) return;

        // Copy so sorting never reorders the caller's list.
        List<FellowProfileData> pool = new List<FellowProfileData>(fellows);
        pool.RemoveAll(f => f == null);

        // Which fellows are worth one of the handful of spawn points is a real decision: a player with
        // 500 referrals only ever sees maxProfiles of them, and the ones they actually recognise are the
        // people they invited themselves. Picking at random used to bury those under level-9 strangers.
        if (rankByCloseness) pool.Sort(CompareByCloseness);

        // Ranking already puts the inviter first, so this only bites when ranking is off or when an app
        // build sends no levels at all — exactly the cases where the one card the player is guaranteed
        // to recognise would otherwise be the one that falls off the end.
        if (alwaysShowInviter) MoveInviterToFront(pool);

        int count = Mathf.Min(maxProfiles, pool.Count, availableSpawnPoints.Count);

        if (count < Mathf.Min(maxProfiles, pool.Count))
        {
            Debug.LogWarning(
                $"[FellowshipVisualization] Only {availableSpawnPoints.Count} spawn point(s) registered, " +
                $"so {count} of {pool.Count} profile(s) will be shown.",
                this);
        }

        for (int i = 0; i < count; i++)
        {
            SpawnFellow(pool[i]);
        }
    }

    private void SpawnFellow(FellowProfileData data)
    {
        if (availableSpawnPoints.Count == 0) return;

        int index = randomize ? Random.Range(0, availableSpawnPoints.Count) : 0;
        Transform spawnPoint = availableSpawnPoints[index];

        // One profile per point.
        availableSpawnPoints.RemoveAt(index);

        GameObject go = Objectpool.Instance != null
            ? Objectpool.Instance.Spawn(fellowProfilePrefab, spawnPoint.position, spawnPoint.rotation)
            : Instantiate(fellowProfilePrefab, spawnPoint.position, spawnPoint.rotation);

        FellowProfileObject profile = go.GetComponent<FellowProfileObject>();
        if (profile == null)
        {
            Debug.LogError(
                $"[FellowshipVisualization] '{fellowProfilePrefab.name}' has no FellowProfileObject component.",
                this);
            return;
        }

        profile.spawnPoint = spawnPoint;
        profile.Bind(data);

        activeProfiles.Add(profile);
    }

    /// <summary>Despawns every active card and returns their spawn points to the pool.</summary>
    public void ClearFellows()
    {
        foreach (FellowProfileObject profile in activeProfiles)
        {
            if (profile == null) continue;

            if (profile.spawnPoint != null && !availableSpawnPoints.Contains(profile.spawnPoint))
                availableSpawnPoints.Add(profile.spawnPoint);

            if (Objectpool.Instance != null) Objectpool.Instance.Despawn(profile.gameObject);
            else Destroy(profile.gameObject);
        }

        activeProfiles.Clear();
    }

    /// <summary>
    /// Closest link in the chain first, and at equal distance the person who invited the player before
    /// the people they invited. Fellows from an app build that sends no level default to level 0, which
    /// sorts last — so an old app degrades to "richest first" rather than to a crash or an empty garden.
    /// </summary>
    private static int CompareByCloseness(FellowProfileData a, FellowProfileData b)
    {
        int rankA = Rank(a);
        int rankB = Rank(b);
        if (rankA != rankB) return rankA.CompareTo(rankB);

        return b.noorCoins.CompareTo(a.noorCoins);
    }

    /// <summary>
    /// Lower sorts first. Distance along the chain is doubled so that upline and downline can interleave
    /// at each step with the upline ahead: inviter = 1, direct referral = 2, inviter's inviter = 3,
    /// second-level referral = 4, and so on. An unknown level sorts last.
    /// </summary>
    private static int Rank(FellowProfileData f)
    {
        int depth = f.isDirect ? 1 : f.level;
        if (depth <= 0) return int.MaxValue;

        return depth * 2 - (f.IsUpline ? 1 : 0);
    }

    /// <summary>
    /// Moves the player's direct inviter to the head of <paramref name="pool"/> if it is in there, so the
    /// truncation below can never drop it. No-op when the player joined without a referral code.
    /// </summary>
    private static void MoveInviterToFront(List<FellowProfileData> pool)
    {
        int index = pool.FindIndex(f => f.IsDirectInviter);
        if (index <= 0) return;

        FellowProfileData inviter = pool[index];
        pool.RemoveAt(index);
        pool.Insert(0, inviter);
    }
}
