using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Drop-in show/hide animation for any panel or popup. Call <see cref="Show"/> and
/// <see cref="Hide"/> instead of <c>SetActive</c>; the GameObject is only deactivated after the
/// exit has finished, and raycasts are blocked while a transition runs so a double tap can't
/// re-trigger the panel half way through.
///
/// For scripts that still toggle <c>SetActive(true)</c> themselves, tick
/// <see cref="playOnEnable"/> and the entry plays automatically. See
/// <c>Docs/UI_Animation_Plan.md</c> §1.2.
/// </summary>
[DisallowMultipleComponent]
public class UIPanelMotion : MonoBehaviour
{
    public enum Preset
    {
        /// <summary>Scale up from <see cref="UIMotionTokens.popFromScale"/> while fading in.</summary>
        Pop,
        /// <summary>Rise from below while fading in.</summary>
        SlideUp,
        /// <summary>Come down from above with a small bounce.</summary>
        DropIn,
        /// <summary>Slide in from the leading edge (left, or right in RTL locales).</summary>
        SlideFromStart,
        /// <summary>Slide in from the trailing edge (right, or left in RTL locales).</summary>
        SlideFromEnd,
        /// <summary>Alpha only.</summary>
        Fade,
    }

    [Tooltip("How the panel enters and leaves.")]
    public Preset preset = Preset.Pop;

    [Tooltip("The transform that moves/scales. Defaults to this RectTransform. Point it at the inner " +
             "window when this object is a full-screen root that also holds the backdrop.")]
    public RectTransform window;

    [Tooltip("Optional dark overlay faded to the token backdrop alpha. Leave empty for no backdrop.")]
    public CanvasGroup backdrop;

    [Tooltip("Alpha the backdrop fades to. Negative = use the token. Set 1 for a backdrop whose image " +
             "already carries its designed darkness.")]
    public float backdropAlphaOverride = -1f;

    [Tooltip("Children that cascade in one after another once the window starts landing.")]
    public RectTransform[] staggerChildren;

    [Tooltip("Play the entry whenever this GameObject is enabled by other code via SetActive(true).")]
    public bool playOnEnable;

    [Tooltip("Deactivate this GameObject when Hide finishes. Turn off for panels that must stay " +
             "active (e.g. because they host coroutines) and should only become invisible.")]
    public bool deactivateOnHide = true;

    [Tooltip("Override the token slide distance (0 = use the token).")]
    public float slideDistanceOverride;

    /// <summary>True from the moment Show is called until Hide is called.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>True while an entry or exit tween is running.</summary>
    public bool IsAnimating => _sequence != null && _sequence.IsActive() && _sequence.IsPlaying();

    /// <summary>Raised when the entry finishes.</summary>
    public event Action Shown;
    /// <summary>Raised when the exit finishes (after deactivation, if enabled).</summary>
    public event Action Hidden;

    private CanvasGroup _group;
    private Sequence _sequence;
    private Vector2 _restPosition;
    private Vector3 _restScale = Vector3.one;
    private Vector3[] _childRestScales;
    private bool _restCaptured;
    private bool _suppressEnableEntry;

    private RectTransform Window => window != null ? window : (RectTransform)transform;

    private void Awake()
    {
        CaptureRest();
    }

    private void OnEnable()
    {
        if (_suppressEnableEntry) return;
        if (playOnEnable) PlayEntry();
    }

    private void OnDisable()
    {
        // Something deactivated us mid-tween (another script, a scene change). Snap to rest so the
        // next Show starts from a clean state instead of a half-scaled window.
        KillSequence();
        RestoreRest();
    }

    /// <summary>Activates the panel and plays the entry. Safe to call while hiding.</summary>
    public void Show()
    {
        IsVisible = true;
        if (!gameObject.activeSelf)
        {
            _suppressEnableEntry = true;
            gameObject.SetActive(true);
            _suppressEnableEntry = false;
        }
        PlayEntry();
    }

    /// <summary>Plays the exit, then deactivates (if enabled) and calls <paramref name="onDone"/>.</summary>
    public void Hide(Action onDone = null)
    {
        IsVisible = false;
        if (!gameObject.activeInHierarchy)
        {
            if (deactivateOnHide) gameObject.SetActive(false);
            onDone?.Invoke();
            Hidden?.Invoke();
            return;
        }
        PlayExit(onDone);
    }

