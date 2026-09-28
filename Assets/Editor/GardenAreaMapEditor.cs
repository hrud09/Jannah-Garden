using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The inspector and Scene view for <see cref="GardenAreaMap"/> — where the garden's areas get their
/// numbers.
///
/// <para>The bake decides <em>where</em> the areas are; this decides <em>which is which</em>. Each
/// pocket is outlined in the Scene view in its own colour with its number floating over its centre,
/// so the numbering can be checked against the garden itself rather than against a list of
/// coordinates. Clicking a number selects that area; the inspector then renumbers it, names it, or
/// makes it the one the player starts with.</para>
///
/// <para>The outlines are traced from the baked grid once and cached, because tracing means walking
/// every cell and sampling terrain height along the result — fine once, far too slow every repaint.</para>
/// </summary>
[CustomEditor(typeof(GardenAreaMap))]
public class GardenAreaMapEditor : Editor
{
    private const float LineHeightOffset = 0.15f;

    /// <summary>Metres the grey sheet floats above the ground, clearing z-fighting with the terrain.</summary>
    private const float GroundOffset = 0.05f;

    /// <summary>
    /// A fade radius far beyond the terrain, which is how the preview covers the whole garden evenly.
    ///
    /// <para>Deliberately not <c>float.MaxValue</c>: the shader fades over <c>[_Radius * 0.72,
    /// _Radius]</c>, and that multiply on a maximal float overflows to infinity. <c>smoothstep</c>
    /// with both edges at infinity is undefined, and the NaN it produced propagated into alpha and
    /// clipped every pixel away — a preview that silently drew nothing at all.</para>
    /// </summary>
    private const float NoFadeRadius = 100000f;

    private static readonly Color LockedOutline = new Color(0.55f, 0.55f, 0.58f);
    private static readonly Color LockedBadge = new Color(0.58f, 0.58f, 0.61f);

    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");

    /// <summary>Outline segment pairs per area slot, in world space. Rebuilt when the bake changes.</summary>
    private Dictionary<byte, Vector3[]> _outlines;
    private int _outlineFingerprint;

    private GardenAreaDefinition _selected;
    private Vector2 _scroll;

    /// <summary>Row order, taken on the layout pass and reused for the repaint. See DrawAreaList.</summary>
    private List<GardenAreaDefinition> _rows;

    /// <summary>Locked ground, drawn flat over the terrain. Rebuilt when the bake or the locks change.</summary>
    private Mesh _lockedMesh;
    private int _lockedFingerprint = int.MinValue;

    /// <summary>
    /// The game's own locked-ground material, instanced so the preview's fade override does not write
    /// back to the asset. There is deliberately no separate editor material: the overlay the player
    /// sees is already grey, so drawing anything else here would be showing you a different garden
    /// than the one you are shipping.
    /// </summary>
    private Material _playerMaterial;

    /// <summary>Which slots the preview covers. See <see cref="GardenAreaMesh.FillLockedSlots"/>.</summary>
    private bool[] _lockedSlots;

    private bool _greyOutLocked = true;

    /// <summary>
    /// Longer runs than the game uses. The preview covers the whole garden rather than a placement
    /// radius, and this terrain is gentle enough that the extra span costs nothing in how closely the
    /// sheet hugs the ground — it halves a build that samples terrain height tens of thousands of times.
    /// </summary>
    private const int PreviewMaxRun = 12;

    private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;

        if (_lockedMesh != null) DestroyImmediate(_lockedMesh);
        if (_playerMaterial != null) DestroyImmediate(_playerMaterial);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  INSPECTOR
    // ═══════════════════════════════════════════════════════════════════════════

