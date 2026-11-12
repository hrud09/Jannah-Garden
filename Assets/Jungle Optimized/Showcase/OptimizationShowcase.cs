using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Before/after demo of the jungle optimization, for showing the result to people.
///
/// Put it in a scene that contains BOTH the original jungle and the optimized one
/// (Tools > Jungle Optimized > Set Up Showcase In Open Scene does that), press Play and use the
/// on-screen panel (mouse or touch):
///  - Original / Optimized : switch the whole jungle and watch the live counters change.
///  - Split                : left of the line renders the original, right of it the optimized one,
///                           same camera, same frame. Drag the white line.
///  - View                 : jump the camera between the saved viewpoints (or back to the free camera).
///  - Benchmark            : measures both versions at every viewpoint.
///  - Full Report          : benchmark + same-view screenshots + pixel-difference images, saved as
///                           Report.html (opens in any browser).
/// Keys: 1 / 2 / 3 modes, V next view, B benchmark, R report, H hide panel.
///
/// Demo only: keeping both jungles in one scene costs memory, so do not ship this in the game scene.
/// </summary>
[DefaultExecutionOrder(-1000)] // runs before the MobileEnvironmentOptimizers it switches on/off
[DisallowMultipleComponent]
public class OptimizationShowcase : MonoBehaviour
{
    public enum ViewMode { Original, Optimized, Split }

    [Header("What to compare")]
    [Tooltip("Active only while showing the ORIGINAL (e.g. Environment/Jungle).")]
    public GameObject[] originalObjects;
    [Tooltip("Active only while showing the OPTIMIZED version (e.g. the Jungle (Optimized) prefab instance).")]
    public GameObject[] optimizedObjects;
    [Tooltip("Kept switched off for the whole demo, e.g. a scene-level Mobile Environment Optimizer that would " +
             "otherwise also optimize the original and make the comparison unfair.")]
    public GameObject[] keepOffObjects;
    [Tooltip("Camera used for split view, benchmark and screenshots. Falls back to Camera.main.")]
    public Camera viewCamera;
    [Tooltip("Fixed camera positions for the benchmark and screenshots, so results are repeatable. " +
             "Empty = use wherever the camera is. Add one from the Scene view with the inspector button.")]
    public Transform[] viewpoints;

    [Header("Start-up")]
    public ViewMode startMode = ViewMode.Split;
    [Tooltip("Switch to the phone quality level for the demo, so both versions are judged with the settings the game uses on Android.")]
    public bool useMobileQuality = true;
    public string mobileQualityName = "Mobile";

    [Header("Overlay")]
    public bool showOverlay = true;
    [Range(0.5f, 2.5f)] public float overlayScale = 1f;
    [Range(0.05f, 0.95f)] public float splitPosition = 0.5f;

    [Header("Benchmark / report")]
    [Tooltip("Seconds to wait after every switch before measuring (culling, particles and streaming settle).")]
    public float warmupSeconds = 1.5f;
    [Tooltip("Seconds measured per version per viewpoint (split into two halves, measured A-B-B-A to cancel drift).")]
    public float sampleSeconds = 4f;
    [Tooltip("Screenshot width in pixels (height follows the screen's aspect).")]
    public int screenshotWidth = 1600;
    [Tooltip("Pause time while taking screenshots so wind and particles are frozen and the images line up exactly.")]
    public bool freezeTimeForScreenshots = true;
    public string reportTitle = "Jungle optimization (Android)";

    // ------------------------------------------------------------------ results

    [Serializable]
    public class BenchmarkResult
    {
        public bool valid;
        public int frames;
        public float avgFps, avgFrameMs, slowFrameMs, mainThreadMs;
        public float drawCalls, setPassCalls, batches, triangles, vertices, shadowCasters;
        public float visibleRenderers, activeLights;
    }

    public class ViewResult
    {
        public string name;
        public string filePrefix;
        public BenchmarkResult original = new BenchmarkResult(), optimized = new BenchmarkResult();
        public float noticeablePixelPercent = -1f, meanPixelDifference = -1f;
        public bool hasImages;
    }

    public ViewMode Mode { get; private set; }
    public bool IsBusy => busy != null;
    public string Status { get; private set; } = "";
    public BenchmarkResult OriginalResult { get; private set; } = new BenchmarkResult();   // average over views
    public BenchmarkResult OptimizedResult { get; private set; } = new BenchmarkResult();
    public List<ViewResult> ViewResults { get; } = new List<ViewResult>();
    public EnvironmentContentStats OriginalContent { get; private set; }
    public EnvironmentContentStats OptimizedContent { get; private set; }
    public float NoticeablePixelPercent { get; private set; } = -1f;                     // average over views
    public string LastReportFolder { get; private set; }
    /// <summary>-1 = free camera, otherwise the index of the viewpoint the camera is pinned to.</summary>
    public int CurrentView { get; private set; } = -1;

    // ------------------------------------------------------------------ state

    private bool initialized;
    private Coroutine busy;
    private bool overlayHiddenForWork;
    private bool panelCollapsed;
    private int previousQuality = -1;
    private readonly List<GameObject> switchedOff = new List<GameObject>();

    private Renderer[] originalRenderers = new Renderer[0], optimizedRenderers = new Renderer[0];
    private Light[] originalLights = new Light[0], optimizedLights = new Light[0];

    // camera pinning
    private Transform pinned;
    private Vector3 savedCamLocalPos;
    private Quaternion savedCamLocalRot;
    private readonly List<MonoBehaviour> pausedCameraScripts = new List<MonoBehaviour>();

    // split view
    private Camera splitCamera;
    private Rect savedRect;
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private readonly List<Light> hiddenLights = new List<Light>();
    private bool draggingDivider;

    // live counters
    private ProfilerRecorder recDraw, recSetPass, recBatches, recTris, recVerts, recShadow, recMainThread;
    private readonly float[] frameTimes = new float[60];
    private int frameIndex, frameCount;
    private int liveVisible, liveLights;
    private float nextVisibleCount;

    // ================================================================== lifecycle

    private void Awake()
    {
        if (IsConfigured) Init();
    }

    private void Start()
    {
        if (!initialized) Init();
    }

    private bool IsConfigured =>
        (originalObjects != null && originalObjects.Length > 0) || (optimizedObjects != null && optimizedObjects.Length > 0);

