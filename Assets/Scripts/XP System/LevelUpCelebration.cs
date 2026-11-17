using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The level-up moment: a medallion with the new level drops in under the top of the screen, a gold
/// "Level Up!" ribbon unfurls beneath it, rays turn behind it and sparkles burst out — then it all
/// lifts away. Triggered by <see cref="XPBarAnimator.LevelUpPeak"/>, i.e. exactly when the HUD bar
/// hits full.
///
/// The visuals are built at runtime from the sprites assigned here, on a nested canvas sorted above
/// popups (a level-up often lands while the quiz panel is open) but below toasts. Nothing on it takes
/// raycasts, so it never blocks a tap.
/// </summary>
public class LevelUpCelebration : MonoBehaviour
{
    [Header("Art")]
    public Sprite ribbonSprite;
    public Sprite medallionSprite;
    [Tooltip("Disc the level number sits on, over the medallion's centre.")]
    public Sprite numberDiscSprite;
    [Tooltip("Font for the number and ribbon text. Defaults to the XP level label's font.")]
    public TMP_FontAsset font;

    [Header("Layout")]
    [Tooltip("Where the medallion rests, measured down from the top-centre of the screen.")]
    public float restY = -300f;
    public float medallionSize = 200f;
    public Vector2 ribbonSize = new Vector2(460f, 92f);
    public Color ribbonTextColor = new Color(0.29f, 0.18f, 0.07f, 1f);
    public Color numberColor = new Color(1f, 0.96f, 0.84f, 1f);
    public Color raysColor = new Color(1f, 0.88f, 0.45f, 0.55f);

    [Header("Timing")]
    [Tooltip("How long the celebration stays fully on screen.")]
    public float holdDuration = 1.7f;
    [Tooltip("Canvas sorting order: above the Pop Up Canvas (10), below the Toaster Message Canvas (15).")]
    public int sortingOrder = 14;

    private const int SparkleCount = 8;

    private RectTransform _root;
    private CanvasGroup _group;
    private RectTransform _rays;
    private RectTransform _medallion;
    private RectTransform _disc;
    private RectTransform _ribbon;
    private TextMeshProUGUI _number;
    private TextMeshProUGUI _title;
    private Image[] _sparkles;
    private Sequence _sequence;
    private Tween _raysSpin;
    private bool _built;
    private float _entryEndsAt;

    private void OnEnable() => XPBarAnimator.LevelUpPeak += Show;
    private void OnDisable() => XPBarAnimator.LevelUpPeak -= Show;

    /// <summary>Plays the celebration for <paramref name="level"/>. A second call while showing just updates the number.</summary>
    public void Show(int level)
    {
        EnsureBuilt();
        _number.text = level.ToString();
        string title = LocalizationManager.Instance != null
            ? LocalizationManager.Instance.GetOrDefault("hud.level_up", "Level Up!")
            : "Level Up!";
        LocalizedRendering.SetText(_title, title);

        // Already on screen: show the new number and restart the hold instead of replaying the entry.
        if (_root.gameObject.activeSelf && _sequence != null && _sequence.IsActive())
        {
            UIMotion.Punch(_disc, 3f);
            // Still entering: the running sequence already ends in a full hold, so leave it be.
            if (Time.unscaledTime < _entryEndsAt) return;

            _sequence.Kill();
            ResetPieces(true);
            _root.anchoredPosition = new Vector2(0f, restY);
            _group.alpha = 1f;
            _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            _sequence.AppendInterval(holdDuration);
            AppendExit(_sequence);
            return;
        }

        PlayEntry();
    }

