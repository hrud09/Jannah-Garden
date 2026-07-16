using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Custom Editor Window for configuring, previewing, and debugging
/// the OcclusionCullingManager settings for the Outer Garden scene.
/// 
/// Access via: Tools → Occlusion Culling Settings
/// </summary>
public class OcclusionCullingEditorWindow : EditorWindow
{
    // ─── Tab System ──────────────────────────────────────────────────
    private enum Tab
    {
        GlobalSettings,
        SceneAnalysis,
        ZoneManagement,
        PreviewDebug,
        BakeHelpers
    }

    private Tab _currentTab = Tab.GlobalSettings;
    private Vector2 _scrollPosition;

    // ─── Scene Analysis Cache ────────────────────────────────────────
    private int _totalRendererCount;
    private int _totalTriangleCount;
    private Dictionary<string, int> _layerBreakdown = new Dictionary<string, int>();
    private Dictionary<string, int> _layerTriBreakdown = new Dictionary<string, int>();
    private bool _hasScannedScene = false;

    // ─── Zone Management Cache ───────────────────────────────────────
    private OcclusionCullingZone[] _cachedZones;
    private Vector2 _zoneScrollPos;

    // ─── Preview State ───────────────────────────────────────────────
    private bool _isPreviewActive = false;
    private List<Renderer> _previewCulledRenderers = new List<Renderer>();
    private List<Renderer> _previewVisibleRenderers = new List<Renderer>();

    // ─── Styles ──────────────────────────────────────────────────────
    private GUIStyle _headerStyle;
    private GUIStyle _subHeaderStyle;
    private GUIStyle _boxStyle;
    private GUIStyle _statLabelStyle;
    private GUIStyle _statValueStyle;
    private bool _stylesInitialized = false;

    // ─── Colors ──────────────────────────────────────────────────────
    private static readonly Color EMERALD = new Color(0.15f, 0.68f, 0.37f);
    private static readonly Color AZURE = new Color(0.18f, 0.52f, 0.89f);
    private static readonly Color AMBER = new Color(0.95f, 0.65f, 0.05f);
    private static readonly Color CRIMSON = new Color(0.86f, 0.20f, 0.18f);
    private static readonly Color TEAL = new Color(0.10f, 0.74f, 0.71f);
    private static readonly Color PURPLE = new Color(0.55f, 0.30f, 0.85f);

    // ═════════════════════════════════════════════════════════════════
    //  MENU ITEM
    // ═════════════════════════════════════════════════════════════════

    [MenuItem("Tools/Occlusion Culling Settings")]
    public static void ShowWindow()
    {
        var window = GetWindow<OcclusionCullingEditorWindow>("Occlusion Culling");
        window.minSize = new Vector2(420, 500);
        window.Show();
    }

    // ═════════════════════════════════════════════════════════════════
    //  STYLES INIT
    // ═════════════════════════════════════════════════════════════════

    private void InitStyles()
    {
        if (_stylesInitialized) return;

        _headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft,
            margin = new RectOffset(4, 4, 8, 4)
        };

