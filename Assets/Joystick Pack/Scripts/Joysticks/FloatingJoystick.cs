using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

public class FloatingJoystick : Joystick
{
    // Jannah Garden motion (UI motion plan, Phase 5): the stick grows and fades in where the finger
    // lands, and on release the knob springs back to the centre while the stick fades away, instead of
    // the whole thing blinking on and off.
    private CanvasGroup _group;
    private Vector3 _backgroundRestScale = Vector3.one;
    private Sequence _motion;

    protected override void Start()
    {
        base.Start();
        _backgroundRestScale = background.localScale == Vector3.zero ? Vector3.one : background.localScale;
        background.gameObject.SetActive(false);
    }

    private CanvasGroup Group
    {
        get
        {
            if (_group == null)
            {
                _group = background.GetComponent<CanvasGroup>();
                if (_group == null) _group = background.gameObject.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false; // the stick itself never takes touches; this object does
            }
            return _group;
        }
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        if (IsOtherFingerHolding(eventData))
            return;

        KillMotion();
        background.anchoredPosition = ScreenPointToAnchoredPosition(eventData.position);
        background.gameObject.SetActive(true);
        Group.alpha = 1f;
        background.localScale = _backgroundRestScale;

        if (!UIMotion.ReduceMotion)
        {
            Group.alpha = 0f;
            background.localScale = _backgroundRestScale * 0.8f;
            _motion = DOTween.Sequence().SetUpdate(true).SetLink(background.gameObject)
                .Join(Group.DOFade(1f, 0.1f))
                .Join(background.DOScale(_backgroundRestScale, 0.16f).SetEase(Ease.OutBack, 2f));
        }

        base.OnPointerDown(eventData);
    }

    public override void OnPointerUp(PointerEventData eventData)
    {
        if (IsOtherFingerHolding(eventData))
            return;

        Vector2 knob = Handle != null ? Handle.anchoredPosition : Vector2.zero;
        base.OnPointerUp(eventData); // zeroes the input and snaps the knob to centre

        if (KeepBackgroundVisible)
        {
            SpringKnob(knob);
            return;
        }

        if (UIMotion.ReduceMotion || !background.gameObject.activeInHierarchy)
        {
            KillMotion();
            background.gameObject.SetActive(false);
            return;
        }

        // The input is already zero; only the picture lingers for a moment.
        KillMotion();
        SpringKnob(knob);
        _motion = DOTween.Sequence().SetUpdate(true).SetLink(background.gameObject)
            .Insert(0.06f, Group.DOFade(0f, 0.16f).SetEase(Ease.InQuad))
            .Insert(0.06f, background.DOScale(_backgroundRestScale * 0.85f, 0.16f).SetEase(Ease.InQuad))
            .OnComplete(() =>
            {
                background.gameObject.SetActive(false);
                Group.alpha = 1f;
                background.localScale = _backgroundRestScale;
            });
    }

    /// <summary>Draws the knob springing back from where the finger left it (visual only).</summary>
    private void SpringKnob(Vector2 from)
    {
        if (Handle == null || UIMotion.ReduceMotion || from.sqrMagnitude < 1f) return;
        Handle.DOKill();
        Handle.anchoredPosition = from;
        Handle.DOAnchorPos(Vector2.zero, 0.22f).SetEase(Ease.OutBack, 2.2f).SetUpdate(true).SetLink(Handle.gameObject);
    }

    private void KillMotion()
    {
        if (_motion != null && _motion.IsActive()) _motion.Kill();
        _motion = null;
        if (Handle != null) Handle.DOKill();
        if (_group != null) _group.alpha = 1f;
        background.localScale = _backgroundRestScale;
    }
}