    private void Init()
    {
        if (!IsConfigured)
        {
            Debug.LogWarning("[Optimization Showcase] Nothing to compare: fill 'Original Objects' and 'Optimized Objects' " +
                             "(or run Tools > Jungle Optimized > Set Up Showcase In Open Scene).", this);
            enabled = false;
            return;
        }
        initialized = true;
        if (viewCamera == null) viewCamera = Camera.main;

        foreach (GameObject go in Safe(keepOffObjects))
            if (go.activeSelf) { go.SetActive(false); switchedOff.Add(go); }

        if (useMobileQuality)
        {
            int index = Array.IndexOf(QualitySettings.names, mobileQualityName);
            if (index >= 0 && index != QualitySettings.GetQualityLevel())
            {
                previousQuality = QualitySettings.GetQualityLevel();
                QualitySettings.SetQualityLevel(index, true);
            }
        }

        originalRenderers = CollectRenderers(originalObjects);
        optimizedRenderers = CollectRenderers(optimizedObjects);
        originalLights = CollectLights(originalObjects);
        optimizedLights = CollectLights(optimizedObjects);

        OriginalContent = EnvironmentContentStats.Analyze(Safe(originalObjects));
        OptimizedContent = EnvironmentContentStats.Analyze(Safe(optimizedObjects));

        ApplyMode(startMode, true);
    }

    private void OnEnable()
    {
        recDraw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        recSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        recBatches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        recTris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        recVerts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
        recShadow = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
        recMainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
    }

    private void OnDisable()
    {
        ExitSplit();
        Unpin();
        recDraw.Dispose(); recSetPass.Dispose(); recBatches.Dispose(); recTris.Dispose();
        recVerts.Dispose(); recShadow.Dispose(); recMainThread.Dispose();
    }

    private void OnDestroy()
    {
        ExitSplit();
        Unpin();
        if (busy != null && Time.timeScale == 0f) Time.timeScale = 1f;
        if (previousQuality >= 0) QualitySettings.SetQualityLevel(previousQuality, true);
        previousQuality = -1;
        foreach (GameObject go in switchedOff) if (go != null) go.SetActive(true);
        switchedOff.Clear();
    }

    private void Update()
    {
        frameTimes[frameIndex] = Time.unscaledDeltaTime;
        frameIndex = (frameIndex + 1) % frameTimes.Length;
        frameCount = Mathf.Min(frameCount + 1, frameTimes.Length);

        if (Time.unscaledTime >= nextVisibleCount)
        {
            nextVisibleCount = Time.unscaledTime + 0.5f;
            liveVisible = CountVisible(ActiveRenderers());
            liveLights = CountActiveLights(ActiveLights());
        }
    }

    private void LateUpdate()
    {
        ApplyPin();

        if (Mode == ViewMode.Split && splitCamera != null && viewCamera != null)
        {
            viewCamera.rect = SubRect(0f, splitPosition);
            splitCamera.rect = SubRect(splitPosition, 1f);
            splitCamera.fieldOfView = viewCamera.fieldOfView;
            splitCamera.nearClipPlane = viewCamera.nearClipPlane;
            splitCamera.farClipPlane = viewCamera.farClipPlane;
        }
    }

    // ================================================================== modes

    public void SetMode(ViewMode mode)
    {
        if (!initialized || IsBusy) return;
        ApplyMode(mode, false);
    }

    private void ApplyMode(ViewMode mode, bool force)
    {
        if (!force && mode == Mode) return;
        if (Mode == ViewMode.Split) ExitSplit();

        bool wantOriginal = mode != ViewMode.Optimized;
        bool wantOptimized = mode != ViewMode.Original;

        // Switch off first (optimizers revert their changes), then switch on.
        if (!wantOptimized) SetActive(optimizedObjects, false);
        if (!wantOriginal) SetActive(originalObjects, false);
        if (wantOriginal) SetActive(originalObjects, true);
        if (wantOptimized) SetActive(optimizedObjects, true);

        Mode = mode;
        if (mode == ViewMode.Split) EnterSplit();
        nextVisibleCount = 0f;
    }

    private static void SetActive(GameObject[] objects, bool on)
    {
        if (objects == null) return;
        if (on) { for (int i = 0; i < objects.Length; i++) if (objects[i] != null) objects[i].SetActive(true); }
        else { for (int i = objects.Length - 1; i >= 0; i--) if (objects[i] != null) objects[i].SetActive(false); }
    }

    // ================================================================== viewpoints

    private List<Transform> ValidViewpoints()
    {
        var list = new List<Transform>();
        if (viewpoints != null) foreach (Transform t in viewpoints) if (t != null) list.Add(t);
        return list;
    }

    /// <summary>Cycles free camera -> viewpoint 1 -> viewpoint 2 ... -> free camera.</summary>
    public void NextView()
    {
        if (!initialized || IsBusy) return;
        List<Transform> views = ValidViewpoints();
        if (views.Count == 0) return;
        int next = CurrentView + 1;
        if (next >= views.Count) { Unpin(); CurrentView = -1; }
        else { Pin(views[next]); CurrentView = next; }
    }

    private void Pin(Transform viewpoint)
    {
        if (viewCamera == null || viewpoint == null) return;
        if (pinned == null)
        {
            Transform cam = viewCamera.transform;
            savedCamLocalPos = cam.localPosition;
            savedCamLocalRot = cam.localRotation;
            // Camera controllers on the camera itself would fight the pinned pose.
            foreach (MonoBehaviour mb in viewCamera.GetComponents<MonoBehaviour>())
            {
                if (mb == null || !mb.enabled || mb is UniversalAdditionalCameraData) continue;
                mb.enabled = false;
                pausedCameraScripts.Add(mb);
            }
            RenderPipelineManager.beginContextRendering += OnBeginContext;
        }
        pinned = viewpoint;
        ApplyPin();
    }

    private void Unpin()
    {
        if (pinned == null) return;
        pinned = null;
        RenderPipelineManager.beginContextRendering -= OnBeginContext;
        if (viewCamera != null)
        {
            viewCamera.transform.localPosition = savedCamLocalPos;
            viewCamera.transform.localRotation = savedCamLocalRot;
        }
        foreach (MonoBehaviour mb in pausedCameraScripts) if (mb != null) mb.enabled = true;
        pausedCameraScripts.Clear();
    }

    private void ApplyPin()
    {
        if (pinned != null && viewCamera != null)
            viewCamera.transform.SetPositionAndRotation(pinned.position, pinned.rotation);
    }

    // The player rig (parent of the camera) may still move; re-assert the pose right before rendering.
    private void OnBeginContext(ScriptableRenderContext context, List<Camera> cameras) => ApplyPin();

    // ================================================================== split view

