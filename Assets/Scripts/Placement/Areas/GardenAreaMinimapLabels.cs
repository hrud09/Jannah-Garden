using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Draws each garden area's name onto the minimap, at the point deepest inside it.
///
/// <para>This is where the division of the garden stops being a rule and becomes a <em>map</em>. The
/// ground overlays say "here" and "not here" about whatever the player is standing next to; a named
/// map says the garden is fifteen places, shows where they are relative to each other, and shows
/// which ones are still to come. It is the one view that answers "what is the shape of this
/// progression" without the player walking the whole terrain to find out.</para>
///
/// <para><b>Why UI labels projected through the camera, and not world-space text the minimap camera
/// happens to see:</b> world text scales with the map's zoom, so a name legible on the collapsed map
/// is a wall of letters on the expanded one. Projecting the anchor point and placing a UI label there
/// keeps every name the same size on screen, whatever the map is doing — and lets the labels be
/// clipped, tinted and ordered like the rest of the HUD.</para>
///
/// <para><b>Why the unlock level is its own object on its own line,</b> rather than a
/// <c>&lt;size=85%&gt;</c> run appended to the name: the two are different kinds of fact. The name is
/// the place; the level is a price. Printed on one line they read as one long name — "Rose Court Lv 12"
/// — and the eye has to parse the whole string to find the number it came for. Stacked, the names form
/// one scannable column and the prices another underneath them, each in its own colour, and the number
/// can be tinted gold without dragging the name's tint with it. It also means a locked area's price can
/// be hidden without rewriting the name, which is what happens the moment it unlocks.</para>
/// </summary>
public class GardenAreaMinimapLabels : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The minimap. Found in the scene when left empty.")]
    public MinimapBehaviour minimap;

    [Tooltip("The rect the labels are placed inside — normally a masked child covering the map " +
             "image exactly. Falls back to this object's own rect.")]
    public RectTransform labelArea;

    [Tooltip("Font for the labels. Taken from the first label built when left empty.")]
    public TMP_FontAsset font;

    [Header("Style")]
    [Range(6f, 48f)]
    public float fontSize = 14f;

    [Tooltip("The unlock line's size as a fraction of the name's. Small enough to read as a caption " +
             "under the name rather than as a second name.")]
    [Range(0.4f, 1f)]
    public float levelFontScale = 0.68f;

    [Tooltip("Gap between the name and the unlock line beneath it, in canvas units.")]
    [Range(-6f, 16f)]
    public float lineGap = 1f;

    [Tooltip("Padding added around each name when deciding whether two of them collide, in canvas " +
             "units. Larger keeps the map airier by dropping more names.")]
    [Range(0f, 30f)]
    public float declutterPadding = 4f;

    [Tooltip("Areas the player has unlocked.")]
    public Color yoursColor = new Color(0.82f, 1f, 0.84f, 1f);

    [Tooltip("Areas still to be earned. Held back so the map reads as 'mine, and those' at a glance " +
             "rather than as fifteen equally loud names.")]
    public Color lockedColor = new Color(1f, 0.92f, 0.72f, 0.75f);

    [Tooltip("The unlock line under a locked name. Gold, because it is the one number on the map the " +
             "player is being invited to chase.")]
    public Color levelColor = new Color(0.98f, 0.76f, 0.35f, 0.95f);

    [Tooltip("Show the level a locked area opens at, under its name. The map is the natural place to " +
             "ask 'what do I get next', and the answer is a number.")]
    public bool showLockLevel = true;

    [Header("Legibility")]
    [Tooltip("Dark outline around the glyphs, so a pale name stays readable over a pale patch of map. " +
             "One material is shared by every label, so this costs no extra draw calls.")]
    public bool outlineLabels = true;

    [Range(0f, 0.4f)]
    public float outlineWidth = 0.18f;

    public Color outlineColor = new Color(0.06f, 0.11f, 0.08f, 0.9f);

    [Tooltip("Hide a name once the map is zoomed far enough out that the names would overlap. " +
             "Measured in metres of map height per label; 0 never hides anything.")]
    [Range(0f, 400f)]
    public float hideAboveMapHeight = 220f;

    [Header("Performance")]
    [Tooltip("Seconds between repositioning passes. The map moves smoothly, so this is the one thing " +
             "here worth doing often; it is still only a handful of rect assignments.")]
    [Range(0f, 0.5f)]
    public float refreshInterval;

    /// <summary>
    /// One label per area, built once and then only moved, tinted and hidden. Parallel lists rather
    /// than a struct per label: every pass walks them by index, and the hot loop only ever touches
    /// <see cref="_roots"/>.
    /// </summary>
    private readonly List<RectTransform> _roots = new List<RectTransform>();
    private readonly List<TMP_Text> _names = new List<TMP_Text>();
    private readonly List<TMP_Text> _levels = new List<TMP_Text>();
    private readonly List<GardenAreaDefinition> _areas = new List<GardenAreaDefinition>();

    /// <summary>
    /// The names that made it onto the map this pass, and the rects they claimed.
    ///
    /// <para>Reused between passes rather than reallocated: this runs every frame the map is on
    /// screen, and fifteen rects a frame is the kind of garbage that shows up as a stutter an hour
    /// into a session rather than as a number in the profiler.</para>
    /// </summary>
    private readonly List<int> _candidates = new List<int>();
    private readonly List<Rect> _claimed = new List<Rect>();

    /// <summary>
    /// The outlined copy of the font's material, created once and shared by every label.
    ///
    /// <para>Setting <c>TMP_Text.outlineWidth</c> per label would instance the material per label, which
    /// is fifteen extra draw calls on a HUD that is otherwise one — the kind of cost that only shows up
    /// on the phones least able to pay it.</para>
    /// </summary>
    private Material _outlined;

    private const string OutlineWidthProperty = "_OutlineWidth";
    private const string OutlineColorProperty = "_OutlineColor";
    private const string OutlineKeyword = "OUTLINE_ON";

    private GardenAreaManager _subscribed;
    private float _timer;
    private bool _textDirty = true;

    private void OnEnable()
    {
        LocalizationManager.OnLocaleChanged += MarkTextDirty;
    }

    private void OnDisable()
    {
        LocalizationManager.OnLocaleChanged -= MarkTextDirty;
    }

    private void OnDestroy()
    {
        if (_subscribed != null) _subscribed.UnlocksChanged -= MarkTextDirty;
        _subscribed = null;

        if (_outlined != null) Destroy(_outlined);
        _outlined = null;
    }

    /// <summary>Guarded: every locale subscriber shares one dispatch, and a throw here would cost the
    /// subscribers after it their own update.</summary>
    private void MarkTextDirty()
    {
        try
        {
            _textDirty = true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[GardenAreaMinimapLabels] " + e.Message);
        }
    }

    private void LateUpdate()
    {
        SyncSubscription();

        if (refreshInterval > 0f)
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
        }

        // After MinimapBehaviour's own LateUpdate has moved the camera - or one frame behind it, if
        // the order falls the other way, which at walking pace is not something an eye can catch.
        Reposition();
    }

    private void SyncSubscription()
    {
        GardenAreaManager areas = GardenAreaManager.Instance;
        if (_subscribed == areas) return;

        if (_subscribed != null) _subscribed.UnlocksChanged -= MarkTextDirty;

        _subscribed = areas;

        if (_subscribed != null) _subscribed.UnlocksChanged += MarkTextDirty;

        _textDirty = true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DRAWING
    // ═══════════════════════════════════════════════════════════════════════════

    private void Reposition()
    {
        GardenAreaManager areas = GardenAreaManager.Instance;
        MinimapBehaviour map = minimap != null ? minimap : (minimap = FindFirstObjectByType<MinimapBehaviour>());

        Camera cam = map != null ? map.MinimapCamera : null;
        RectTransform area = labelArea != null ? labelArea : transform as RectTransform;

        if (areas == null || !areas.IsReady || cam == null || area == null)
        {
            HideAll();
            return;
        }

        EnsureLabels(areas);
        if (_textDirty) WriteText(areas);

        // The map's height in metres: two orthographic half-heights. What the "too far out to read"
        // test is measured against, so it responds to expanding the map as well as to zooming it.
        float mapHeight = cam.orthographicSize * 2f;
        bool tooFarOut = hideAboveMapHeight > 0f && mapHeight > hideAboveMapHeight;

        if (tooFarOut)
        {
            HideAll();
            return;
        }

        Vector2 size = area.rect.size;

        // Pass one: project every name and keep the ones that land on the map, positioning them as
        // we go so the collision test below measures where they would actually sit.
        _candidates.Clear();

        for (int i = 0; i < _roots.Count; i++)
        {
            RectTransform root = _roots[i];
            GardenAreaDefinition def = _areas[i];

            if (root == null || def == null) continue;

            Vector3 world = new Vector3(def.labelAnchor.x, 0f, def.labelAnchor.y);
            Vector3 viewport = cam.WorldToViewportPoint(world);

            // Behind the camera, or off the map: nothing to draw. Tested before the rect is touched
            // so an off-map name costs one projection and nothing else.
            bool onMap = viewport.z > 0f
                         && viewport.x > -0.05f && viewport.x < 1.05f
                         && viewport.y > -0.05f && viewport.y < 1.05f;

            if (!onMap)
            {
                if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
                continue;
            }

            root.anchoredPosition = new Vector2(
                (viewport.x - 0.5f) * size.x,
                (viewport.y - 0.5f) * size.y);

            _candidates.Add(i);
        }

        SortByPriority(areas);

        // Pass two: hand out the space. A map with fifteen names printed over each other answers
        // nothing - it is strictly worse than a map with four names on it - so a name that would
        // land on one already placed is simply dropped this frame. The order above decides who wins:
        // the zone underfoot first, then the big ones, so what disappears as the player zooms out is
        // always the least useful thing on screen.
        _claimed.Clear();

        var bounds = new Rect(-size.x * 0.5f, -size.y * 0.5f, size.x, size.y);

        for (int c = 0; c < _candidates.Count; c++)
        {
            int index = _candidates[c];
            RectTransform root = _roots[index];
            Rect rect = ClaimRect(index);

            // A name that only half fits is worse than no name: the mask cuts it mid-word and leaves
            // a fragment like "Lv 10" floating at the edge with nothing to attach it to.
            bool blocked = !Contains(bounds, rect);
            for (int j = 0; j < _claimed.Count && !blocked; j++)
            {
                if (_claimed[j].Overlaps(rect)) blocked = true;
            }

            if (blocked)
            {
                if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
                continue;
            }

            _claimed.Add(rect);
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Orders the visible candidates by how much the player needs to see each one: the zone they are
    /// standing in first, then the largest zones, which are the ones whose names help most in placing
    /// everything else.
    ///
    /// <para>An insertion sort rather than <c>List.Sort</c>: the list is at most fifteen entries and
    /// nearly ordered from the frame before, which is the case insertion sort is best at and the one
    /// a comparison sort allocates a delegate for.</para>
    /// </summary>
    private void SortByPriority(GardenAreaManager areas)
    {
        GardenAreaDefinition here = null;

        TargetDirectionController player = TargetDirectionController.Instance;
        if (player != null) here = areas.AreaAt(player.transform.position);

        for (int i = 1; i < _candidates.Count; i++)
        {
            int value = _candidates[i];
            float score = Priority(_areas[value], here);

            int j = i - 1;
            while (j >= 0 && Priority(_areas[_candidates[j]], here) < score)
            {
                _candidates[j + 1] = _candidates[j];
                j--;
            }

            _candidates[j + 1] = value;
        }
    }

    private static float Priority(GardenAreaDefinition def, GardenAreaDefinition here)
    {
        if (def == null) return float.MinValue;

        // Far above any area in square metres, so the zone underfoot is never the one dropped.
        return ReferenceEquals(def, here) ? float.MaxValue : def.squareMetres;
    }

    /// <summary>Whether <paramref name="inner"/> sits entirely within <paramref name="outer"/>.</summary>
    private static bool Contains(Rect outer, Rect inner)
    {
        return inner.xMin >= outer.xMin && inner.xMax <= outer.xMax
               && inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;
    }

    /// <summary>
    /// The space a label would take on the map, padded, in the label area's own coordinates — the name
    /// and, when it is showing, the unlock line stacked under it.
    /// </summary>
    private Rect ClaimRect(int index)
    {
        Vector2 centre = _roots[index].anchoredPosition;

        TMP_Text name = _names[index];
        TMP_Text level = _levels[index];

        // GetPreferredValues rather than the preferredWidth/Height properties: those are only refreshed
        // when the text is laid out, and a name that was hidden last frame has not been.
        Vector2 preferred = name.GetPreferredValues();

        float width = preferred.x;
        float height = preferred.y;

        if (level != null && level.gameObject.activeSelf)
        {
            Vector2 levelPreferred = level.GetPreferredValues();
            width = Mathf.Max(width, levelPreferred.x);
            height += lineGap + levelPreferred.y;
        }

        width += declutterPadding * 2f;
        height += declutterPadding * 2f;

        return new Rect(centre.x - width * 0.5f, centre.y - height * 0.5f, width, height);
    }

    private void HideAll()
    {
        for (int i = 0; i < _roots.Count; i++)
        {
            if (_roots[i] != null && _roots[i].gameObject.activeSelf) _roots[i].gameObject.SetActive(false);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  BUILDING
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Builds one label per area the first time, and again if the map is ever re-baked into
    /// a different number of areas.</summary>
    private void EnsureLabels(GardenAreaManager areas)
    {
        if (_roots.Count == areas.map.areas.Count) return;

        for (int i = 0; i < _roots.Count; i++)
        {
            if (_roots[i] != null) Destroy(_roots[i].gameObject);
        }

        _roots.Clear();
        _names.Clear();
        _levels.Clear();
        _areas.Clear();

        RectTransform parent = labelArea != null ? labelArea : transform as RectTransform;

        foreach (GardenAreaDefinition def in areas.map.areas)
        {
            var go = new GameObject("Zone Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var root = go.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(150f, 26f);

            TMP_Text name = BuildLine(root, "Name", fontSize, TMPro.FontWeight.Bold);
            TMP_Text level = BuildLine(root, "Unlock Level", fontSize * levelFontScale, TMPro.FontWeight.Medium);

            // Off until a locked area asks for it, so an unlocked map is exactly as quiet as it was.
            level.gameObject.SetActive(false);

            _roots.Add(root);
            _names.Add(name);
            _levels.Add(level);
            _areas.Add(def);
        }

        _textDirty = true;
    }

    /// <summary>One line of a label: its own object, so the two lines can be sized, tinted and shown
    /// independently of each other.</summary>
    private TMP_Text BuildLine(RectTransform parent, string objectName, float size, TMPro.FontWeight weight)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(150f, Mathf.Max(8f, size * 1.25f));

        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;

        Material outlined = EnsureOutlineMaterial(text);
        if (outlined != null) text.fontSharedMaterial = outlined;

        text.fontSize = size;
        text.fontWeight = weight;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;

        return text;
    }

    /// <summary>
    /// The shared outlined material, made from the first label's font the first time one is built.
    ///
    /// <para>Guarded on the property rather than assumed: the project ships several TMP shader variants,
    /// and a font whose material has no <c>_OutlineWidth</c> should fall back to a plain face rather
    /// than log a warning per label per scene load.</para>
    /// </summary>
    private Material EnsureOutlineMaterial(TMP_Text source)
    {
        if (!outlineLabels || outlineWidth <= 0f) return null;
        if (_outlined != null) return _outlined;

        Material shared = source.fontSharedMaterial;
        if (shared == null) return null;

        var copy = new Material(shared) { name = shared.name + " (Minimap Labels)" };

        // Looked up by name rather than through ShaderUtilities' cached ids: those ids are only
        // populated for the shader family TMP shipped with, and the project's fonts do not all use it.
        if (!copy.HasProperty(OutlineWidthProperty))
        {
            Destroy(copy);
            return null;
        }

        copy.SetFloat(OutlineWidthProperty, outlineWidth);
        if (copy.HasProperty(OutlineColorProperty)) copy.SetColor(OutlineColorProperty, outlineColor);
        copy.EnableKeyword(OutlineKeyword);

        _outlined = copy;
        return _outlined;
    }

    private void WriteText(GardenAreaManager areas)
    {
        _textDirty = false;

        LocalizationManager loc = LocalizationManager.Instance;
        if (loc == null) return;

        for (int i = 0; i < _roots.Count; i++)
        {
            TMP_Text name = _names[i];
            TMP_Text level = _levels[i];
            GardenAreaDefinition def = _areas[i];
            if (name == null || level == null || def == null) continue;

            bool unlocked = areas.IsUnlocked(def);
            bool showLevel = !unlocked && showLockLevel;

            name.color = unlocked ? yoursColor : lockedColor;
            name.fontSize = fontSize;

            LocalizedRendering.SetText(name, areas.map.DisplayName(def));

            if (level.gameObject.activeSelf != showLevel) level.gameObject.SetActive(showLevel);

            if (showLevel)
            {
                level.color = levelColor;
                level.fontSize = fontSize * levelFontScale;
                LocalizedRendering.SetText(level, loc.Get("zone.minimap_level", def.RequiredLevel));
            }

            Stack(i, showLevel);
        }
    }

    /// <summary>
    /// Centres the name over the unlock line, or centres the name alone when there is no second line.
    ///
    /// <para>Done here rather than with a vertical layout group: a layout group on fifteen labels that
    /// move every frame rebuilds fifteen layouts every frame, and the arithmetic it would do is the two
    /// assignments below.</para>
    /// </summary>
    private void Stack(int index, bool showLevel)
    {
        RectTransform nameRect = _names[index].rectTransform;
        RectTransform levelRect = _levels[index].rectTransform;

        if (!showLevel)
        {
            nameRect.anchoredPosition = Vector2.zero;
            return;
        }

        float nameHeight = nameRect.sizeDelta.y;
        float levelHeight = levelRect.sizeDelta.y;

        nameRect.anchoredPosition = new Vector2(0f, (levelHeight + lineGap) * 0.5f);
        levelRect.anchoredPosition = new Vector2(0f, -(nameHeight + lineGap) * 0.5f);
    }
}
