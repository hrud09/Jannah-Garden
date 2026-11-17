using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Slides the HUD in from the screen edges, one piece after another, when the scene is revealed —
/// as the loading screen fades out, or one frame after Start when there is no loading screen (the
/// Editor's direct Play). Each piece enters from the edge it is anchored to, so the HUD visibly
/// "assembles" around the garden instead of already sitting there.
///
/// Only <c>anchoredPosition</c> and a CanvasGroup's alpha are touched, and both are returned to
/// exactly their authored values; the rest pose is read at the moment the entrance starts (after
/// every Start has run), so scripts that position HUD pieces in Awake/Start are respected.
/// </summary>
public class UIHudEntrance : MonoBehaviour
{
    public enum Edge { Auto, Top, Bottom, Left, Right, FadeOnly }

    [Serializable]
    public class Entry
    {
        public RectTransform target;
        [Tooltip("Auto picks the screen edge the target is anchored nearest to.")]
        public Edge edge = Edge.Auto;
    }

    [Tooltip("HUD pieces, in the order they should arrive.")]
    public Entry[] entries;

    [Tooltip("Delay after the reveal before the first piece moves.")]
    public float startDelay = 0.15f;
    [Tooltip("Delay between consecutive pieces.")]
    public float step = 0.07f;
    public float duration = 0.55f;
    [Tooltip("Extra distance beyond the piece's own size, so it starts fully off-screen.")]
    public float offscreenMargin = 40f;

    private bool _played;

    private void Start()
    {
        LoadingScreenManager loader = LoadingScreenManager.Instance;
        if (loader != null && loader.IsLoading)
            LoadingScreenManager.Revealing += OnRevealing;
        else
            StartCoroutine(PlayNextFrame());
    }

    private void OnDestroy() => LoadingScreenManager.Revealing -= OnRevealing;

    private void OnRevealing()
    {
        LoadingScreenManager.Revealing -= OnRevealing;
        Play();
    }

    private IEnumerator PlayNextFrame()
    {
        yield return null; // let every Start (and the first layout pass) finish
        Play();
    }

    /// <summary>Plays the entrance. Runs once per scene; later calls are ignored.</summary>
    public void Play()
    {
        if (_played || entries == null) return;
        _played = true;

        bool reduced = UIMotion.ReduceMotion;
        int index = 0;
        foreach (Entry entry in entries)
        {
            RectTransform t = entry?.target;
            if (t == null || !t.gameObject.activeInHierarchy) continue;

            float delay = startDelay + index * step;
            index++;

            CanvasGroup group = t.GetComponent<CanvasGroup>();
            if (group == null) group = t.gameObject.AddComponent<CanvasGroup>();
            float restAlpha = group.alpha;
            group.alpha = 0f;
            group.DOFade(restAlpha, reduced ? 0.2f : duration * 0.6f)
                .SetDelay(delay).SetEase(Ease.OutQuad).SetUpdate(true).SetLink(t.gameObject);

            Edge edge = entry.edge == Edge.Auto ? NearestEdge(t) : entry.edge;
            if (reduced || edge == Edge.FadeOnly) continue;

            Vector2 rest = t.anchoredPosition;
            t.anchoredPosition = rest + OffscreenOffset(t, edge);
            t.DOAnchorPos(rest, duration)
                .SetDelay(delay).SetEase(Ease.OutBack, 1.1f).SetUpdate(true).SetLink(t.gameObject)
                // If anything kills the tween early, never leave the piece stranded off-screen.
                .OnKill(() => { if (t != null) t.anchoredPosition = rest; });
        }
    }

    private Vector2 OffscreenOffset(RectTransform t, Edge edge)
    {
        Vector2 size = t.rect.size;
        switch (edge)
        {
            case Edge.Top: return new Vector2(0f, size.y + offscreenMargin);
            case Edge.Bottom: return new Vector2(0f, -(size.y + offscreenMargin));
            case Edge.Left: return new Vector2(-(size.x + offscreenMargin), 0f);
            case Edge.Right: return new Vector2(size.x + offscreenMargin, 0f);
            default: return Vector2.zero;
        }
    }

    /// <summary>The screen edge the target's anchor centre sits closest to (ties go to top/bottom).</summary>
    private static Edge NearestEdge(RectTransform t)
    {
        Vector2 a = (t.anchorMin + t.anchorMax) * 0.5f;
        float dx = Mathf.Min(a.x, 1f - a.x);
        float dy = Mathf.Min(a.y, 1f - a.y);
        if (dy <= dx) return a.y >= 0.5f ? Edge.Top : Edge.Bottom;
        return a.x >= 0.5f ? Edge.Right : Edge.Left;
    }
}
