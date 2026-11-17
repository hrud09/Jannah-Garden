using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared entry point for UI motion: the active <see cref="UIMotionTokens"/>, the player's
/// Reduce Motion preference, and small tween helpers every UI script can reuse.
///
/// Every helper runs on unscaled time (UI must keep moving while gameplay is paused) and is linked
/// to its target's GameObject, so a destroyed panel never leaves a tween writing to a dead object.
/// Helpers return the tween they start and never <c>DOKill</c> a whole target — other scripts
/// (MCQ, Dhikr, Settings) tween the same transforms and must not have their tweens cut off.
/// </summary>
public static class UIMotion
{
    private const string ReduceMotionPrefKey = "ReduceMotion";

    private static UIMotionTokens _tokens;
    private static bool? _reduceMotion;

    /// <summary>The tuned tokens asset, or an in-memory copy with default values when it is missing.</summary>
    public static UIMotionTokens Tokens
    {
        get
        {
            if (_tokens == null)
            {
                _tokens = Resources.Load<UIMotionTokens>("UIMotionTokens");
                if (_tokens == null)
                {
                    _tokens = ScriptableObject.CreateInstance<UIMotionTokens>();
                    _tokens.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            return _tokens;
        }
    }

    /// <summary>Raised when the player flips Reduce Motion, so looping idle animations can stop or start.</summary>
    public static event Action<bool> ReduceMotionChanged;

    /// <summary>
    /// When on, scale/slide transitions become short fades and idle loops stay still.
    /// Persisted in PlayerPrefs alongside the other settings.
    /// </summary>
    public static bool ReduceMotion
    {
        get
        {
            if (!_reduceMotion.HasValue) _reduceMotion = PlayerPrefs.GetInt(ReduceMotionPrefKey, 0) == 1;
            return _reduceMotion.Value;
        }
        set
        {
            if (ReduceMotion == value) return;
            _reduceMotion = value;
            PlayerPrefs.SetInt(ReduceMotionPrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
            ReduceMotionChanged?.Invoke(value);
        }
    }

    /// <summary>True when the current locale reads right to left, so horizontal slides should mirror.</summary>
    public static bool IsRightToLeft =>
        LocalizationManager.Instance != null && LocalizationManager.Instance.IsRightToLeft;

    // ---------------------------------------------------------------- tween configuration

    /// <summary>
    /// Applies a token's ease, unscaled time and lifetime link to a standalone tween.
    /// For tweens nested in a Sequence use <see cref="WithEase{T}"/> — the Sequence owns update and link.
    /// </summary>
    public static T Apply<T>(this T tween, MotionSpec spec, GameObject link) where T : Tween
    {
        tween.WithEase(spec);
        tween.SetUpdate(true);
        if (link != null) tween.SetLink(link);
        return tween;
    }

    /// <summary>Applies only a token's ease (with its overshoot/amplitude where the ease uses one).</summary>
    public static T WithEase<T>(this T tween, MotionSpec spec) where T : Tween
    {
        if (spec.ease == Ease.Unset) return tween;
        switch (spec.ease)
        {
            case Ease.InBack: case Ease.OutBack: case Ease.InOutBack:
            case Ease.InElastic: case Ease.OutElastic: case Ease.InOutElastic:
                tween.SetEase(spec.ease, spec.overshoot);
                break;
            default:
                tween.SetEase(spec.ease);
                break;
        }
        return tween;
    }

    /// <summary>Delay for the <paramref name="index"/>th child of a cascade, capped by the tokens.</summary>
    public static float StaggerDelay(int index)
    {
        UIMotionTokens t = Tokens;
        return Mathf.Min(index * t.staggerStep, t.staggerMax);
    }

    // ---------------------------------------------------------------- feedback helpers

    /// <summary>
    /// A quick scale punch around the target's current scale. Skipped under Reduce Motion.
    /// Completes (rather than kills) any punch already running, so repeated calls never drift the scale.
    /// </summary>
    public static Tween Punch(Transform target, float strengthMultiplier = 1f)
    {
        if (target == null || ReduceMotion) return null;
        UIMotionTokens t = Tokens;

        // Juiced buttons own their scale; let them punch from their rest pose.
        if (target.TryGetComponent(out UIButtonJuice juice) && juice.isActiveAndEnabled)
            return juice.PlayPunch(t.punchStrength * strengthMultiplier, t.punch.duration, t.punchVibrato);

        string id = PunchId(target);
        DOTween.Complete(id);
        return target
            .DOPunchScale(Vector3.one * (t.punchStrength * strengthMultiplier), t.punch.duration, t.punchVibrato, 1f)
            .SetId(id)
            .SetUpdate(true)
            .SetLink(target.gameObject);
    }

    /// <summary>
    /// Briefly tints a graphic and eases it back to its current colour. Used for "you gained" /
    /// "you spent" feedback on counters. The colour still flashes under Reduce Motion — it isn't motion.
    /// </summary>
    public static Tween Flash(Graphic graphic, Color flashColor, float duration = 0.45f)
    {
        if (graphic == null) return null;
        string id = FlashId(graphic);

        // Restore the colour from any interrupted flash before reading the "rest" colour.
        DOTween.Complete(id);
        Color rest = graphic.color;
        Color flash = new Color(flashColor.r, flashColor.g, flashColor.b, rest.a);

        graphic.color = flash;
        return graphic.DOColor(rest, duration)
            .SetEase(Ease.InQuad)
            .SetId(id)
            .SetUpdate(true)
            .SetLink(graphic.gameObject);
    }

    /// <summary>Scale 0 → 1 pop for something appearing on its own (a badge, an icon).</summary>
    public static Tween PopIn(Transform target, float delay = 0f, Vector3? restScale = null)
    {
        if (target == null) return null;
        Vector3 rest = restScale ?? Vector3.one;
        if (ReduceMotion)
        {
            target.localScale = rest;
            return null;
        }
        UIMotionTokens t = Tokens;
        target.localScale = rest * t.staggerFromScale;
        return target.DOScale(rest, t.staggerChild.duration)
            .Apply(t.staggerChild, target.gameObject)
            .SetDelay(delay);
    }

    /// <summary>
    /// "Stamped on" entrance: the target starts oversized and slams down to its rest scale, like a
    /// seal pressed onto paper. Used for rarity badges and verdicts.
    /// </summary>
    public static Tween Stamp(Transform target, float delay = 0f, Vector3? restScale = null)
    {
        if (target == null) return null;
        Vector3 rest = restScale ?? Vector3.one;
        if (ReduceMotion)
        {
            target.localScale = rest;
            return null;
        }
        target.localScale = rest * 1.7f;
        return target.DOScale(rest, 0.32f)
            .SetEase(Ease.OutBack, 1.2f)
            .SetDelay(delay)
            .SetUpdate(true)
            .SetLink(target.gameObject);
    }

    /// <summary>
    /// The "something new is here" entrance (plan §6): the target grows from nothing past its rest
    /// scale and settles, then a ring ripples out from it. Used when onboarding hands the player a new
    /// button, so every new feature arrives the same recognisable way.
    /// </summary>
    public static Tween Reveal(Transform target, float delay = 0f, Vector3? restScale = null)
    {
        if (target == null) return null;
        Vector3 rest = restScale ?? Vector3.one;
        if (ReduceMotion)
        {
            target.localScale = rest;
            return null;
        }
        target.localScale = Vector3.zero;
        Sequence s = DOTween.Sequence().SetUpdate(true).SetLink(target.gameObject).SetDelay(delay);
        s.Append(target.DOScale(rest * 1.2f, 0.28f).SetEase(Ease.OutCubic));
        s.Append(target.DOScale(rest, 0.22f).SetEase(Ease.OutBack, 2f));
        s.InsertCallback(0.24f, () => Ripple(target as RectTransform));
        // Never strand a half-grown button if something kills the reveal.
        s.OnKill(() => { if (target != null) target.localScale = rest; });
        return s;
    }

    /// <summary>
    /// A soft ring that spreads out from <paramref name="target"/> and fades, once. Drawn as the target's
    /// own child so it shares its sorting (it shows above a tutorial dim when the target does).
    /// </summary>
    public static Tween Ripple(RectTransform target, Color? color = null, float delay = 0f)
    {
        if (target == null || ReduceMotion || !target.gameObject.activeInHierarchy) return null;

        Transform existing = target.Find(RippleName);
        Image ring;
        if (existing != null)
        {
            ring = existing.GetComponent<Image>();
        }
        else
        {
            var go = new GameObject(RippleName, typeof(RectTransform));
            go.layer = target.gameObject.layer;
            go.transform.SetParent(target, false);
            ring = go.AddComponent<Image>();
            ring.sprite = UIFxSprites.Ring;
            ring.raycastTarget = false;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        RectTransform rt = ring.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        float size = Mathf.Max(target.rect.width, target.rect.height) * 1.15f;
        rt.sizeDelta = new Vector2(size, size);
        rt.SetAsLastSibling();

        Color c = color ?? new Color(1f, 0.93f, 0.62f, 1f);
        ring.gameObject.SetActive(true);
        rt.localScale = Vector3.one;
        ring.color = new Color(c.r, c.g, c.b, 0f);

        DOTween.Kill(ring);
        Sequence s = DOTween.Sequence().SetUpdate(true).SetLink(ring.gameObject).SetTarget(ring).SetDelay(delay);
        s.Append(ring.DOFade(0.75f, 0.08f));
        s.Join(rt.DOScale(2f, 0.65f).SetEase(Ease.OutCubic));
        s.Insert(0.12f, ring.DOFade(0f, 0.53f).SetEase(Ease.InQuad));
        s.OnComplete(() => { if (ring != null) ring.gameObject.SetActive(false); });
        return s;
    }

    private const string RippleName = "UI Ripple";

    // ------------------------------------------------------------------ panels driven by their own code

    /// <summary>
    /// Fades a full-screen backdrop (dim, blur) in or out through a CanvasGroup. Hiding deactivates it
    /// only once the fade ends. Calling it again mid-fade just retargets the fade, so callers can fire
    /// it from every close path without tracking state.
    /// </summary>
    public static void SetBackdrop(GameObject backdrop, bool show)
    {
        if (backdrop == null) return;
        CanvasGroup group = backdrop.GetComponent<CanvasGroup>();
        if (group == null) group = backdrop.AddComponent<CanvasGroup>();
        DOTween.Kill(group);
        UIMotionTokens t = Tokens;

        if (show)
        {
            if (!backdrop.activeSelf)
            {
                backdrop.SetActive(true);
                group.alpha = 0f;
            }
            group.DOFade(1f, ReduceMotion ? t.reducedFade.duration : t.appearSoft.duration)
                .SetEase(Ease.OutQuad).SetUpdate(true).SetLink(backdrop);
            return;
        }

        if (!backdrop.activeSelf) return;
        group.DOFade(0f, ReduceMotion ? t.reducedFade.duration : t.disappearSoft.duration * 1.5f)
            .SetEase(Ease.InQuad).SetUpdate(true).SetLink(backdrop)
            .OnComplete(() =>
            {
                if (backdrop == null) return;
                backdrop.SetActive(false);
                group.alpha = 1f;
            });
    }

    /// <summary>
    /// The shared "dialog opens" entrance for panels whose scripts animate themselves (quiz, dhikr):
    /// pops from <see cref="UIMotionTokens.popFromScale"/> to <paramref name="scale"/> and fades in.
    /// Kills whatever the panel itself was doing (these panels own their transform).
    /// </summary>
    public static void PanelIn(GameObject panel, float scale = 1f)
    {
        if (panel == null) return;
        UIMotionTokens t = Tokens;
        Transform tr = panel.transform;
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.AddComponent<CanvasGroup>();
        tr.DOKill();
        DOTween.Kill(group);
        panel.SetActive(true);

        if (ReduceMotion)
        {
            tr.localScale = Vector3.one * scale;
            group.alpha = 0f;
            group.DOFade(1f, t.reducedFade.duration).SetUpdate(true).SetLink(panel);
            return;
        }

        tr.localScale = Vector3.one * (scale * t.popFromScale);
        group.alpha = 0f;
        // A dialog is bigger than a button, so it gets a touch more time than the bare token.
        tr.DOScale(scale, t.appear.duration * 1.3f).Apply(t.appear, panel);
        group.DOFade(1f, t.appearSoft.duration).SetEase(Ease.OutQuad).SetUpdate(true).SetLink(panel);
    }

    /// <summary>
    /// The matching exit: shrinks a little and fades out at exit speed, then runs
    /// <paramref name="onDone"/> (usually the script's own <c>SetActive(false)</c> and cleanup). The
    /// panel's alpha is restored afterwards so the next <see cref="PanelIn"/> starts clean.
    /// </summary>
    public static void PanelOut(GameObject panel, Action onDone)
    {
        if (panel == null || !panel.activeInHierarchy)
        {
            onDone?.Invoke();
            return;
        }
        UIMotionTokens t = Tokens;
        Transform tr = panel.transform;
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.AddComponent<CanvasGroup>();
        tr.DOKill();
        DOTween.Kill(group);
        group.blocksRaycasts = false;

        float d = ReduceMotion ? t.reducedFade.duration : t.disappear.duration * 1.3f;
        Sequence s = DOTween.Sequence().SetUpdate(true).SetLink(panel);
        s.Insert(0f, group.DOFade(0f, d).SetEase(Ease.InQuad));
        if (!ReduceMotion) s.Insert(0f, tr.DOScale(tr.localScale * t.popFromScale, d).WithEase(t.disappear));
        s.OnComplete(() =>
        {
            onDone?.Invoke();
            if (group != null)
            {
                group.alpha = 1f;
                group.blocksRaycasts = true;
            }
        });
    }

    private static string PunchId(Transform t) => "uimotion.punch." + t.GetInstanceID();
    private static string FlashId(Graphic g) => "uimotion.flash." + g.GetInstanceID();
}