    private void PlayEntry()
    {
        _sequence?.Kill();
        _root.gameObject.SetActive(true);
        _root.anchoredPosition = new Vector2(0f, restY);
        _group.alpha = 0f;

        Sequence s = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
        _sequence = s;

        if (UIMotion.ReduceMotion)
        {
            ResetPieces(true);
            s.Append(_group.DOFade(1f, 0.2f));
            s.AppendInterval(holdDuration);
            AppendExit(s);
            return;
        }

        ResetPieces(false);
        Image raysImg = _rays.GetComponent<Image>();

        s.Insert(0f, _group.DOFade(1f, 0.15f));

        // Medallion drops in from above and settles with a bounce.
        _medallion.anchoredPosition = new Vector2(0f, 170f);
        _medallion.localScale = Vector3.one * 0.6f;
        s.Insert(0f, _medallion.DOAnchorPos(Vector2.zero, 0.5f).SetEase(Ease.OutBack, 1.6f));
        s.Insert(0f, _medallion.DOScale(1f, 0.5f).SetEase(Ease.OutBack, 1.6f));

        // The number disc pops in once the medallion has landed.
        s.Insert(0.3f, _disc.DOScale(1f, 0.35f).SetEase(Ease.OutBack, 2.6f));

        // Rays bloom behind, then keep turning slowly while shown.
        s.Insert(0.25f, _rays.DOScale(1f, 0.6f).SetEase(Ease.OutCubic));
        s.Insert(0.25f, raysImg.DOFade(raysColor.a, 0.4f));
        _raysSpin?.Kill();
        _raysSpin = _rays.DOLocalRotate(new Vector3(0f, 0f, -360f), 24f, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Incremental).SetEase(Ease.Linear).SetUpdate(true).SetLink(_rays.gameObject);

        // Ribbon unfurls sideways from the centre.
        s.Insert(0.4f, _ribbon.DOScaleX(1f, 0.45f).SetEase(Ease.OutBack, 1.4f));
        s.Insert(0.4f, _ribbon.DOScaleY(1f, 0.3f).SetEase(Ease.OutQuad));

        // Sparkles burst outward from the medallion.
        for (int i = 0; i < _sparkles.Length; i++)
        {
            RectTransform sp = _sparkles[i].rectTransform;
            float angle = (i / (float)_sparkles.Length) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            float radius = Random.Range(150f, 230f);
            Vector2 dest = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            float delay = 0.35f + Random.Range(0f, 0.12f);
            float size = Random.Range(0.7f, 1.2f);
            s.Insert(delay, sp.DOAnchorPos(dest, 0.7f).SetEase(Ease.OutCubic));
            s.Insert(delay, sp.DOScale(size, 0.25f).SetEase(Ease.OutBack, 3f));
            s.Insert(delay, sp.DOLocalRotate(new Vector3(0f, 0f, Random.Range(90f, 180f)), 0.7f));
            s.Insert(delay + 0.4f, sp.DOScale(0f, 0.3f).SetEase(Ease.InQuad));
        }

        _entryEndsAt = Time.unscaledTime + s.Duration(false);
        s.AppendInterval(holdDuration);
        AppendExit(s);
    }

    private void AppendExit(Sequence s)
    {
        if (UIMotion.ReduceMotion)
        {
            s.Append(_group.DOFade(0f, 0.2f));
        }
        else
        {
            s.Append(_root.DOAnchorPosY(restY + 70f, 0.3f).SetEase(Ease.InQuad));
            s.Join(_group.DOFade(0f, 0.25f).SetEase(Ease.InQuad));
        }
        s.OnComplete(() =>
        {
            _raysSpin?.Kill();
            _root.gameObject.SetActive(false);
        });
    }

    /// <summary>Puts every piece at its start (animated) or rest (reduced motion) pose.</summary>
    private void ResetPieces(bool atRest)
    {
        Image raysImg = _rays.GetComponent<Image>();
        raysImg.color = atRest ? raysColor : new Color(raysColor.r, raysColor.g, raysColor.b, 0f);
        _rays.localScale = Vector3.one * (atRest ? 1f : 0.4f);
        _rays.localRotation = Quaternion.identity;
        _medallion.anchoredPosition = Vector2.zero;
        _medallion.localScale = Vector3.one;
        _disc.localScale = atRest ? Vector3.one : Vector3.zero;
        _ribbon.localScale = atRest ? Vector3.one : new Vector3(0f, 0.6f, 1f);
        foreach (Image sp in _sparkles)
        {
            sp.rectTransform.anchoredPosition = Vector2.zero;
            sp.rectTransform.localScale = Vector3.zero;
        }
    }

