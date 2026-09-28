using TMPro;
using UnityEngine;

/// <summary>
/// One line under the minimap naming the patch of garden the player is standing in, and saying
/// whether it is theirs.
///
/// <para>The ground overlays answer "where may I build" while something is in hand. This answers the
/// other half — that the garden is a set of <em>places</em>, each with a name, each earned — and it
/// answers it while the player is simply walking, which is when they are actually looking at the
/// garden.</para>
///
/// <para><b>Why a caption under the map and not a banner across the screen:</b> it is the same kind of
/// fact the minimap already shows, so it belongs in the same corner, read in the same glance. A card
/// in the middle of the screen makes a small standing fact look like an announcement, and has to be
/// dismissed — by time or by tapping — before the player can see the garden it is describing.</para>
///
/// <para>It stays put rather than fading away (see <see cref="staysVisible"/>): the question "which
/// part of the garden am I in" does not stop being asked, and a line small enough to sit under the
/// map is small enough to leave there.</para>
/// </summary>
public class GardenAreaBanner : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Faded in when the zone changes.")]
    public CanvasGroup group;

    [Tooltip("The caption's object — a child, never this object itself. Switched off when there is " +
             "nothing to say; this component has to keep running to notice the next crossing, and a " +
             "MonoBehaviour on a deactivated object does not.")]
    public GameObject content;

    [Tooltip("The single line: zone name, then what the player may do there.")]
    public TMP_Text lineText;

    [Header("Placement")]
    [Tooltip("Kept directly beneath this — the minimap. The map grows and shrinks when the player " +
             "expands it, so the caption follows its bottom edge rather than sitting at a fixed " +
             "offset that would end up on top of the expanded map.")]
    public RectTransform anchorUnder;

    [Tooltip("Gap between the bottom of the minimap and the top of the caption, in canvas units.")]
    public float gapUnderAnchor = 10f;

    [Header("Colours")]
    [Tooltip("Ground the player may build on.")]
    public Color yoursColor = new Color(0.72f, 1f, 0.76f, 1f);

    [Tooltip("A zone that has not been earned yet.")]
    public Color lockedColor = new Color(1f, 0.85f, 0.55f, 1f);

    [Tooltip("The roads between the zones, which belong to nobody.")]
    public Color pathColor = new Color(0.86f, 0.86f, 0.86f, 1f);

    [Tooltip("A zone of the player's own that has no room left. Its own colour rather than the " +
             "'yours' green, because 'finished, go and start another' is the one thing the caption " +
             "says that asks the player to do something.")]
    public Color fullColor = new Color(1f, 0.78f, 0.42f, 1f);

    [Header("Timing")]
    [Tooltip("The caption stays up once shown, re-reading itself on every crossing. Off, it fades " +
             "away again after the hold below.")]
    public bool staysVisible = true;

    [Tooltip("Seconds the caption holds before fading, when it does not stay.")]
    public float holdSeconds = 3f;

    public float fadeSeconds = 0.25f;

    [Tooltip("How often the player's footing is checked, in seconds. A crossing is not a per-frame " +
             "question and the lookup walks to the area map, so four times a second is plenty.")]
    public float pollInterval = 0.25f;

    /// <summary>
    /// The area last named. A separate "have we ever looked" flag is not needed: null is a real value
    /// here — it is the road between two areas, and stepping onto one is worth saying.
    /// </summary>
    private GardenAreaDefinition _current;
    private bool _hasCurrent;

    /// <summary>The score the caption was last written with, so a placement inside the current zone
    /// updates the count without waiting for the player to leave and come back.</summary>
    private int _shownItems = -1;

    private float _pollTimer;
    private float _holdRemaining;
    private float _alpha;
    private float _targetAlpha;

    private void Start()
    {
        SetContentActive(false);
        SetAlphaImmediate(0f);
    }

    private void OnEnable() => LocalizationManager.OnLocaleChanged += HandleLocaleChanged;

    private void OnDisable() => LocalizationManager.OnLocaleChanged -= HandleLocaleChanged;

    /// <summary>
    /// Re-renders the caption in the new language.
    ///
    /// <para>Guarded because every locale subscriber shares one dispatch: a throw here — an area map
    /// that is not ready, a UI reference cleared in the Inspector — must not cost the subscribers
    /// after it their own update.</para>
    /// </summary>
    private void HandleLocaleChanged()
    {
        try
        {
            if (_hasCurrent) Render(_current);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[GardenAreaBanner] Failed to re-render on locale change: " + e.Message);
        }
    }

    private void Update()
    {
        Poll();
        FollowAnchor();
        Fade();
    }

    private void Poll()
    {
        _pollTimer -= Time.unscaledDeltaTime;
        if (_pollTimer > 0f) return;
        _pollTimer = Mathf.Max(0.05f, pollInterval);

        GardenAreaDefinition area = AreaUnderPlayer();

        bool sameArea = _hasCurrent && ReferenceEquals(area, _current);
        if (sameArea && ItemsIn(area) == _shownItems) return;

        _current = area;
        _hasCurrent = true;

        Render(area);

        // Only a genuine crossing restarts the fade; a count ticking over while the player plants in
        // the zone they are already standing in should change the number, not flash the line.
        if (!sameArea) Show();
    }

    private static GardenAreaDefinition AreaUnderPlayer()
    {
        GardenAreaManager areas = GardenAreaManager.Instance;
        TargetDirectionController player = TargetDirectionController.Instance;

        if (areas == null || !areas.IsReady || player == null) return null;

        return areas.AreaAt(player.transform.position);
    }

    private static int ItemsIn(GardenAreaDefinition area)
    {
        GardenZoneProgress progress = GardenZoneProgress.Instance;
        return progress != null ? progress.ItemsIn(area) : 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  RENDERING
    // ═══════════════════════════════════════════════════════════════════════════

    private void Render(GardenAreaDefinition area)
    {
        GardenAreaManager areas = GardenAreaManager.Instance;
        LocalizationManager loc = LocalizationManager.Instance;
        if (loc == null || lineText == null) return;

        _shownItems = ItemsIn(area);

        string name;
        string status;
        Color color;

        // The road between two zones. Named rather than left blank, because "you are between places"
        // is what makes the places themselves legible.
        if (area == null || areas == null)
        {
            name = loc.Get("zone.path_title");
            status = loc.Get("zone.path_status");
            color = pathColor;
        }
        else if (areas.IsUnlocked(area))
        {
            name = areas.map != null ? areas.map.DisplayName(area) : area.displayNameKey;

            GardenZoneProgress progress = GardenZoneProgress.Instance;
            int limit = areas.LimitFor(area);

            if (areas.IsFull(area))
            {
                status = loc.Get("zone.status_full", limit);
                color = fullColor;
            }
            else
            {
                status = progress != null
                    ? loc.Get("zone.progress_format", _shownItems, progress.TargetItemsFor(area))
                    : loc.Get("zone.status_yours");
                color = yoursColor;
            }
        }
        else
        {
            name = areas.map != null ? areas.map.DisplayName(area) : area.displayNameKey;
            status = loc.Get("zone.status_locked", area.RequiredLevel);
            color = lockedColor;
        }

        lineText.color = color;

        // Through LocalizedRendering rather than straight onto TMP_Text.text, so Arabic and Urdu
        // arrive shaped instead of as disconnected letters.
        LocalizedRendering.SetText(lineText, loc.Get("zone.banner_line", name, status));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PLACEMENT AND FADE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Keeps the caption's top edge just below <see cref="anchorUnder"/>, left edges aligned.
    ///
    /// <para>Done every frame rather than once, because the minimap animates between its small and
    /// expanded sizes and the caption has to travel with it instead of jumping when it arrives.</para>
    /// </summary>
    private void FollowAnchor()
    {
        if (anchorUnder == null) return;

        var self = transform as RectTransform;
        var parent = self != null ? self.parent as RectTransform : null;
        if (self == null || parent == null) return;

        var corners = new Vector3[4];
        anchorUnder.GetWorldCorners(corners); // 0 = bottom-left, 3 = bottom-right

        // Local space of our own parent, so the result is valid whatever either object is anchored to.
        Vector3 bottomLeft = parent.InverseTransformPoint(corners[0]);
        Vector3 bottomRight = parent.InverseTransformPoint(corners[3]);

        float width = Mathf.Abs(bottomRight.x - bottomLeft.x);
        self.sizeDelta = new Vector2(width, self.sizeDelta.y);

        // Pivot-independent: place by the rect's own top-left, then convert back to anchoredPosition.
        Vector2 topLeft = new Vector2(Mathf.Min(bottomLeft.x, bottomRight.x), bottomLeft.y - gapUnderAnchor);

        Vector2 pivotOffset = new Vector2(
            self.rect.width * self.pivot.x,
            -self.rect.height * (1f - self.pivot.y));

        Vector2 target = topLeft + pivotOffset;

        // Anchors may not be at the parent's corner, so subtract where the anchor reference sits.
        Vector2 anchorReference = new Vector2(
            Mathf.Lerp(-parent.rect.width * parent.pivot.x, parent.rect.width * (1f - parent.pivot.x),
                (self.anchorMin.x + self.anchorMax.x) * 0.5f),
            Mathf.Lerp(-parent.rect.height * parent.pivot.y, parent.rect.height * (1f - parent.pivot.y),
                (self.anchorMin.y + self.anchorMax.y) * 0.5f));

        self.anchoredPosition = target - anchorReference;
    }

    private void Show()
    {
        _holdRemaining = staysVisible ? float.PositiveInfinity : Mathf.Max(0.1f, holdSeconds);
        _targetAlpha = 1f;
        SetContentActive(true);
    }

    private void Fade()
    {
        if (!staysVisible && !float.IsPositiveInfinity(_holdRemaining) && _holdRemaining > 0f)
        {
            _holdRemaining -= Time.unscaledDeltaTime;
            if (_holdRemaining <= 0f) _targetAlpha = 0f;
        }

        if (Mathf.Approximately(_alpha, _targetAlpha)) return;

        float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds);
        SetAlphaImmediate(Mathf.MoveTowards(_alpha, _targetAlpha, step));
    }

    private void SetAlphaImmediate(float alpha)
    {
        _alpha = alpha;

        if (group != null)
        {
            group.alpha = _alpha;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        // Switched off once it has finished fading out, never on the way in: hiding it at alpha 0
        // while it is fading up would switch the caption off on the very frame it was asked for.
        if (_alpha <= 0.001f && _targetAlpha <= 0f) SetContentActive(false);
    }

    private void SetContentActive(bool active)
    {
        if (content != null && content.activeSelf != active) content.SetActive(active);
    }

    /// <summary>
    /// Names <paramref name="area"/> without waiting for the player to walk into it. Used by the
    /// unlock celebration, and by onboarding when it wants the player reading the caption.
    /// </summary>
    public void Announce(GardenAreaDefinition area)
    {
        _current = area;
        _hasCurrent = true;
        Render(area);
        Show();
    }
}
