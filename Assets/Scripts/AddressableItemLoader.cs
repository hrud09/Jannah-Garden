using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Resolves a shop/treasure-box item's <see cref="AssetReferenceGameObject"/> into a concrete,
/// loaded <see cref="GameObject"/> template that <see cref="Objectpool"/> can spawn from.
///
/// Item prefabs live in a Remote Addressables group so they aren't baked into the shipped app —
/// this is the layer that downloads the one a player actually unlocks/places, instead of every item
/// shipping up front. Keyed by <see cref="AssetReferenceGameObject.AssetGUID"/> rather than the
/// reference instance itself, because separate <see cref="ShopItemData"/>/<see cref="TreasureBoxRewardItemData"/>
/// assets can point at the same underlying prefab and must dedupe to one download/cache entry, not
/// one per referencing field.
///
/// A load is cached indefinitely once made — but is evictable via <see cref="TrimCache"/>. Source
/// meshes here run tens of MB each (Meshy AI output), and this process also carries the Flutter
/// engine embedding it, so letting every item a player ever previews stay resident for the session
/// is what was driving the OOM kills on-device (see the "export size & shop bundle audit" note):
/// <see cref="ItemPlacementManager"/> calls <see cref="TrimCache"/> after every placement/return/cancel
/// to release whatever the garden no longer needs.
/// </summary>
public static class AddressableItemLoader
{
    private static readonly Dictionary<string, GameObject> _cache = new Dictionary<string, GameObject>();
    private static readonly Dictionary<string, AsyncOperationHandle<GameObject>> _handles = new Dictionary<string, AsyncOperationHandle<GameObject>>();
    private static readonly Dictionary<string, float> _lastUsed = new Dictionary<string, float>();
    private static readonly Dictionary<string, List<Action<GameObject>>> _waiters = new Dictionary<string, List<Action<GameObject>>>();
    private static readonly Dictionary<string, AsyncOperationHandle<GameObject>> _inFlight = new Dictionary<string, AsyncOperationHandle<GameObject>>();

    /// <summary>Synchronous fast path — true if this reference's prefab is already downloaded and cached.</summary>
    public static bool TryGetCached(AssetReferenceGameObject reference, out GameObject prefab)
    {
        prefab = null;
        if (reference == null || !reference.RuntimeKeyIsValid()) return false;
        if (!_cache.TryGetValue(reference.AssetGUID, out prefab)) return false;
        _lastUsed[reference.AssetGUID] = Time.unscaledTime;
        return true;
    }

    /// <summary>
    /// How far along an in-flight download/load is for this reference, from Addressables' own
    /// <see cref="AsyncOperationHandle.PercentComplete"/> (a rough but live estimate that reflects actual
    /// network transfer for a remote bundle, not just the local asset load once bytes are in). Returns 1
    /// if the reference is already cached or invalid, 0 if nothing is loading for it yet.
    /// </summary>
    public static float GetProgress(AssetReferenceGameObject reference)
    {
        if (reference == null || !reference.RuntimeKeyIsValid()) return 1f;
        string key = reference.AssetGUID;
        if (_cache.ContainsKey(key)) return 1f;
        if (_inFlight.TryGetValue(key, out AsyncOperationHandle<GameObject> handle) && handle.IsValid())
        {
            return handle.PercentComplete;
        }
        return 0f;
    }

    /// <summary>
    /// Resolves <paramref name="reference"/> to its loaded prefab, downloading it first if needed.
    /// Calls <paramref name="onLoaded"/> synchronously on a cache hit, otherwise once the load completes.
    /// Invokes with null if the reference is unassigned/invalid or the load fails — callers must handle null.
    /// Concurrent requests for the same GUID are coalesced into a single Addressables load.
    /// </summary>
    public static void LoadAsync(AssetReferenceGameObject reference, Action<GameObject> onLoaded)
    {
        if (reference == null || !reference.RuntimeKeyIsValid())
        {
            onLoaded?.Invoke(null);
            return;
        }

        string key = reference.AssetGUID;

        if (_cache.TryGetValue(key, out GameObject cached))
        {
            _lastUsed[key] = Time.unscaledTime;
            onLoaded?.Invoke(cached);
            return;
        }

        if (_waiters.TryGetValue(key, out List<Action<GameObject>> waiters))
        {
            waiters.Add(onLoaded);
            return;
        }

        _waiters[key] = new List<Action<GameObject>> { onLoaded };

        AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(key);
        _inFlight[key] = handle;
        handle.Completed += op => OnLoadCompleted(key, op);
    }

    private static void OnLoadCompleted(string key, AsyncOperationHandle<GameObject> handle)
    {
        GameObject result = handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null;

        _inFlight.Remove(key);

        if (result != null)
        {
            _cache[key] = result;
            _handles[key] = handle;
            _lastUsed[key] = Time.unscaledTime;
        }
        else
        {
            Debug.LogError($"[AddressableItemLoader] Failed to load item prefab '{key}': {handle.OperationException}");
            Addressables.Release(handle);
        }

        if (_waiters.TryGetValue(key, out List<Action<GameObject>> waiters))
        {
            _waiters.Remove(key);
            foreach (Action<GameObject> callback in waiters)
            {
                callback?.Invoke(result);
            }
        }
    }

    /// <summary>
    /// Releases cached prefabs down to <paramref name="maxEntries"/>, oldest-by-last-use first, never
    /// touching anything in <paramref name="keepKeys"/> (the AssetGUIDs the garden/current placement
    /// still needs — see <see cref="ItemPlacementManager"/>). Safe to call freely: instances already
    /// instantiated from an evicted prefab (placed items, pooled inactive clones) are untouched —
    /// Addressables releases the loaded template, not objects already spawned from it — the only cost
    /// is that spawning that item again later re-downloads/reloads it instead of hitting the cache.
    /// </summary>
    public static void TrimCache(int maxEntries, ISet<string> keepKeys)
    {
        int evictableCount = 0;
        foreach (string key in _cache.Keys)
        {
            if (keepKeys == null || !keepKeys.Contains(key)) evictableCount++;
        }

        int overBudget = (_cache.Count - maxEntries);
        int toEvict = Mathf.Min(overBudget, evictableCount);
        if (toEvict <= 0) return;

        List<string> candidates = new List<string>(evictableCount);
        foreach (string key in _cache.Keys)
        {
            if (keepKeys == null || !keepKeys.Contains(key)) candidates.Add(key);
        }
        candidates.Sort((a, b) => _lastUsed.GetValueOrDefault(a, 0f).CompareTo(_lastUsed.GetValueOrDefault(b, 0f)));

        for (int i = 0; i < toEvict; i++)
        {
            string key = candidates[i];

            if (_handles.TryGetValue(key, out AsyncOperationHandle<GameObject> handle))
            {
                Addressables.Release(handle);
                _handles.Remove(key);
            }

            _cache.Remove(key);
            _lastUsed.Remove(key);
        }
    }
}
