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
/// this is the layer that downloads (and caches, for the process lifetime) the one a player actually
/// unlocks/places, instead of every item shipping up front. Keyed by <see cref="AssetReferenceGameObject.AssetGUID"/>
/// rather than the reference instance itself, because separate <see cref="ShopItemData"/>/
/// <see cref="TreasureBoxRewardItemData"/> assets can point at the same underlying prefab and must
/// dedupe to one download/cache entry, not one per referencing field.
/// </summary>
public static class AddressableItemLoader
{
    private static readonly Dictionary<string, GameObject> _cache = new Dictionary<string, GameObject>();
    private static readonly Dictionary<string, List<Action<GameObject>>> _waiters = new Dictionary<string, List<Action<GameObject>>>();

    /// <summary>Synchronous fast path — true if this reference's prefab is already downloaded and cached.</summary>
    public static bool TryGetCached(AssetReferenceGameObject reference, out GameObject prefab)
    {
        prefab = null;
        if (reference == null || !reference.RuntimeKeyIsValid()) return false;
        return _cache.TryGetValue(reference.AssetGUID, out prefab);
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
        handle.Completed += op => OnLoadCompleted(key, op);
    }

    private static void OnLoadCompleted(string key, AsyncOperationHandle<GameObject> handle)
    {
        GameObject result = handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null;

        if (result != null)
        {
            _cache[key] = result;
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
}
