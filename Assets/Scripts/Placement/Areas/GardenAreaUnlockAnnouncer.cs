using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

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
                ToastMessageManager.Instance.ShowToast(
                    loc.Get("zone.unlocked_toast", areas.map.DisplayName(area)));
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

        RectTransform punch = punchTarget != null ? punchTarget : panel.transform as RectTransform;

        if (punch != null)
        {
            punch.DOKill();
            punch.localScale = Vector3.one;
            punch.DOPunchScale(new Vector3(0.25f, 0.25f, 0.25f), 0.5f, 8, 1f).SetUpdate(true);
        }

        yield return FadeTo(1f);
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, holdSeconds));
        yield return FadeTo(0f);

        if (content != null) content.SetActive(false);

        if (banner != null) banner.Announce(area);
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