    /// <summary>Shows or hides without animation (initial state, save restore).</summary>
    public void SetVisibleImmediate(bool visible)
    {
        KillSequence();
        IsVisible = visible;
        RestoreRest();
        if (!visible)
        {
            if (deactivateOnHide)
            {
                gameObject.SetActive(false);
            }
            else
            {
                Group.alpha = 0f;
                Group.blocksRaycasts = false;
                if (backdrop != null) backdrop.alpha = 0f;
            }
        }
        else
        {
            _suppressEnableEntry = true;
            gameObject.SetActive(true);
            _suppressEnableEntry = false;
        }
    }

    /// <summary>
    /// Re-reads the window's rest position and scale. Call after moving the panel in code
    /// (e.g. anchoring it to a new screen position) so the next entry lands there.
    /// </summary>
    public void CaptureRest()
    {
        RectTransform w = Window;
        _restPosition = w.anchoredPosition;
        _restScale = w.localScale == Vector3.zero ? Vector3.one : w.localScale;

        if (staggerChildren != null)
        {
            _childRestScales = new Vector3[staggerChildren.Length];
            for (int i = 0; i < staggerChildren.Length; i++)
            {
                Vector3 s = staggerChildren[i] != null ? staggerChildren[i].localScale : Vector3.one;
                _childRestScales[i] = s == Vector3.zero ? Vector3.one : s;
            }
        }
        _restCaptured = true;
    }

    // ---------------------------------------------------------------- internals

    private CanvasGroup Group
    {
        get
        {
            if (_group == null)
            {
                _group = Window.GetComponent<CanvasGroup>();
                if (_group == null) _group = Window.gameObject.AddComponent<CanvasGroup>();
            }
            return _group;
        }
    }

    private float BackdropAlpha => backdropAlphaOverride >= 0f ? backdropAlphaOverride : UIMotion.Tokens.backdropAlpha;

    private float SlideDistance => slideDistanceOverride > 0f ? slideDistanceOverride : UIMotion.Tokens.slideDistance;

    /// <summary>Offset from rest the window sits at when hidden, for the current preset and locale.</summary>
    private Vector2 HiddenOffset()
    {
        float d = SlideDistance;
        float startSign = UIMotion.IsRightToLeft ? 1f : -1f;
        switch (preset)
        {
            case Preset.SlideUp: return new Vector2(0f, -d);
            case Preset.DropIn: return new Vector2(0f, d);
            case Preset.SlideFromStart: return new Vector2(startSign * d, 0f);
            case Preset.SlideFromEnd: return new Vector2(-startSign * d, 0f);
            default: return Vector2.zero;
        }
    }

    private void PlayEntry()
    {
        if (!_restCaptured) CaptureRest();
        KillSequence();

        UIMotionTokens t = UIMotion.Tokens;
        RectTransform w = Window;
        CanvasGroup g = Group;
        GameObject link = gameObject;
        bool reduced = UIMotion.ReduceMotion;

        g.blocksRaycasts = false;
        _sequence = DOTween.Sequence().SetUpdate(true).SetLink(link);

        if (backdrop != null)
        {
            backdrop.alpha = 0f;
            _sequence.Insert(0f, backdrop.DOFade(BackdropAlpha, t.appearSoft.duration).SetEase(Ease.OutQuad));
        }

        if (reduced)
        {
            w.anchoredPosition = _restPosition;
            w.localScale = _restScale;
            g.alpha = 0f;
            _sequence.Insert(0f, g.DOFade(1f, t.reducedFade.duration).WithEase(t.reducedFade));
            RestoreChildren();
        }
        else
        {
            MotionSpec spec = preset == Preset.Fade ? t.appearSoft : t.appear;
            g.alpha = 0f;
            _sequence.Insert(0f, g.DOFade(1f, Mathf.Min(spec.duration, t.appearSoft.duration)).SetEase(Ease.OutQuad));

            if (preset == Preset.Pop)
            {
                w.anchoredPosition = _restPosition;
                w.localScale = _restScale * t.popFromScale;
                _sequence.Insert(0f, w.DOScale(_restScale, spec.duration).WithEase(spec));
            }
            else if (preset != Preset.Fade)
            {
                w.localScale = _restScale;
                w.anchoredPosition = _restPosition + HiddenOffset();
                MotionSpec slide = preset == Preset.DropIn ? spec : new MotionSpec(spec.duration, Ease.OutCubic);
                _sequence.Insert(0f, w.DOAnchorPos(_restPosition, slide.duration).WithEase(slide));
            }

            InsertStagger(_sequence, spec.duration * 0.45f);
        }

        _sequence.OnComplete(() =>
        {
            g.blocksRaycasts = true;
            _sequence = null;
            Shown?.Invoke();
        });
    }

