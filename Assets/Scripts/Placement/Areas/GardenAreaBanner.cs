using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The card under the minimap naming the patch of garden the player is standing in, and saying
/// whether it is theirs.
///
/// <para>The ground overlays answer "where may I build" while something is in hand. This answers the
/// other half — that the garden is a set of <em>places</em>, each with a name, each earned — and it
/// answers it while the player is simply walking, which is when they are actually looking at the
/// garden.</para>
///
/// <para><b>Why a card under the map and not a banner across the screen:</b> it is the same kind of
/// fact the minimap already shows, so it belongs in the same corner, read in the same glance. A card
/// in the middle of the screen makes a small standing fact look like an announcement, and has to be
/// dismissed — by time or by tapping — before the player can see the garden it is describing.</para>
///
/// <para><b>Why a panel with two lines and not one tinted string:</b> "Rose Court · 3 of 12 planted"
/// is two facts wearing one shape. Where the player <em>is</em> is a label — it should be the biggest
/// thing in the corner and it should look the same in every zone, so the eye learns where to find it.
/// What they may <em>do</em> there is a changing status, and belongs underneath in smaller type, in the
/// colour that carries the state. Giving the pair a panel of their own separates them from the map
/// pixels directly above, which a floating line of text over the garden never quite manages; the
/// coloured spine down the leading edge then says "yours / locked / path" before a word is read.</para>
///
/// <para>It stays put rather than fading away (see <see cref="staysVisible"/>): the question "which
/// part of the garden am I in" does not stop being asked, and a card small enough to sit under the
/// map is small enough to leave there.</para>
/// </summary>
public class GardenAreaBanner : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Faded in when the zone changes.")]
    public CanvasGroup group;

    [Tooltip("Legacy single-line caption object, from before the card below existed. Switched off and " +
             "left alone once the card is built; still used as the content object when card building " +
             "is turned off.")]
    public GameObject content;

    [Tooltip("Legacy single line. Its font is borrowed for the card, so the card matches whatever the " +
             "rest of the HUD is set in.")]
    public TMP_Text lineText;

    [Header("Placement")]
    [Tooltip("Kept directly beneath this — the minimap. The map grows and shrinks when the player " +
             "expands it, so the card follows its bottom edge rather than sitting at a fixed " +
             "offset that would end up on top of the expanded map.")]
    public RectTransform anchorUnder;

    [Tooltip("Gap between the bottom of the minimap and the top of the card, in canvas units.")]
    public float gapUnderAnchor = 10f;

    [Header("Card")]
    [Tooltip("Build the two-line card. Off, the component falls back to writing the legacy single " +
             "line above.")]
    public bool buildCard = true;

    [Tooltip("Nine-sliced background for the card. A plain tinted rectangle is used when empty.")]
    public Sprite cardSprite;

    [Tooltip("Tint on the background. White leaves the sprite as it was drawn.")]
    public Color cardColor = Color.white;

    [Tooltip("Height of the card, in canvas units — raised automatically if the fonts below need more. " +
             "Its width always matches the minimap above it.")]
    public float cardHeight = 72f;

    [Tooltip("Inset from the card's edges to its text, in canvas units.")]
    public float cardPadding = 12f;

    [Tooltip("Width of the coloured spine down the card's leading edge — the part that says 'yours' " +
             "or 'locked' before any word is read.")]
    public float accentWidth = 6f;

    [Tooltip("The zone's name. Kept one colour in every zone so it reads as a label rather than as a " +
             "status; the spine and the line beneath it carry the state.")]
    public Color nameColor = new Color(0.98f, 0.96f, 0.93f, 1f);

    public float nameFontSize = 23f;

    public float statusFontSize = 15f;

    [Header("Colours")]
    [Tooltip("Ground the player may build on.")]
    public Color yoursColor = new Color(0.72f, 1f, 0.76f, 1f);

    [Tooltip("A zone that has not been earned yet.")]
    public Color lockedColor = new Color(1f, 0.85f, 0.55f, 1f);

    [Tooltip("The roads between the zones, which belong to nobody.")]
    public Color pathColor = new Color(0.86f, 0.86f, 0.86f, 1f);

    [Tooltip("A zone of the player's own that has no room left. Its own colour rather than the " +
             "'yours' green, because 'finished, go and start another' is the one thing the card " +
             "says that asks the player to do something.")]
    public Color fullColor = new Color(1f, 0.78f, 0.42f, 1f);

    [Header("Timing")]
    [Tooltip("The card stays up once shown, re-reading itself on every crossing. Off, it fades " +
             "away again after the hold below.")]
    public bool staysVisible = true;

    [Tooltip("Seconds the card holds before fading, when it does not stay.")]
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

    /// <summary>The score the card was last written with, so a placement inside the current zone
    /// updates the count without waiting for the player to leave and come back.</summary>
    private int _shownItems = -1;

    private float _pollTimer;
    private float _holdRemaining;
    private float _alpha;
    private float _targetAlpha;

    /// <summary>The card, built on first use. Null while <see cref="buildCard"/> is off.</summary>
    private RectTransform _card;
    private Image _accent;
    private TMP_Text _nameText;
    private TMP_Text _statusText;

    /// <summary>The reading direction the card was last laid out for, so the mirror below only runs
    /// when the language actually changes rather than on every render.</summary>
    private bool _laidOutRtl;
    private bool _hasLayout;

    private void Start()
    {
        SetContentActive(false);
        SetAlphaImmediate(0f);
    }

    private void OnEnable() => LocalizationManager.OnLocaleChanged += HandleLocaleChanged;

    private void OnDisable() => LocalizationManager.OnLocaleChanged -= HandleLocaleChanged;

    /// <summary>
    /// Re-renders the card in the new language.
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
        // the zone they are already standing in should change the number, not flash the card.
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
        if (loc == null) return;

        EnsureCard();
        if (_card == null && lineText == null) return;

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

        if (_card != null)
        {
            ApplyDirection(loc.IsRightToLeft);

            if (_accent != null) _accent.color = color;

            _nameText.color = nameColor;
            _statusText.color = color;

            // Through LocalizedRendering rather than straight onto TMP_Text.text, so Arabic and Urdu
            // arrive shaped instead of as disconnected letters.
            LocalizedRendering.SetText(_nameText, name);
            LocalizedRendering.SetText(_statusText, status);
            return;
        }

        lineText.color = color;
        LocalizedRendering.SetText(lineText, loc.Get("zone.banner_line", name, status));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  THE CARD
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds the card the first time something asks for it, and retires the legacy single line.
    ///
    /// <para>Built in code rather than laid out in the scene so the card travels with the component:
    /// the onboarding and the unlock celebration both point at this object, and a prefab variant that
    /// had drifted from the script's expectations would show them an empty rectangle.</para>
    /// </summary>
    private void EnsureCard()
    {
        if (!buildCard || _card != null) return;

        var self = transform as RectTransform;
        if (self == null) return;

        // The old line is left in the scene but switched off: nothing else references it, and deleting
        // it would make this change impossible to back out of by unticking one box.
        if (content != null) content.SetActive(false);
        if (lineText != null && lineText.gameObject != content) lineText.gameObject.SetActive(false);

        var go = new GameObject("Zone Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(self, false);

        _card = go.GetComponent<RectTransform>();
        _card.anchorMin = Vector2.zero;
        _card.anchorMax = Vector2.one;
        _card.offsetMin = Vector2.zero;
        _card.offsetMax = Vector2.zero;

        var background = go.GetComponent<Image>();
        background.sprite = cardSprite;
        background.type = cardSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        background.color = cardColor;
        background.raycastTarget = false;

        var accentGo = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        accentGo.transform.SetParent(_card, false);
        _accent = accentGo.GetComponent<Image>();
        _accent.raycastTarget = false;

        TMP_FontAsset font = lineText != null ? lineText.font : null;

        _nameText = BuildLine(_card, "Name", font, nameFontSize, TMPro.FontWeight.Bold);
        _statusText = BuildLine(_card, "Status", font, statusFontSize, TMPro.FontWeight.Medium);

        _hasLayout = false;

        LocalizationManager loc = LocalizationManager.Instance;
        ApplyDirection(loc != null && loc.IsRightToLeft);
    }

    private static TMP_Text BuildLine(RectTransform parent, string objectName, TMP_FontAsset font,
                                      float size, TMPro.FontWeight weight)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;

        text.fontSize = size;
        text.fontWeight = weight;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        return text;
    }

    /// <summary>
    /// Lays the card's parts out for the reading direction: spine on the leading edge, text running
    /// away from it. Arabic and Urdu get the mirror image, because a spine on the left of a
    /// right-aligned card reads as a stray bar rather than as the card's own edge.
    /// </summary>
    private void ApplyDirection(bool rtl)
    {
        if (_card == null) return;
        if (_hasLayout && _laidOutRtl == rtl) return;

        _laidOutRtl = rtl;
        _hasLayout = true;

        RectTransform accent = _accent.rectTransform;
        accent.anchorMin = new Vector2(rtl ? 1f : 0f, 0f);
        accent.anchorMax = new Vector2(rtl ? 1f : 0f, 1f);
        accent.pivot = new Vector2(rtl ? 1f : 0f, 0.5f);
        accent.sizeDelta = new Vector2(accentWidth, -cardPadding * 2f);
        accent.anchoredPosition = new Vector2(rtl ? -cardPadding : cardPadding, 0f);

        // The text starts clear of the spine on the leading side and stops a padding short of the
        // trailing edge, whichever side each of those is today.
        float lead = cardPadding * 1.5f + accentWidth;
        float trail = cardPadding;

        Layout(_nameText.rectTransform, rtl, lead, trail, new Vector2(0f, 0.5f), new Vector2(1f, 1f),
               -cardPadding * 0.5f, 0f);
        Layout(_statusText.rectTransform, rtl, lead, trail, new Vector2(0f, 0f), new Vector2(1f, 0.5f),
               0f, cardPadding * 0.5f);

        // Bottom-aligned name over top-aligned status closes the gap between the two lines, so they
        // read as one block sitting in the middle of the card rather than as two lines in two halves.
        _nameText.alignment = rtl ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.BottomLeft;
        _statusText.alignment = rtl ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
    }

    private static void Layout(RectTransform rect, bool rtl, float lead, float trail,
                               Vector2 anchorMin, Vector2 anchorMax, float top, float bottom)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;

        float left = rtl ? trail : lead;
        float right = rtl ? lead : trail;

        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, top);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PLACEMENT AND FADE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Keeps the card's top edge just below <see cref="anchorUnder"/>, left edges aligned.
    ///
    /// <para>Done every frame rather than once, because the minimap animates between its small and
    /// expanded sizes and the card has to travel with it instead of jumping when it arrives.</para>
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
        float height = _card != null ? Mathf.Max(cardHeight, MinimumCardHeight()) : self.sizeDelta.y;
        self.sizeDelta = new Vector2(width, height);

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

    /// <summary>
    /// The height the two lines and their padding actually need.
    ///
    /// <para>A floor rather than the height itself, so <see cref="cardHeight"/> can still be set taller
    /// for the look of it — but a font size raised in the Inspector can never silently push the status
    /// line out through the bottom of the panel, which is a bug that only shows up in the language with
    /// the tallest glyphs.</para>
    /// </summary>
    private float MinimumCardHeight()
    {
        return nameFontSize * 1.3f + statusFontSize * 1.35f + cardPadding * 2f;
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
        // while it is fading up would switch the card off on the very frame it was asked for.
        if (_alpha <= 0.001f && _targetAlpha <= 0f) SetContentActive(false);
    }

    private void SetContentActive(bool active)
    {
        if (_card != null)
        {
            if (_card.gameObject.activeSelf != active) _card.gameObject.SetActive(active);
            return;
        }

        if (content != null && content.activeSelf != active) content.SetActive(active);
    }

    /// <summary>
    /// Names <paramref name="area"/> without waiting for the player to walk into it. Used by the
    /// unlock celebration, and by onboarding when it wants the player reading the card.
    /// </summary>
    public void Announce(GardenAreaDefinition area)
    {
        _current = area;
        _hasCurrent = true;
        Render(area);
        Show();
    }
}
