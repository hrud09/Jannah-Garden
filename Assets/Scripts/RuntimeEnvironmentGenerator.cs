using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Integration note: This system handles coarse distance-based activation (SetActive).
// OcclusionCullingManager handles fine-grained frustum/renderer culling.
// In "Combined" mode, both systems cooperate without conflict.

public class RuntimeEnvironmentGenerator : MonoBehaviour
{
    public static RuntimeEnvironmentGenerator Instance { get; private set; }

    [Header("Default Distance Settings")]
    [Tooltip("Distance from the player inside which assets will be activated.")]
    public float defaultActivationRadius = 40f;
    [Tooltip("Distance from the player outside which assets will be deactivated.")]
    public float defaultDeactivationRadius = 45f;

    [Header("Optimization Settings")]
    [Tooltip("How often in seconds the generator checks player distance.")]
    public float checkInterval = 0.2f;
    [Tooltip("The player must move at least this distance before a full proximity check is run.")]
    public float playerMoveThreshold = 2.0f;
    [Tooltip("Layer mask containing the layers to search for and optimize.")]
    public LayerMask optimizableLayers;

    [Header("Player Settings (Optional)")]
    [Tooltip("Reference to the player's Transform. If null, it will be auto-detected at runtime.")]
    public Transform playerTransform;

    [Header("Debug Settings")]
    [Tooltip("Show visual culling boundaries in the Scene view.")]
    public bool showGizmos = true;
    [Tooltip("Color of the activation radius wire sphere.")]
    public Color activationGizmoColor = Color.green;
    [Tooltip("Color of the deactivation radius wire sphere.")]
    public Color deactivationGizmoColor = Color.red;

    [Header("Monitored Scene Objects")]
    [Tooltip("List of all game objects tracked in the scene. You can assign these manually or use the 'Assign All Assets From Scene' button.")]
    public List<GameObject> sceneObjects = new List<GameObject>();

    private readonly List<GameObject> _registeredObjects = new List<GameObject>();
    private readonly HashSet<GameObject> _deactivatedObjects = new HashSet<GameObject>();
    private readonly Dictionary<GameObject, System.DateTime> _deactivationTimes = new Dictionary<GameObject, System.DateTime>();
    private Vector3 _lastCheckPlayerPos = new Vector3(-9999f, -9999f, -9999f);
    private Coroutine _checkCoroutine;

    /// <summary>
    /// Returns true if the OcclusionCullingManager is present and actively culling.
    /// Other systems can query this to avoid redundant culling work.
    /// </summary>
    public bool IsOcclusionCullingActive
    {
        get
        {
            return OcclusionCullingManager.Instance != null
                && OcclusionCullingManager.Instance.IsOcclusionCullingActive;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        Vector3 playerPos = GetPlayerPosition();
        
        // Pre-register and sync any assets configured in the inspector list
        foreach (var go in sceneObjects)
        {
            if (go != null && !_registeredObjects.Contains(go))
            {
                _registeredObjects.Add(go);
                ForceSyncObject(go, playerPos);
            }
        }

        _checkCoroutine = StartCoroutine(CheckDistanceCoroutine());
    }

    private void OnDestroy()
    {
        if (_checkCoroutine != null)
        {
            StopCoroutine(_checkCoroutine);
        }
    }

    /// <summary>
    /// Registers an object to be tracked and optimized.
    /// </summary>
    public void RegisterObject(GameObject go)
    {
        if (go == null) return;

        if (!_registeredObjects.Contains(go))
        {
            _registeredObjects.Add(go);
            
            // Sync the object state immediately based on its distance to the player
            ForceSyncObject(go, GetPlayerPosition());
        }
    }

    /// <summary>
    /// Unregisters an object from optimization tracking.
    /// </summary>
    public void UnregisterObject(GameObject go)
    {
        if (go == null) return;
        _registeredObjects.Remove(go);
        _deactivatedObjects.Remove(go);
        _deactivationTimes.Remove(go);
    }

    /// <summary>
    /// Checks if a GameObject is currently deactivated by this culling manager.
    /// </summary>
    public bool IsDeactivatedByGenerator(GameObject go)
    {
        return _deactivatedObjects.Contains(go);
    }

    private Vector3 GetPlayerPosition()
    {
        if (playerTransform == null)
        {
            FindPlayerTransform();
        }
        
        return playerTransform != null ? playerTransform.position : Vector3.zero;
    }

    private void FindPlayerTransform()
    {
        // 1. Try to find the player by the standard "Player" tag
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            return;
        }

        // 2. Try to find the PlayerMovement component from IdyllicFantasyNature
        var movement = FindObjectOfType<IdyllicFantasyNature.PlayerMovement>();
        if (movement != null)
        {
            playerTransform = movement.transform;
            return;
        }

        // 3. Try to find the TargetDirectionController instance (also attached to the player)
        if (TargetDirectionController.Instance != null)
        {
            playerTransform = TargetDirectionController.Instance.transform;
            return;
        }

        // 4. Fall back to Camera.main
        if (Camera.main != null)
        {
            playerTransform = Camera.main.transform;
        }
    }