    private void InsertStagger(Sequence seq, float startAt)
    {
        if (staggerChildren == null) return;
        UIMotionTokens t = UIMotion.Tokens;
        int index = 0;
        for (int i = 0; i < staggerChildren.Length; i++)
        {
            RectTransform child = staggerChildren[i];
            if (child == null || !child.gameObject.activeSelf) continue;

            Vector3 rest = _childRestScales != null && i < _childRestScales.Length ? _childRestScales[i] : Vector3.one;
            child.localScale = rest * t.staggerFromScale;
            seq.Insert(startAt + UIMotion.StaggerDelay(index),
                child.DOScale(rest, t.staggerChild.duration).WithEase(t.staggerChild));
            index++;
        }
    }

    private void PlayExit(Action onDone)
    {
        if (!_restCaptured) CaptureRest();
        KillSequence();

        UIMotionTokens t = UIMotion.Tokens;
        RectTransform w = Window;
        CanvasGroup g = Group;
        bool reduced = UIMotion.ReduceMotion;

        g.blocksRaycasts = false;
        _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);

        MotionSpec fadeSpec = reduced ? t.reducedFade : t.disappearSoft;
        _sequence.Insert(0f, g.DOFade(0f, reduced ? fadeSpec.duration : t.disappear.duration).WithEase(fadeSpec));

        if (backdrop != null)
            _sequence.Insert(0f, backdrop.DOFade(0f, t.disappear.duration).SetEase(Ease.InQuad));

        if (!reduced)
        {
            if (preset == Preset.Pop)
            {
                _sequence.Insert(0f, w.DOScale(_restScale * t.popFromScale, t.disappear.duration).WithEase(t.disappear));
            }
            else if (preset != Preset.Fade)
            {
                // Leave a little less far than we came in, so the exit reads as quick, not dramatic.
                Vector2 target = _restPosition + HiddenOffset() * 0.6f;
                _sequence.Insert(0f, w.DOAnchorPos(target, t.disappear.duration).WithEase(t.disappearSoft));
            }
        }

        _sequence.OnComplete(() =>
        {
            _sequence = null;
            if (deactivateOnHide)
            {
                gameObject.SetActive(false); // OnDisable restores the rest pose for next time
            }
            else
            {
                w.anchoredPosition = _restPosition;
                w.localScale = _restScale;
            }
            onDone?.Invoke();
            Hidden?.Invoke();
        });
    }

    private void RestoreRest()
    {
        if (!_restCaptured) return;
        RectTransform w = Window;
        w.anchoredPosition = _restPosition;
        w.localScale = _restScale;
        // A panel that stays active while hidden must stay invisible; everything else rests visible.
        bool restVisible = IsVisible || deactivateOnHide;
        if (_group != null)
        {
            _group.alpha = restVisible ? 1f : 0f;
            _group.blocksRaycasts = restVisible;
        }
        if (backdrop != null) backdrop.alpha = IsVisible ? BackdropAlpha : 0f;
        RestoreChildren();
    }

    private void RestoreChildren()
    {
        if (staggerChildren == null || _childRestScales == null) return;
        for (int i = 0; i < staggerChildren.Length && i < _childRestScales.Length; i++)
            if (staggerChildren[i] != null) staggerChildren[i].localScale = _childRestScales[i];
    }

    private void KillSequence()
    {
        if (_sequence != null && _sequence.IsActive()) _sequence.Kill();
        _sequence = null;
    }
}