    public override void OnInspectorGUI()
    {
        var map = (GardenAreaMap)target;

        if (GUILayout.Button("Bake Area Map From Terrain Paint", GUILayout.Height(28f)))
        {
            GardenAreaBaker.Bake();
            _outlines = null;
            return;
        }

        if (!map.IsBaked)
        {
            EditorGUILayout.HelpBox(
                "Not baked yet. Open the garden scene and bake — the areas are read from the terrain's " +
                "painted roads, so there is nothing to author by hand.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            map.areas.Count + " areas   •   " + map.resolution + "x" + map.resolution +
            " grid   •   " + map.CellSize.ToString("F2") + " m per cell",
            EditorStyles.miniLabel);

        GardenAreaDefinition starting = map.areas.Find(a => a.unlockedFromStart);
        if (starting == null)
        {
            EditorGUILayout.HelpBox(
                "No starting area. Pick the one the player lands with, or they begin with nowhere to build.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        _greyOutLocked = EditorGUILayout.ToggleLeft(
            "Grey out locked areas in the Scene view", _greyOutLocked);
        if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

        EditorGUILayout.LabelField(
            Application.isPlaying && GardenAreaManager.Instance != null
                ? "Locks shown: live unlock state from the running game, in the player's own overlay."
                : "Locks shown: everything except the starting area, in the player's own overlay. " +
                  "Enter play mode for live state.",
            EditorStyles.miniLabel);

        EditorGUILayout.Space();
        DrawAreaList(map);

        EditorGUILayout.Space();
        if (GUILayout.Button("Renumber In Reading Order (north to south)"))
        {
            RenumberInReadingOrder(map);
        }

        EditorGUILayout.HelpBox(
            "Numbers are the player-facing order and can be changed at any time. Unlock save data is " +
            "keyed to each area's hidden id instead, so renumbering never moves a player's unlocked ground.",
            MessageType.None);
    }

    /// <summary>
    /// The list of areas, in player-facing order.
    ///
    /// <para><b>Why the row order and the selection are snapshotted:</b> IMGUI matches a control to
    /// its value by the order controls are drawn, and it draws the whole inspector at least twice per
    /// frame — once to lay out, once to paint. Anything that changes the sequence of controls between
    /// those two passes silently hands one row's edit to another row. Sorting by a number the user is
    /// editing, and styling a row by a selection that a button inside the same loop can change, both
    /// do exactly that: ticking "start" on one area moved it to a different one. So the order is taken
    /// once on the layout pass and reused, the selection is read once up front, and every row draws
    /// the same controls whether it is selected or not.</para>
    /// </summary>
    private void DrawAreaList(GardenAreaMap map)
    {
        if (Event.current.type == EventType.Layout || _rows == null || _rows.Count != map.areas.Count)
        {
            _rows = map.InNumberOrder();
        }

        GardenAreaDefinition selected = _selected;
        GardenAreaDefinition clicked = null;
        bool frameClicked = false;

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(420f));

        foreach (GardenAreaDefinition area in _rows)
        {
            bool isSelected = area == selected;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            Color previousContent = GUI.color;
            GUI.color = _greyOutLocked && IsLocked(area) ? LockedBadge : area.editorColor;
            GUILayout.Label("■", GUILayout.Width(16f));
            GUI.color = previousContent;

            // Separate change checks: a change to one field must never write the other back, which is
            // what let a repaint of the number field re-assert a stale "start".
            EditorGUI.BeginChangeCheck();
            int number = EditorGUILayout.IntField(area.number, GUILayout.Width(34f));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(map, "Renumber Garden Area");
                area.number = Mathf.Max(1, number);
                EditorUtility.SetDirty(map);
            }

            EditorGUI.BeginChangeCheck();
            bool start = GUILayout.Toggle(
                area.unlockedFromStart, "start", EditorStyles.miniButton, GUILayout.Width(44f));

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(map, "Set Starting Garden Area");

                // Exactly one starting area: two would both be unlocked, which is not what "the area
                // you start with" means, and the onboarding has no way to point at both. Unticking
                // the current one is allowed and simply leaves none, which the header warns about.
                if (start)
                {
                    foreach (GardenAreaDefinition other in map.areas) other.unlockedFromStart = false;
                }

                area.unlockedFromStart = start;
                EditorUtility.SetDirty(map);
                SceneView.RepaintAll();
            }

            GUILayout.Label(area.squareMetres.ToString("F0") + " m²", EditorStyles.miniLabel, GUILayout.Width(56f));

            if (GUILayout.Button("Look", EditorStyles.miniButton, GUILayout.Width(44f)))
            {
                clicked = area;
                frameClicked = true;
            }

            EditorGUILayout.EndHorizontal();

            // Drawn for every row, not just the selected one, so the control sequence is identical on
            // the layout and repaint passes. Only the selected row's fields are editable.
            using (new EditorGUI.DisabledScope(!isSelected))
            {
                EditorGUI.BeginChangeCheck();
                string nameKey = EditorGUILayout.TextField("Name key", area.displayNameKey);
                if (EditorGUI.EndChangeCheck() && isSelected)
                {
                    Undo.RecordObject(map, "Rename Garden Area");
                    area.displayNameKey = nameKey;
                    EditorUtility.SetDirty(map);
                }
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();

        // Applied after the loop: changing the selection mid-loop is one of the things that desynced
        // the layout in the first place.
        if (clicked != null)
        {
            _selected = clicked;
            if (frameClicked) FrameArea(clicked);
        }
    }

    /// <summary>
    /// Renumbers every area north-to-south then west-to-east — the order they read in a top-down view.
    /// A starting point for numbering, not a rule: hand-edit afterwards as much as you like.
    /// </summary>
    private void RenumberInReadingOrder(GardenAreaMap map)
    {
        Undo.RecordObject(map, "Renumber Garden Areas");

        List<GardenAreaDefinition> ordered = new List<GardenAreaDefinition>(map.areas);
        ordered.Sort((a, b) =>
        {
            int byRow = b.centroid.y.CompareTo(a.centroid.y);
            return byRow != 0 ? byRow : a.centroid.x.CompareTo(b.centroid.x);
        });

        for (int i = 0; i < ordered.Count; i++) ordered[i].number = i + 1;

        EditorUtility.SetDirty(map);
    }

    private static void FrameArea(GardenAreaDefinition area)
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null) return;

