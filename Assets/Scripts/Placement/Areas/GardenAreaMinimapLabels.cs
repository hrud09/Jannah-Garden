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

    [Tooltip("Padding added around each name when deciding whether two of them collide, in canvas " +
             "units. Larger keeps the map airier by dropping more names.")]
    [Range(0f, 30f)]
    public float declutterPadding = 4f;

    [Tooltip("Areas the player has unlocked.")]
    public Color yoursColor = new Color(0.82f, 1f, 0.84f, 1f);

    [Tooltip("Areas still to be earned. Held back so the map reads as 'mine, and those' at a glance " +
             "rather than as fifteen equally loud names.")]
    public Color lockedColor = new Color(1f, 0.92f, 0.72f, 0.75f);

    [Tooltip("Show the level a locked area opens at, after its name. The map is the natural place to " +
             "ask 'what do I get next', and the answer is a number.")]
    public bool showLockLevel = true;

    [Tooltip("Hide a name once the map is zoomed far enough out that the names would overlap. " +
             "Measured in metres of map height per label; 0 never hides anything.")]
    [Range(0f, 400f)]
    public float hideAboveMapHeight = 220f;

    [Header("Performance")]
    [Tooltip("Seconds between repositioning passes. The map moves smoothly, so this is the one thing " +
             "here worth doing often; it is still only a handful of rect assignments.")]
    [Range(0f, 0.5f)]
    public float refreshInterval;

    /// <summary>One label per area, built once and then only moved, tinted and hidden.</summary>
    private readonly List<TMP_Text> _labels = new List<TMP_Text>();
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

        for (int i = 0; i < _labels.Count; i++)
        {
            TMP_Text label = _labels[i];
            GardenAreaDefinition def = _areas[i];

            if (label == null || def == null) continue;

            Vector3 world = new Vector3(def.labelAnchor.x, 0f, def.labelAnchor.y);
            Vector3 viewport = cam.WorldToViewportPoint(world);

            // Behind the camera, or off the map: nothing to draw. Tested before the rect is touched
            // so an off-map name costs one projection and nothing else.
            bool onMap = viewport.z > 0f
                         && viewport.x > -0.05f && viewport.x < 1.05f
                         && viewport.y > -0.05f && viewport.y < 1.05f;

            if (!onMap)
            {
                if (label.gameObject.activeSelf) label.gameObject.SetActive(false);
                continue;
            }

            label.rectTransform.anchoredPosition = new Vector2(
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
            TMP_Text label = _labels[_candidates[c]];
            Rect rect = ClaimRect(label);

            // A name that only half fits is worse than no name: the mask cuts it mid-word and leaves
            // a fragment like "Lv 10" floating at the edge with nothing to attach it to.
            bool blocked = !Contains(bounds, rect);
            for (int j = 0; j < _claimed.Count && !blocked; j++)
            {
                if (_claimed[j].Overlaps(rect)) blocked = true;
            }

            if (blocked)
            {
                if (label.gameObject.activeSelf) label.gameObject.SetActive(false);
                continue;
            }

            _claimed.Add(rect);
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
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

    /// <summary>The space a name would take on the map, padded, in the label area's own coordinates.</summary>
    private Rect ClaimRect(TMP_Text label)
    {
        Vector2 centre = label.rectTransform.anchoredPosition;

        // GetPreferredValues rather than the preferredWidth/Height properties: those are only refreshed
        // when the text is laid out, and a name that was hidden last frame has not been.
        Vector2 preferred = label.GetPreferredValues();

        float width = preferred.x + declutterPadding * 2f;
        float height = preferred.y + declutterPadding * 2f;

        return new Rect(centre.x - width * 0.5f, centre.y - height * 0.5f, width, height);
    }

    private void HideAll()
    {
        for (int i = 0; i < _labels.Count; i++)
        {
            if (_labels[i] != null && _labels[i].gameObject.activeSelf) _labels[i].gameObject.SetActive(false);
        }
    }

    /// <summary>Builds one label per area the first time, and again if the map is ever re-baked into
    /// a different number of areas.</summary>
    private void EnsureLabels(GardenAreaManager areas)
    {
        if (_labels.Count == areas.map.areas.Count) return;

        for (int i = 0; i < _labels.Count; i++)
        {
            if (_labels[i] != null) Destroy(_labels[i].gameObject);
        }

        _labels.Clear();
        _areas.Clear();

        RectTransform parent = labelArea != null ? labelArea : transform as RectTransform;

        foreach (GardenAreaDefinition def in areas.map.areas)
        {
            var go = new GameObject("Zone Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(150f, 26f);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;

            _labels.Add(text);
            _areas.Add(def);
        }

        _textDirty = true;
    }

    private void WriteText(GardenAreaManager areas)
    {
        _textDirty = false;

        LocalizationManager loc = LocalizationManager.Instance;
        if (loc == null) return;

        for (int i = 0; i < _labels.Count; i++)
        {
            TMP_Text label = _labels[i];
            GardenAreaDefinition def = _areas[i];
            if (label == null || def == null) continue;

            string name = areas.map.DisplayName(def);
            bool unlocked = areas.IsUnlocked(def);

            label.color = unlocked ? yoursColor : lockedColor;
            label.fontSize = fontSize;

            string value = !unlocked && showLockLevel
                ? loc.Get("zone.minimap_locked_format", name, def.RequiredLevel)
                : name;

            LocalizedRendering.SetText(label, value);
        }
    }
}
