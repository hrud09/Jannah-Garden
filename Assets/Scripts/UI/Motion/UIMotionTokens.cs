using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// One timing + easing pair. Kept as a serializable struct so every token is tunable from the
/// <see cref="UIMotionTokens"/> asset without code changes.
/// </summary>
[Serializable]
public struct MotionSpec
{
    [Min(0f)] public float duration;
    public Ease ease;
    [Tooltip("Overshoot for Back eases, amplitude for Elastic. Ignored by other eases.")]
    public float overshoot;

    public MotionSpec(float duration, Ease ease, float overshoot = 1.70158f)
    {
        this.duration = duration;
        this.ease = ease;
        this.overshoot = overshoot;
    }
}

/// <summary>
/// The single source of truth for how Jannah Garden's UI moves. Lives at
/// <c>Resources/UIMotionTokens.asset</c>; when the asset is missing the defaults below are used,
/// so code never has to null-check it. See <c>Docs/UI_Animation_Plan.md</c> §1.1.
/// </summary>
[CreateAssetMenu(fileName = "UIMotionTokens", menuName = "Jannah/UI Motion Tokens")]
public class UIMotionTokens : ScriptableObject
{
    [Header("Panels")]
    [Tooltip("Panels and popups entering.")]
    public MotionSpec appear = new MotionSpec(0.32f, Ease.OutBack, 1.3f);
    [Tooltip("HUD pieces, labels and plain fades entering.")]
    public MotionSpec appearSoft = new MotionSpec(0.25f, Ease.OutCubic);
    [Tooltip("Anything leaving. Exits run at roughly 60% of the entry time.")]
    public MotionSpec disappear = new MotionSpec(0.18f, Ease.InBack, 1.2f);
    [Tooltip("Soft exit used for fades and slides, where a back-ease would look like a hiccup.")]
    public MotionSpec disappearSoft = new MotionSpec(0.16f, Ease.InQuad);

    [Header("Panel shape")]
    [Tooltip("Scale a Pop panel starts from.")]
    [Range(0f, 1f)] public float popFromScale = 0.85f;
    [Tooltip("Distance in canvas units a Slide panel travels.")]
    public float slideDistance = 120f;
    [Tooltip("Backdrop alpha when a panel is fully shown.")]
    [Range(0f, 1f)] public float backdropAlpha = 0.55f;

    [Header("Stagger")]
    [Tooltip("Delay between consecutive children in a cascade.")]
    public float staggerStep = 0.035f;
    [Tooltip("Cap on the total cascade delay, so long lists don't drag.")]
    public float staggerMax = 0.3f;
    [Tooltip("Scale a cascading child starts from.")]
    [Range(0f, 1f)] public float staggerFromScale = 0.6f;
    public MotionSpec staggerChild = new MotionSpec(0.28f, Ease.OutBack, 1.5f);

    [Header("Buttons")]
    [Tooltip("Scale a button squashes to while held.")]
    [Range(0.5f, 1f)] public float pressScale = 0.92f;
    public MotionSpec pressDown = new MotionSpec(0.07f, Ease.OutQuad);
    public MotionSpec pressRelease = new MotionSpec(0.22f, Ease.OutBack, 2.2f);
    [Tooltip("Peak scale of the pop when a button becomes interactable.")]
    public float enabledPopScale = 1.08f;
    public MotionSpec enabledPop = new MotionSpec(0.3f, Ease.OutQuad);

    [Header("Feedback")]
    public MotionSpec punch = new MotionSpec(0.35f, Ease.Unset);
    public float punchStrength = 0.12f;
    public int punchVibrato = 8;
    [Tooltip("How long a number takes to roll to its new value (scaled down for small changes).")]
    public float countDuration = 0.6f;
    public Color gainFlash = new Color(1f, 0.84f, 0.3f, 1f);
    public Color spendFlash = new Color(1f, 0.45f, 0.4f, 1f);

    [Header("Idle")]
    public float idleBreathPeriod = 2.4f;
    public float idleBreathAmount = 0.025f;

    [Header("Reduce Motion")]
    [Tooltip("Fade used instead of every scale/slide when the player turns Reduce Motion on.")]
    public MotionSpec reducedFade = new MotionSpec(0.15f, Ease.Linear);
}