        Terrain terrain = ResolveTerrain();
        var centre = new Vector3(area.labelAnchor.x, 0f, area.labelAnchor.y);
        if (terrain != null) centre.y = terrain.SampleHeight(centre) + terrain.transform.position.y;

        // Framing radius from the pocket's own area: a small pocket filling the view and a big one
        // spilling off the edges are both useless for checking a number against the ground.
        float radius = Mathf.Max(12f, Mathf.Sqrt(Mathf.Max(1f, area.squareMetres)) * 0.9f);
        view.Frame(new Bounds(centre, Vector3.one * radius * 2f), false);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SCENE VIEW
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnSceneGUI(SceneView view)
    {
        var map = (GardenAreaMap)target;
        if (!map.IsBaked) return;

        if (_greyOutLocked && Event.current.type == EventType.Repaint) DrawLockedGround(map);

        EnsureOutlines(map);

        foreach (GardenAreaDefinition area in map.areas)
        {
            int slot = map.areas.IndexOf(area) + 1;
            if (!_outlines.TryGetValue((byte)slot, out Vector3[] segments) || segments.Length == 0) continue;

            bool isSelected = area == _selected;

            // A locked area's outline goes grey along with its ground, so the Scene view answers
            // "which of these can be built in" at a glance rather than only under the numbers.
            Color colour = _greyOutLocked && IsLocked(area) ? LockedOutline : area.editorColor;
            colour.a = isSelected ? 1f : 0.65f;

            Handles.color = colour;
            Handles.DrawLines(segments);
        }

        DrawNumberLabels(map, view);
    }

