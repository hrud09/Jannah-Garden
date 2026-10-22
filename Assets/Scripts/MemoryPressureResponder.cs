using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Process-wide response to OS memory pressure.
///
/// <para>This used to live on <see cref="ItemPlacementManager"/>, which exists only in the Jannah
/// Garden scene. Outer Garden loads <c>LoadSceneMode.Single</c>, so that manager — and with it the
/// only <see cref="Application.lowMemory"/> subscriber in the project — was destroyed on the way in.
/// For the whole of an Outer Garden visit nothing answered iOS's <c>didReceiveMemoryWarning</c> or
/// Android's <c>onTrimMemory</c>, which is exactly the scene where it mattered most: a ~150 MB remote
/// scene bundle, plus up to <c>maxCachedUnusedItemPrefabs</c> shop prefabs still pinned in
/// <see cref="AddressableItemLoader"/>'s static cache, plus the Flutter engine sharing the process.
/// See <c>oom_crash_fix_guide.md</c>.</para>
///
/// <para>Bootstrapped before the first scene and <c>DontDestroyOnLoad</c>, so the handler is live in
/// every scene regardless of which one the game boots into. It deliberately does not depend on
/// <see cref="ItemPlacementManager"/> existing: when there is no garden manager (Outer Garden, Init
/// Scene) nothing is using a shop prefab, so the entire cache is evictable.</para>
/// </summary>
public class MemoryPressureResponder : MonoBehaviour
{
    private const string GameObjectName = "MemoryPressureResponder";

    public static MemoryPressureResponder Instance { get; private set; }

    /// <summary>
    /// Keys that must survive a trim when no <see cref="ItemPlacementManager"/> is loaded. Empty by
    /// design — nothing outside the garden scene holds a shop prefab — but allocated once rather than
    /// per call, since a low-memory warning is the worst moment to be allocating.
    /// </summary>
    private static readonly HashSet<string> EmptyKeySet = new HashSet<string>();

    // ─── Bootstrap ────────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        GameObject host = new GameObject(GameObjectName);
        host.AddComponent<MemoryPressureResponder>(); // Awake wires up Instance + DontDestroyOnLoad
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        Application.lowMemory += HandleLowMemory;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        Application.lowMemory -= HandleLowMemory;
        Instance = null;
    }

    // ─── Memory pressure ──────────────────────────────────────────────────────

    /// <summary>
    /// The OS is warning this process is close to being killed. Neither platform gives a size budget
    /// with the warning, so there is nothing to tune: drop every downloaded item prefab the process is
    /// not actively using, right now. iOS has no <c>largeHeap</c>-style escape hatch, so this is the
    /// only lever available there once the OS is already unhappy.
    /// </summary>
    private void HandleLowMemory()
    {
        Debug.LogWarning("[MemoryPressureResponder] OS low-memory warning — releasing every unused item prefab.");
        ReleaseUnusedItemPrefabs(0);
    }

    /// <summary>
    /// Releases <see cref="AddressableItemLoader"/>'s cache down to <paramref name="maxEntries"/>
    /// prefabs beyond whatever is genuinely in use, then drops derived caches and unreferenced assets.
    ///
    /// <para>Safe to call from any scene: evicting a prefab never touches instances already spawned
    /// from it — Addressables releases the template, not the clones — so the only cost is that the
    /// item re-downloads next time it is placed.</para>
    /// </summary>
    public void ReleaseUnusedItemPrefabs(int maxEntries)
    {
        AddressableItemLoader.TrimCache(maxEntries, CollectInUseKeys());
        ItemFootprint.ClearCache();
        Resources.UnloadUnusedAssets();
    }

    /// <summary>
    /// Releases <em>every</em> cached item prefab, ignoring what is currently in use. For the moment
    /// just before a <c>LoadSceneMode.Single</c> transition: the items whose prefabs the keep-set
    /// would protect are about to be destroyed by that very load, so protecting them only carries
    /// tens of MB per prefab into the new scene for nothing.
    ///
    /// <para>Deliberately skips <see cref="Resources.UnloadUnusedAssets"/> — a Single-mode load runs
    /// its own unload pass, and doing it twice only adds a second synchronous stall.</para>
    /// </summary>
    public void ReleaseAllItemPrefabsForSceneExit()
    {
        AddressableItemLoader.TrimCache(0, EmptyKeySet);
        ItemFootprint.ClearCache();
    }

    /// <summary>
    /// What the garden still needs, or an empty set when there is no garden loaded. A destroyed
    /// manager reports as fake-null through <see cref="ItemPlacementManager.Instance"/>, so the
    /// null check also covers the moment mid-transition when the old scene is gone.
    /// </summary>
    private static ISet<string> CollectInUseKeys()
    {
        ItemPlacementManager garden = ItemPlacementManager.Instance;
        return garden != null ? garden.ComputeInUseAddressableKeys() : EmptyKeySet;
    }
}
