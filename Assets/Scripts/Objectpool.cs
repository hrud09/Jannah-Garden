using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Objectpool : MonoBehaviour
{
    public static Objectpool Instance { get; private set; }

    // Map prefab instance ID to a queue of inactive, pooled GameObjects
    private Dictionary<int, Queue<GameObject>> poolDictionary = new Dictionary<int, Queue<GameObject>>();
    
    // Reverse lookup to find which prefab an instantiated object belongs to, useful for despawning.
    // Keyed by the clone itself rather than its instance ID so a destroyed clone can be recognised as
    // destroyed (an int ID cannot be resolved back to an object to null-check it).
    private Dictionary<GameObject, int> instanceToPrefabMap = new Dictionary<GameObject, int>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Instance = null;
    }

    /// <summary>
    /// Pooled clones are plain scene objects, so a Single-mode load destroys them — but this pool is
    /// DontDestroyOnLoad, so its bookkeeping survives and would otherwise accumulate an entry per
    /// clone per scene change for the life of the process. Spawn already skips destroyed queue
    /// entries, so this is purely about not growing the maps forever.
    /// </summary>
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;

        PruneDestroyedEntries();
    }

    private void PruneDestroyedEntries()
    {
        foreach (var pair in poolDictionary)
        {
            Queue<GameObject> queue = pair.Value;
            int count = queue.Count;

            // Re-enqueue the survivors in order; a destroyed clone is simply not put back.
            for (int i = 0; i < count; i++)
            {
                GameObject pooled = queue.Dequeue();
                if (pooled != null) queue.Enqueue(pooled);
            }
        }

        // Spawned-but-never-despawned clones died with the scene too. Anything still alive — a clone
        // parented under a DontDestroyOnLoad object, say — keeps its entry and stays poolable.
        var stale = new List<GameObject>();
        foreach (GameObject clone in instanceToPrefabMap.Keys)
        {
            if (clone == null) stale.Add(clone);
        }

        foreach (GameObject clone in stale) instanceToPrefabMap.Remove(clone);
    }

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        int prefabId = prefab.GetInstanceID();

        if (!poolDictionary.ContainsKey(prefabId))
        {
            poolDictionary[prefabId] = new Queue<GameObject>();
        }

        GameObject objToSpawn = null;

        while (poolDictionary[prefabId].Count > 0)
        {
            objToSpawn = poolDictionary[prefabId].Dequeue();
            if (objToSpawn != null)
            {
                break;
            }
        }

        if (objToSpawn == null)
        {
            objToSpawn = Instantiate(prefab);
            instanceToPrefabMap[objToSpawn] = prefabId;
        }

        objToSpawn.transform.position = position;
        objToSpawn.transform.rotation = rotation;
        if (parent != null)
        {
            objToSpawn.transform.SetParent(parent);
        }
        
        objToSpawn.SetActive(true);

        return objToSpawn;
    }

    public GameObject Spawn(GameObject prefab, Transform parent = null)
    {
        return Spawn(prefab, prefab.transform.position, prefab.transform.rotation, parent);
    }
    
    public GameObject Spawn(GameObject prefab)
    {
        return Spawn(prefab, prefab.transform.position, prefab.transform.rotation, null);
    }

    public void Despawn(GameObject obj)
    {
        if (obj == null) return;

        if (instanceToPrefabMap.TryGetValue(obj, out int prefabId))
        {
            obj.SetActive(false);
            if (!poolDictionary.ContainsKey(prefabId))
            {
                poolDictionary[prefabId] = new Queue<GameObject>();
            }
            poolDictionary[prefabId].Enqueue(obj);
        }
        else
        {
            // Object wasn't spawned by this pool system, or dictionary lost it.
            Destroy(obj);
        }
    }

    public void Despawn(GameObject obj, float delay)
    {
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(DespawnCoroutine(obj, delay));
        }
        else
        {
            // Cannot start coroutine if pool is disabled/destroyed
            Despawn(obj);
        }
    }

    private IEnumerator DespawnCoroutine(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        Despawn(obj);
    }
}
