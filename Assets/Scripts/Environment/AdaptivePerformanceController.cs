using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Keeps the game at a steady frame rate on whatever phone it runs on.
///
/// Android devices range from flagships to 2 GB budget phones, and a single fixed quality level is
/// either too heavy for the low end (the "laggy, hard to move" reports) or wastes the high end. This
/// controller measures the real frame rate on the device and steps a ladder of quality tiers down
/// (render scale, shadows, terrain grass/flowers, LOD bias, view distance) until the target holds,
/// and back up when FrameTimingManager shows clear headroom. The tier that worked is remembered in
/// PlayerPrefs so the next launch starts there instead of re-learning it with a stutter.
///
/// It bootstraps itself (no scene object needed), only runs on mobile devices or when the "Mobile"
/// quality level is active (so the editor previews it), and restores every global setting it touched
/// when destroyed, so play mode in the editor never leaves the URP asset or QualitySettings modified.
/// </summary>
[DefaultExecutionOrder(200)]
public sealed class AdaptivePerformanceController : MonoBehaviour
{
    public static AdaptivePerformanceController Instance { get; private set; }

    [System.Serializable]
    public struct Tier
    {
        public string name;
        public float renderScale;
        public float shadowDistance;   // 0 = main-light shadows off
        public float detailDistance;   // terrain grass/flowers
        public float detailDensity;
        public float lodBias;
        public float terrainPixelError;
        public float viewDistanceScale; // MobileEnvironmentOptimizer cull bands / far plane / fog
        public int minimapInterval;
    }

    // Tier 0 matches the tuned Mobile settings; every step below trades a little image quality for frame time.
    public static readonly Tier[] Tiers =
    {
        new Tier { name = "High",    renderScale = 0.75f, shadowDistance = 25f, detailDistance = 45f, detailDensity = 0.6f,  lodBias = 0.6f,  terrainPixelError = 10f, viewDistanceScale = 1f,    minimapInterval = 3 },
        new Tier { name = "Medium",  renderScale = 0.68f, shadowDistance = 18f, detailDistance = 35f, detailDensity = 0.45f, lodBias = 0.5f,  terrainPixelError = 15f, viewDistanceScale = 0.85f, minimapInterval = 4 },
        new Tier { name = "Low",     renderScale = 0.6f,  shadowDistance = 12f, detailDistance = 26f, detailDensity = 0.3f,  lodBias = 0.4f,  terrainPixelError = 22f, viewDistanceScale = 0.72f, minimapInterval = 5 },
        new Tier { name = "Lowest",  renderScale = 0.52f, shadowDistance = 0f,  detailDistance = 18f, detailDensity = 0.2f,  lodBias = 0.3f,  terrainPixelError = 32f, viewDistanceScale = 0.6f,  minimapInterval = 6 },
    };

    private const string PrefTier = "perf.tier";
    private const string PrefCap30 = "perf.cap30";
    private const int TargetFps = 60;
    private const int FallbackFps = 30;

    private const float WarmupSeconds = 6f;       // skip scene load / shader warm-up / Addressables hitches
    private const float WindowSeconds = 2f;
    private const float CooldownSeconds = 4f;     // a render-scale change reallocates targets; let it settle
    private const float HitchSeconds = 0.25f;     // single long frames (GC, loading) are not sustained load

    public int CurrentTier { get; private set; } = -1;
    public float MeasuredFps { get; private set; }
    public bool CappedTo30 { get; private set; }

    private float windowTime;
    private int windowFrames;
    private float warmupUntil;
    private float cooldownUntil;
    private int slowWindows, verySlowWindows, fastWindows;

    private readonly FrameTiming[] timings = new FrameTiming[1];
    private double gpuMsSum, cpuMsSum;
    private int timingSamples;

