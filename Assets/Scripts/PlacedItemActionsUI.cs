using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The little panel that opens when the player interacts with something they have already placed,
/// offering to move it somewhere else or hand it back to the Asset Store.
///
/// Every reference below is optional. Left empty, the panel builds itself at runtime the first time it
/// is needed — the same approach <see cref="PlaceableItem"/> takes with its floating timer, so the
/// feature works in any scene that has a canvas without waiting on someone to lay one out. Wire the
/// fields up in the Inspector to replace it with a designed panel; nothing else changes.
/// </summary>
public class PlacedItemActionsUI : MonoBehaviour
{
    public static PlacedItemActionsUI Instance { get; private set; }

    [Header("Panel (optional — built at runtime when empty)")]
    [Tooltip("Root object of the panel. Toggled on/off as the panel opens and closes.")]
    public GameObject panel;

    [Tooltip("Shows the name of the item the options apply to.")]
    public TMP_Text titleText;

    [Header("Buttons (optional — built at runtime when empty)")]
    public Button relocateButton;
    public Button returnButton;
    public Button closeButton;

    [Header("Behaviour")]
    [Tooltip("Canvas the runtime panel is built under. Falls back to the highest-sorted canvas in the scene.")]
    public Canvas targetCanvas;

    [Header("Runtime Panel Styling (optional)")]
    [Tooltip("Button whose look the runtime-built panel copies — sprite, 9-slice settings, colour block " +
             "and label font — along with the panel it sits in. Left empty, any styled button in the " +
             "scene is used, so the panel matches the rest of the game without being told how.")]
    public Button styleSource;

    [Tooltip("Overrides the background sprite taken from the style source.")]
    public Sprite panelSprite;

    [Tooltip("Overrides the button sprite taken from the style source.")]
    public Sprite buttonSprite;

    [Tooltip("Overrides the label font taken from the style source.")]
    public TMP_FontAsset labelFont;

    /// <summary>
    /// The look lifted off an existing button and the panel behind it, so a runtime-built panel is in
    /// keeping with the rest of the UI instead of being flat rectangles.
    /// </summary>
    private struct UIStyle
    {
        public Sprite panelSprite;
        public Image.Type panelImageType;
        public float panelPixelsPerUnit;
        public Color panelColor;
        public Outline panelOutline;

        public Sprite buttonSprite;
        public Image.Type buttonImageType;
        public float buttonPixelsPerUnit;
        public Color buttonColor;
        public ColorBlock buttonColors;
        public Selectable.Transition buttonTransition;

        public TMP_FontAsset font;
        public float fontSize;
        public FontStyles fontStyle;
        public Color fontColor;
    }

    private UIStyle _style;

    /// <summary>The item the open panel is acting on.</summary>
    private PlaceableItem _target;

    /// <summary>True while the panel is open.</summary>
    public bool IsOpen => panel != null && panel.activeSelf;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (panel == null)
        {
            BuildRuntimePanel();
        }

        if (relocateButton != null) relocateButton.onClick.AddListener(OnRelocateClicked);
        if (returnButton != null) returnButton.onClick.AddListener(OnReturnClicked);
        if (closeButton != null) closeButton.onClick.AddListener(Hide);

        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (relocateButton != null) relocateButton.onClick.RemoveListener(OnRelocateClicked);
        if (returnButton != null) returnButton.onClick.RemoveListener(OnReturnClicked);
        if (closeButton != null) closeButton.onClick.RemoveListener(Hide);

        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // The item can go away underneath the panel — the garden being rebuilt from the cloud, say.
        // Pooled objects are deactivated rather than destroyed, so check both. Leaving the panel open
        // would offer to move something that is no longer in the garden.
        if (IsOpen && (_target == null || !_target.isActiveAndEnabled)) Hide();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Opens the options for <paramref name="item"/>.</summary>
    public void Show(PlaceableItem item)
    {
        if (item == null) return;

        // Never offer to move or return something while another item is mid-placement: the manager
        // refuses it anyway, and the panel would be a dead end.
        if (ItemPlacementManager.Instance != null && ItemPlacementManager.Instance.IsPlacing) return;

        if (_target != null && _target != item) _target.SetHighlight(false);

        _target = item;
        _target.SetHighlight(true);

        if (titleText != null) titleText.text = BuildTitle(item);

        if (panel != null) panel.SetActive(true);

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
    }

    /// <summary>Closes the panel and clears the selection outline.</summary>
    public void Hide()
    {
        if (_target != null)
        {
            _target.SetHighlight(false);
            _target = null;
        }

        if (panel != null) panel.SetActive(false);
    }

