using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Celebrates a garden area opening.
///
/// <para>The unlock is the payoff the grey overlay has been promising since the first minute, so it
/// cannot be allowed to happen quietly — a patch of ground silently changing colour somewhere behind
/// the player is not a reward, it is a rendering change. This names the place that opened and says
/// what it is for.</para>
///
/// <para>Queued rather than fired per event, because crossing several levels at once (a treasure box,
/// a long quiz run, or a save that predates level-gated areas) opens several areas in the same frame,
/// and three overlapping celebrations read as a bug.</para>
/// </summary>
public class GardenAreaUnlockAnnouncer : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("The celebration card, faded in and out.")]
    public CanvasGroup panel;

    [Tooltip("The card's visible contents — a child, never this object itself. Switched off between " +
             "unlocks; this component has to keep running to hear the next one, and a MonoBehaviour " +
             "on a deactivated object does not.")]
    public GameObject content;

    public TMP_Text titleText;
    public TMP_Text bodyText;

    [Tooltip("Optional: the transform punched when the card appears. Falls back to the panel itself.")]
    public RectTransform punchTarget;

    [Header("Timing")]
    public float fadeSeconds = 0.3f;
    public float holdSeconds = 3.5f;

    [Tooltip("Gap between two celebrations when several areas open at once, so they read as separate " +
             "rewards rather than as one flicker.")]
    public float gapSeconds = 0.4f;

    [Header("Also")]
    [Tooltip("Optional: the area banner, asked to name the new area right after the card closes so " +
             "the player has something to walk towards.")]
    public GardenAreaBanner banner;

    [Header("Motion")]
    [Tooltip("Colour of the rays that turn slowly behind the card.")]
    public Color raysColor = new Color(1f, 0.86f, 0.45f, 0.55f);

    private Image _rays;
    private Tween _raysSpin;

    private readonly Queue<GardenAreaDefinition> _pending = new Queue<GardenAreaDefinition>();
    private Coroutine _running;
    private GardenAreaManager _subscribed;

    private void Start()
    {
        if (panel != null) panel.alpha = 0f;
        if (content != null) content.SetActive(false);

        SyncSubscription();
    }

    /// <summary>Polled for the same reason the rest of the area code is: neither this nor the manager
    /// controls which of them wakes first.</summary>
    private void Update() => SyncSubscription();

    private void OnDestroy()
    {
        if (_subscribed != null) _subscribed.AreaUnlocked -= HandleAreaUnlocked;
        _subscribed = null;
    }

    private void SyncSubscription()
    {
        GardenAreaManager areas = GardenAreaManager.Instance;
        if (_subscribed == areas) return;

        if (_subscribed != null) _subscribed.AreaUnlocked -= HandleAreaUnlocked;

        _subscribed = areas;

        if (_subscribed != null) _subscribed.AreaUnlocked += HandleAreaUnlocked;
    }

    private void HandleAreaUnlocked(GardenAreaDefinition area)
    {
        if (area == null) return;

        // The starting area opens during load, before the player has done anything to earn it, and
        // congratulating them for it teaches the wrong thing about what an unlock is.
        if (area.unlockedFromStart) return;

        _pending.Enqueue(area);
        _running ??= StartCoroutine(DrainQueue());
    }

    /// <summary>Plays the celebration for the first area that isn't open from the start, without
    /// unlocking anything — for checking the look in Play mode.</summary>
    [ContextMenu("Preview Celebration")]
    public void PreviewCelebration()
    {
        if (!Application.isPlaying) return;

        // Without an area manager (a test scene) the card still plays, just with no area name.
        GardenAreaDefinition pick = null;
        if (_subscribed != null && _subscribed.map != null)
        {
            foreach (GardenAreaDefinition area in _subscribed.map.areas)
            {
                if (area != null && !area.unlockedFromStart) { pick = area; break; }
            }
        }
        _pending.Enqueue(pick);
        _running ??= StartCoroutine(DrainQueue());
    }

    private IEnumerator DrainQueue()
    {
        while (_pending.Count > 0)
        {
            yield return Celebrate(_pending.Dequeue());
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, gapSeconds));
        }

        _running = null;
    }

    private IEnumerator Celebrate(GardenAreaDefinition area)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        GardenAreaManager areas = _subscribed;

        if (panel == null || loc == null)
        {
            // No card wired up: say it in the one place that is always there rather than swallowing
            // the moment entirely.
            if (ToastMessageManager.Instance != null && loc != null && areas != null && areas.map != null)
            {
                // The toast plays its own success chime, so the fanfare would double up here.
                ToastMessageManager.Instance.ShowToast(
                    loc.Get("zone.unlocked_toast", areas.map.DisplayName(area)), ToastTone.Success);
            }
            else
            {
                GameSfx.Play(SoundEffect.AreaUnlockFanfare);
            }

            yield break;
        }

        string name = areas != null && areas.map != null ? areas.map.DisplayName(area) : string.Empty;

        // Through LocalizedRendering so Arabic and Urdu arrive shaped, the same as every other panel.
        if (titleText != null) LocalizedRendering.SetText(titleText, loc.Get("zone.unlocked_title"));
        if (bodyText != null) LocalizedRendering.SetText(bodyText, loc.Get("zone.unlocked_body", name));

        if (content != null) content.SetActive(true);
        panel.blocksRaycasts = false;
        panel.interactable = false;

        // Two layers, matching the two beats of the animation: a rising reveal as the card appears,
        // then the fanfare landing on the punch.
        GameSfx.Play(SoundEffect.AreaUnlockReveal);

        RectTransform punch = punchTarget != null ? punchTarget : panel.transform as RectTransform;

        if (punch != null)
        {
            punch.DOKill();
            punch.localScale = Vector3.one;
            punch.DOPunchScale(new Vector3(0.25f, 0.25f, 0.25f), 0.5f, 8, 1f).SetUpdate(true);
        }

        PlayEntranceExtras(punch);

        GameSfx.Play(SoundEffect.AreaUnlockFanfare);

        yield return FadeTo(1f);
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, holdSeconds));

        // Leaving: the card settles back a touch as it fades, rather than just dissolving.
        if (punch != null && !UIMotion.ReduceMotion)
            punch.DOScale(0.94f, Mathf.Max(0.01f, fadeSeconds)).SetEase(Ease.InQuad).SetUpdate(true).SetLink(punch.gameObject);
        yield return FadeTo(0f);

        StopEntranceExtras(punch);
        if (content != null) content.SetActive(false);

        if (banner != null && area != null) banner.Announce(area);
    }

    /// <summary>
    /// The rest of the celebration on top of the card's punch: rays bloom and turn slowly behind it, the
    /// title stamps down, the body fades in after it, and sparkles burst from the card.
    /// </summary>
    private void PlayEntranceExtras(RectTransform card)
    {
        if (UIMotion.ReduceMotion) return;

        EnsureRays(card);
        if (_rays != null)
        {
            RectTransform rt = _rays.rectTransform;
            _rays.gameObject.SetActive(true);
            rt.localScale = Vector3.one * 0.5f;
            rt.localRotation = Quaternion.identity;
            rt.DOScale(1f, 0.6f).SetEase(Ease.OutBack, 1.4f).SetUpdate(true).SetLink(rt.gameObject);
            _raysSpin?.Kill();
            _raysSpin = rt.DOLocalRotate(new Vector3(0f, 0f, -360f), 16f, RotateMode.FastBeyond360)
                .SetEase(Ease.Linear).SetLoops(-1).SetUpdate(true).SetLink(rt.gameObject);
        }

        if (titleText != null) UIMotion.Stamp(titleText.transform, 0.1f);

        if (bodyText != null)
        {
            // Through a CanvasGroup so the Bengali shaped renderer (a child graphic) fades with it.
            CanvasGroup body = bodyText.GetComponent<CanvasGroup>();
            if (body == null) body = bodyText.gameObject.AddComponent<CanvasGroup>();
            body.alpha = 0f;
            body.DOFade(1f, 0.35f).SetDelay(0.3f).SetUpdate(true).SetLink(bodyText.gameObject);
        }

        if (card != null)
        {
            Vector2 at = UIFlyToTarget.ScreenPoint(card);
            float radius = Screen.height * 0.18f;
            DOVirtual.DelayedCall(0.12f, () =>
            {
                UIFlyToTarget.Burst(at, new Color(1f, 0.9f, 0.55f, 1f), 14, radius);
                UIFlyToTarget.Burst(at, new Color(0.75f, 1f, 0.78f, 1f), 8, radius * 0.7f);
            }, true);
        }
    }

    private void StopEntranceExtras(RectTransform card)
    {
        _raysSpin?.Kill();
        _raysSpin = null;
        if (_rays != null) _rays.gameObject.SetActive(false);
        if (card != null) card.localScale = Vector3.one;
        if (titleText != null) titleText.transform.localScale = Vector3.one;
        CanvasGroup body = bodyText != null ? bodyText.GetComponent<CanvasGroup>() : null;
        if (body != null) body.alpha = 1f;
    }

    /// <summary>Builds the ray burst once, behind everything in the card, centred on it.</summary>
    private void EnsureRays(RectTransform card)
    {
        if (_rays != null || content == null || card == null) return;

        var go = new GameObject("Unlock Rays", typeof(RectTransform));
        go.layer = content.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(content.transform, false);
        rt.SetAsFirstSibling();
        go.AddComponent<LayoutElement>().ignoreLayout = true;

        _rays = go.AddComponent<Image>();
        _rays.sprite = UIFxSprites.Rays;
        _rays.color = raysColor;
        _rays.raycastTarget = false;

        float size = Mathf.Max(card.rect.width, card.rect.height) * 1.6f;
        rt.sizeDelta = new Vector2(size, size);
        rt.position = card.TransformPoint(card.rect.center);
        go.SetActive(false);
    }

    private IEnumerator FadeTo(float target)
    {
        float duration = Mathf.Max(0.01f, fadeSeconds);

        while (!Mathf.Approximately(panel.alpha, target))
        {
            panel.alpha = Mathf.MoveTowards(panel.alpha, target, Time.unscaledDeltaTime / duration);
            yield return null;
        }
    }
}
