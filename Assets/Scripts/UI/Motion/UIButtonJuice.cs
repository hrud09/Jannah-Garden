using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Gives a button a tactile squash while held, a springy release, and a small pop when it becomes
/// interactable. Added automatically to every <see cref="Button"/> and <see cref="Toggle"/> by
/// <see cref="UIButtonJuiceBootstrap"/>; add <see cref="NoButtonJuice"/> to opt a button (or a
/// whole subtree) out.
///
/// Many buttons are also scaled by their own scripts (MCQ options punch, Settings options cascade).
/// To stay out of their way this never <c>DOKill</c>s the transform, skips the press when another
/// tween already owns the scale, and — if another script kills our tween mid-press — snaps straight
/// back to the rest scale, so their tween starts from the right size instead of from 0.92.
/// </summary>
[DisallowMultipleComponent]
public class UIButtonJuice : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("Pop when the button turns from non-interactable to interactable.")]
    public bool popWhenEnabled = true;

    // Interactable changes in the first moments after enabling are panel setup, not news.
    private const float EnablePopGrace = 0.4f;

    private Selectable _selectable;
    private Tween _tween;
    private Vector3 _restScale = Vector3.one;
    private bool _pressed;
    private bool _killingOwn;
    private bool _ownCompleted;
    private bool _wasInteractable;
    private float _enabledAt;

    private Vector3 _checkRestScale = Vector3.one;

    private void Awake()
    {
        _selectable = GetComponent<Selectable>();

        // Toggles: the checkmark pops in when switched on (it already fades out when switched off).
        // Only real taps fire this; SetIsOnWithoutNotify (panel setup) stays still.
        if (_selectable is Toggle toggle && toggle.graphic != null)
        {
            Vector3 s = toggle.graphic.transform.localScale;
            _checkRestScale = s == Vector3.zero ? Vector3.one : s;
            toggle.onValueChanged.AddListener(OnToggled);
        }
    }

    private void OnToggled(bool on)
    {
        if (!on || !isActiveAndEnabled || !(_selectable is Toggle toggle) || toggle.graphic == null) return;
        Transform check = toggle.graphic.transform;
        check.DOKill();
        UIMotion.PopIn(check, 0f, _checkRestScale);
    }

    private void OnDestroy()
    {
        if (_selectable is Toggle toggle) toggle.onValueChanged.RemoveListener(OnToggled);
    }

    private void OnEnable()
    {
        _enabledAt = Time.unscaledTime;
        _wasInteractable = _selectable != null && _selectable.interactable;
    }

    private void OnDisable()
    {
        _pressed = false;
        if (KillOwn()) transform.localScale = _restScale;
    }

    private void LateUpdate()
    {
        if (_selectable == null) return;
        bool interactable = _selectable.interactable;
        if (interactable == _wasInteractable) return;
        _wasInteractable = interactable;

        if (interactable && popWhenEnabled && !_pressed && Time.unscaledTime - _enabledAt > EnablePopGrace)
            PlayEnabledPop();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (_selectable != null && !_selectable.IsInteractable()) return;
        if (UIMotion.ReduceMotion) return;

        bool ownRunning = KillOwn();
        // Another script is animating this transform's scale: let it finish undisturbed.
        if (DOTween.IsTweening(transform)) return;
        // A second finger on an already-held button must not mistake the squashed size for rest.
        if (!ownRunning && !_pressed) _restScale = transform.localScale;

        _pressed = true;
        UIMotionTokens t = UIMotion.Tokens;
        StartOwn(transform.DOScale(_restScale * t.pressScale, t.pressDown.duration).WithEase(t.pressDown));
    }

    public void OnPointerUp(PointerEventData eventData) => Release();

    public void OnPointerExit(PointerEventData eventData)
    {
        // Dragging off a button (e.g. starting a scroll on a shop card) cancels the press visually too.
        if (_pressed) Release();
    }

    private void Release()
    {
        if (!_pressed) return;
        _pressed = false;
        KillOwn();
        UIMotionTokens t = UIMotion.Tokens;
        StartOwn(transform.DOScale(_restScale, t.pressRelease.duration).WithEase(t.pressRelease));
    }

    /// <summary>
    /// Punches the button from its rest scale, replacing any press/release spring in flight. Called by
    /// <see cref="UIMotion.Punch"/> so a punch fired from a click handler (which runs while the release
    /// spring is still going) can't capture a squashed scale as its base and leave the button small.
    /// </summary>
    public Tween PlayPunch(float strength, float duration, int vibrato)
    {
        bool ownRunning = KillOwn();
        // Mid-spring, or held down at the squashed size: the stored rest scale is the truth.
        if (ownRunning || _pressed) transform.localScale = _restScale;
        else if (!DOTween.IsTweening(transform)) _restScale = transform.localScale;
        else return null; // someone else owns the scale right now; don't fight them
        _pressed = false;

        StartOwn(transform.DOPunchScale(Vector3.one * strength, duration, vibrato, 1f));
        return _tween;
    }

    private void PlayEnabledPop()
    {
        if (UIMotion.ReduceMotion || !isActiveAndEnabled) return;
        bool ownRunning = KillOwn();
        if (DOTween.IsTweening(transform)) return;
        if (!ownRunning) _restScale = transform.localScale;

        UIMotionTokens t = UIMotion.Tokens;
        float half = t.enabledPop.duration * 0.5f;
        Sequence s = DOTween.Sequence()
            .Append(transform.DOScale(_restScale * t.enabledPopScale, half).SetEase(Ease.OutQuad))
            .Append(transform.DOScale(_restScale, half).SetEase(Ease.OutBack, 2f));
        StartOwn(s);
    }

    private void StartOwn(Tween tween)
    {
        _ownCompleted = false;
        _tween = tween
            .SetUpdate(true)
            .SetLink(gameObject)
            // IsComplete() is unreliable inside OnKill (the tween is already invalid), so track it here.
            .OnComplete(() => _ownCompleted = true)
            .OnKill(OnOwnKilled);
    }

    /// <summary>Kills our tween if running. Returns true when one was running (scale is mid-flight).</summary>
    private bool KillOwn()
    {
        if (_tween == null || !_tween.IsActive()) { _tween = null; return false; }
        _killingOwn = true;
        _tween.Kill();
        _killingOwn = false;
        _tween = null;
        return true;
    }

    private void OnOwnKilled()
    {
        // Completed normally: nothing to fix. Killed by us: the caller handles the scale.
        // Killed by someone else (a DOKill on this transform): snap to rest before their tween
        // reads its start value, so we never leave the button stuck squashed.
        if (_killingOwn || _tween == null) return;
        _tween = null;
        if (_ownCompleted) return; // finished normally; a held press stays squashed until release
        _pressed = false;
        if (this != null) transform.localScale = _restScale;
    }
}