        _subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            margin = new RectOffset(4, 4, 6, 2)
        };

        _boxStyle = new GUIStyle("helpbox")
        {
            padding = new RectOffset(10, 10, 8, 8),
            margin = new RectOffset(4, 4, 4, 4)
        };

        _statLabelStyle = new GUIStyle(EditorStyles.label)
        {
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft
        };

        _statValueStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleRight
        };

        _stylesInitialized = true;
    }

    // ═════════════════════════════════════════════════════════════════
    //  MAIN GUI
    // ═════════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        InitStyles();

        // ── Title Bar ────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("🔮 Occlusion Culling Settings", _headerStyle);
        DrawSeparator();

        // ── Tab Bar ──────────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        DrawTabButton("⚙ Global", Tab.GlobalSettings);
        DrawTabButton("📊 Analysis", Tab.SceneAnalysis);
        DrawTabButton("🗺 Zones", Tab.ZoneManagement);
        DrawTabButton("👁 Preview", Tab.PreviewDebug);
        DrawTabButton("🧱 Bake", Tab.BakeHelpers);
        EditorGUILayout.EndHorizontal();

        DrawSeparator();

        // ── Tab Content ──────────────────────────────────────────────
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        switch (_currentTab)
        {
            case Tab.GlobalSettings:
                DrawGlobalSettingsTab();
                break;
            case Tab.SceneAnalysis:
                DrawSceneAnalysisTab();
                break;
            case Tab.ZoneManagement:
                DrawZoneManagementTab();
                break;
            case Tab.PreviewDebug:
                DrawPreviewDebugTab();
                break;
            case Tab.BakeHelpers:
                DrawBakeHelpersTab();
                break;
        }

        EditorGUILayout.EndScrollView();

        // ── Footer ───────────────────────────────────────────────────
        DrawSeparator();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("Jannah Garden — Occlusion Culling System", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);
    }

    // ═════════════════════════════════════════════════════════════════
    //  TAB 1: GLOBAL SETTINGS
    // ═════════════════════════════════════════════════════════════════

    private void DrawGlobalSettingsTab()
    {
        var manager = FindManager();

        if (manager == null)
        {
            DrawNoManagerWarning();
            return;
        }

        Undo.RecordObject(manager, "Occlusion Culling Settings Change");

        // ── Culling Mode ─────────────────────────────────────────────
        EditorGUILayout.LabelField("Culling Mode", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);
        manager.cullingMode = (OcclusionCullingManager.CullingMode)EditorGUILayout.EnumPopup(
            new GUIContent("Mode", "How occlusion culling interacts with RuntimeEnvironmentGenerator."),
            manager.cullingMode
        );
        switch (manager.cullingMode)
        {
            case OcclusionCullingManager.CullingMode.Combined:
                EditorGUILayout.HelpBox("Both systems active: RuntimeEnvironmentGenerator handles SetActive, OcclusionCulling handles Renderer.enabled.", MessageType.Info);
                break;
            case OcclusionCullingManager.CullingMode.OcclusionOnly:
                EditorGUILayout.HelpBox("Only the occlusion culling system is active.", MessageType.Info);
                break;
            case OcclusionCullingManager.CullingMode.DistanceOnly:
                EditorGUILayout.HelpBox("Occlusion culling is DISABLED. Only RuntimeEnvironmentGenerator runs.", MessageType.Warning);
                break;
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // ── Distance Tiers ───────────────────────────────────────────
        EditorGUILayout.LabelField("Distance Tiers", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);

        GUI.backgroundColor = EMERALD;
        manager.nearDistance = EditorGUILayout.Slider(
            new GUIContent("🟢 Near Distance", "Objects closer than this are ALWAYS visible."),
            manager.nearDistance, 5f, 100f
        );
        GUI.backgroundColor = AMBER;
        manager.midDistance = EditorGUILayout.Slider(
            new GUIContent("🟡 Mid Distance", "Small objects outside frustum start being culled."),
            manager.midDistance, 10f, 150f
        );
        GUI.backgroundColor = CRIMSON;
        manager.farDistance = EditorGUILayout.Slider(
            new GUIContent("🔴 Far Distance", "Everything beyond this is culled (except Never Cull objects)."),
            manager.farDistance, 20f, 200f
        );
        GUI.backgroundColor = TEAL;
        manager.shadowDistance = EditorGUILayout.Slider(
            new GUIContent("🔵 Shadow Distance", "Shadows disabled beyond this distance."),
            manager.shadowDistance, 10f, 150f
        );
        GUI.backgroundColor = Color.white;

        // Validate distances
        if (manager.midDistance < manager.nearDistance)
            manager.midDistance = manager.nearDistance;
        if (manager.farDistance < manager.midDistance)
            manager.farDistance = manager.midDistance;

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // ── Performance ──────────────────────────────────────────────
        EditorGUILayout.LabelField("Performance", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);

        manager.checkEveryNFrames = EditorGUILayout.IntSlider(
            new GUIContent("Check Every N Frames", "Higher = better FPS but less responsive culling."),
            manager.checkEveryNFrames, 1, 6
        );
        manager.batchSize = EditorGUILayout.IntSlider(
            new GUIContent("Batch Size", "Objects processed per culling frame. Lower = smoother, but slower to converge."),
            manager.batchSize, 16, 256
        );

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // ── Filters ──────────────────────────────────────────────────
        EditorGUILayout.LabelField("Filters", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);

        manager.cullableLayers = EditorGUILayout.MaskField(
            new GUIContent("Cullable Layers", "Only objects on these layers will be culled."),
            InternalEditorUtility_LayerMaskToConcatenatedLayersMask(manager.cullableLayers),
            UnityEditorInternal.InternalEditorUtility.layers
        );
        // Convert back from concatenated to LayerMask
        manager.cullableLayers = InternalEditorUtility_ConcatenatedLayersMaskToLayerMask(manager.cullableLayers);

        manager.cullingCamera = (Camera)EditorGUILayout.ObjectField(
            new GUIContent("Culling Camera", "Leave empty to auto-detect Camera.main."),
            manager.cullingCamera, typeof(Camera), true
        );

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // ── Debug ────────────────────────────────────────────────────
        EditorGUILayout.LabelField("Debug", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);
        manager.showDebugGizmos = EditorGUILayout.Toggle("Show Gizmos in Scene View", manager.showDebugGizmos);
        manager.logStats = EditorGUILayout.Toggle("Log Stats to Console", manager.logStats);
        EditorGUILayout.EndVertical();

        // ── Runtime Stats (Play Mode Only) ───────────────────────────
        if (Application.isPlaying)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Runtime Stats", _subHeaderStyle);
            EditorGUILayout.BeginVertical(_boxStyle);
            DrawStatRow("Total Registered", manager.TotalRegistered.ToString());
            DrawStatRow("Visible", manager.VisibleCount.ToString());
            DrawStatRow("Culled", manager.CulledCount.ToString());
            DrawStatRow("Shadow Only", manager.ShadowOnlyCount.ToString());
            EditorGUILayout.EndVertical();

            // Force repaint for live stats
            Repaint();
        }

        if (GUI.changed)
        {
            EditorUtility.SetDirty(manager);
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  TAB 2: SCENE ANALYSIS
    // ═════════════════════════════════════════════════════════════════

    private void DrawSceneAnalysisTab()
    {
        EditorGUILayout.LabelField("Scene Analysis", _subHeaderStyle);
        EditorGUILayout.HelpBox(
            "Scan the active scene to analyze all renderers, their layers, and estimated triangle count.",
            MessageType.Info
        );

        EditorGUILayout.Space(4);

        GUI.backgroundColor = AZURE;
        if (GUILayout.Button("🔍  Scan Scene", GUILayout.Height(32)))
        {
            ScanScene();
        }
        GUI.backgroundColor = Color.white;

        if (!_hasScannedScene)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Click 'Scan Scene' to analyze the current scene.", MessageType.None);
            return;
        }

        EditorGUILayout.Space(8);

        // ── Summary ──────────────────────────────────────────────────
        EditorGUILayout.LabelField("Summary", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);
        DrawStatRow("Total Renderers", _totalRendererCount.ToString("N0"));
        DrawStatRow("Estimated Triangles", _totalTriangleCount.ToString("N0"));
        DrawStatRow("Layers With Objects", _layerBreakdown.Count.ToString());
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6);

        // ── Layer Breakdown ──────────────────────────────────────────
        EditorGUILayout.LabelField("Breakdown by Layer", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);

        foreach (var kvp in _layerBreakdown.OrderByDescending(x => x.Value))
        {
            string triCount = _layerTriBreakdown.ContainsKey(kvp.Key)
                ? _layerTriBreakdown[kvp.Key].ToString("N0")
                : "?";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(kvp.Key, GUILayout.Width(160));
            EditorGUILayout.LabelField($"{kvp.Value} renderers", _statLabelStyle, GUILayout.Width(100));
            EditorGUILayout.LabelField($"~{triCount} tris", _statValueStyle);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);

        // ── Quick Register ───────────────────────────────────────────
        var manager = FindManager();
        if (manager != null)
        {
            GUI.backgroundColor = EMERALD;
            if (GUILayout.Button("✅  Register All Scanned Renderers with Manager", GUILayout.Height(30)))
            {
                Undo.RecordObject(manager, "Register All Renderers");
                manager.ScanAndRegisterAll();
                EditorUtility.SetDirty(manager);
            }
            GUI.backgroundColor = Color.white;
        }
    }

    private void ScanScene()
    {
        _layerBreakdown.Clear();
        _layerTriBreakdown.Clear();
        _totalRendererCount = 0;
        _totalTriangleCount = 0;

        Renderer[] allRenderers = FindObjectsOfType<Renderer>(true);

        foreach (var rend in allRenderers)
        {
            if (rend == null) continue;
            if (rend is CanvasRenderer) continue;

            _totalRendererCount++;

            string layerName = LayerMask.LayerToName(rend.gameObject.layer);
            if (string.IsNullOrEmpty(layerName)) layerName = $"Layer {rend.gameObject.layer}";

            if (!_layerBreakdown.ContainsKey(layerName))
            {
                _layerBreakdown[layerName] = 0;
                _layerTriBreakdown[layerName] = 0;
            }

            _layerBreakdown[layerName]++;

            // Estimate triangles from mesh filters
            MeshFilter mf = rend.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                int tris = mf.sharedMesh.triangles.Length / 3;
                _totalTriangleCount += tris;
                _layerTriBreakdown[layerName] += tris;
            }

            // Also check skinned mesh renderers
            SkinnedMeshRenderer smr = rend as SkinnedMeshRenderer;
            if (smr != null && smr.sharedMesh != null)
            {
                int tris = smr.sharedMesh.triangles.Length / 3;
                _totalTriangleCount += tris;
                _layerTriBreakdown[layerName] += tris;
            }
        }

        _hasScannedScene = true;
        Debug.Log($"[OcclusionCulling Editor] Scene scan complete: {_totalRendererCount} renderers, ~{_totalTriangleCount:N0} triangles.");
    }

    // ═════════════════════════════════════════════════════════════════
    //  TAB 3: ZONE MANAGEMENT
    // ═════════════════════════════════════════════════════════════════

    private void DrawZoneManagementTab()
    {
        EditorGUILayout.LabelField("Zone Management", _subHeaderStyle);
        EditorGUILayout.HelpBox(
            "Manage OcclusionCullingZone components in the scene. Add zones to objects for per-object culling overrides.",
            MessageType.Info
        );

        EditorGUILayout.Space(4);

        // ── Quick Actions ────────────────────────────────────────────
        EditorGUILayout.LabelField("Quick Actions (Selected Objects)", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);

        int selectedCount = Selection.gameObjects.Length;
        EditorGUILayout.LabelField($"Selected: {selectedCount} object(s)");

        EditorGUILayout.Space(4);

        EditorGUI.BeginDisabledGroup(selectedCount == 0);

        GUI.backgroundColor = AZURE;
        if (GUILayout.Button("➕  Add Culling Zone to Selection", GUILayout.Height(26)))
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go.GetComponent<OcclusionCullingZone>() == null)
                {
                    Undo.AddComponent<OcclusionCullingZone>(go);
                }
            }
            RefreshZoneCache();
        }

        GUI.backgroundColor = EMERALD;
        if (GUILayout.Button("🛡  Mark Selection as Never Cull", GUILayout.Height(26)))
        {
            foreach (var go in Selection.gameObjects)
            {
                var zone = go.GetComponent<OcclusionCullingZone>();
                if (zone == null)
                {
                    zone = Undo.AddComponent<OcclusionCullingZone>(go);
                }
                Undo.RecordObject(zone, "Mark Never Cull");
                zone.neverCull = true;
                EditorUtility.SetDirty(zone);
            }
            RefreshZoneCache();
        }

        GUI.backgroundColor = AMBER;
        if (GUILayout.Button("⬆  Set Selection to High Priority", GUILayout.Height(26)))
        {
            foreach (var go in Selection.gameObjects)
            {
                var zone = go.GetComponent<OcclusionCullingZone>();
                if (zone == null)
                {
                    zone = Undo.AddComponent<OcclusionCullingZone>(go);
                }
                Undo.RecordObject(zone, "Set High Priority");
                zone.priority = OcclusionCullingZone.CullingPriority.High;
                EditorUtility.SetDirty(zone);
            }
            RefreshZoneCache();
        }

        GUI.backgroundColor = CRIMSON;
        if (GUILayout.Button("⬇  Set Selection to Low Priority", GUILayout.Height(26)))
        {
            foreach (var go in Selection.gameObjects)
            {
                var zone = go.GetComponent<OcclusionCullingZone>();
                if (zone == null)
                {
                    zone = Undo.AddComponent<OcclusionCullingZone>(go);
                }
                Undo.RecordObject(zone, "Set Low Priority");
                zone.priority = OcclusionCullingZone.CullingPriority.Low;
                EditorUtility.SetDirty(zone);
            }
            RefreshZoneCache();
        }

        GUI.backgroundColor = Color.white;

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);

        // ── Existing Zones List ──────────────────────────────────────
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Existing Zones in Scene", _subHeaderStyle);
        if (GUILayout.Button("🔄 Refresh", GUILayout.Width(80)))
        {
            RefreshZoneCache();
        }
        EditorGUILayout.EndHorizontal();

        if (_cachedZones == null)
        {
            RefreshZoneCache();
        }

        if (_cachedZones.Length == 0)
        {
            EditorGUILayout.HelpBox("No OcclusionCullingZone components found in the scene.", MessageType.None);
            return;
        }

        _zoneScrollPos = EditorGUILayout.BeginScrollView(_zoneScrollPos, GUILayout.MaxHeight(300));
        EditorGUILayout.BeginVertical(_boxStyle);

        foreach (var zone in _cachedZones)
        {
            if (zone == null) continue;

            EditorGUILayout.BeginHorizontal();

            // Icon based on settings
            string icon = zone.neverCull ? "🛡" :
                           zone.priority == OcclusionCullingZone.CullingPriority.High ? "⬆" :
                           zone.priority == OcclusionCullingZone.CullingPriority.Low ? "⬇" : "●";

            string label = $"{icon} {zone.gameObject.name}";
            if (zone.neverCull) label += " [Never Cull]";
            else if (zone.useCustomDistances) label += $" [Custom: {zone.customFarDistance}m]";

            // Clickable label to select the object
            if (GUILayout.Button(label, EditorStyles.linkLabel))
            {
                Selection.activeGameObject = zone.gameObject;
                EditorGUIUtility.PingObject(zone.gameObject);
            }

            // Remove button
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(18)))
            {
                Undo.DestroyObjectImmediate(zone);
                RefreshZoneCache();
                GUIUtility.ExitGUI();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    private void RefreshZoneCache()
    {
        _cachedZones = FindObjectsOfType<OcclusionCullingZone>(true);
    }

    // ═════════════════════════════════════════════════════════════════
    //  TAB 4: PREVIEW & DEBUG
    // ═════════════════════════════════════════════════════════════════

    private void DrawPreviewDebugTab()
    {
        EditorGUILayout.LabelField("Preview & Debug", _subHeaderStyle);

        if (Application.isPlaying)
        {
            DrawPlayModePreview();
        }
        else
        {
            DrawEditModePreview();
        }
    }

    private void DrawPlayModePreview()
    {
        var manager = FindManager();
        if (manager == null)
        {
            DrawNoManagerWarning();
            return;
        }

        EditorGUILayout.HelpBox("The culling system is running in Play Mode. Live stats shown below.", MessageType.Info);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Live Statistics", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);
        DrawStatRow("Total Registered", manager.TotalRegistered.ToString("N0"));

        GUI.contentColor = EMERALD;
        DrawStatRow("Visible", manager.VisibleCount.ToString("N0"));
        GUI.contentColor = CRIMSON;
        DrawStatRow("Culled", manager.CulledCount.ToString("N0"));
        GUI.contentColor = AMBER;
        DrawStatRow("Shadow Only", manager.ShadowOnlyCount.ToString("N0"));
        GUI.contentColor = Color.white;

        if (manager.TotalRegistered > 0)
        {
            float cullPercent = (float)manager.CulledCount / manager.TotalRegistered * 100f;
            EditorGUILayout.Space(4);
            DrawStatRow("Cull Ratio", $"{cullPercent:F1}%");

            // Visual bar
            Rect barRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            barRect.x += 4;
            barRect.width -= 8;

            float visiblePct = (float)manager.VisibleCount / manager.TotalRegistered;
            float shadowPct = (float)manager.ShadowOnlyCount / manager.TotalRegistered;
            float culledPct = (float)manager.CulledCount / manager.TotalRegistered;

            // Background
            EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f));

            // Visible segment (green)
            Rect visRect = barRect;
            visRect.width = barRect.width * visiblePct;
            EditorGUI.DrawRect(visRect, EMERALD);

            // Shadow segment (yellow)
            Rect shadRect = barRect;
            shadRect.x = visRect.xMax;
            shadRect.width = barRect.width * shadowPct;
            EditorGUI.DrawRect(shadRect, AMBER);

            // Culled segment (red)
            Rect cullRect = barRect;
            cullRect.x = shadRect.xMax;
            cullRect.width = barRect.width * culledPct;
            EditorGUI.DrawRect(cullRect, CRIMSON);
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);

        // Quick Actions
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = EMERALD;
        if (GUILayout.Button("Scan & Register All", GUILayout.Height(28)))
        {
            manager.ScanAndRegisterAll();
        }
        GUI.backgroundColor = CRIMSON;
        if (GUILayout.Button("Restore All & Clear", GUILayout.Height(28)))
        {
            manager.RestoreAllAndClear();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        Repaint(); // Keep refreshing for live stats
    }

    private void DrawEditModePreview()
    {
        EditorGUILayout.HelpBox(
            "Preview simulates which objects would be culled from the current Scene view camera. " +
            "This does NOT modify the scene — it only highlights objects in the Scene view.",
            MessageType.Info
        );

        EditorGUILayout.Space(4);

        var manager = FindManager();
        if (manager == null)
        {
            DrawNoManagerWarning();
            return;
        }

        EditorGUILayout.BeginVertical(_boxStyle);

        bool newPreview = EditorGUILayout.Toggle("Preview Active", _isPreviewActive);
        if (newPreview != _isPreviewActive)
        {
            _isPreviewActive = newPreview;
            if (_isPreviewActive)
            {
                RunPreviewAnalysis(manager);
                SceneView.duringSceneGui += OnSceneGUIPreview;
            }
            else
            {
                _previewCulledRenderers.Clear();
                _previewVisibleRenderers.Clear();
                SceneView.duringSceneGui -= OnSceneGUIPreview;
            }
            SceneView.RepaintAll();
        }

        if (_isPreviewActive)
        {
            EditorGUILayout.Space(4);
            DrawStatRow("Would Be Visible", _previewVisibleRenderers.Count.ToString());
            DrawStatRow("Would Be Culled", _previewCulledRenderers.Count.ToString());

            EditorGUILayout.Space(4);
            if (GUILayout.Button("🔄  Refresh Preview", GUILayout.Height(24)))
            {
                RunPreviewAnalysis(manager);
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "🟢 Green wireframe = Visible\n🔴 Red wireframe = Would be culled",
                MessageType.None
            );
        }

        EditorGUILayout.EndVertical();
    }

    private void RunPreviewAnalysis(OcclusionCullingManager manager)
    {
        _previewCulledRenderers.Clear();
        _previewVisibleRenderers.Clear();

        // Use the Scene view camera for preview
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null || sceneView.camera == null) return;

        Camera previewCam = sceneView.camera;
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(previewCam);
        Vector3 camPos = previewCam.transform.position;

        Renderer[] allRenderers = FindObjectsOfType<Renderer>(true);

        foreach (var rend in allRenderers)
        {
            if (rend == null) continue;
            if (rend is CanvasRenderer) continue;
            if (((1 << rend.gameObject.layer) & manager.cullableLayers.value) == 0) continue;

            // Check zone overrides
            var zone = rend.GetComponentInParent<OcclusionCullingZone>();
            if (zone != null && zone.neverCull)
            {
                _previewVisibleRenderers.Add(rend);
                continue;
            }

            float dist = Vector3.Distance(rend.transform.position, camPos);
            float effectiveFar = (zone != null && zone.useCustomDistances) ? zone.customFarDistance : manager.farDistance;
            float effectiveNear = (zone != null && zone.useCustomDistances) ? zone.customNearDistance : manager.nearDistance;

            if (dist <= effectiveNear)
            {
                _previewVisibleRenderers.Add(rend);
                continue;
            }

            if (dist > effectiveFar)
            {
                _previewCulledRenderers.Add(rend);
                continue;
            }

            // Frustum check
            bool inFrustum = GeometryUtility.TestPlanesAABB(planes, rend.bounds);
            if (inFrustum)
            {
                _previewVisibleRenderers.Add(rend);
            }
            else
            {
                _previewCulledRenderers.Add(rend);
            }
        }
    }

    private void OnSceneGUIPreview(SceneView sceneView)
    {
        if (!_isPreviewActive) return;

        // Draw culled objects in red
        Handles.color = new Color(1f, 0.15f, 0.1f, 0.35f);
        foreach (var rend in _previewCulledRenderers)
        {
            if (rend == null) continue;
            Handles.DrawWireCube(rend.bounds.center, rend.bounds.size);
        }

        // Draw visible objects in green (only nearby ones to avoid clutter)
        Handles.color = new Color(0.1f, 0.9f, 0.3f, 0.15f);
        Vector3 camPos = sceneView.camera.transform.position;
        foreach (var rend in _previewVisibleRenderers)
        {
            if (rend == null) continue;
            float dist = Vector3.Distance(rend.bounds.center, camPos);
            if (dist < 50f) // Only draw wireframes for close visible objects
            {
                Handles.DrawWireCube(rend.bounds.center, rend.bounds.size);
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  TAB 5: BAKE HELPERS
    // ═════════════════════════════════════════════════════════════════

    private void DrawBakeHelpersTab()
    {
        EditorGUILayout.LabelField("Unity Built-in Occlusion Culling", _subHeaderStyle);
        EditorGUILayout.HelpBox(
            "These helpers configure Unity's built-in baked occlusion culling system for STATIC objects. " +
            "This complements the runtime occlusion system for environments that don't change.",
            MessageType.Info
        );

        EditorGUILayout.Space(6);

        // ── Mark Static ──────────────────────────────────────────────
        EditorGUILayout.LabelField("Static Flags", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);

        GUI.backgroundColor = PURPLE;
        if (GUILayout.Button("🏗  Mark All Static MeshRenderers as Occluder Static", GUILayout.Height(28)))
        {
            MarkStaticObjects(StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }

        EditorGUILayout.Space(2);
        GUI.backgroundColor = AZURE;
        if (GUILayout.Button("🏠  Mark All Static MeshRenderers as Occludee Static Only", GUILayout.Height(28)))
        {
            MarkStaticObjects(StaticEditorFlags.OccludeeStatic);
        }

        EditorGUILayout.Space(2);
        GUI.backgroundColor = CRIMSON;
        if (GUILayout.Button("🧹  Clear Occlusion Static Flags from All", GUILayout.Height(28)))
        {
            ClearOcclusionStaticFlags();
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);

        // ── Bake Controls ────────────────────────────────────────────
        EditorGUILayout.LabelField("Bake Controls", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);

        GUI.backgroundColor = EMERALD;
        if (GUILayout.Button("🔨  Open Unity Occlusion Culling Window", GUILayout.Height(30)))
        {
            EditorWindow.GetWindow(System.Type.GetType("UnityEditor.OcclusionCullingWindow,UnityEditor"));
        }

        EditorGUILayout.Space(4);
        GUI.backgroundColor = AMBER;
        if (GUILayout.Button("🗑  Clear Baked Occlusion Data", GUILayout.Height(28)))
        {
            if (EditorUtility.DisplayDialog("Clear Bake Data",
                "This will remove all baked occlusion culling data from the scene. Continue?",
                "Clear", "Cancel"))
            {
                // Unity stores bake data in Library/Occlusion — we trigger a clear via StaticOcclusionCulling
                StaticOcclusionCulling.Clear();
                Debug.Log("[OcclusionCulling Editor] Baked occlusion data cleared.");
            }
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);

        // ── Info ─────────────────────────────────────────────────────
        EditorGUILayout.LabelField("Bake Info", _subHeaderStyle);
        EditorGUILayout.BeginVertical(_boxStyle);
        DrawStatRow("Bake Data Size", $"{StaticOcclusionCulling.umbraDataSize / 1024f:F1} KB");
        DrawStatRow("Is Baked", StaticOcclusionCulling.umbraDataSize > 0 ? "Yes" : "No");
        EditorGUILayout.EndVertical();
    }

    private void MarkStaticObjects(StaticEditorFlags flags)
    {
        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>(true);
        int count = 0;

        foreach (var rend in renderers)
        {
            if (rend == null) continue;
            GameObject go = rend.gameObject;

            // Skip objects that are marked as dynamic / part of the placement system
            if (go.GetComponent<PlaceableItem>() != null) continue;

            // Only mark objects that are already static or at least not tagged dynamic
            GameObjectUtility.SetStaticEditorFlags(go,
                GameObjectUtility.GetStaticEditorFlags(go) | flags);
            count++;
        }

        Debug.Log($"[OcclusionCulling Editor] Marked {count} MeshRenderers with static flags: {flags}");
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    private void ClearOcclusionStaticFlags()
    {
        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>(true);
        int count = 0;

        foreach (var rend in renderers)
        {
            if (rend == null) continue;
            var currentFlags = GameObjectUtility.GetStaticEditorFlags(rend.gameObject);
            var cleaned = currentFlags & ~(StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            if (cleaned != currentFlags)
            {
                GameObjectUtility.SetStaticEditorFlags(rend.gameObject, cleaned);
                count++;
            }
        }

        Debug.Log($"[OcclusionCulling Editor] Cleared occlusion static flags from {count} objects.");
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    // ═════════════════════════════════════════════════════════════════
    //  UTILITY METHODS
    // ═════════════════════════════════════════════════════════════════

    private OcclusionCullingManager FindManager()
    {
        if (OcclusionCullingManager.Instance != null) return OcclusionCullingManager.Instance;
        return FindObjectOfType<OcclusionCullingManager>();
    }

    private void DrawNoManagerWarning()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "No OcclusionCullingManager found in the scene.\n\n" +
            "Create an empty GameObject and add the OcclusionCullingManager component, " +
            "or use the button below.",
            MessageType.Warning
        );

        EditorGUILayout.Space(4);
        GUI.backgroundColor = EMERALD;
        if (GUILayout.Button("➕  Create OcclusionCullingManager in Scene", GUILayout.Height(30)))
        {
            var go = new GameObject("OcclusionCullingManager");
            go.AddComponent<OcclusionCullingManager>();
            Undo.RegisterCreatedObjectUndo(go, "Create OcclusionCullingManager");
            Selection.activeGameObject = go;
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawTabButton(string label, Tab tab)
    {
        bool isActive = _currentTab == tab;
        GUI.backgroundColor = isActive ? AZURE : Color.white;
        if (GUILayout.Button(label, GUILayout.Height(26)))
        {
            _currentTab = tab;
        }
        GUI.backgroundColor = Color.white;
    }

    private void DrawSeparator()
    {
        EditorGUILayout.Space(2);
        Rect rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
        rect.x += 4;
        rect.width -= 8;
        EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f, 0.5f));
        EditorGUILayout.Space(2);
    }

    private void DrawStatRow(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, _statLabelStyle);
        EditorGUILayout.LabelField(value, _statValueStyle, GUILayout.Width(120));
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Helper to convert LayerMask to the concatenated format used by EditorGUILayout.MaskField.
    /// </summary>
    private static int InternalEditorUtility_LayerMaskToConcatenatedLayersMask(LayerMask mask)
    {
        int result = 0;
        string[] layers = UnityEditorInternal.InternalEditorUtility.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            int layer = LayerMask.NameToLayer(layers[i]);
            if ((mask.value & (1 << layer)) != 0)
            {
                result |= (1 << i);
            }
        }
        return result;
    }

    /// <summary>
    /// Helper to convert from the concatenated format back to LayerMask.
    /// </summary>
    private static LayerMask InternalEditorUtility_ConcatenatedLayersMaskToLayerMask(int concatenated)
    {
        int result = 0;
        string[] layers = UnityEditorInternal.InternalEditorUtility.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if ((concatenated & (1 << i)) != 0)
            {
                int layer = LayerMask.NameToLayer(layers[i]);
                result |= (1 << layer);
            }
        }
        return result;
    }

    private void OnDisable()
    {
        // Clean up preview state
        if (_isPreviewActive)
        {
            _isPreviewActive = false;
            SceneView.duringSceneGui -= OnSceneGUIPreview;
            _previewCulledRenderers.Clear();
            _previewVisibleRenderers.Clear();
        }
    }
}