    /// <summary>
    /// Draws the locked ground itself, flat over the terrain.
    ///
    /// <para>Built by <see cref="GardenAreaMesh.BuildGroundPatch"/> — the same call the in-game
    /// overlay makes — so this preview cannot show different ground than the player will be refused.
    /// The difference is deliberate and only one: the game clips the patch to the placement radius
    /// around the player, and this covers the whole garden, because the question being answered here
    /// is about the map rather than about where someone happens to be standing.</para>
    /// </summary>
    private void DrawLockedGround(GardenAreaMap map)
    {
        Terrain terrain = ResolveTerrain();
        if (terrain == null) return;

        EnsureLockedMesh(map, terrain);

        if (_playerMaterial == null || _lockedMesh == null || _lockedMesh.vertexCount == 0) return;

        _playerMaterial.SetPass(0);
        Graphics.DrawMeshNow(_lockedMesh, Matrix4x4.identity);
    }

    private void EnsureLockedMesh(GardenAreaMap map, Terrain terrain)
    {
        EnsureMaterials();

        int fingerprint = LockFingerprint(map);
        if (_lockedMesh != null && _lockedFingerprint == fingerprint) return;

        _lockedFingerprint = fingerprint;

        if (_lockedMesh == null)
        {
            _lockedMesh = new Mesh { name = "GardenAreaLockedPreview" };
            _lockedMesh.MarkDynamic();
            _lockedMesh.hideFlags = HideFlags.HideAndDontSave;
        }

        float terrainY = terrain.transform.position.y;

        GardenAreaMesh.BuildGroundPatch(
            map,
            GardenAreaMesh.FillLockedSlots(map, IsLocked, ref _lockedSlots),
            (x, z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + terrainY,
            Vector3.zero,
            -1f,
            PreviewMaxRun,
            GroundOffset,
            _lockedMesh);
    }

    private void EnsureMaterials()
    {
        Material source = Resources.Load<Material>("Placement/PlacementLockedGround");
        if (source == null) return;

        if (_playerMaterial == null)
        {
            _playerMaterial = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            // The radial falloff exists to hide the edge of a patch clipped to the placement radius.
            // This patch has no such edge, so the fade is pushed far enough away to never bite.
            _playerMaterial.SetFloat(RadiusId, NoFadeRadius);
            _playerMaterial.SetVector(CenterId, Vector4.zero);
        }

        // The scene's own style, so the preview shows the look being shipped rather than the
        // material's defaults. Re-read every repaint: the style is an asset someone may be editing.
        PlacementGridView view = FindFirstObjectByType<PlacementGridView>();
        if (view != null && view.lockedGroundStyle != null) view.lockedGroundStyle.ApplyTo(_playerMaterial);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  LOCK STATE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Whether an area reads as locked right now.
    ///
    /// <para>In play mode that is the running game's own unlock set, so the Scene view tracks a real
    /// session as areas open. Outside play mode there is no session to ask, and the only honest
    /// answer is the one the map itself carries: everything but the starting area.</para>
    /// </summary>
    private static bool IsLocked(GardenAreaDefinition area)
    {
        GardenAreaManager live = Application.isPlaying ? GardenAreaManager.Instance : null;

        if (live != null && live.IsReady) return !live.IsUnlocked(area);

        return !area.unlockedFromStart;
    }

    /// <summary>Changes to the bake or to any lock, as one number, so the mesh rebuilds when it must.</summary>
    private int LockFingerprint(GardenAreaMap map)
    {
        int hash = map.resolution * 397 ^ map.areas.Count * 31 ^ map.cells.Length;

        for (int i = 0; i < map.areas.Count; i++)
        {
            hash = hash * 31 + (IsLocked(map.areas[i]) ? 1 : 0);
        }

        return hash;
    }

    private void DrawNumberLabels(GardenAreaMap map, SceneView view)
    {
        Terrain terrain = ResolveTerrain();
        Handles.BeginGUI();

        foreach (GardenAreaDefinition area in map.areas)
        {
            var world = new Vector3(area.labelAnchor.x, 0f, area.labelAnchor.y);
            if (terrain != null) world.y = terrain.SampleHeight(world) + terrain.transform.position.y;

            Vector3 screen = HandleUtility.WorldToGUIPointWithDepth(world);
            if (screen.z < 0f) continue;

            var rect = new Rect(screen.x - 22f, screen.y - 16f, 44f, 32f);
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = _greyOutLocked && IsLocked(area) ? LockedBadge : area.editorColor;

            if (GUI.Button(rect, area.number.ToString(), style))
            {
                _selected = area;
                Repaint();
            }

            GUI.backgroundColor = previous;

            if (area.unlockedFromStart)
            {
                GUI.Label(new Rect(rect.x - 8f, rect.yMax - 2f, 60f, 16f), "start", EditorStyles.miniLabel);
            }
        }

        Handles.EndGUI();
        view.Repaint();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  OUTLINE TRACING
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Traces each area's border out of the baked grid: every cell edge where the slot changes.
    ///
    /// <para>Cached against a fingerprint of the bake rather than rebuilt per repaint — the trace
    /// walks a quarter of a million cells and samples terrain height along everything it finds.</para>
    /// </summary>
    private void EnsureOutlines(GardenAreaMap map)
    {
        int fingerprint = map.resolution * 397 ^ map.areas.Count * 31 ^ map.cells.Length;
        if (_outlines != null && _outlineFingerprint == fingerprint) return;

        _outlineFingerprint = fingerprint;
        _outlines = new Dictionary<byte, Vector3[]>();

        Terrain terrain = ResolveTerrain();
        int res = map.resolution;
        float cell = map.CellSize;

        var builders = new Dictionary<byte, List<Vector3>>();

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                byte slot = map.cells[y * res + x];
                if (slot == GardenAreaMap.NoArea) continue;

                if (!builders.TryGetValue(slot, out List<Vector3> segments))
                {
                    segments = new List<Vector3>();
                    builders[slot] = segments;
                }

                float x0 = map.origin.x + x * cell;
                float z0 = map.origin.z + y * cell;

                // Only the sides facing a different slot become border. Comparing against the
                // out-of-bounds neighbour as NoArea is what closes an area that runs to the terrain edge.
                if (x == 0 || map.cells[y * res + (x - 1)] != slot) AddSegment(segments, terrain, x0, z0, x0, z0 + cell);
                if (x == res - 1 || map.cells[y * res + (x + 1)] != slot) AddSegment(segments, terrain, x0 + cell, z0, x0 + cell, z0 + cell);
                if (y == 0 || map.cells[(y - 1) * res + x] != slot) AddSegment(segments, terrain, x0, z0, x0 + cell, z0);
                if (y == res - 1 || map.cells[(y + 1) * res + x] != slot) AddSegment(segments, terrain, x0, z0 + cell, x0 + cell, z0 + cell);
            }
        }

        foreach (KeyValuePair<byte, List<Vector3>> entry in builders)
        {
            _outlines[entry.Key] = entry.Value.ToArray();
        }
    }

    private static void AddSegment(List<Vector3> into, Terrain terrain, float x0, float z0, float x1, float z1)
    {
        into.Add(OnGround(terrain, x0, z0));
        into.Add(OnGround(terrain, x1, z1));
    }

    private static Vector3 OnGround(Terrain terrain, float x, float z)
    {
        var world = new Vector3(x, 0f, z);
        if (terrain != null) world.y = terrain.SampleHeight(world) + terrain.transform.position.y;
        return world + Vector3.up * LineHeightOffset;
    }

    private static Terrain ResolveTerrain()
    {
        var go = GameObject.Find("LandTerrain");
        Terrain terrain = go != null ? go.GetComponent<Terrain>() : null;
        return terrain != null ? terrain : Terrain.activeTerrain;
    }
}
