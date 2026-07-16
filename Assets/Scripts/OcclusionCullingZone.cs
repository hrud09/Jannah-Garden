using UnityEngine;

/// <summary>
/// Attach this component to individual GameObjects or parent zone objects
/// to override the global OcclusionCullingManager settings for specific objects.
/// 
/// All child renderers will inherit these settings unless they have their own zone.
/// </summary>
public class OcclusionCullingZone : MonoBehaviour
{
    /// <summary>Priority level for culling. High-priority objects stay visible longer.</summary>
    public enum CullingPriority
    {
        /// <summary>Landmarks, important buildings — last to be culled.</summary>
        High,
        /// <summary>Normal environment objects — default behavior.</summary>
        Medium,
        /// <summary>Small decorations, grass patches — first to be culled.</summary>
        Low
    }

    [Header("Culling Overrides")]
    [Tooltip("If true, this object (and its children) will NEVER be culled by the occlusion system.")]
    public bool neverCull = false;

    [Tooltip("Priority level affects culling order. High-priority objects are culled last.")]
    public CullingPriority priority = CullingPriority.Medium;

    [Header("Distance Overrides")]
    [Tooltip("Use custom near/mid/far distances instead of the global manager values.")]
    public bool useCustomDistances = false;

    [Tooltip("Custom near distance (objects closer than this are always visible).")]
    public float customNearDistance = 15f;

    [Tooltip("Custom mid distance (small objects start being frustum-culled).")]
    public float customMidDistance = 40f;

    [Tooltip("Custom far distance (everything beyond this is culled).")]
    public float customFarDistance = 60f;

    [Header("Shadow Settings")]
    [Tooltip("Custom shadow cutoff distance. 0 = use global setting.")]
    public float customShadowDistance = 0f;

    [Tooltip("If true, at far distance the object renders shadows only (invisible mesh, but casts shadow).")]
    public bool shadowOnlyMode = false;

    [Header("Auto Registration")]
    [Tooltip("Automatically register all child renderers with OcclusionCullingManager on Start.")]
    public bool autoRegister = true;

    private void Start()
    {
        if (autoRegister && OcclusionCullingManager.Instance != null)
        {
            OcclusionCullingManager.Instance.RegisterGameObject(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (OcclusionCullingManager.Instance != null)
        {
            OcclusionCullingManager.Instance.UnregisterGameObject(gameObject);
        }
    }

    // ─── Gizmos for Scene View ───────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!useCustomDistances && !neverCull) return;

        Vector3 center = transform.position;

        if (neverCull)
        {
            // Draw a shield icon (diamond) to indicate never-cull
            Gizmos.color = new Color(0.1f, 0.6f, 1f, 0.6f);
            Gizmos.DrawWireSphere(center, 1.5f);
            Gizmos.DrawIcon(center, "d_Lighting", true);
            return;
        }

        if (useCustomDistances)
        {
            // Near — green
            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(center, customNearDistance);

            // Mid — yellow
            Gizmos.color = new Color(0.95f, 0.85f, 0.1f, 0.2f);
            Gizmos.DrawWireSphere(center, customMidDistance);

            // Far — red
            Gizmos.color = new Color(0.95f, 0.2f, 0.15f, 0.15f);
            Gizmos.DrawWireSphere(center, customFarDistance);
        }

        if (customShadowDistance > 0f)
        {
            Gizmos.color = new Color(0.1f, 0.8f, 0.9f, 0.15f);
            Gizmos.DrawWireSphere(center, customShadowDistance);
        }
    }
}