    private void EnterSplit()
    {
        if (viewCamera == null || splitCamera != null) return;

        savedRect = viewCamera.rect;
        var go = new GameObject("Showcase Split Camera (temporary)") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(viewCamera.transform, false);
        splitCamera = go.AddComponent<Camera>();
        splitCamera.CopyFrom(viewCamera);
        splitCamera.depth = viewCamera.depth + 0.01f;

        var src = viewCamera.GetUniversalAdditionalCameraData();
        var dst = splitCamera.GetUniversalAdditionalCameraData();
        if (src != null && dst != null)
        {
            dst.renderPostProcessing = src.renderPostProcessing;
            dst.antialiasing = src.antialiasing;
            dst.antialiasingQuality = src.antialiasingQuality;
            dst.renderShadows = src.renderShadows;
            dst.volumeLayerMask = src.volumeLayerMask;
            dst.volumeTrigger = src.volumeTrigger;
            dst.dithering = src.dithering;
            dst.stopNaN = src.stopNaN;
            dst.requiresColorOption = src.requiresColorOption;
            dst.requiresDepthOption = src.requiresDepthOption;
        }

        LateUpdate();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    private void ExitSplit()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        RestoreHidden();
        if (splitCamera == null) return;
        Destroy(splitCamera.gameObject);
        splitCamera = null;
        if (viewCamera != null)
        {
            viewCamera.rect = savedRect;
            viewCamera.ResetProjectionMatrix();
        }
    }

    private Rect SubRect(float x0, float x1) =>
        new Rect(savedRect.x + savedRect.width * x0, savedRect.y, savedRect.width * (x1 - x0), savedRect.height);

    // Each half renders its slice of the FULL view (off-centre frustum), so the two halves join seamlessly.
    private void SetSliceProjection(Camera cam, float x0, float x1)
    {
        float w = viewCamera.targetTexture != null ? viewCamera.targetTexture.width : Screen.width;
        float h = viewCamera.targetTexture != null ? viewCamera.targetTexture.height : Screen.height;
        float aspect = (w * savedRect.width) / Mathf.Max(1f, h * savedRect.height);
        float n = viewCamera.nearClipPlane, f = viewCamera.farClipPlane;
        float top = n * Mathf.Tan(0.5f * viewCamera.fieldOfView * Mathf.Deg2Rad);
        float right = top * aspect;
        cam.projectionMatrix = Matrix4x4.Frustum(Mathf.Lerp(-right, right, x0), Mathf.Lerp(-right, right, x1), -top, top, n, f);
    }

