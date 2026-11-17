using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Animates the HUD XP bar. <see cref="PlayerXPManager"/> calls it instead of setting the slider directly.
///
/// On an XP gain, a bright "ghost" fill jumps straight to the new value and the real fill catches up
/// behind it, with a sparkle riding the fill's edge. On a level-up, the bar fills to the brim and
/// flashes, the level number bursts with rays behind the XP badge, <see cref="LevelUpPeak"/> fires
/// (the <see cref="LevelUpCelebration"/> listens for it), and then the bar empties and fills to the
/// carried-over XP.
///
/// The ghost, the sparkle and the rays are built at runtime from the slider's own fill image, so
/// restyling the bar art needs no changes here.
/// </summary>
public class XPBarAnimator : MonoBehaviour
{
    [Tooltip("The XP slider. Its fillRect must hold an Image.")]
    public Slider slider;

    [Tooltip("Label showing the level number.")]
    public TMP_Text levelText;

    [Tooltip("The XP badge the level-up rays burst from. Defaults to the level label.")]
    public RectTransform burstAnchor;

    [Header("Look")]
    public Color ghostColor = new Color(1f, 0.97f, 0.82f, 0.85f);
    public Color sparkleColor = new Color(1f, 0.93f, 0.6f, 1f);
    public Color raysColor = new Color(1f, 0.85f, 0.35f, 0.9f);
    [Tooltip("Seconds the real fill takes to catch up to the ghost.")]
    public float fillDuration = 0.55f;
    [Tooltip("Pause before the real fill starts moving, so the ghost reads as leading.")]
    public float fillDelay = 0.12f;

    /// <summary>Raised at the moment the bar fills on a level-up, with the new level.</summary>
    public static event Action<int> LevelUpPeak;

    private Image _fill;
    private Image _ghost;
    private Image _tip;
    private Image _rays;
    private Sequence _sequence;
    private bool _built;
    private int _shownLevel = -1;

    // Values that arrived while a reward hold had the bar (XP icons still flying in).
    private bool _hasPending;
    private int _pendingLevel;
    private float _pendingValue, _pendingMax;

    private void Awake()
    {
        UIRewardHold.Released += OnRewardReleased;
        UIRewardHold.Landed += OnRewardLanded;
    }

    private void Start()
    {
        RectTransform target = burstAnchor != null ? burstAnchor : (levelText != null ? levelText.rectTransform : null);
        Image icon = target != null ? target.GetComponent<Image>() : null;
        if (target != null) UIFlyToTarget.RegisterTarget(RewardChannel.XP, target, icon != null ? icon.sprite : null, target);
    }

    private void OnDestroy()
    {
        UIRewardHold.Released -= OnRewardReleased;
        UIRewardHold.Landed -= OnRewardLanded;
        if (burstAnchor != null) UIFlyToTarget.UnregisterTarget(RewardChannel.XP, burstAnchor);
    }

    private void OnRewardLanded(RewardChannel channel, int amount)
    {
        // Each landing nudges the ghost a little, so the bar visibly "drinks" the icons before it fills.
        if (channel != RewardChannel.XP || !_hasPending || !_built || !isActiveAndEnabled) return;
        if (_pendingLevel != _shownLevel || slider.maxValue <= 0f) return;

        float baseValue = _ghost.gameObject.activeSelf ? _ghostValue : slider.value;
        _ghostValue = Mathf.Min(_pendingValue, baseValue + amount);
        _ghost.gameObject.SetActive(true);
        _ghost.color = ghostColor;
        PlaceGhost(_ghostValue / slider.maxValue);
    }

    private float _ghostValue;

    private void OnRewardReleased(RewardChannel channel)
    {
        if (channel != RewardChannel.XP || !_hasPending) return;
        _hasPending = false;
        AnimateTo(_pendingLevel, _pendingValue, _pendingMax);
    }

    /// <summary>Shows the values with no animation (load, reset).</summary>
    public void SetImmediate(int level, float value, float max)
    {
        EnsureBuilt();
        _hasPending = false;
        KillSequence();
        slider.maxValue = Mathf.Max(0.0001f, max);
        slider.value = Mathf.Clamp(value, 0f, max);
        SetLevelText(level);
        HideEffects();
    }