    // ---- originals, restored in OnDestroy
    private UniversalRenderPipelineAsset urp;
    private float origRenderScale, origShadowDistance;
    private float origLodBias, origQualityShadowDistance;
    private float origQDetailDistance, origQDetailDensity, origQPixelError;
    private bool captured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null || !ShouldRun()) return;
        var go = new GameObject("[AdaptivePerformance]");
        DontDestroyOnLoad(go);
        go.AddComponent<AdaptivePerformanceController>();
    }

    private static bool ShouldRun()
    {
        if (Application.isMobilePlatform) return true;
        int level = QualitySettings.GetQualityLevel();
        string[] names = QualitySettings.names;
        return level >= 0 && level < names.Length && names[level] == "Mobile";
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        CaptureOriginals();

        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        int saved = PlayerPrefs.GetInt(PrefTier, -1);
        int device = DeviceStartTier();
#if UNITY_EDITOR
        if (!AdaptInEditor) { saved = -1; device = 0; }
#endif
        // Re-probe one tier better than last session's result: a device that was throttling then may be cool now.
        int start = saved < 0 ? device : Mathf.Max(device, saved - 1);
        CappedTo30 = PlayerPrefs.GetInt(PrefCap30, 0) == 1 && start == Tiers.Length - 1;

        SetTier(start, save: false);
        ResetWindow(WarmupSeconds);
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        RestoreOriginals();
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Terrains, cameras and the environment optimizer are per scene: push the current tier onto the new ones.
        ApplyTier(Tiers[CurrentTier]);
        ResetWindow(WarmupSeconds);
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused) ResetWindow(WarmupSeconds * 0.5f);
    }

    /// <summary>
    /// A rough starting point so a 2-3 GB phone doesn't spend its first seconds stuttering at the High tier.
    /// </summary>
    private static int DeviceStartTier()
    {
        int ram = SystemInfo.systemMemorySize; // MB
        int cores = SystemInfo.processorCount;
        if (ram <= 3200 || cores <= 4) return 2;
        if (ram <= 4600) return 1;
        return 0;
    }

    // ------------------------------------------------------------------ measurement

    private void ResetWindow(float extraDelay)
    {
        windowTime = 0f;
        windowFrames = 0;
        gpuMsSum = cpuMsSum = 0;
        timingSamples = 0;
        warmupUntil = Time.unscaledTime + extraDelay;
    }

    /// <summary>
    /// Editor frame rates (unfocused Game view, editor overhead) say nothing about a phone, so in the editor
    /// the controller only applies the start tier unless this is switched on.
    /// </summary>
    public static bool AdaptInEditor = false;

    private void Update()
    {
#if UNITY_EDITOR
        if (!AdaptInEditor) return;
#endif
        float now = Time.unscaledTime;
        if (now < warmupUntil || now < cooldownUntil) return;

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f || dt > HitchSeconds) return;

        windowTime += dt;
        windowFrames++;
        SampleFrameTiming();

        if (windowTime < WindowSeconds) return;

        MeasuredFps = windowFrames / windowTime;
        EvaluateWindow();
        ResetWindow(0f);
    }

    private void SampleFrameTiming()
    {
        if (!FrameTimingManager.IsFeatureEnabled()) return;
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timings) < 1) return;
        double gpu = timings[0].gpuFrameTime;
        double cpu = System.Math.Max(timings[0].cpuMainThreadFrameTime, timings[0].cpuRenderThreadFrameTime);
        if (gpu <= 0 || cpu <= 0) return;
        gpuMsSum += gpu;
        cpuMsSum += cpu;
        timingSamples++;
    }

    private void EvaluateWindow()
    {
        int target = CappedTo30 ? FallbackFps : TargetFps;

        if (MeasuredFps < target * 0.85f) { slowWindows++; fastWindows = 0; }
        else slowWindows = 0;

        verySlowWindows = MeasuredFps < 36f ? verySlowWindows + 1 : 0;

        // Upgrades need proof of headroom; a capped frame rate alone can't show it.
        if (slowWindows == 0 && timingSamples > 10)
        {
            double budget = 1000.0 / TargetFps;
            double worst = System.Math.Max(gpuMsSum / timingSamples, cpuMsSum / timingSamples);
            fastWindows = worst < budget * 0.6 ? fastWindows + 1 : 0;
        }

        if (CappedTo30)
        {
            // Stay at the stable 30 cap unless frame timings say 60 is now comfortably reachable.
            if (fastWindows >= 5) { CappedTo30 = false; ApplyFrameCap(); Save(); fastWindows = 0; cooldownUntil = Time.unscaledTime + CooldownSeconds; }
            return;
        }

        if (slowWindows >= 2 && CurrentTier < Tiers.Length - 1)
        {
            SetTier(CurrentTier + 1, save: true);
            slowWindows = 0;
        }
        else if (CurrentTier == Tiers.Length - 1 && verySlowWindows >= 3)
        {
            // Even the lowest tier can't get near 60: a locked 30 paces far more evenly than 25-45 jitter.
            CappedTo30 = true;
            ApplyFrameCap();
            Save();
            verySlowWindows = 0;
            cooldownUntil = Time.unscaledTime + CooldownSeconds;
        }
        else if (fastWindows >= 5 && CurrentTier > 0)
        {
            SetTier(CurrentTier - 1, save: true);
            fastWindows = 0;
        }
    }

    // ------------------------------------------------------------------ applying tiers

    private void SetTier(int tier, bool save)
    {
        CurrentTier = Mathf.Clamp(tier, 0, Tiers.Length - 1);
        if (CurrentTier < Tiers.Length - 1) CappedTo30 = false;
        ApplyTier(Tiers[CurrentTier]);
        ApplyFrameCap();
        cooldownUntil = Time.unscaledTime + CooldownSeconds;
        slowWindows = verySlowWindows = fastWindows = 0;
        if (save) Save();
    }

    private void Save()
    {
        PlayerPrefs.SetInt(PrefTier, CurrentTier);
        PlayerPrefs.SetInt(PrefCap30, CappedTo30 ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void ApplyFrameCap()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = CappedTo30 ? FallbackFps : TargetFps;
    }

    private void ApplyTier(Tier t)
    {
        if (urp != null)
        {
            urp.renderScale = Mathf.Min(origRenderScale, t.renderScale);
            urp.shadowDistance = t.shadowDistance > 0f ? Mathf.Min(origShadowDistance, t.shadowDistance) : 0f;
        }

        QualitySettings.lodBias = Mathf.Min(origLodBias, t.lodBias);
        QualitySettings.terrainDetailDistance = Mathf.Min(origQDetailDistance, t.detailDistance);
        QualitySettings.terrainDetailDensityScale = Mathf.Min(origQDetailDensity, t.detailDensity);
        QualitySettings.terrainPixelError = Mathf.Max(origQPixelError, t.terrainPixelError);

        // The Mobile quality level overrides the terrains' own pixel error / detail distance / density
        // (terrainQualityOverrides), so the QualitySettings values above are what the terrains use.

        // Main-light shadows: shadow distance 0 stops URP drawing casters, but turning the camera's shadow
        // rendering off also skips the shadow-map pass and its render-target switch entirely.
        Camera main = Camera.main;
        if (main != null)
        {
            var data = main.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderShadows = t.shadowDistance > 0f;
        }

        var optimizer = FindFirstObjectByType<MobileEnvironmentOptimizer>();
        if (optimizer != null)
        {
            optimizer.SetDistanceScale(t.viewDistanceScale);
            optimizer.minimapRenderInterval = t.minimapInterval;
        }
    }

    private void CaptureOriginals()
    {
        if (urp != null)
        {
            origRenderScale = urp.renderScale;
            origShadowDistance = urp.shadowDistance;
        }
        origLodBias = QualitySettings.lodBias;
        origQualityShadowDistance = QualitySettings.shadowDistance;
        origQDetailDistance = QualitySettings.terrainDetailDistance;
        origQDetailDensity = QualitySettings.terrainDetailDensityScale;
        origQPixelError = QualitySettings.terrainPixelError;
        captured = true;
    }

    private void RestoreOriginals()
    {
        if (!captured) return;
        if (urp != null)
        {
            urp.renderScale = origRenderScale;
            urp.shadowDistance = origShadowDistance;
        }
        QualitySettings.lodBias = origLodBias;
        QualitySettings.shadowDistance = origQualityShadowDistance;
        QualitySettings.terrainDetailDistance = origQDetailDistance;
        QualitySettings.terrainDetailDensityScale = origQDetailDensity;
        QualitySettings.terrainPixelError = origQPixelError;
        Application.targetFrameRate = TargetFps;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>Debug hook: force a tier (e.g. from an MCP script) to preview it.</summary>
    public void ForceTier(int tier) => SetTier(tier, save: false);
#endif
}
