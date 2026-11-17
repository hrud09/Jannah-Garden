using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Rolls a number label from the value it shows to a new value, with a punch and a gain/spend
/// colour flash, instead of the number jumping. Attach it to a TMP label, or let
/// <see cref="Roll"/> add it on first use.
///
/// The component remembers the value currently on screen, so a second change arriving mid-roll
/// continues from where the first one had got to rather than snapping.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class UICountTo : MonoBehaviour
{
    [Tooltip("Optional transform to punch instead of the label (e.g. the coin icon beside it).")]
    public Transform punchTarget;

    [Tooltip("Flash the label gold on gains and red on spends.")]
    public bool flashOnChange = true;

    private TMP_Text _label;
    private Tween _tween;
    private Func<int, string> _format;
    private int _shown;
    private bool _hasValue;

    /// <summary>The value currently displayed (mid-roll values included).</summary>
    public int ShownValue => _shown;

    private TMP_Text Label => _label != null ? _label : (_label = GetComponent<TMP_Text>());

    /// <summary>Rolls <paramref name="label"/> to <paramref name="value"/>, adding the component if needed.</summary>
    public static UICountTo Roll(TMP_Text label, int value, Func<int, string> format = null)
    {
        if (label == null) return null;
        if (!label.TryGetComponent(out UICountTo counter)) counter = label.gameObject.AddComponent<UICountTo>();
        counter.RollTo(value, format);
        return counter;
    }

    /// <summary>Shows <paramref name="value"/> immediately, with no roll or feedback (initial load).</summary>
    public void SetImmediate(int value, Func<int, string> format = null)
    {
        if (format != null) _format = format;
        KillTween();
        _shown = value;
        _hasValue = true;
        Label.text = Format(value);
    }

    /// <summary>Rolls from the displayed value to <paramref name="value"/>.</summary>
    public void RollTo(int value, Func<int, string> format = null)
    {
        if (format != null) _format = format;

        // First value ever, or the label is hidden: nothing to animate from.
        if (!_hasValue || !isActiveAndEnabled)
        {
            SetImmediate(value);
            return;
        }

        int from = _shown;
        int delta = value - from;
        if (delta == 0)
        {
            KillTween();
            Label.text = Format(value);
            return;
        }

        UIMotionTokens t = UIMotion.Tokens;

        // Small changes tick over quickly; big ones take the full duration.
        float magnitude = Mathf.Clamp01(Mathf.Log10(Mathf.Abs(delta) + 1f) / 3f);
        float duration = t.countDuration * Mathf.Lerp(0.35f, 1f, magnitude);

        KillTween();
        _tween = DOTween.To(() => _shown, v =>
            {
                _shown = v;
                Label.text = Format(v);
            }, value, duration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetLink(gameObject);

        UIMotion.Punch(punchTarget != null ? punchTarget : transform, delta > 0 ? 1f : 0.6f);
        if (flashOnChange) UIMotion.Flash(Label, delta > 0 ? t.gainFlash : t.spendFlash);
    }

    private string Format(int value) => _format != null ? _format(value) : value.ToString();

    private void OnDisable()
    {
        // Land on the final value so a hidden label never stays frozen mid-roll.
        if (_tween != null && _tween.IsActive()) _tween.Complete();
        _tween = null;
    }

    private void KillTween()
    {
        if (_tween != null && _tween.IsActive()) _tween.Kill();
        _tween = null;
    }
}
