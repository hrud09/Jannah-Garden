using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Progressive scene-mesh activator for the Outer Garden scene.
///
/// Workflow:
///  1. In the editor, press "Collect Scene Meshes &amp; Deactivate" on this component
///     (button provided by OcclusionCullingManagerEditor). It gathers every mesh
///     object in the scene into <see cref="managedMeshes"/> and sets them inactive,
///     so the scene starts empty.
///  2. At runtime, while the loading screen still covers the scene, the meshes are
///     re-activated in small batches spread across several frames (cheap on mobile).
///  3. Once every mesh is active, the loading panel is released so it can fade out.
/// </summary>
public class OcclusionCullingManager : MonoBehaviour
{
    public static OcclusionCullingManager Instance { get; private set; }

    // ─── Collection Filter ───────────────────────────────────────────
    [Header("Collection Filter")]
    [Tooltip("Only meshes on these layers are collected by the " +
             "'Collect Scene Meshes & Deactivate' button. Meshes on any other layer are ignored.")]
    public LayerMask meshLayers = ~0;

    // ─── Managed Meshes ──────────────────────────────────────────────
    [Header("Managed Meshes")]
    [Tooltip("Every mesh object that gets activated at scene start. Populate this " +
             "with the 'Collect Scene Meshes & Deactivate' button in the inspector.")]
    public List<GameObject> managedMeshes = new List<GameObject>();

    // ─── Activation ──────────────────────────────────────────────────
    [Header("Activation")]
    [Tooltip("How many meshes to activate per frame while loading. " +
             "Lower = smoother frame pacing, higher = faster reveal.")]
    [Range(1, 500)]
    public int meshesPerFrame = 40;

    [Tooltip("Deactivate every managed mesh in Awake, guaranteeing the scene starts " +
             "empty even if it was accidentally saved with some meshes active.")]
    public bool forceDeactivateOnAwake = true;

    [Tooltip("Keep the loading screen visible until every mesh has been activated, " +
             "so the world is fully populated before the panel fades out.")]
    public bool holdLoadingScreen = true;

    // ─── Debug ───────────────────────────────────────────────────────
    [Header("Debug")]
    [Tooltip("Log activation progress to the console.")]
    public bool logProgress = false;

    // ─── Runtime State ───────────────────────────────────────────────
    /// <summary>True while the activation coroutine is running.</summary>
    public bool IsActivating { get; private set; }
    /// <summary>How many meshes have been activated so far this run.</summary>
    public int ActivatedCount { get; private set; }
    /// <summary>Total number of managed meshes.</summary>
    public int TotalCount => managedMeshes != null ? managedMeshes.Count : 0;

    private Coroutine _activateRoutine;

    // ─── Lifecycle ───────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (forceDeactivateOnAwake)
        {
            DeactivateAll();
        }
    }

    private void Start()
    {
        _activateRoutine = StartCoroutine(ActivateAllRoutine());
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ─── Activation ──────────────────────────────────────────────────

    /// <summary>Immediately sets every managed mesh inactive (no batching).</summary>
    private void DeactivateAll()
    {
        if (managedMeshes == null) return;

        for (int i = 0; i < managedMeshes.Count; i++)
        {
            var go = managedMeshes[i];
            if (go != null && go.activeSelf)
            {
                go.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Re-activates every managed mesh in batches, holding the loading screen open
    /// until it finishes so the reveal stays hidden behind the panel.
    /// </summary>
    private IEnumerator ActivateAllRoutine()
    {
        IsActivating = true;
        ActivatedCount = 0;

        // Hold the loading screen up while we populate the world behind it.
        bool holding = false;
        if (holdLoadingScreen
            && LoadingScreenManager.Instance != null
            && LoadingScreenManager.Instance.IsLoading)
        {
            LoadingScreenManager.Instance.AddLoadHold(this);
            holding = true;
        }

        int perFrame = Mathf.Max(1, meshesPerFrame);
        int inFrame = 0;

        if (managedMeshes != null)
        {
            for (int i = 0; i < managedMeshes.Count; i++)
            {
                var go = managedMeshes[i];
                if (go != null && !go.activeSelf)
                {
                    go.SetActive(true);
                }
                ActivatedCount++;

                if (++inFrame >= perFrame)
                {
                    inFrame = 0;
                    yield return null;
                }
            }
        }

        if (logProgress)
        {
            Debug.Log($"[OcclusionCullingManager] Activated {ActivatedCount}/{TotalCount} meshes.");
        }

        IsActivating = false;
        _activateRoutine = null;

        // Release the loading screen so it can fade out.
        if (holding && LoadingScreenManager.Instance != null)
        {
            LoadingScreenManager.Instance.RemoveLoadHold(this);
        }
    }

#if UNITY_EDITOR
    // ─── Editor Setup ────────────────────────────────────────────────

    /// <summary>
    /// Editor-only: scans the current scene for every mesh object (MeshRenderer or
    /// SkinnedMeshRenderer), stores them in <see cref="managedMeshes"/>, and sets
    /// them inactive so the scene starts empty. Invoked from the inspector button.
    /// </summary>
    public void CollectAndDeactivateSceneMeshes()
    {
        var collected = new List<GameObject>();
        var seen = new HashSet<GameObject>();

        void Consider(Renderer rend)
        {
            if (rend == null) return;
            GameObject go = rend.gameObject;

            if (go == gameObject) return;                           // never manage ourselves
            if (seen.Contains(go)) return;
            if (((1 << go.layer) & meshLayers.value) == 0) return;  // only the chosen layers
            if (go.CompareTag("Player")) return;
            if (go.transform.root.CompareTag("Player")) return;
            if (go.GetComponentInParent<Canvas>() != null) return;  // skip UI renderers

            seen.Add(go);
            collected.Add(go);
        }

        foreach (var r in FindObjectsOfType<MeshRenderer>(true)) Consider(r);
        foreach (var r in FindObjectsOfType<SkinnedMeshRenderer>(true)) Consider(r);

        UnityEditor.Undo.RecordObject(this, "Collect Scene Meshes");
        managedMeshes = collected;
        UnityEditor.EditorUtility.SetDirty(this);

        // Deactivate each collected mesh (recorded for undo).
        foreach (var go in collected)
        {
            if (go.activeSelf)
            {
                UnityEditor.Undo.RecordObject(go, "Deactivate Scene Mesh");
                go.SetActive(false);
                UnityEditor.EditorUtility.SetDirty(go);
            }
        }

        if (!Application.isPlaying)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        Debug.Log($"[OcclusionCullingManager] Collected and deactivated {collected.Count} scene meshes.");
    }

    /// <summary>
    /// Editor-only: re-activates every managed mesh. Handy for editing the scene
    /// with everything visible again after a collect pass.
    /// </summary>
    public void ActivateAllInEditor()
    {
        if (managedMeshes == null) return;

        foreach (var go in managedMeshes)
        {
            if (go != null && !go.activeSelf)
            {
                UnityEditor.Undo.RecordObject(go, "Activate Scene Mesh");
                go.SetActive(true);
                UnityEditor.EditorUtility.SetDirty(go);
            }
        }

        if (!Application.isPlaying)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    }
#endif
}