    private IEnumerator CheckDistanceCoroutine()
    {
        WaitForSeconds wait = new WaitForSeconds(checkInterval);
        bool isFirstCheck = true;

        while (true)
        {
            yield return wait;

            Vector3 playerPos = GetPlayerPosition();
            if (playerTransform == null) continue;

            float distMoved = Vector3.Distance(playerPos, _lastCheckPlayerPos);
            if (distMoved >= playerMoveThreshold || isFirstCheck)
            {
                _lastCheckPlayerPos = playerPos;
                isFirstCheck = false;
                UpdateAllObjects(playerPos);
            }
        }
    }

    private void UpdateAllObjects(Vector3 playerPos)
    {
        // Iterate backwards to safely handle any dynamically unregistered items
        for (int i = _registeredObjects.Count - 1; i >= 0; i--)
        {
            var go = _registeredObjects[i];
            if (go == null)
            {
                _registeredObjects.RemoveAt(i);
                continue;
            }
            
            UpdateSingleObject(go, playerPos);
        }
    }

    private void UpdateSingleObject(GameObject go, Vector3 playerPos)
    {
        float actRad = defaultActivationRadius;
        float deactRad = defaultDeactivationRadius;

        // Ensure culling radii are valid (deactivation must be >= activation)
        if (deactRad < actRad)
        {
            deactRad = actRad;
        }

        float distSqr = (go.transform.position - playerPos).sqrMagnitude;
        bool isDeactivated = _deactivatedObjects.Contains(go);

        if (isDeactivated)
        {
            // Currently deactivated; activate it if the player comes within the activation radius
            if (distSqr <= actRad * actRad)
            {
                _deactivatedObjects.Remove(go);
                SetObjectState(go, true);
            }
        }
        else
        {
            // Currently active; deactivate it if the player moves beyond the deactivation radius
            if (distSqr > deactRad * deactRad)
            {
                _deactivatedObjects.Add(go);
                SetObjectState(go, false);
            }
        }
    }

    /// <summary>
    /// Synchronizes the object active/inactive state immediately based on distance to player.
    /// </summary>
    public void ForceSyncObject(GameObject go, Vector3 playerPos)
    {
        float actRad = defaultActivationRadius;
        float distSqr = (go.transform.position - playerPos).sqrMagnitude;

        bool shouldBeActive = distSqr <= actRad * actRad;
        
        if (shouldBeActive)
        {
            _deactivatedObjects.Remove(go);
            _deactivationTimes.Remove(go);
            go.SetActive(true);
        }
        else
        {
            _deactivatedObjects.Add(go);
            _deactivationTimes[go] = System.DateTime.UtcNow;
            go.SetActive(false);
        }
    }