    /// <summary>
    /// Shows the celebration frozen at its resting pose — for screenshots and layout checks in the
    /// Editor, where tweens don't run. Undo with <see cref="ClearPreview"/>.
    /// </summary>
    [ContextMenu("Preview Rest Pose")]
    public void PreviewRestPose()
    {
        EnsureBuilt();
        _number.text = "12";
        string title = LocalizationManager.Instance != null
            ? LocalizationManager.Instance.GetOrDefault("hud.level_up", "Level Up!")
            : "Level Up!";
        LocalizedRendering.SetText(_title, title);
        _root.gameObject.SetActive(true);
        _root.anchoredPosition = new Vector2(0f, restY);
        _group.alpha = 1f;
        ResetPieces(true);
    }

    /// <summary>Removes everything <see cref="PreviewRestPose"/> built.</summary>
    [ContextMenu("Clear Preview")]
    public void ClearPreview()
    {
        if (_root != null) DestroyImmediate(_root.gameObject);
        Canvas c = GetComponent<Canvas>();
        if (c != null) DestroyImmediate(c);
        _built = false;
    }

    // ---------------------------------------------------------------- construction

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        if (font == null && PlayerXPManager.Instance != null && PlayerXPManager.Instance.xpLevelText != null)
            font = PlayerXPManager.Instance.xpLevelText.font;

        // Own canvas so it sorts above popups regardless of where this object sits.
        var canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;

        _root = NewRect("Level Up Root", (RectTransform)transform);
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 1f);
        _root.sizeDelta = new Vector2(ribbonSize.x, medallionSize + ribbonSize.y);
        _group = _root.gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;

        _rays = NewImage("Rays", _root, UIFxSprites.Rays, raysColor).rectTransform;
        _rays.sizeDelta = Vector2.one * medallionSize * 3f;

        _medallion = NewImage("Medallion", _root, medallionSprite, Color.white).rectTransform;
        _medallion.sizeDelta = Vector2.one * medallionSize;

        _disc = NewImage("Number Disc", _medallion, numberDiscSprite, Color.white).rectTransform;
        _disc.sizeDelta = Vector2.one * medallionSize * 0.52f;
        _number = NewLabel("Level Number", _disc, numberColor, medallionSize * 0.3f);

        _ribbon = NewImage("Ribbon", _root, ribbonSprite, Color.white).rectTransform;
        _ribbon.GetComponent<Image>().type = Image.Type.Sliced;
        _ribbon.sizeDelta = ribbonSize;
        _ribbon.anchoredPosition = new Vector2(0f, -medallionSize * 0.5f - ribbonSize.y * 0.15f);
        _title = NewLabel("Title", _ribbon, ribbonTextColor, ribbonSize.y * 0.48f);
        _title.rectTransform.offsetMin = new Vector2(50f, 8f);
        _title.rectTransform.offsetMax = new Vector2(-50f, -8f);

        _sparkles = new Image[SparkleCount];
        for (int i = 0; i < SparkleCount; i++)
        {
            _sparkles[i] = NewImage("Sparkle", _root, UIFxSprites.Sparkle, new Color(1f, 0.95f, 0.7f, 1f));
            _sparkles[i].rectTransform.sizeDelta = Vector2.one * 56f;
        }

        _root.gameObject.SetActive(false);
    }

    private static RectTransform NewRect(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer; // UI cameras cull by layer; match the canvas
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, RectTransform parent, Sprite sprite, Color color)
    {
        RectTransform rt = NewRect(name, parent);
        rt.gameObject.AddComponent<CanvasRenderer>();
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.preserveAspect = true;
        return img;
    }

    private TextMeshProUGUI NewLabel(string name, RectTransform parent, Color color, float size)
    {
        RectTransform rt = NewRect(name, parent);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.color = color;
        label.fontSize = size;
        label.fontStyle = FontStyles.Bold;
        label.enableAutoSizing = true;
        label.fontSizeMin = size * 0.5f;
        label.fontSizeMax = size;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return label;
    }
}
