using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Plays the treasure box open reveal animation sequence.
///
/// Designed to be placed on a dedicated "reveal overlay" GameObject that sits
/// above the regular slot UI. Call <see cref="PlayOpenAnimation"/> with the
/// relevant reward data to kick off the sequence.
///
/// Animation stages:
///   1. Box scale-punch (quick scale-up then settle)
///   2. Particle/VFX burst spawn
///   3. Reward icon fade in
///   4. Reward name text slide up
///   5. Short hold, then fade out the whole overlay
/// </summary>
public class TreasureBoxOpenAnimator : MonoBehaviour
{
    // ─── Inspector References ─────────────────────────────────────────────────

    [Header("Box Visuals")]
    [Tooltip("The Image showing the treasure box in the reveal overlay.")]
    public Image revealBoxImage;

    [Header("Reward Display")]
    [Tooltip("Image that shows the reward item icon after the box opens.")]
    public Image rewardIcon;

    [Tooltip("Text that shows the reward item name after the box opens.")]
    public TMP_Text rewardNameText;

    [Tooltip("Text label showing e.g. 'Set Complete!' or 'Noor Coins Earned'.")]
    public TMP_Text rewardSubtitleText;

    [Header("Overlay")]
    [Tooltip("CanvasGroup on the root of this reveal overlay, used for fading.")]
    public CanvasGroup overlayCanvasGroup;

    [Tooltip("RectTransform of the reward name text, used for the slide-up animation.")]
    public RectTransform rewardNameRect;

    [Header("Timing")]
    [Tooltip("Duration of the scale-punch on the box (seconds).")]
    public float punchDuration = 0.3f;

    [Tooltip("Scale factor at the peak of the punch.")]
    public float punchPeakScale = 1.4f;

    [Tooltip("How long the reward is displayed before the overlay fades out.")]
    public float holdDuration = 2.5f;

    [Tooltip("Duration of the fade-out at the end.")]
    public float fadeOutDuration = 0.5f;

    [Tooltip("How far the reward name slides up during reveal (in UI units).")]
    public float slideUpDistance = 40f;

    [Header("VFX")]
    [Tooltip("Optional particle / VFX prefab instantiated at the box position during the burst.")]
    public GameObject vfxOverridePrefab;

    // ─── Private State ────────────────────────────────────────────────────────

    private Coroutine _animCoroutine;
    private Vector2   _rewardNameBasePos;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (rewardNameRect != null)
            _rewardNameBasePos = rewardNameRect.anchoredPosition;

        HideImmediate();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the full reveal animation sequence for a box open event.
    /// </summary>
    /// <param name="tier">Which tier was opened (used for the box sprite).</param>
    /// <param name="rewardData">The reward data for the opened tier.</param>
    /// <param name="isSetComplete">
    ///   True if opening this box completed the set of 3, which changes the subtitle.
    /// </param>
    public void PlayOpenAnimation(TreasureBoxTier tier, TreasureBoxData rewardData, bool isSetComplete)
    {
        if (_animCoroutine != null) StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(AnimationSequence(tier, rewardData, isSetComplete));
    }

    /// <summary>
    /// Immediately hides the reveal overlay without animation.
    /// </summary>
    public void HideImmediate()
    {
        gameObject.SetActive(false);
        if (overlayCanvasGroup != null) overlayCanvasGroup.alpha = 0f;
        if (rewardIcon  != null) { var c = rewardIcon.color;  c.a = 0f; rewardIcon.color  = c; }
        if (rewardNameText != null) { var c = rewardNameText.color; c.a = 0f; rewardNameText.color = c; }
    }

    // ─── Animation Sequence ───────────────────────────────────────────────────