    // ─── Button handlers ──────────────────────────────────────────────────────

    private void OnRelocateClicked()
    {
        PlaceableItem item = _target;
        Hide(); // Clears the outline before the object is pooled away.

        if (item == null || ItemPlacementManager.Instance == null) return;

        ItemPlacementManager.Instance.BeginRelocate(item);
    }

    private void OnReturnClicked()
    {
        PlaceableItem item = _target;
        Hide();

        if (item == null || ItemPlacementManager.Instance == null) return;

        ItemPlacementManager.Instance.ReturnToStore(item);
    }

    /// <summary>
    /// A readable name for the panel header. Placed objects are pooled clones, so the prefab name is
    /// the most reliable label available without a lookup back into the shop catalogue.
    /// </summary>
    private static string BuildTitle(PlaceableItem item)
    {
        string name = !string.IsNullOrEmpty(item.prefabName)
            ? item.prefabName
            : item.gameObject.name.Replace("(Clone)", "").Trim();

        return name.Replace('_', ' ');
    }

    // ─── Runtime panel ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the panel for scenes that have no authored one, wearing whatever the rest of the UI wears
    /// (see <see cref="CaptureStyle"/>). Laid out to match the scene panel: a title with three stacked
    /// buttons under it.
    /// </summary>
    private void BuildRuntimePanel()
    {
        Canvas canvas = targetCanvas != null ? targetCanvas : FindTopmostCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("[PlacedItemActionsUI] No canvas in the scene — the relocate/return panel cannot be shown.");
            return;
        }

        _style = CaptureStyle();

        panel = new GameObject("Placed Item Actions", typeof(RectTransform), typeof(Image));
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.SetParent(canvas.transform, false);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(618f, 380f);
        panelRect.SetAsLastSibling();

        Image background = panel.GetComponent<Image>();
        background.sprite = _style.panelSprite;
        background.type = _style.panelImageType;
        background.pixelsPerUnitMultiplier = _style.panelPixelsPerUnit;
        background.color = _style.panelColor;

        // A drop shadow on the panel is part of the house look; copy it when the source panel has one.
        if (_style.panelOutline != null)
        {
            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = _style.panelOutline.effectColor;
            outline.effectDistance = _style.panelOutline.effectDistance;
            outline.useGraphicAlpha = _style.panelOutline.useGraphicAlpha;
        }

        titleText = CreateLabel(panelRect, "Title", new Vector2(0f, -26f), new Vector2(540f, 54f),
            _style.fontSize + 4f);

