using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sends icons flying from a point on screen (a reward chip, a shop card, a box in the world) to a HUD
/// target along curved paths: they burst out from the source, then get pulled in one after another,
/// and each landing is reported so the counter can tick in time with it.
///
/// Icons are drawn on a private screen-space overlay canvas above every game canvas, so they're never
/// hidden behind the panel they came from. They're pooled, capped at <see cref="MaxIcons"/> per flight,
/// and the target position is re-read every frame, so a HUD that is still sliding in is tracked.
///
/// The usual entry points are <see cref="Coins"/> and <see cref="XP"/>, which find the registered HUD
/// target and drive <see cref="UIRewardHold"/>. <see cref="Fly"/> is the raw version.
/// </summary>
public static class UIFlyToTarget
{
    public const int MaxIcons = 12;

    private struct HudTarget
    {
        public RectTransform target;
        public Sprite sprite;
        public Transform punch;
    }

    private static readonly HudTarget[] Targets = new HudTarget[2];
    private static readonly Stack<Image> Pool = new Stack<Image>();
    private static RectTransform _layer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Array.Clear(Targets, 0, Targets.Length);
        Pool.Clear();
        _layer = null;
    }

    // ------------------------------------------------------------------ HUD targets

    /// <summary>
    /// Registers where rewards of <paramref name="channel"/> land: the icon they fly into, the sprite
    /// the flying copies use, and (optionally) what punches on each landing when no counter does it.
    /// </summary>
    public static void RegisterTarget(RewardChannel channel, RectTransform target, Sprite sprite, Transform punch = null)
    {
        if (target == null) return;
        Targets[(int)channel] = new HudTarget { target = target, sprite = sprite, punch = punch };
    }

    public static void UnregisterTarget(RewardChannel channel, RectTransform target)
    {
        if (Targets[(int)channel].target == target) Targets[(int)channel] = default;
    }

    // ------------------------------------------------------------------ reward helpers

    /// <summary>
    /// Holds the coin counter, then flies coins to it after <paramref name="delay"/>; the counter ticks
    /// up as each coin lands. <b>Call this just before granting the coins</b> so the counter doesn't
    /// run ahead. Pass an existing <paramref name="hold"/> when it had to start earlier.
    /// </summary>
    public static void Coins(int amount, Vector2 fromScreen, float delay = 0f, UIRewardHold.Hold hold = null)
        => Reward(RewardChannel.Coins, amount, fromScreen, delay, hold);

    /// <summary>Same as <see cref="Coins"/> for XP; the bar fills once the last icon lands.</summary>
    public static void XP(int amount, Vector2 fromScreen, float delay = 0f, UIRewardHold.Hold hold = null)
        => Reward(RewardChannel.XP, amount, fromScreen, delay, hold);

    private static void Reward(RewardChannel channel, int amount, Vector2 fromScreen, float delay, UIRewardHold.Hold hold)
    {
        hold ??= UIRewardHold.Begin(channel, delay + 4f);
        if (amount <= 0)
        {
            hold.Release();
            return;
        }

        HudTarget t = Targets[(int)channel];
        int pieces = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(amount)) + 2, 3, channel == RewardChannel.Coins ? 10 : 6);
        pieces = Mathf.Min(pieces, amount);

        // Share the amount out so the landings add up to exactly the reward.
        int[] shares = new int[pieces];
        for (int i = 0; i < pieces; i++) shares[i] = amount / pieces + (i < amount % pieces ? 1 : 0);

        Fly(t.sprite, fromScreen, t.target, pieces, delay,
            i => UIRewardHold.Land(channel, shares[i]),
            hold.Release,
            t.punch);
    }

    // ------------------------------------------------------------------ screen helpers

    /// <summary>Screen position of a UI element's pivot, whatever its canvas's render mode.</summary>
    public static Vector2 ScreenPoint(RectTransform rect)
    {
        if (rect == null) return new Vector2(Screen.width, Screen.height) * 0.5f;
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null)
        {
            Canvas root = canvas.rootCanvas;
            if (root.renderMode != RenderMode.ScreenSpaceOverlay) cam = root.worldCamera;
        }
        return RectTransformUtility.WorldToScreenPoint(cam, rect.position);
    }

    /// <summary>Screen position of a world point (seen by the main camera).</summary>
    public static Vector2 ScreenPoint(Vector3 world)
    {
        Camera cam = Camera.main;
        if (cam == null) return new Vector2(Screen.width, Screen.height) * 0.5f;
        return cam.WorldToScreenPoint(world);
    }

    /// <summary>On-screen pixel size of a UI element (its larger side).</summary>
    private static float ScreenSize(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.rootCanvas.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        return Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
    }

    // ------------------------------------------------------------------ the flight

    /// <summary>
    /// Flies <paramref name="count"/> copies of <paramref name="sprite"/> from <paramref name="fromScreen"/>
    /// into <paramref name="target"/>. <paramref name="onLand"/> runs per icon (index), then
    /// <paramref name="onDone"/> once. Without a usable target, sprite, or with Reduce Motion on, every
    /// landing is reported straight after the delay with nothing drawn — callers never need a fallback.
    /// </summary>
    public static void Fly(Sprite sprite, Vector2 fromScreen, RectTransform target, int count, float delay,
        Action<int> onLand, Action onDone, Transform punchOnLand = null)
    {
        count = Mathf.Clamp(count, 1, MaxIcons);

        bool canDraw = sprite != null && target != null && target.gameObject.activeInHierarchy && !UIMotion.ReduceMotion;
        if (!canDraw)
        {
            DOVirtual.DelayedCall(Mathf.Max(0f, delay), () =>
            {
                for (int i = 0; i < count; i++) onLand?.Invoke(i);
                if (punchOnLand != null) UIMotion.Punch(punchOnLand);
                onDone?.Invoke();
            }, true);
            return;
        }

        RectTransform layer = Layer();
        float iconSize = Mathf.Clamp(ScreenSize(target) * 0.9f, Screen.height * 0.035f, Screen.height * 0.09f);
        float burstRadius = iconSize * 1.6f;

        int remaining = count;
        void Finish(int index)
        {
            onLand?.Invoke(index);
            if (punchOnLand != null) UIMotion.Punch(punchOnLand, 0.6f);
            if (--remaining == 0) onDone?.Invoke();
        }

        for (int i = 0; i < count; i++)
        {
            int index = i;
            Image icon = Take(layer, sprite);
            RectTransform rt = icon.rectTransform;
            rt.sizeDelta = new Vector2(iconSize, iconSize);
            rt.anchoredPosition = fromScreen;
            rt.localScale = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            icon.color = Color.white;

            // Burst: a ring of icons fanning out from the source, slightly uneven so it feels thrown.
            float angle = (index / (float)count) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.35f, 0.35f);
            Vector2 burst = fromScreen + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                * burstRadius * UnityEngine.Random.Range(0.6f, 1.15f);

            float burstTime = 0.3f;
            float flyTime = UnityEngine.Random.Range(0.45f, 0.58f);
            float start = delay + index * 0.045f;
            float side = UnityEngine.Random.Range(0.18f, 0.4f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            bool landed = false;

            Sequence s = DOTween.Sequence().SetUpdate(true).SetLink(icon.gameObject);
            s.AppendInterval(start);
            s.AppendCallback(() => icon.gameObject.SetActive(true));
            s.Append(rt.DOAnchorPos(burst, burstTime).SetEase(Ease.OutCubic));
            s.Join(rt.DOScale(1f, burstTime).SetEase(Ease.OutBack, 2f));
            s.Join(rt.DOLocalRotate(new Vector3(0f, 0f, UnityEngine.Random.Range(-25f, 25f)), burstTime));
            s.AppendInterval(UnityEngine.Random.Range(0.02f, 0.1f));
            // Fly: a curved pull into the target, re-aimed every frame, shrinking a little as it lands.
            s.Append(DOVirtual.Float(0f, 1f, flyTime, p =>
            {
                Vector2 end = ScreenPoint(target);
                Vector2 mid = (burst + end) * 0.5f;
                Vector2 dir = end - burst;
                Vector2 control = mid + new Vector2(-dir.y, dir.x) * side;
                float u = 1f - p;
                rt.anchoredPosition = u * u * burst + 2f * u * p * control + p * p * end;
                rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.7f, p);
            }).SetEase(Ease.InQuad));
            s.Join(rt.DOLocalRotate(Vector3.zero, flyTime));
            s.OnComplete(() =>
            {
                landed = true;
                Return(icon);
                Finish(index);
            });
            // Killed early (scene teardown): still report the landing so holds and counters settle.
            s.OnKill(() =>
            {
                if (landed) return;
                landed = true;
                Return(icon);
                Finish(index);
            });
        }
    }

    /// <summary>
    /// A quick spray of sparkles from a screen point (a purchase, an unlock). Drawn on the same overlay
    /// as the flights; does nothing with Reduce Motion on.
    /// </summary>
    public static void Burst(Vector2 screen, Color color, int count = 10, float radius = 0f, Sprite sprite = null)
    {
        if (UIMotion.ReduceMotion) return;
        sprite = sprite != null ? sprite : UIFxSprites.Sparkle;
        RectTransform layer = Layer();
        if (radius <= 0f) radius = Screen.height * 0.09f;
        count = Mathf.Clamp(count, 1, MaxIcons * 2);

        for (int i = 0; i < count; i++)
        {
            Image icon = Take(layer, sprite);
            RectTransform rt = icon.rectTransform;
            float size = Screen.height * UnityEngine.Random.Range(0.022f, 0.04f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = screen;
            rt.localScale = Vector3.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 90f));
            icon.color = color;
            icon.gameObject.SetActive(true);

            float angle = (i / (float)count) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.3f, 0.3f);
            Vector2 end = screen + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * UnityEngine.Random.Range(0.55f, 1.1f);
            float life = UnityEngine.Random.Range(0.45f, 0.65f);

            Sequence s = DOTween.Sequence().SetUpdate(true).SetLink(icon.gameObject);
            s.Insert(0f, rt.DOAnchorPos(end, life).SetEase(Ease.OutCubic));
            s.Insert(0f, rt.DOScale(1f, life * 0.35f).SetEase(Ease.OutBack, 2f));
            s.Insert(life * 0.45f, rt.DOScale(0f, life * 0.55f).SetEase(Ease.InQuad));
            s.Insert(0f, rt.DOLocalRotate(new Vector3(0f, 0f, UnityEngine.Random.Range(90f, 200f)), life, RotateMode.LocalAxisAdd));
            s.OnKill(() => Return(icon));
        }
    }

    // ------------------------------------------------------------------ overlay + pool

    private static RectTransform Layer()
    {
        if (_layer != null) return _layer;

        var go = new GameObject("UI Fly Layer", typeof(RectTransform));
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.layer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 0;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100; // above every game canvas (the highest is the toast canvas at 15)
        // No CanvasScaler: one unit is one pixel, so screen positions can be used directly.
        _layer = (RectTransform)go.transform;
        return _layer;
    }

    private static Image Take(RectTransform layer, Sprite sprite)
    {
        Image icon = null;
        while (Pool.Count > 0 && icon == null) icon = Pool.Pop();
        if (icon == null)
        {
            var go = new GameObject("FlyIcon", typeof(RectTransform));
            go.layer = layer.gameObject.layer;
            go.transform.SetParent(layer, false);
            icon = go.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            RectTransform rt = icon.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero; // anchoredPosition == screen position
            rt.pivot = new Vector2(0.5f, 0.5f);
        }
        icon.sprite = sprite;
        icon.gameObject.SetActive(false); // shown when its stagger starts
        return icon;
    }

    private static void Return(Image icon)
    {
        if (icon == null) return;
        icon.gameObject.SetActive(false);
        Pool.Push(icon);
    }
}