    private IEnumerator AnimationSequence(TreasureBoxTier tier, TreasureBoxData rewardData, bool isSetComplete)
    {
        // ── Setup ─────────────────────────────────────────────────────────────
        gameObject.SetActive(true);

        if (overlayCanvasGroup != null) overlayCanvasGroup.alpha = 1f;

        // Hide reward elements initially
        SetAlpha(rewardIcon,       0f);
        SetTextAlpha(rewardNameText,    0f);
        SetTextAlpha(rewardSubtitleText, 0f);

        if (rewardNameRect != null)
            rewardNameRect.anchoredPosition = _rewardNameBasePos - new Vector2(0, slideUpDistance);

        // ── Stage 1: Scale punch on box ───────────────────────────────────────
        if (revealBoxImage != null)
        {
            yield return StartCoroutine(ScalePunch(revealBoxImage.transform,
                                                   punchDuration, punchPeakScale));
        }

        // ── Stage 2: Spawn VFX ────────────────────────────────────────────────
        GameObject vfxPrefab = vfxOverridePrefab;
        if (vfxPrefab != null)
        {
            Vector3 spawnPos = revealBoxImage != null
                ? revealBoxImage.transform.position
                : transform.position;

            GameObject vfx = Objectpool.Instance.Spawn(vfxPrefab, spawnPos, Quaternion.identity);
            Objectpool.Instance.Despawn(vfx, 3f); // Auto-despawn VFX after 3 seconds
        }

        // ── Stage 4: Fade in reward icon + name ───────────────────────────────
        TreasureBoxRewardItemData reward = rewardData?.exclusiveRewardItem;

        if (rewardIcon != null && reward?.itemIcon != null)
            rewardIcon.sprite = reward.itemIcon;

        if (rewardNameText != null)
            rewardNameText.text = reward != null ? reward.itemName : "Noor Coins!";

        if (rewardSubtitleText != null)
        {
            rewardSubtitleText.text = isSetComplete
                ? (rewardData != null ? $"{rewardData.tierDisplayName} Complete!" : "Set Complete!")
                : string.Empty;
        }

        float fadeDur = 0.4f;
        float elapsed = 0f;
        while (elapsed < fadeDur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDur);

            SetAlpha(rewardIcon, t);
            SetTextAlpha(rewardNameText, t);
            SetTextAlpha(rewardSubtitleText, t);

            // Slide reward name up
            if (rewardNameRect != null)
            {
                rewardNameRect.anchoredPosition = Vector2.Lerp(
                    _rewardNameBasePos - new Vector2(0, slideUpDistance),
                    _rewardNameBasePos,
                    t);
            }

            yield return null;
        }

        SetAlpha(rewardIcon, 1f);
        SetTextAlpha(rewardNameText, 1f);
        SetTextAlpha(rewardSubtitleText, 1f);

        // ── Stage 5: Hold ─────────────────────────────────────────────────────
        yield return new WaitForSeconds(holdDuration);

        // ── Stage 6: Fade out overlay ─────────────────────────────────────────
        if (overlayCanvasGroup != null)
        {
            elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                overlayCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);
                yield return null;
            }
            overlayCanvasGroup.alpha = 0f;
        }

        gameObject.SetActive(false);
        _animCoroutine = null;
    }

    private IEnumerator ScalePunch(Transform target, float duration, float peakScale)
    {
        Vector3 originalScale = target.localScale;
        float   halfDur       = duration * 0.5f;
        float   elapsed       = 0f;

        // Scale up
        while (elapsed < halfDur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / halfDur);
            target.localScale = Vector3.Lerp(originalScale, originalScale * peakScale, t);
            yield return null;
        }

        elapsed = 0f;

        // Scale back down
        while (elapsed < halfDur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / halfDur);
            target.localScale = Vector3.Lerp(originalScale * peakScale, originalScale, t);
            yield return null;
        }

        target.localScale = originalScale;
    }

    // ─── Utility ─────────────────────────────────────────────────────────────

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null) return;
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }

    private static void SetTextAlpha(TMP_Text text, float alpha)
    {
        if (text == null) return;
        Color c = text.color;
        c.a = alpha;
        text.color = c;
    }
}
