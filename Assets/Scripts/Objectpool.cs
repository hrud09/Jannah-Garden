using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Objectpool : MonoBehaviour
{
    public static Objectpool Instance { get; private set; }

    // Map prefab instance ID to a queue of inactive, pooled GameObjects
    private Dictionary<int, Queue<GameObject>> poolDictionary = new Dictionary<int, Queue<GameObject>>();
    
    // Reverse lookup to find which prefab an instantiated object belongs to, useful for despawning
    private Dictionary<int, int> instanceToPrefabMap = new Dictionary<int, int>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
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
            instanceToPrefabMap[objToSpawn.GetInstanceID()] = prefabId;
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

        int instanceId = obj.GetInstanceID();
        if (instanceToPrefabMap.TryGetValue(instanceId, out int prefabId))
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