/// <summary>
/// Adds <see cref="UIButtonJuice"/> to every Button and Toggle as it becomes enabled, the same way
/// <see cref="ButtonClickSfx"/> hooks clicks — scene, runtime-spawned prefabs and late panels alike.
/// </summary>
public class UIButtonJuiceBootstrap : MonoBehaviour
{
    private readonly HashSet<int> _seen = new HashSet<int>();
    private Selectable[] _buffer = new Selectable[64];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameObject go = new GameObject(nameof(UIButtonJuiceBootstrap));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<UIButtonJuiceBootstrap>();
    }

    private float _nextScan;

    private void LateUpdate()
    {
        // New buttons only appear when panels open; a 0.2 s scan catches them before a thumb can reach them.
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 0.2f;

        int count = Selectable.allSelectableCount;
        if (_buffer.Length < count) _buffer = new Selectable[Mathf.NextPowerOfTwo(count)];

        int filled = Selectable.AllSelectablesNoAlloc(_buffer);
        for (int i = 0; i < filled; i++)
        {
            Selectable selectable = _buffer[i];
            _buffer[i] = null;

            if (!(selectable is Button) && !(selectable is Toggle)) continue;
            if (!_seen.Add(selectable.GetInstanceID())) continue;
            if (!ShouldJuice(selectable)) continue;

            selectable.gameObject.AddComponent<UIButtonJuice>();
        }
    }

    private static bool ShouldJuice(Selectable selectable)
    {
        // Buttons driven by an Animator (some GUI-kit prefabs) already animate their own presses.
        if (selectable.transition == Selectable.Transition.Animation) return false;
        if (selectable.TryGetComponent(out Animator animator) && animator.enabled) return false;
        if (selectable.TryGetComponent(out UIButtonJuice _)) return false;
        if (selectable.GetComponentInParent<NoButtonJuice>(true) != null) return false;
        return true;
    }
}