    /// <summary>Animates from what is on screen to the new values, celebrating if the level went up.</summary>
    public void AnimateTo(int level, float value, float max)
    {
        EnsureBuilt();

        // XP icons are still flying in: remember the values and play them when the last one lands.
        if (UIRewardHold.IsHeld(RewardChannel.XP) && isActiveAndEnabled && !UIMotion.ReduceMotion)
        {
            _hasPending = true;
            _pendingLevel = level;
            _pendingValue = value;
            _pendingMax = max;
            return;
        }

        bool leveledUp = _shownLevel >= 0 && level > _shownLevel;

        if (UIMotion.ReduceMotion || !isActiveAndEnabled)
        {
            SetImmediate(level, value, max);
            if (leveledUp) LevelUpPeak?.Invoke(level);
            return;
        }

        // Finish whatever is mid-flight so a second gain starts from a settled bar.
        if (_sequence != null && _sequence.IsActive()) _sequence.Complete(true);

        if (leveledUp)
        {
            PlayLevelUp(level, value, max);
        }
        else
        {
            if (level != _shownLevel) SetLevelText(level); // e.g. a debug reset to a lower level
            PlayGain(value, max);
        }
    }

    // ---------------------------------------------------------------- sequences

    private void PlayGain(float value, float max)
    {
        slider.maxValue = Mathf.Max(0.0001f, max);
        float target = Mathf.Clamp(value, 0f, max);
        if (target <= slider.value)
        {
            slider.value = target;
            return;
        }

        _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
        AppendFill(_sequence, target, max);
        _sequence.Append(_ghost.DOFade(0f, 0.25f));
        _sequence.Join(_tip.DOFade(0f, 0.2f));
        _sequence.OnComplete(HideEffects);
    }

    private void PlayLevelUp(int level, float carry, float newMax)
    {
        float oldMax = slider.maxValue;
        _sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);

        // 1. Fill to the brim.
        AppendFill(_sequence, oldMax, oldMax, 0.75f);

        // 2. Peak: flash, new number, rays, celebration.
        _sequence.AppendCallback(() =>
        {
            UIMotion.Flash(_fill, Color.white, 0.5f);
            SetLevelText(level);
            if (levelText != null) UIMotion.Punch(levelText.transform, 4f);
            BurstRays();
            LevelUpPeak?.Invoke(level);
        });
        _sequence.Append(_tip.DOFade(0f, 0.2f));
        _sequence.Join(_ghost.DOFade(0f, 0.3f));
        _sequence.AppendInterval(0.35f);

