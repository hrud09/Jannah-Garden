using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// "+25" style text that pops up beside a HUD element, rises and fades. Labels are pooled per
/// anchor and copy their font from a style source, so they match the HUD they belong to.
/// </summary>
public static class UIFloatingText
{
    private static readonly Dictionary<int, Stack<TextMeshProUGUI>> Pools = new Dictionary<int, Stack<TextMeshProUGUI>>();

    /// <summary>
    /// Shows <paramref name="text"/> at <paramref name="anchor"/>'s pivot plus <paramref name="offset"/>
    /// and floats it up by <paramref name="rise"/> canvas units.
    /// </summary>
    public static void Spawn(RectTransform anchor, string text, Color color, TMP_Text styleSource,
        Vector2 offset = default, float rise = 55f, float fontSize = 0f)
    {
        if (anchor == null || !anchor.gameObject.activeInHierarchy) return;

        TextMeshProUGUI label = Take(anchor, styleSource);
        RectTransform rt = label.rectTransform;
        label.text = text;
        label.color = color;
        label.fontSize = fontSize > 0f ? fontSize : (styleSource != null ? styleSource.fontSize : 32f);
        rt.anchoredPosition = offset;
        rt.localScale = Vector3.one * 0.6f;
        rt.SetAsLastSibling();
        label.gameObject.SetActive(true);

        bool reduced = UIMotion.ReduceMotion;
        Sequence s = DOTween.Sequence().SetUpdate(true).SetLink(label.gameObject);
        if (reduced)
        {
            rt.localScale = Vector3.one;
            s.Append(label.DOFade(color.a, 0.1f).From(0f));
            s.AppendInterval(0.6f);
            s.Append(label.DOFade(0f, 0.25f));
        }
        else
        {
            s.Insert(0f, rt.DOScale(1f, 0.25f).SetEase(Ease.OutBack, 2.5f));
            s.Insert(0f, rt.DOAnchorPosY(offset.y + rise, 0.9f).SetEase(Ease.OutCubic));
            s.Insert(0.55f, label.DOFade(0f, 0.35f).SetEase(Ease.InQuad));
        }
        s.OnComplete(() => Return(anchor, label));
    }

    private static TextMeshProUGUI Take(RectTransform anchor, TMP_Text styleSource)
    {
        int key = anchor.GetInstanceID();
        if (Pools.TryGetValue(key, out Stack<TextMeshProUGUI> pool))
        {
            while (pool.Count > 0)
            {
                TextMeshProUGUI pooled = pool.Pop();
                if (pooled != null) return pooled;
            }
        }

        var go = new GameObject("FloatingText", typeof(RectTransform));
        go.layer = anchor.gameObject.layer; // UI cameras cull by layer; match the canvas
        go.transform.SetParent(anchor, false);
        var label = go.AddComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        if (styleSource != null)
        {
            label.font = styleSource.font;
            label.fontSharedMaterial = styleSource.fontSharedMaterial;
            label.fontStyle = styleSource.fontStyle;
        }
        RectTransform rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(200f, 50f);
        return label;
    }

    private static void Return(RectTransform anchor, TextMeshProUGUI label)
    {
        if (label == null) return;
        label.gameObject.SetActive(false);
        if (anchor == null) { Object.Destroy(label.gameObject); return; }

        int key = anchor.GetInstanceID();
        if (!Pools.TryGetValue(key, out Stack<TextMeshProUGUI> pool))
        {
            pool = new Stack<TextMeshProUGUI>();
            Pools[key] = pool;
        }
        pool.Push(label);
    }
}