    private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam.cameraType != CameraType.Game || Mode != ViewMode.Split) return;
        RestoreHidden();

        if (cam == viewCamera)
        {
            SetSliceProjection(cam, 0f, splitPosition);
            Hide(optimizedRenderers, optimizedLights);
        }
        else if (cam == splitCamera)
        {
            SetSliceProjection(cam, splitPosition, 1f);
            Hide(originalRenderers, originalLights);
        }
        else
        {
            Hide(originalRenderers, originalLights); // minimap etc. show the optimized jungle only
        }
    }

    private void OnEndCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam.cameraType == CameraType.Game) RestoreHidden();
    }

    private void Hide(Renderer[] renderers, Light[] lights)
    {
        foreach (Renderer r in renderers)
            if (r != null && !r.forceRenderingOff) { r.forceRenderingOff = true; hiddenRenderers.Add(r); }
        foreach (Light l in lights)
            if (l != null && l.enabled) { l.enabled = false; hiddenLights.Add(l); }
    }

    private void RestoreHidden()
    {
        foreach (Renderer r in hiddenRenderers) if (r != null) r.forceRenderingOff = false;
        foreach (Light l in hiddenLights) if (l != null) l.enabled = true;
        hiddenRenderers.Clear();
        hiddenLights.Clear();
    }

    // ================================================================== benchmark & report

    public void RunBenchmark()
    {
        if (initialized && !IsBusy) busy = StartCoroutine(Work(false));
    }

    public void RunFullReport()
    {
        if (initialized && !IsBusy) busy = StartCoroutine(Work(true));
    }

    private class Sampler
    {
        public readonly List<float> times = new List<float>();
        public double draw, setPass, batches, tris, verts, shadow, mainMs;
        public double visible, lights;
        public int segments;
    }

    private IEnumerator Work(bool fullReport)
    {
        ViewMode modeBefore = Mode;
        int viewBefore = CurrentView;
        Transform pinnedBefore = pinned;
        string folder = fullReport ? MakeReportFolder() : null;
        ViewResults.Clear();
        NoticeablePixelPercent = -1f;

        try
        {
            List<Transform> views = ValidViewpoints();
            if (views.Count == 0) views.Add(null); // current camera view

            for (int v = 0; v < views.Count; v++)
            {
                Transform view = views[v];
                if (view != null) Pin(view);
                var vr = new ViewResult
                {
                    name = view != null ? view.name : "Current view",
                    filePrefix = "View " + (v + 1),
                };
                string where = views.Count > 1 ? " (view " + (v + 1) + "/" + views.Count + ")" : "";

                // A-B-B-A: anything that drifts over time (spawning butterflies, streaming...) cancels out.
                var a = new Sampler();
                var b = new Sampler();
                float half = sampleSeconds * 0.5f;
                yield return Measure(ViewMode.Original, a, half, where);
                yield return Measure(ViewMode.Optimized, b, half, where);
                yield return Measure(ViewMode.Optimized, b, half, where);
                yield return Measure(ViewMode.Original, a, half, where);
                vr.original = Finish(a);
                vr.optimized = Finish(b);

                if (fullReport) yield return CaptureScreenshots(folder, vr, where);
                ViewResults.Add(vr);
            }

            OriginalResult = Average(ViewResults, true);
            OptimizedResult = Average(ViewResults, false);
            float diffSum = 0f;
            int diffCount = 0;
            foreach (ViewResult vr in ViewResults)
                if (vr.hasImages) { diffSum += vr.noticeablePixelPercent; diffCount++; }
            NoticeablePixelPercent = diffCount > 0 ? diffSum / diffCount : -1f;

            if (fullReport)
            {
                WriteHtmlReport(folder);
                LastReportFolder = folder;
                Status = "Report saved: " + folder;
                Debug.Log("[Optimization Showcase] Report saved to " + folder);
                Application.OpenURL(new Uri(Path.Combine(folder, "Report.html")).AbsoluteUri);
            }
            else
            {
                Status = "Benchmark done (" + ViewResults.Count + " view" + (ViewResults.Count == 1 ? "" : "s") + ").";
            }
        }
        finally
        {
            overlayHiddenForWork = false;
            busy = null;
            if (pinnedBefore != null) Pin(pinnedBefore); else Unpin();
            CurrentView = viewBefore;
            ApplyMode(modeBefore, true);
        }
    }

    private IEnumerator Measure(ViewMode mode, Sampler s, float seconds, string where)
    {
        ApplyMode(mode, true);
        Status = "Measuring " + mode.ToString().ToUpperInvariant() + where + "...";
        yield return new WaitForSecondsRealtime(warmupSeconds);

        overlayHiddenForWork = true; // the panel itself must not end up in the numbers
        yield return null;
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end)
        {
            yield return null;
            s.times.Add(Time.unscaledDeltaTime);
            s.draw += recDraw.LastValue; s.setPass += recSetPass.LastValue; s.batches += recBatches.LastValue;
            s.tris += recTris.LastValue; s.verts += recVerts.LastValue; s.shadow += recShadow.LastValue;
            s.mainMs += AverageNs(recMainThread) * 1e-6;
        }
        overlayHiddenForWork = false;
        s.visible += CountVisible(ActiveRenderers());
        s.lights += CountActiveLights(ActiveLights());
        s.segments++;
    }

    private static BenchmarkResult Finish(Sampler s)
    {
        var r = new BenchmarkResult();
        int n = s.times.Count;
        if (n == 0) return r;
        float total = 0f;
        foreach (float t in s.times) total += t;
        s.times.Sort();

        r.valid = true;
        r.frames = n;
        r.avgFrameMs = total / n * 1000f;
        r.avgFps = total > 0f ? n / total : 0f;
        r.slowFrameMs = s.times[Mathf.Clamp(Mathf.CeilToInt(n * 0.99f) - 1, 0, n - 1)] * 1000f;
        r.mainThreadMs = (float)(s.mainMs / n);
        r.drawCalls = (float)(s.draw / n);
        r.setPassCalls = (float)(s.setPass / n);
        r.batches = (float)(s.batches / n);
        r.triangles = (float)(s.tris / n);
        r.vertices = (float)(s.verts / n);
        r.shadowCasters = (float)(s.shadow / n);
        r.visibleRenderers = (float)(s.visible / Mathf.Max(1, s.segments));
        r.activeLights = (float)(s.lights / Mathf.Max(1, s.segments));
        return r;
    }

    private static BenchmarkResult Average(List<ViewResult> views, bool original)
    {
        var r = new BenchmarkResult();
        int n = 0;
        foreach (ViewResult v in views)
        {
            BenchmarkResult x = original ? v.original : v.optimized;
            if (!x.valid) continue;
            n++;
            r.frames += x.frames;
            r.avgFps += x.avgFps; r.avgFrameMs += x.avgFrameMs; r.slowFrameMs += x.slowFrameMs; r.mainThreadMs += x.mainThreadMs;
            r.drawCalls += x.drawCalls; r.setPassCalls += x.setPassCalls; r.batches += x.batches;
            r.triangles += x.triangles; r.vertices += x.vertices; r.shadowCasters += x.shadowCasters;
            r.visibleRenderers += x.visibleRenderers; r.activeLights += x.activeLights;
        }
        if (n == 0) return r;
        r.valid = true;
        r.avgFps /= n; r.avgFrameMs /= n; r.slowFrameMs /= n; r.mainThreadMs /= n;
        r.drawCalls /= n; r.setPassCalls /= n; r.batches /= n;
        r.triangles /= n; r.vertices /= n; r.shadowCasters /= n;
        r.visibleRenderers /= n; r.activeLights /= n;
        return r;
    }

    // ------------------------------------------------------------------ screenshots

    private IEnumerator CaptureScreenshots(string folder, ViewResult vr, string where)
    {
        Status = "Taking screenshots" + where + "...";
        overlayHiddenForWork = true;
        float oldScale = Time.timeScale;
        if (freezeTimeForScreenshots) Time.timeScale = 0f;

        int w = Mathf.Max(256, screenshotWidth);
        int h = Mathf.Max(144, Mathf.RoundToInt(w * (float)Screen.height / Mathf.Max(1, Screen.width)));
        Texture2D orig = null, opt = null;
        try
        {
            ApplyMode(ViewMode.Original, true);
            for (int i = 0; i < 10; i++) yield return null;
            orig = RenderView(w, h);

            ApplyMode(ViewMode.Optimized, true);
            for (int i = 0; i < 10; i++) yield return null;
            opt = RenderView(w, h);

            Save(folder, vr.filePrefix + " - Original.png", orig);
            Save(folder, vr.filePrefix + " - Optimized.png", opt);
            BuildComparisonImages(folder, vr, orig, opt);
            vr.hasImages = true;
        }
        finally
        {
            if (freezeTimeForScreenshots) Time.timeScale = oldScale;
            overlayHiddenForWork = false;
            if (orig != null) Destroy(orig);
            if (opt != null) Destroy(opt);
        }
    }

    // Renders the camera straight into a texture: the 3D view only, without the game's UI on top.
    private Texture2D RenderView(int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = viewCamera.targetTexture;
        viewCamera.targetTexture = rt;
        viewCamera.Render();
        viewCamera.targetTexture = previousTarget;

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply(false);
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    private void BuildComparisonImages(string folder, ViewResult vr, Texture2D a, Texture2D b)
    {
        int w = a.width, h = a.height;
        Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();

        // Pixel difference
        var diff = new Color32[pa.Length];
        long noticeable = 0;
        double sum = 0;
        const int threshold = 20; // out of 255 (~8%): smaller differences are not visible on a phone screen
        for (int i = 0; i < pa.Length; i++)
        {
            int d = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Max(Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b)));
            sum += d;
            int grey = ((pa[i].r * 77 + pa[i].g * 150 + pa[i].b * 29) >> 8) * 35 / 100;
            if (d > threshold)
            {
                noticeable++;
                diff[i] = new Color32((byte)Mathf.Min(255, 120 + d * 2), (byte)(grey / 2), (byte)(grey / 2), 255);
            }
            else diff[i] = new Color32((byte)grey, (byte)grey, (byte)grey, 255);
        }
        vr.noticeablePixelPercent = 100f * noticeable / pa.Length;
        vr.meanPixelDifference = (float)(100.0 * sum / pa.Length / 255.0);
        SavePixels(folder, vr.filePrefix + " - Difference.png", w, h, diff);

        // Split: left half original, right half optimized
        var split = new Color32[pa.Length];
        int mid = w / 2;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                split[i] = Mathf.Abs(x - mid) <= 1 ? new Color32(255, 255, 255, 255) : (x < mid ? pa[i] : pb[i]);
            }
        SavePixels(folder, vr.filePrefix + " - Split.png", w, h, split);
    }

    private static void Save(string folder, string name, Texture2D tex) =>
        File.WriteAllBytes(Path.Combine(folder, name), tex.EncodeToPNG());

    private static void SavePixels(string folder, string name, int w, int h, Color32[] pixels)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.SetPixels32(pixels);
        tex.Apply(false);
        Save(folder, name, tex);
        Destroy(tex);
    }

    private static string MakeReportFolder()
    {
        string root = Application.isEditor
            ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "OptimizationShowcase")
            : Path.Combine(Application.persistentDataPath, "OptimizationShowcase");
        string folder = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        return folder;
    }

    // ------------------------------------------------------------------ HTML report

    private void WriteHtmlReport(string folder)
    {
        if (OriginalContent.renderers == 0 && OptimizedContent.renderers == 0) // e.g. lost to a script reload during Play
        {
            OriginalContent = EnvironmentContentStats.Analyze(Safe(originalObjects));
            OptimizedContent = EnvironmentContentStats.Analyze(Safe(optimizedObjects));
        }

        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        BenchmarkResult a = OriginalResult, b = OptimizedResult;
        EnvironmentContentStats ca = OriginalContent, cb = OptimizedContent;
        bool multi = ViewResults.Count > 1;

        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>").Append(Html(reportTitle)).Append("</title><style>");
        sb.Append(":root{--bg:#f4f6f4;--card:#fff;--ink:#1d241f;--muted:#5b675f;--line:#e3e8e4;--head:#e9efea;--good:#1f7a3a;--bad:#b3261e}");
        sb.Append("@media(prefers-color-scheme:dark){:root{--bg:#141815;--card:#1e2420;--ink:#e6ece7;--muted:#9aa79e;--line:#2c342e;--head:#262e28;--good:#6fd28a;--bad:#ff8a80}}");
        sb.Append("body{font-family:Segoe UI,Roboto,Arial,sans-serif;margin:0;background:var(--bg);color:var(--ink)}");
        sb.Append("main{max-width:1100px;margin:0 auto;padding:24px 16px 48px}h1{margin:0 0 4px;font-size:28px}");
        sb.Append("h2{margin:40px 0 10px;font-size:21px}h3{margin:22px 0 8px;font-size:17px}.sub{color:var(--muted);margin:0 0 20px}");
        sb.Append(".kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px}");
        sb.Append(".kpi{background:var(--card);border-radius:10px;padding:14px 16px;box-shadow:0 1px 3px rgba(0,0,0,.1)}");
        sb.Append(".kpi b{display:block;font-size:30px;color:var(--good)}.kpi span{color:var(--muted);font-size:14px}");
        sb.Append(".wrap{overflow-x:auto}table{width:100%;border-collapse:collapse;background:var(--card);border-radius:10px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,.1)}");
        sb.Append("th,td{padding:9px 12px;text-align:right;border-bottom:1px solid var(--line);font-variant-numeric:tabular-nums;white-space:nowrap}");
        sb.Append("th:first-child,td:first-child{text-align:left;white-space:normal}th{background:var(--head);font-size:14px}");
        sb.Append(".good{color:var(--good);font-weight:600}.bad{color:var(--bad);font-weight:600}.same{color:var(--muted)}");
        sb.Append("figure{margin:0 0 14px}figure img{width:100%;border-radius:8px;display:block}figcaption{color:var(--muted);font-size:14px;margin-top:6px}");
        sb.Append(".two{display:grid;grid-template-columns:1fr 1fr;gap:12px}@media(max-width:700px){.two{grid-template-columns:1fr}}");
        sb.Append("ul{line-height:1.6;padding-left:20px}.note{color:var(--muted);font-size:13px}</style></head><body><main>");

        sb.Append("<h1>").Append(Html(reportTitle)).Append("</h1>");
        sb.Append("<p class=\"sub\">").Append(DateTime.Now.ToString("d MMMM yyyy, HH:mm", ci))
          .Append(" &middot; quality level: ").Append(Html(QualitySettings.names[QualitySettings.GetQualityLevel()]))
          .Append(" &middot; ").Append(Html(Application.isEditor ? "measured in the Unity editor" : SystemInfo.deviceModel))
          .Append(" &middot; ").Append(Html(SystemInfo.graphicsDeviceName)).Append("</p>");

        // Headline numbers
        sb.Append("<div class=\"kpis\">");
        if (a.valid && b.valid)
        {
            Kpi(sb, Pct(a.drawCalls, b.drawCalls), "fewer draw calls");
            Kpi(sb, Pct(a.shadowCasters, b.shadowCasters), "fewer shadow casters");
            Kpi(sb, Pct(a.visibleRenderers, b.visibleRenderers), "fewer objects rendered");
            Kpi(sb, Math.Round(a.activeLights).ToString(ci) + " &rarr; " + Math.Round(b.activeLights).ToString(ci), "lantern lights switched on");
        }
        if (NoticeablePixelPercent >= 0f)
            Kpi(sb, (100f - NoticeablePixelPercent).ToString("0.0", ci) + "%", "of the picture is visually identical");
        sb.Append("</div>");
        if (multi) sb.Append("<p class=\"note\">Averages over ").Append(ViewResults.Count).Append(" camera views.</p>");
        if (NoticeablePixelPercent >= 0f)
            sb.Append("<p class=\"note\">Where the pictures differ, it is mostly: distant trees fading into the fog sooner (the phone draw distance is 125 m), " +
                      "small flowers near the camera that the original hid after a few metres and now stay visible, and only the nearest " +
                      "lantern lights being switched on.</p>");

        // Totals
        if (a.valid && b.valid)
        {
            sb.Append("<h2>Rendering cost per frame").Append(multi ? " (average of all views)" : "").Append("</h2>");
            BenchmarkTable(sb, a, b);
            sb.Append("<p class=\"note\">Each version was measured for ").Append(sampleSeconds.ToString("0.#", ci))
              .Append(" s per view, in the order original, optimized, optimized, original, so anything that changes over time cancels out. ");
            if (Application.isEditor)
                sb.Append("Measured in the Unity editor with the phone quality level: draw calls, triangles, shadows and lights carry over to the phone. " +
                          "Frame times depend on this PC and editor overhead, and the game caps itself at 60 FPS, so a fast PC runs both versions near the cap; " +
                          "the frame-time gain shows on phones. Draw calls include every camera and the shadow map.");
            sb.Append("</p>");
        }

        // Per view
        foreach (ViewResult vr in ViewResults)
        {
            sb.Append("<h2>").Append(Html(vr.name)).Append("</h2>");
            if (vr.hasImages)
            {
                string p = Uri.EscapeDataString(vr.filePrefix);
                sb.Append("<figure><img src=\"").Append(p).Append("%20-%20Split.png\" alt=\"Left half original, right half optimized\"><figcaption>Left of the line: original. Right of the line: optimized. Same camera, same moment.</figcaption></figure>");
                sb.Append("<div class=\"two\"><figure><img src=\"").Append(p).Append("%20-%20Original.png\" alt=\"Original\"><figcaption>Original</figcaption></figure>");
                sb.Append("<figure><img src=\"").Append(p).Append("%20-%20Optimized.png\" alt=\"Optimized\"><figcaption>Optimized</figcaption></figure></div>");
                sb.Append("<figure><img src=\"").Append(p).Append("%20-%20Difference.png\" alt=\"Pixel difference\"><figcaption>Red = pixels that changed noticeably (")
                  .Append(vr.noticeablePixelPercent.ToString("0.0", ci)).Append("% of the picture, average colour difference ")
                  .Append(vr.meanPixelDifference.ToString("0.0", ci)).Append("%). Grey = no visible change.</figcaption></figure>");
            }
            if (multi && vr.original.valid && vr.optimized.valid)
            {
                sb.Append("<h3>Rendering cost from this view</h3>");
                BenchmarkTable(sb, vr.original, vr.optimized);
            }
        }

        // Content
        sb.Append("<h2>What the jungle is made of</h2><div class=\"wrap\"><table><tr><th>Whole jungle</th><th>Original</th><th>Optimized</th><th>Change</th></tr>");
        Row(sb, "Renderers (all detail levels)", ca.renderers, cb.renderers, true, false);
        Row(sb, "Draw calls if everything were on screen", ca.drawItems, cb.drawItems, true, false);
        Row(sb, "Triangles at the closest detail level", ca.triangles, cb.triangles, true, false);
        Row(sb, "Objects casting shadows", ca.shadowCasters, cb.shadowCasters, true, false);
        Row(sb, "GameObjects", ca.gameObjects, cb.gameObjects, true, false);
        Row(sb, "Particle budget (max particles)", ca.maxParticles, cb.maxParticles, true, false);
        Row(sb, "Unique meshes", ca.uniqueMeshes, cb.uniqueMeshes, true, false);
        Row(sb, "Mesh memory (MB, estimated)", ca.meshBytes / 1048576.0, cb.meshBytes / 1048576.0, true, true);
        Row(sb, "Materials", ca.uniqueMaterials, cb.uniqueMaterials, true, false);
        sb.Append("</table></div><p class=\"note\">Mesh memory: merging the small plants adds mesh data (they no longer share one mesh each), " +
                  "and no longer using the heaviest tree detail levels removes some. Estimated from the meshes each version references.</p>");

        // What was done
        sb.Append("<h2>What was optimized</h2><ul>");
        sb.Append("<li><b>Undergrowth merged:</b> 1,751 separate plants baked into 138 chunks, so each chunk is drawn in one call and culled as a whole.</li>");
        sb.Append("<li><b>Draw distance tuned for phones:</b> trees, rocks, mushrooms and props stop drawing at distances hidden by fog.</li>");
        sb.Append("<li><b>Shadows trimmed:</b> trees keep their shadows; small props, where shadows are barely visible, no longer cast them.</li>");
        sb.Append("<li><b>Heavy trees:</b> the most detailed version of the 7k&ndash;23k triangle trees is skipped on mobile.</li>");
        sb.Append("<li><b>Lantern lights:</b> only the 4 nearest of 22 realtime lights are on at any time.</li>");
        sb.Append("<li><b>Particles:</b> fireflies and falling leaves pause when not visible; particle pools capped to what they use.</li>");
        sb.Append("<li><b>Mobile shader:</b> same look and wind, fewer shader variants and less work per pixel.</li>");
        sb.Append("<li><b>Unchanged:</b> layout, colliders and gameplay. The original asset packs were not modified.</li>");
        sb.Append("</ul></main></body></html>");

        File.WriteAllText(Path.Combine(folder, "Report.html"), sb.ToString(), new UTF8Encoding(false));
    }

    private static void BenchmarkTable(StringBuilder sb, BenchmarkResult a, BenchmarkResult b)
    {
        sb.Append("<div class=\"wrap\"><table><tr><th>Per frame</th><th>Original</th><th>Optimized</th><th>Change</th></tr>");
        Row(sb, "Draw calls", a.drawCalls, b.drawCalls, true, false);
        Row(sb, "Batches", a.batches, b.batches, true, false);
        Row(sb, "SetPass calls (GPU state changes)", a.setPassCalls, b.setPassCalls, true, false);
        Row(sb, "Triangles", a.triangles, b.triangles, true, false);
        Row(sb, "Vertices", a.vertices, b.vertices, true, false);
        Row(sb, "Shadow casters", a.shadowCasters, b.shadowCasters, true, false);
        Row(sb, "Jungle objects rendered", a.visibleRenderers, b.visibleRenderers, true, false);
        Row(sb, "Lantern lights switched on", a.activeLights, b.activeLights, true, false);
        Row(sb, "Frame time (ms)", a.avgFrameMs, b.avgFrameMs, true, true);
        Row(sb, "Slowest 1% of frames (ms)", a.slowFrameMs, b.slowFrameMs, true, true);
        Row(sb, "Frames per second", a.avgFps, b.avgFps, false, true);
        sb.Append("</table></div>");
    }

    private static void Kpi(StringBuilder sb, string value, string label) =>
        sb.Append("<div class=\"kpi\"><b>").Append(value).Append("</b><span>").Append(label).Append("</span></div>");

    private static string Pct(double before, double after)
    {
        if (before <= 0) return "&ndash;";
        return Math.Round(100.0 * (1.0 - after / before)).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private static void Row(StringBuilder sb, string name, double before, double after, bool lowerIsBetter, bool decimals)
    {
        string change = ChangeText(before, after, lowerIsBetter, out string css);
        sb.Append("<tr><td>").Append(Html(name)).Append("</td><td>").Append(Num(before, decimals)).Append("</td><td>")
          .Append(Num(after, decimals)).Append("</td><td class=\"").Append(css).Append("\">").Append(change).Append("</td></tr>");
    }

    public static string ChangeText(double before, double after, bool lowerIsBetter, out string css)
    {
        if (before <= 0 && after <= 0) { css = "same"; return "&ndash;"; }
        if (before <= 0) { css = lowerIsBetter ? "bad" : "good"; return "new"; }
        double pct = 100.0 * (after - before) / before;
        if (Math.Abs(pct) < 0.5) { css = "same"; return "same"; }
        bool better = lowerIsBetter ? pct < 0 : pct > 0;
        css = better ? "good" : "bad";
        return (pct > 0 ? "+" : "&minus;") + Math.Abs(pct).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    public static string Num(double v, bool decimals) =>
        decimals ? v.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture);

    private static string Html(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    // ================================================================== helpers

    private static IEnumerable<GameObject> Safe(GameObject[] objects)
    {
        if (objects == null) yield break;
        foreach (GameObject go in objects) if (go != null) yield return go;
    }

    private static Renderer[] CollectRenderers(GameObject[] objects)
    {
        var list = new List<Renderer>();
        foreach (GameObject go in Safe(objects)) list.AddRange(go.GetComponentsInChildren<Renderer>(true));
        return list.ToArray();
    }

    private static Light[] CollectLights(GameObject[] objects)
    {
        var list = new List<Light>();
        foreach (GameObject go in Safe(objects)) list.AddRange(go.GetComponentsInChildren<Light>(true));
        return list.ToArray();
    }

    private IEnumerable<Renderer> ActiveRenderers()
    {
        if (Mode != ViewMode.Optimized) foreach (Renderer r in originalRenderers) yield return r;
        if (Mode != ViewMode.Original) foreach (Renderer r in optimizedRenderers) yield return r;
    }

    private IEnumerable<Light> ActiveLights()
    {
        if (Mode != ViewMode.Optimized) foreach (Light l in originalLights) yield return l;
        if (Mode != ViewMode.Original) foreach (Light l in optimizedLights) yield return l;
    }

    private static int CountVisible(IEnumerable<Renderer> renderers)
    {
        int n = 0;
        foreach (Renderer r in renderers)
            if (r != null && r.enabled && r.isVisible && r.gameObject.activeInHierarchy) n++;
        return n;
    }

    private static int CountActiveLights(IEnumerable<Light> lights)
    {
        int n = 0;
        foreach (Light l in lights)
            if (l != null && l.isActiveAndEnabled && (l.type == LightType.Point || l.type == LightType.Spot)) n++;
        return n;
    }

    private static double AverageNs(ProfilerRecorder r)
    {
        if (!r.Valid || r.Count == 0) return 0;
        double sum = 0;
        int count = r.Count;
        for (int i = 0; i < count; i++) sum += r.GetSample(i).Value;
        return sum / count;
    }

    private static string Short(double v)
    {
        var ci = CultureInfo.InvariantCulture;
        if (v >= 1e6) return (v / 1e6).ToString("0.00", ci) + " M";
        if (v >= 1e4) return (v / 1e3).ToString("0", ci) + " k";
        return Math.Round(v).ToString("#,0", ci);
    }

    private void OnDrawGizmosSelected()
    {
        if (viewpoints == null || Application.isPlaying) return;
        Camera cam = viewCamera != null ? viewCamera : Camera.main;
        float fov = cam != null ? cam.fieldOfView : 60f;
        Gizmos.color = new Color(0.3f, 1f, 0.45f, 0.9f);
        foreach (Transform t in viewpoints)
        {
            if (t == null) continue;
            Gizmos.matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, fov, 6f, 0.3f, 16f / 9f);
            Gizmos.DrawSphere(Vector3.zero, 0.25f);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }

    // ================================================================== on-screen panel

    private GUIStyle panelStyle, titleStyle, labelStyle, valueStyle, buttonStyle, activeButtonStyle, smallStyle, tagStyle, knobStyle;
    private GUIStyle goodStyle, badStyle;
    private Texture2D panelTex, activeTex;

    private void EnsureStyles()
    {
        if (panelStyle != null) return;
        panelTex = SolidTex(new Color(0.06f, 0.09f, 0.07f, 0.86f));
        activeTex = SolidTex(new Color(0.18f, 0.55f, 0.28f, 1f));
        panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 12, 12), normal = { background = panelTex } };
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = new Color(0.82f, 0.88f, 0.84f) } };
        valueStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        goodStyle = new GUIStyle(valueStyle) { normal = { textColor = new Color(0.45f, 0.9f, 0.55f) } };
        badStyle = new GUIStyle(valueStyle) { normal = { textColor = new Color(1f, 0.5f, 0.45f) } };
        smallStyle = new GUIStyle(labelStyle) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.65f, 0.72f, 0.67f) } };
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 15, fixedHeight = 34 };
        activeButtonStyle = new GUIStyle(buttonStyle)
        {
            fontStyle = FontStyle.Bold,
            normal = { background = activeTex, textColor = Color.white },
            hover = { background = activeTex, textColor = Color.white },
        };
        tagStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(14, 14, 6, 6), normal = { background = panelTex, textColor = Color.white },
        };
        knobStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
    }

    private static Texture2D SolidTex(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private void OnGUI()
    {
        if (!initialized || !showOverlay) return;
        Event e = Event.current;
        if (e.type == EventType.KeyDown && !IsBusy)
        {
            switch (e.keyCode)
            {
                case KeyCode.Alpha1: case KeyCode.Keypad1: SetMode(ViewMode.Original); e.Use(); break;
                case KeyCode.Alpha2: case KeyCode.Keypad2: SetMode(ViewMode.Optimized); e.Use(); break;
                case KeyCode.Alpha3: case KeyCode.Keypad3: SetMode(ViewMode.Split); e.Use(); break;
                case KeyCode.V: NextView(); e.Use(); break;
                case KeyCode.B: RunBenchmark(); e.Use(); break;
                case KeyCode.R: RunFullReport(); e.Use(); break;
                case KeyCode.H: panelCollapsed = !panelCollapsed; e.Use(); break;
            }
        }
        if (e.type == EventType.Layout) lyHidden = overlayHiddenForWork;
        if (lyHidden) return;

        EnsureStyles();
        float scale = overlayScale * Mathf.Max(0.75f, Mathf.Min(Screen.width, Screen.height) / 900f);
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float sw = Screen.width / scale, sh = Screen.height / scale;

        Rect panelRect = DrawPanel(sw, sh);
        if (Mode == ViewMode.Split) DrawSplitGui(sw, sh, panelRect);

        GUI.matrix = oldMatrix;
    }

    private Rect lastPanelRect;

    // IMGUI needs the same controls in the Layout and the Repaint pass; coroutines can change state in
    // between, so everything that adds/removes controls is frozen at Layout time.
    private bool lyHidden, lyCollapsed, lyViews, lyCounters, lySplit, lyStatus, lyResults, lyPixels;

    private Rect DrawPanel(float sw, float sh)
    {
        var ci = CultureInfo.InvariantCulture;
        List<Transform> views = ValidViewpoints();
        if (Event.current.type == EventType.Layout)
        {
            lyCollapsed = panelCollapsed;
            lyViews = views.Count > 0;
            lyCounters = recDraw.Valid && recDraw.LastValue > 0;
            lySplit = Mode == ViewMode.Split;
            lyStatus = !string.IsNullOrEmpty(Status);
            lyResults = OriginalResult.valid && OptimizedResult.valid;
            lyPixels = NoticeablePixelPercent >= 0f;
        }
        float width = Mathf.Min(370f, sw - 24f);
        GUILayout.BeginArea(new Rect(12f, 12f, width, sh - 24f));
        GUILayout.BeginVertical(panelStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Jungle Optimization", titleStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(lyCollapsed ? "+" : "–", buttonStyle, GUILayout.Width(36f))) panelCollapsed = !panelCollapsed;
        GUILayout.EndHorizontal();

        GUI.enabled = !IsBusy;
        GUILayout.BeginHorizontal();
        ModeButton("Original", ViewMode.Original);
        ModeButton("Optimized", ViewMode.Optimized);
        ModeButton("Split", ViewMode.Split);
        GUILayout.EndHorizontal();
        GUI.enabled = true;

        if (!lyCollapsed)
        {
            GUILayout.Space(4f);
            GUILayout.Label(ModeDescription(), smallStyle);

            if (lyViews)
            {
                GUI.enabled = !IsBusy;
                string viewLabel = CurrentView < 0 || CurrentView >= views.Count ? "Free camera" : views[CurrentView].name;
                if (GUILayout.Button("View: " + viewLabel + "   ▶", buttonStyle)) NextView();
                GUI.enabled = true;
            }
            GUILayout.Space(6f);

            float avg = 0f;
            for (int i = 0; i < frameCount; i++) avg += frameTimes[i];
            avg = frameCount > 0 ? avg / frameCount : 0f;
            Stat("FPS", avg > 0f ? (1f / avg).ToString("0", ci) + "   (" + (avg * 1000f).ToString("0.0", ci) + " ms)" : "-");
            if (lyCounters)
            {
                Stat("Draw calls", Short(recDraw.LastValue));
                Stat("SetPass calls", Short(recSetPass.LastValue));
                Stat("Triangles", Short(recTris.LastValue));
                Stat("Shadow casters", Short(recShadow.LastValue));
            }
            else
            {
                GUILayout.Label("Draw-call counters need the editor or a Development Build.", smallStyle);
            }
            Stat("Jungle objects rendered", Mode == ViewMode.Split ? "–" : Short(liveVisible));
            Stat("Lantern lights on", Mode == ViewMode.Split ? "–" : liveLights.ToString(ci));
            if (lySplit)
                GUILayout.Label("Split view draws both versions at once, so these counters show their sum. Use Benchmark for a fair comparison.", smallStyle);

            GUILayout.Space(8f);
            GUI.enabled = !IsBusy;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Benchmark", buttonStyle)) RunBenchmark();
            if (GUILayout.Button("Full Report", buttonStyle)) RunFullReport();
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            if (lyStatus) GUILayout.Label(Status ?? "", smallStyle);

            if (lyResults)
            {
                GUILayout.Space(6f);
                ResultHeader(ViewResults.Count > 1 ? "Average" : "Benchmark");
                ResultRow("Draw calls", OriginalResult.drawCalls, OptimizedResult.drawCalls, false);
                ResultRow("Triangles", OriginalResult.triangles, OptimizedResult.triangles, false);
                ResultRow("Shadow casters", OriginalResult.shadowCasters, OptimizedResult.shadowCasters, false);
                ResultRow("Objects", OriginalResult.visibleRenderers, OptimizedResult.visibleRenderers, false);
                ResultRow("Lights on", OriginalResult.activeLights, OptimizedResult.activeLights, false);
                ResultRow("Frame ms", OriginalResult.avgFrameMs, OptimizedResult.avgFrameMs, true);
            }
            if (lyPixels)
                GUILayout.Label("Visually identical: " + (100f - NoticeablePixelPercent).ToString("0.0", ci) + "% of the picture", labelStyle);

            GUILayout.Space(4f);
            GUILayout.Label("Keys: 1 2 3 modes · V view · B benchmark · R report · H hide", smallStyle);
        }

        GUILayout.EndVertical();
        if (Event.current.type == EventType.Repaint) lastPanelRect = GUILayoutUtility.GetLastRect();
        GUILayout.EndArea();
        return new Rect(12f + lastPanelRect.x, 12f + lastPanelRect.y, lastPanelRect.width, lastPanelRect.height);
    }

    private string ModeDescription()
    {
        switch (Mode)
        {
            case ViewMode.Original: return "Showing the ORIGINAL jungle, as it was before optimization.";
            case ViewMode.Optimized: return "Showing the OPTIMIZED jungle (Jungle Optimized prefab + mobile runtime optimizer).";
            default: return "Left: ORIGINAL  |  Right: OPTIMIZED. Same camera, same frame. Drag the white line.";
        }
    }

    private void ModeButton(string label, ViewMode mode)
    {
        if (GUILayout.Button(label, Mode == mode ? activeButtonStyle : buttonStyle)) SetMode(mode);
    }

    private void Stat(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(value, valueStyle);
        GUILayout.EndHorizontal();
    }

    private void ResultHeader(string title)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(title, labelStyle, GUILayout.Width(118f));
        GUILayout.Label("Orig.", valueStyle, GUILayout.Width(66f));
        GUILayout.Label("Opt.", valueStyle, GUILayout.Width(66f));
        GUILayout.Label("", valueStyle);
        GUILayout.EndHorizontal();
    }

    private void ResultRow(string label, double before, double after, bool decimals)
    {
        string change = ChangeText(before, after, true, out string css).Replace("&minus;", "−").Replace("&ndash;", "-");
        GUIStyle style = css == "good" ? goodStyle : css == "bad" ? badStyle : valueStyle;
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(118f));
        GUILayout.Label(decimals ? Num(before, true) : Short(before), valueStyle, GUILayout.Width(66f));
        GUILayout.Label(decimals ? Num(after, true) : Short(after), valueStyle, GUILayout.Width(66f));
        GUILayout.Label(change, style);
        GUILayout.EndHorizontal();
    }

    private void DrawSplitGui(float sw, float sh, Rect panelRect)
    {
        float x = splitPosition * sw;
        Event e = Event.current;

        // Drag the divider (mouse or touch) anywhere outside the panel.
        if (e.type == EventType.MouseDown && !panelRect.Contains(e.mousePosition) && Mathf.Abs(e.mousePosition.x - x) < 48f)
        { draggingDivider = true; e.Use(); }
        else if (e.type == EventType.MouseDrag && draggingDivider)
        { splitPosition = Mathf.Clamp(e.mousePosition.x / sw, 0.05f, 0.95f); e.Use(); }
        else if (e.type == EventType.MouseUp && draggingDivider)
        { draggingDivider = false; e.Use(); }

        x = splitPosition * sw;
        GUI.DrawTexture(new Rect(x - 1.5f, 0f, 3f, sh), Texture2D.whiteTexture);
        var knob = new Rect(x - 14f, sh * 0.5f - 30f, 28f, 60f);
        GUI.DrawTexture(knob, activeTex);
        GUI.Label(knob, "↔", knobStyle);

        var orig = new GUIContent("ORIGINAL");
        var opt = new GUIContent("OPTIMIZED");
        Vector2 so = tagStyle.CalcSize(orig), sp = tagStyle.CalcSize(opt);
        float y = sh - so.y - 18f;
        GUI.Label(new Rect(Mathf.Max(8f, x - so.x - 16f), y, so.x, so.y), orig, tagStyle);
        GUI.Label(new Rect(Mathf.Min(sw - sp.x - 8f, x + 16f), y, sp.x, sp.y), opt, tagStyle);
    }
}
