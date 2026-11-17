using DG.Tweening;
using UnityEngine;

/// <summary>
/// Animated replacement for <c>SetActive</c> on small UI pieces that come and go with game state —
/// walk labels, the inspector fly buttons, the minimap's expanded-only buttons. Showing pops the
/// piece in (scale + fade, optionally sliding from an offset); hiding shrinks and fades it, and only
/// then deactivates it.
///
/// Use it through <see cref="SetActive(GameObject, bool, float, Vector2)"/>; the component is added
/// on first use and remembers the piece's authored scale and position, so repeated toggles — even
/// mid-animation — always land on the same rest pose.
/// </summary>
[DisallowMultipleComponent]
public class UIToggleMotion : MonoBehaviour
{
    private Vector3 _restScale = Vector3.one;
    private Vector2 _restPosition;
    private bool _captured;
    private bool _hiding;
    private CanvasGroup _group;
    private Sequence _sequence;

    /// <summary>
    /// Shows or hides <paramref name="go"/> with motion. <paramref name="delay"/> holds the entry back
    /// (for staggering a group); <paramref name="slideFrom"/> is the offset the piece enters from and
    /// leaves towards (zero = pop in place). Passing <paramref name="animate"/> false, or Reduce Motion
    /// being on with no fade, behaves like a plain <c>SetActive</c>.
    /// </summary>
    public static void SetActive(GameObject go, bool active, float delay = 0f, Vector2 slideFrom = default, bool animate = true)
    {
        if (go == null) return;

        if (!animate || !Application.isPlaying)
        {
            if (go.TryGetComponent(out UIToggleMotion existing)) existing.SnapTo(active);
            else go.SetActive(active);
            return;
        }

        if (!go.TryGetComponent(out UIToggleMotion motion)) motion = go.AddComponent<UIToggleMotion>();
        if (active) motion.Show(delay, slideFrom);
        else motion.Hide(slideFrom);
    }

    /// <summary>True when the piece is showing or about to (false while it plays its exit).</summary>
    public static bool IsShown(GameObject go)
    {
        if (go == null || !go.activeSelf) return false;
        return !go.TryGetComponent(out UIToggleMotion m) || !m._hiding;
    }

    private void Capture()
    {
        if (_captured) return;
        _captured = true;
        _restScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
        if (transform is RectTransform rt) _restPosition = rt.anchoredPosition;
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
    }

    private void Show(float delay, Vector2 slideFrom)
    {
        // Already up (and not leaving): nothing to do — avoids re-popping on every state refresh.
        if (gameObject.activeSelf && !_hiding) return;

        Capture();
        Kill();
        _hiding = false;

        var rt = transform as RectTransform;
        gameObject.SetActive(true);

        UIMotionTokens t = UIMotion.Tokens;
        _group.alpha = 0f;
        _group.blocksRaycasts = false;

        _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject).SetDelay(delay);
        _sequence.OnStart(() => _group.blocksRaycasts = true);

        if (UIMotion.ReduceMotion)
        {
            transform.localScale = _restScale;
            if (rt != null) rt.anchoredPosition = _restPosition;
            _sequence.Append(_group.DOFade(1f, t.reducedFade.duration));
            return;
        }

        transform.localScale = _restScale * t.staggerFromScale;
        if (rt != null) rt.anchoredPosition = _restPosition + slideFrom;

        _sequence.Insert(0f, _group.DOFade(1f, t.appearSoft.duration * 0.7f));
        _sequence.Insert(0f, transform.DOScale(_restScale, t.staggerChild.duration).WithEase(t.staggerChild));
        if (rt != null && slideFrom != Vector2.zero)
            _sequence.Insert(0f, rt.DOAnchorPos(_restPosition, t.appear.duration).SetEase(Ease.OutCubic));
    }

    private void Hide(Vector2 slideTo)
    {
        if (!gameObject.activeSelf || _hiding) return;

        Capture();
        Kill();
        _hiding = true;
        _group.blocksRaycasts = false;

        UIMotionTokens t = UIMotion.Tokens;
        var rt = transform as RectTransform;
        _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);

        if (UIMotion.ReduceMotion)
        {
            _sequence.Append(_group.DOFade(0f, t.reducedFade.duration));
        }
        else
        {
            float d = t.disappear.duration;
            _sequence.Insert(0f, _group.DOFade(0f, d).SetEase(Ease.InQuad));
            _sequence.Insert(0f, transform.DOScale(_restScale * t.popFromScale, d).WithEase(t.disappear));
            if (rt != null && slideTo != Vector2.zero)
                _sequence.Insert(0f, rt.DOAnchorPos(_restPosition + slideTo * 0.6f, d).SetEase(Ease.InQuad));
        }

        _sequence.OnComplete(() =>
        {
            _sequence = null;
            SnapTo(false);
        });
    }

    /// <summary>Jumps to the shown or hidden state at the rest pose, with no motion.</summary>
    private void SnapTo(bool active)
    {
        Kill();
        _hiding = false;
        if (_captured)
        {
            transform.localScale = _restScale;
            if (transform is RectTransform rt) rt.anchoredPosition = _restPosition;
            if (_group != null)
            {
                _group.alpha = 1f;
                _group.blocksRaycasts = true;
            }
        }
        gameObject.SetActive(active);
    }

    private void OnDisable()
    {
        // Deactivated from outside mid-animation (a parent hid, the scene is closing): reset the pose
        // so the next Show starts clean.
        if (_sequence == null) return;
        Kill();
        _hiding = false;
        transform.localScale = _restScale;
        if (transform is RectTransform rt) rt.anchoredPosition = _restPosition;
        if (_group != null)
        {
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
        }
    }

    private void Kill()
    {
        if (_sequence != null && _sequence.IsActive()) _sequence.Kill();
        _sequence = null;
    }
}