    private void SetObjectState(GameObject go, bool active)
    {
        if (active)
        {
            if (_deactivationTimes.TryGetValue(go, out System.DateTime deactTime))
            {
                double elapsed = (System.DateTime.UtcNow - deactTime).TotalSeconds;
                _deactivationTimes.Remove(go);
                AdjustTimers(go, elapsed);
            }
            go.SetActive(true);

            // When activating, register with OcclusionCullingManager if in Combined mode
            RegisterWithOcclusionCulling(go);
        }
        else
        {
            // When deactivating, unregister from OcclusionCullingManager
            UnregisterFromOcclusionCulling(go);

            _deactivationTimes[go] = System.DateTime.UtcNow;
            go.SetActive(false);
        }
    }

    /// <summary>
    /// Registers a GameObject's renderers with the OcclusionCullingManager
    /// so it can handle frustum-based culling while this system handles distance activation.
    /// </summary>
    private void RegisterWithOcclusionCulling(GameObject go)
    {
        if (OcclusionCullingManager.Instance == null) return;
        if (OcclusionCullingManager.Instance.cullingMode != OcclusionCullingManager.CullingMode.Combined) return;

        OcclusionCullingManager.Instance.RegisterGameObject(go);
    }

    /// <summary>
    /// Unregisters a GameObject's renderers from the OcclusionCullingManager
    /// when this system deactivates it entirely.
    /// </summary>
    private void UnregisterFromOcclusionCulling(GameObject go)
    {
        if (OcclusionCullingManager.Instance == null) return;
        if (OcclusionCullingManager.Instance.cullingMode != OcclusionCullingManager.CullingMode.Combined) return;

        OcclusionCullingManager.Instance.UnregisterGameObject(go);
    }

    private void AdjustTimers(GameObject go, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0) return;

        PlaceableItem placeable = go.GetComponent<PlaceableItem>();
        if (placeable != null && placeable.enabled)
        {
            if (placeable.remainingDuration > 0f)
            {
                placeable.remainingDuration -= (float)elapsedSeconds;
                if (placeable.remainingDuration < 0f)
                {
                    placeable.remainingDuration = 0f;
                }
            }
        }
    }

    /// <summary>
    /// Finds all GameObjects in the scene matching the LayerMask and populates the sceneObjects list.
    /// Excludes player, camera, and the generator itself.
    /// </summary>
    public void AssignAllAssetsFromScene()
    {
        sceneObjects.Clear();
        GameObject[] allObjects = FindObjectsOfType<GameObject>(true);
        foreach (var go in allObjects)
        {
            if (go != null && ((1 << go.layer) & optimizableLayers.value) != 0)
            {
                // Exclude player, camera, and this generator
                if (go == gameObject || go.CompareTag("Player") || go == Camera.main?.gameObject)
                    continue;

                if (playerTransform != null && go.transform.IsChildOf(playerTransform))
                    continue;

                if (!sceneObjects.Contains(go))
                {
                    sceneObjects.Add(go);
                }
            }
        }
        Debug.Log($"[RuntimeEnvironmentGenerator] Successfully assigned {sceneObjects.Count} assets matching LayerMask from the scene.");
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor helper method to find assets and mark scene/object dirty so it is saved correctly.
    /// </summary>
    public void EditorAssignAllAssetsFromScene()
    {
        UnityEditor.Undo.RecordObject(this, "Assign All Assets From Scene");
        AssignAllAssetsFromScene();
        UnityEditor.EditorUtility.SetDirty(this);
        if (!Application.isPlaying)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    }
#endif

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        // Determine center point (player position at runtime, or generator position in edit mode)
        Vector3 center = transform.position;
        if (Application.isPlaying && playerTransform != null)
        {
            center = playerTransform.position;
        }
        else
        {
            // Try to find player in editor if possible to make editing intuitive
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                center = playerObj.transform.position;
            }
            else if (TargetDirectionController.Instance != null)
            {
                center = TargetDirectionController.Instance.transform.position;
            }
        }

        // Draw activation radius
        Gizmos.color = activationGizmoColor;
        Gizmos.DrawWireSphere(center, defaultActivationRadius);

        // Draw deactivation radius
        Gizmos.color = deactivationGizmoColor;
        Gizmos.DrawWireSphere(center, defaultDeactivationRadius);
    }
}