        relocateButton = CreateButton(panelRect, "Relocate Button", "Move to a new spot", -104f);
        returnButton = CreateButton(panelRect, "Return Button", "Return to the Asset Store", -188f);
        closeButton = CreateButton(panelRect, "Close Button", "Keep it here", -272f);
    }

    /// <summary>
    /// Reads the game's own button and panel styling out of the scene, so the runtime panel does not
    /// need a second set of art decisions kept in sync by hand. Explicit Inspector overrides win; what
    /// is left is taken from <see cref="styleSource"/>, or from any styled button if none is assigned.
    /// </summary>
    private UIStyle CaptureStyle()
    {
        UIStyle style = new UIStyle
        {
            // Sensible plain defaults, used only when the scene has nothing to copy.
            panelImageType = Image.Type.Sliced,
            panelPixelsPerUnit = 1f,
            panelColor = new Color(0.04f, 0.09f, 0.06f, 0.94f),
            buttonImageType = Image.Type.Sliced,
            buttonPixelsPerUnit = 1f,
            buttonColor = Color.white,
            buttonColors = ColorBlock.defaultColorBlock,
            buttonTransition = Selectable.Transition.ColorTint,
            fontSize = 30f,
            fontStyle = FontStyles.Bold,
            fontColor = Color.white
        };

        Button source = styleSource != null ? styleSource : FindStyleSourceButton();

        if (source != null)
        {
            style.buttonColors = source.colors;
            style.buttonTransition = source.transition;

            Image sourceImage = source.targetGraphic as Image ?? source.GetComponent<Image>();
            if (sourceImage != null)
            {
                style.buttonSprite = sourceImage.sprite;
                style.buttonImageType = sourceImage.type;
                style.buttonPixelsPerUnit = sourceImage.pixelsPerUnitMultiplier;
                style.buttonColor = sourceImage.color;
            }

            TMP_Text sourceLabel = source.GetComponentInChildren<TMP_Text>(true);
            if (sourceLabel != null)
            {
                style.font = sourceLabel.font;
                style.fontSize = sourceLabel.fontSize;
                style.fontStyle = sourceLabel.fontStyle;
                style.fontColor = sourceLabel.color;
            }

            // The panel a styled button sits in is, by definition, a panel in the house style.
            Image sourcePanel = FindBackingPanel(source);
            if (sourcePanel != null)
            {
                style.panelSprite = sourcePanel.sprite;
                style.panelImageType = sourcePanel.type;
                style.panelPixelsPerUnit = sourcePanel.pixelsPerUnitMultiplier;
                style.panelColor = sourcePanel.color;
                style.panelOutline = sourcePanel.GetComponent<Outline>();
            }
        }

        // The style source may be an icon-only button; take the font from anywhere in the scene instead.
        if (style.font == null) style.font = FindSceneFont();

        if (panelSprite != null) style.panelSprite = panelSprite;
        if (buttonSprite != null) style.buttonSprite = buttonSprite;
        if (labelFont != null) style.font = labelFont;

        // Better a button-shaped background than an untextured block.
        if (style.panelSprite == null) style.panelSprite = style.buttonSprite;

        return style;
    }

    /// <summary>
    /// A button in the scene that has been given a sprite — i.e. a designed one rather than Unity's
    /// default white box. One with a text label is preferred, since it also supplies the font, but an
    /// icon-only button is still a better source for the sprite than nothing.
    /// </summary>
    private Button FindStyleSourceButton()
    {
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Button fallback = null;

        foreach (Button candidate in buttons)
        {
            if (candidate == null || candidate.transform.IsChildOf(transform)) continue;

            Image image = candidate.targetGraphic as Image ?? candidate.GetComponent<Image>();
            if (image == null || image.sprite == null) continue;

            if (candidate.GetComponentInChildren<TMP_Text>(true) != null) return candidate;

            if (fallback == null) fallback = candidate;
        }

        return fallback;
    }

    /// <summary>The font used elsewhere in this scene, for when the style source button has no label.</summary>
    private TMP_FontAsset FindSceneFont()
    {
        TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (TMP_Text candidate in texts)
        {
            if (candidate == null || candidate.font == null) continue;
            if (candidate.transform.IsChildOf(transform)) continue;

            return candidate.font;
        }

        return null;
    }

    /// <summary>The nearest ancestor image with a sprite — the panel the button is sitting on.</summary>
    private static Image FindBackingPanel(Button button)
    {
        Transform current = button.transform.parent;

        while (current != null)
        {
            Image image = current.GetComponent<Image>();
            if (image != null && image.sprite != null) return image;
            current = current.parent;
        }

        return null;
    }

    /// <summary>
    /// The canvas most likely to be on top of the rest of the HUD. Sorting order first, then hierarchy
    /// order, which is how Unity itself resolves overlapping canvases.
    /// </summary>
    private static Canvas FindTopmostCanvas()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        Canvas best = null;

        foreach (Canvas candidate in canvases)
        {
            if (candidate == null || !candidate.isRootCanvas) continue;
            if (best == null || candidate.sortingOrder >= best.sortingOrder) best = candidate;
        }

        return best;
    }

    /// <summary>A label anchored to the top of the panel, wearing the font taken from the scene.</summary>
    private TMP_Text CreateLabel(RectTransform parent, string name, Vector2 position, Vector2 size, float fontSize)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        return StyleLabel(go, fontSize);
    }

    private Button CreateButton(RectTransform parent, string name, string label, float y)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(430f, 68f);

        Image image = go.GetComponent<Image>();
        image.sprite = _style.buttonSprite;
        image.type = _style.buttonImageType;
        image.pixelsPerUnitMultiplier = _style.buttonPixelsPerUnit;
        image.color = _style.buttonColor;

        // Stretched to fill, the way the labels on the game's own buttons are.
        GameObject textGo = new GameObject("Text", typeof(RectTransform));
        RectTransform textRect = (RectTransform)textGo.transform;
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;

        StyleLabel(textGo, _style.fontSize).text = label;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = _style.buttonTransition;
        button.colors = _style.buttonColors;

        return button;
    }

    private TMP_Text StyleLabel(GameObject host, float fontSize)
    {
        TextMeshProUGUI label = host.AddComponent<TextMeshProUGUI>();

        // A null font asset makes TMP fall back to the project default, which is the right behaviour
        // when there was nothing in the scene to copy.
        if (_style.font != null) label.font = _style.font;

        label.fontSize = fontSize;
        label.fontStyle = _style.fontStyle;
        label.color = _style.fontColor;
        label.alignment = TextAlignmentOptions.Center;
        label.overflowMode = TextOverflowModes.Ellipsis;

        return label;
    }
}
