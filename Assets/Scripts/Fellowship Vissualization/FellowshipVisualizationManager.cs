using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns fellow profile cards around the map at registered spawn points.
///
/// Right now the profiles come from a dummy JSON file (Resources/fellow_profiles.txt). When the real
/// profile system arrives, call <see cref="ShowFellows"/> with the live data instead — the spawning,
/// spawn-point bookkeeping, and the prefab itself stay exactly as they are.
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

    [Tooltip("Pick a random subset of profiles and random spawn points. Turn off to spawn the JSON order into the point order (useful for debugging layout).")]
    public bool randomize = true;

    public bool spawnOnStart = true;

    [Header("Data")]
    [Tooltip("Dummy profile JSON. Falls back to Resources/fellow_profiles.txt when unassigned.")]
    public TextAsset fellowProfileFile;

    private readonly List<Transform> availableSpawnPoints = new List<Transform>();
    private readonly List<FellowProfileObject> activeProfiles = new List<FellowProfileObject>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        RegisterSpawnPoints();

        if (spawnOnStart)
        {
            List<FellowProfileData> fellows = LoadDummyFellows();
            if (fellows != null) ShowFellows(fellows);
        }
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

        // Copy so shuffling never reorders the caller's list.
        List<FellowProfileData> pool = new List<FellowProfileData>(fellows);
        if (randomize) Shuffle(pool);

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

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
