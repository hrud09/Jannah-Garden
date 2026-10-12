using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class QuestionMarkOrbManager : MonoBehaviour
{
    public static QuestionMarkOrbManager Instance;

    public GameObject orbPrefab;
    public Transform spawnPointsParent;
    public Terrain terrainReference;
    
    [Header("Settings")]
    public int maxOrbs = 5;
    [Tooltip("How long a finished orb takes to shrink away before it returns to the pool.")]
    public float vanishDuration = 0.4f;
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
        if (spawnPointsParent != null)
        {
            foreach (Transform child in spawnPointsParent)
            {
                availableSpawnPoints.Add(child);
            }
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

        // Pick a random available spawn point. Occupied points are never in this list, so the new
        // orb always lands somewhere no other orb is standing.
        int randIndex = Random.Range(0, availableSpawnPoints.Count);
        Transform spawnPoint = availableSpawnPoints[randIndex];
        
        // Remove it from available points
        availableSpawnPoints.RemoveAt(randIndex);

        Vector3 spawnPos = spawnPoint.position;
        if (terrainReference != null)
        {
            float terrainY = terrainReference.SampleHeight(spawnPos) + terrainReference.transform.position.y;
            spawnPos = new Vector3(spawnPos.x, terrainY + 2f, spawnPos.z);
        }

        GameObject newOrb = Objectpool.Instance.Spawn(orbPrefab, spawnPos, spawnPoint.rotation);

        // A pooled orb comes back exactly as VanishOrb left it — shrunk to zero with its colliders
        // off — so restore both, then pop it in.
        newOrb.transform.DOKill();
        SetCollidersEnabled(newOrb, true);
        newOrb.transform.localScale = Vector3.zero;
        newOrb.transform.DOScale(orbPrefab.transform.localScale, vanishDuration).SetEase(Ease.OutBack);
        
        // Attach a reference to the spawn point so we can free it later
        QuestionMarkOrb orbScript = newOrb.GetComponent<QuestionMarkOrb>();
        if (orbScript != null)
        {
            orbScript.spawnPoint = spawnPoint;
        }

        activeOrbs.Add(newOrb);
    }

    // Called once the task inside an orb (question or dhikr) is finished: the orb vanishes and a
    // replacement appears straight away at a different, unoccupied spawn point.
    public void OnOrbOpened(QuestionMarkOrb orb)
    {
        if (orb == null || !activeOrbs.Contains(orb.gameObject)) return;

        activeOrbs.Remove(orb.gameObject);

        // Spawn the replacement before freeing the finished orb's point, so the replacement can't
        // pick the very spot the player just cleared.
        bool hadFreePoint = availableSpawnPoints.Count > 0;
        SpawnOrb();

        if (orb.spawnPoint != null && !availableSpawnPoints.Contains(orb.spawnPoint))
        {
            availableSpawnPoints.Add(orb.spawnPoint);
        }
        orb.spawnPoint = null;

        // Every other point was taken (as many spawn points as orbs), so the only free spot is the
        // one just cleared — reuse it rather than letting the garden lose an orb.
        if (!hadFreePoint) SpawnOrb();

        VanishOrb(orb.gameObject);
    }

    void VanishOrb(GameObject orb)
    {
        // Colliders go off first so the player can't target the orb again while it shrinks.
        SetCollidersEnabled(orb, false);

        orb.transform.DOKill();
        orb.transform.DOScale(0f, vanishDuration).SetEase(Ease.InBack)
            .OnComplete(() => Objectpool.Instance.Despawn(orb));
    }

    static void SetCollidersEnabled(GameObject orb, bool enabled)
    {
        foreach (Collider col in orb.GetComponentsInChildren<Collider>(true))
        {
            col.enabled = enabled;
        }
    }
}