        // 3. Empty and fill to the carried-over XP against the new requirement.
        _sequence.AppendCallback(() =>
        {
            slider.maxValue = Mathf.Max(0.0001f, newMax);
            slider.value = 0f;
            HideEffects();
        });
        float target = Mathf.Clamp(carry, 0f, newMax);
        if (target > 0f)
        {
            AppendFill(_sequence, target, newMax);
            _sequence.Append(_ghost.DOFade(0f, 0.25f));
            _sequence.Join(_tip.DOFade(0f, 0.2f));
        }
        _sequence.OnComplete(HideEffects);
    }

    /// <summary>Ghost jumps to <paramref name="target"/>, the real fill eases after it, the sparkle rides the edge.</summary>
    private void AppendFill(Sequence seq, float target, float max, float durationScale = 1f)
    {
        seq.AppendCallback(() =>
        {
            PlaceGhost(target / Mathf.Max(0.0001f, max));
            _ghost.gameObject.SetActive(true);
            _ghost.color = ghostColor;
            _tip.gameObject.SetActive(true);
            _tip.color = sparkleColor;
            _tip.rectTransform.localScale = Vector3.one;
            PlaceTip(slider.normalizedValue);
        });
        seq.AppendInterval(fillDelay);
        seq.Append(slider.DOValue(target, fillDuration * durationScale)
            .SetEase(Ease.OutCubic)
            .OnUpdate(() =>
            {
                PlaceTip(slider.normalizedValue);
                _tip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * 240f);
            }));
    }

    private void BurstRays()
    {
        if (_rays == null) return;
        RectTransform rt = _rays.rectTransform;
        RectTransform anchor = burstAnchor != null ? burstAnchor : levelText.rectTransform;
        // Centre on the badge now (not at build time) so a layout change since then can't misplace it.
        rt.position = anchor.TransformPoint(anchor.rect.center);
        rt.sizeDelta = Vector2.one * Mathf.Max(Mathf.Max(anchor.rect.width, anchor.rect.height) * 2.8f, 120f);
        rt.DOKill();
        _rays.DOKill();
        _rays.gameObject.SetActive(true);
        _rays.color = raysColor;
        rt.localScale = Vector3.one * 0.3f;
        rt.localRotation = Quaternion.identity;
        DOTween.Sequence().SetUpdate(true).SetLink(_rays.gameObject)
            .Insert(0f, rt.DOScale(1.8f, 0.8f).SetEase(Ease.OutCubic))
            .Insert(0f, rt.DOLocalRotate(new Vector3(0f, 0f, -70f), 0.8f).SetEase(Ease.OutCubic))
            .Insert(0.25f, _rays.DOFade(0f, 0.55f).SetEase(Ease.InQuad))
            .OnComplete(() => _rays.gameObject.SetActive(false));
    }

    // ---------------------------------------------------------------- layout helpers

    private bool Reversed => slider.direction == Slider.Direction.RightToLeft || slider.direction == Slider.Direction.TopToBottom;
    private bool Horizontal => slider.direction == Slider.Direction.LeftToRight || slider.direction == Slider.Direction.RightToLeft;

    /// <summary>Sets the ghost's anchors the way Slider sets the fill's, for normalized value <paramref name="v"/>.</summary>
    private void PlaceGhost(float v)
    {
        v = Mathf.Clamp01(v);
        RectTransform g = _ghost.rectTransform;
        int axis = Horizontal ? 0 : 1;
        Vector2 min = Vector2.zero, max = Vector2.one;
        if (Reversed) min[axis] = 1f - v; else max[axis] = v;
        g.anchorMin = min;
        g.anchorMax = max;
    }

    /// <summary>Moves the sparkle to the fill's leading edge.</summary>
    private void PlaceTip(float v)
    {
        RectTransform t = _tip.rectTransform;
        float edge = Reversed ? 1f - Mathf.Clamp01(v) : Mathf.Clamp01(v);
        Vector2 a = Horizontal ? new Vector2(edge, 0.5f) : new Vector2(0.5f, edge);
        t.anchorMin = t.anchorMax = a;
        t.anchoredPosition = Vector2.zero;
    }

    private void SetLevelText(int level)
    {
        _shownLevel = level;
        if (levelText != null) levelText.text = level.ToString();
    }

    private void HideEffects()
    {
        if (!_built) return;
        _ghost.gameObject.SetActive(false);
        _tip.gameObject.SetActive(false);
    }

    private void KillSequence()
    {
        if (_sequence != null && _sequence.IsActive()) _sequence.Kill();
        _sequence = null;
    }

    private void OnDisable()
    {
        // Land on the final values rather than freezing a half-filled bar.
        if (_sequence != null && _sequence.IsActive()) _sequence.Complete(true);
        _sequence = null;
    }

    // ---------------------------------------------------------------- construction

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _fill = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
        RectTransform fillArea = slider.fillRect != null ? (RectTransform)slider.fillRect.parent : (RectTransform)slider.transform;

        // Ghost: a copy of the fill's look, drawn just behind it.
        _ghost = NewImage("XP Ghost Fill", fillArea, _fill != null ? _fill.sprite : null);
        if (_fill != null)
        {
            _ghost.type = _fill.type;
            _ghost.pixelsPerUnitMultiplier = _fill.pixelsPerUnitMultiplier;
            _ghost.rectTransform.offsetMin = slider.fillRect.offsetMin;
            _ghost.rectTransform.offsetMax = slider.fillRect.offsetMax;
            _ghost.rectTransform.SetSiblingIndex(slider.fillRect.GetSiblingIndex());
        }

        // Sparkle riding the leading edge, drawn above the fill.
        _tip = NewImage("XP Fill Sparkle", fillArea, UIFxSprites.Sparkle);
        float h = fillArea.rect.height > 1f ? fillArea.rect.height : 21f;
        _tip.rectTransform.sizeDelta = Vector2.one * (h * 2.6f);
        _tip.rectTransform.SetSiblingIndex(slider.fillRect != null ? slider.fillRect.GetSiblingIndex() + 1 : fillArea.childCount - 1);

        // Rays behind the badge (or the level label).
        RectTransform anchor = burstAnchor != null ? burstAnchor : (levelText != null ? levelText.rectTransform : null);
        if (anchor != null && anchor.parent != null)
        {
            // A sibling drawn just before the badge, so the rays fan out from behind it.
            _rays = NewImage("Level Up Rays", (RectTransform)anchor.parent, UIFxSprites.Rays);
            RectTransform rt = _rays.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.SetSiblingIndex(anchor.GetSiblingIndex());
            _rays.gameObject.SetActive(false);
        }

        HideEffects();
    }

    private static Image NewImage(string name, RectTransform parent, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer; // UI cameras cull by layer; match the canvas
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }
}
