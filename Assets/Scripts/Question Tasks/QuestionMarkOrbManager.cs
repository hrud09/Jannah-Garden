using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class QuestionMarkOrbManager : MonoBehaviour
{
    public static QuestionMarkOrbManager Instance;

    public GameObject orbPrefab;
    public Transform[] spawnPoints;
    
    [Header("Settings")]
    public int maxOrbs = 5;
    public float respawnDelay = 20f;
    public int rewardCoins = 10;

    private List<GameObject> activeOrbs = new List<GameObject>();
    private List<Transform> availableSpawnPoints = new List<Transform>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (spawnPoints != null)
        {
            availableSpawnPoints.AddRange(spawnPoints);
        }
        
        // Initial spawn
        int amountToSpawn = Mathf.Min(maxOrbs, availableSpawnPoints.Count);
        for (int i = 0; i < amountToSpawn; i++)
        {
            SpawnOrb();
        }
    }

    void SpawnOrb()
    {
        if (availableSpawnPoints.Count == 0 || orbPrefab == null) return;

        // Pick a random available spawn point
        int randIndex = Random.Range(0, availableSpawnPoints.Count);
        Transform spawnPoint = availableSpawnPoints[randIndex];
        
        // Remove it from available points
        availableSpawnPoints.RemoveAt(randIndex);

        GameObject newOrb = Objectpool.Instance.Spawn(orbPrefab, spawnPoint.position, spawnPoint.rotation);
        
        // Attach a reference to the spawn point so we can free it later
        QuestionMarkOrb orbScript = newOrb.GetComponent<QuestionMarkOrb>();
        if (orbScript != null)
        {
            orbScript.spawnPoint = spawnPoint;
        }

        activeOrbs.Add(newOrb);
    }

    public void OnOrbOpened(QuestionMarkOrb orb)
    {
        // Free the spawn point
        if (orb.spawnPoint != null && !availableSpawnPoints.Contains(orb.spawnPoint))
        {
            availableSpawnPoints.Add(orb.spawnPoint);
        }

        if (activeOrbs.Contains(orb.gameObject))
        {
            activeOrbs.Remove(orb.gameObject);
        }

        Objectpool.Instance.Despawn(orb.gameObject);

        // Schedule respawn
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);

        if (activeOrbs.Count < maxOrbs && availableSpawnPoints.Count > 0)
        {
            SpawnOrb();
        }
    }
}
