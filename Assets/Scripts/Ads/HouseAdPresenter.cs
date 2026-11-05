using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen static image ad, shown when mediation has no rewarded ad to offer.
///
/// To add a creative, drop an image into <c>Assets/Resources/HouseAds</c>. HouseAdImportSettings imports it
/// as a sprite, and the ads take turns. No scene setup: the overlay is built when it is needed and destroyed
/// when it closes, so the images are only in memory while one is on screen.
///
/// The player waits out <see cref="MinWatchSeconds"/> and then closes the ad, which always earns the reward.
/// There is no way to close it sooner, so there is no skipped outcome to report.
/// </summary>
public class HouseAdPresenter : MonoBehaviour
{
    private const string ResourceFolder = "HouseAds";
    private const float MinWatchSeconds = 5f;

    // Above every game canvas, including the loading screen.
    private const int CanvasSortOrder = 32000;
    private const float ChipSize = 96f;
    private const float ChipMargin = 32f;

    private static HouseAdPresenter _active;
    private static int _nextAdIndex;
    private static bool? _hasAds;

    private Action _onFinished;
    private RectTransform _safeArea;
    private Rect _appliedSafeArea;
    private Texture2D _chipTexture;
    private Sprite _chipSprite;
    private float _timeScaleBeforeAd = 1f;

    /// <summary>True while an ad is on screen. A second ad must not be requested until it closes.</summary>
    public static bool IsShowing => _active != null;

    /// <summary>True if at least one creative is in the build. Checked once: Resources cannot change at runtime.</summary>
    public static bool HasAds
    {
        get
        {
            _hasAds ??= Resources.LoadAll<Sprite>(ResourceFolder).Length > 0;
            return _hasAds.Value;
        }
    }

    /// <summary>
    /// Puts the next house ad on screen.
    /// </summary>
    /// <param name="onFinished">Runs when the player closes the ad, which means they earned the reward.</param>
    /// <returns>False if nothing was shown, in which case <paramref name="onFinished"/> will not run.</returns>
    public static bool TryShow(Action onFinished)
    {
        if (IsShowing) return false;

        Sprite[] ads = Resources.LoadAll<Sprite>(ResourceFolder);
        if (ads.Length == 0) return false;

        Sprite ad = ads[_nextAdIndex % ads.Length];
        _nextAdIndex++;

        var root = new GameObject("HouseAd");
        DontDestroyOnLoad(root);

        _active = root.AddComponent<HouseAdPresenter>();
        _active.Begin(ad, onFinished);
        return true;
    }

    private void Begin(Sprite ad, Action onFinished)
    {
        _onFinished = onFinished;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortOrder;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        // The scrim catches every touch, so nothing behind the ad can be tapped.
        Image scrim = CreateImage("Scrim", transform, Color.black);
        Stretch(scrim.rectTransform);

        _safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
        _safeArea.SetParent(transform, false);
        ApplySafeArea();

        Image picture = CreateImage("Picture", _safeArea, Color.white);
        picture.sprite = ad;
        picture.preserveAspect = true;
        picture.raycastTarget = false;
        Stretch(picture.rectTransform);

        _timeScaleBeforeAd = Time.timeScale;
        Time.timeScale = 0f;

        Debug.Log($"[HouseAdPresenter] Showing house ad '{ad.name}'.");
        StartCoroutine(RunCountdown());
    }

    private IEnumerator RunCountdown()
    {
        _chipTexture = CreateCircleTexture(64);
        _chipSprite = Sprite.Create(_chipTexture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f));

        // Mid-grey, because it may sit over the black scrim or over a light part of the picture.
        Image chip = CreateImage("Chip", _safeArea, new Color(0.25f, 0.25f, 0.25f, 0.85f));
        chip.sprite = _chipSprite;
        RectTransform chipRect = chip.rectTransform;
        chipRect.anchorMin = chipRect.anchorMax = chipRect.pivot = Vector2.one;
        chipRect.sizeDelta = new Vector2(ChipSize, ChipSize);
        chipRect.anchoredPosition = new Vector2(-ChipMargin, -ChipMargin);

        var countdown = new GameObject("Countdown", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        countdown.transform.SetParent(chipRect, false);
        countdown.alignment = TextAlignmentOptions.Center;
        countdown.fontSize = 44f;
        countdown.color = Color.white;
        countdown.raycastTarget = false;
        Stretch(countdown.rectTransform);

        // Realtime: the game is paused at timeScale 0 for as long as the ad is up.
        for (int remaining = Mathf.CeilToInt(MinWatchSeconds); remaining > 0; remaining--)
        {
            countdown.text = remaining.ToString();
            yield return new WaitForSecondsRealtime(1f);
        }

        // A close button nobody can press would trap the player here, so without an EventSystem the ad
        // closes itself instead.
        if (EventSystem.current == null)
        {
            Debug.LogWarning("[HouseAdPresenter] No EventSystem in the scene, closing the ad automatically.");
            Finish();
            yield break;
        }

        countdown.gameObject.SetActive(false);
        AddCloseCross(chipRect);
        chip.gameObject.AddComponent<Button>().onClick.AddListener(Finish);
    }

    private void Finish()
    {
        if (_onFinished == null) return;

        Action finished = _onFinished;
        _onFinished = null;

        Destroy(gameObject);

        // Destroying a GameObject takes effect at the end of the frame, so clear the flag and restore time
        // here. The reward callback may well ask for something that checks either.
        Time.timeScale = _timeScaleBeforeAd;
        _active = null;

        finished.Invoke();
    }

    private void Update()
    {
        if (Screen.safeArea != _appliedSafeArea) ApplySafeArea();
    }

    private void OnDestroy()
    {
        if (_active == this)
        {
            // Torn down without Finish, which only happens as the game shuts down around it.
            _active = null;
            Time.timeScale = _timeScaleBeforeAd;
        }

        if (_chipSprite != null) Destroy(_chipSprite);
        if (_chipTexture != null) Destroy(_chipTexture);

        // The images came from Resources.LoadAll, and only this call lets them go.
        Resources.UnloadUnusedAssets();
    }

    private void ApplySafeArea()
    {
        _appliedSafeArea = Screen.safeArea;

        _safeArea.anchorMin = new Vector2(_appliedSafeArea.xMin / Screen.width, _appliedSafeArea.yMin / Screen.height);
        _safeArea.anchorMax = new Vector2(_appliedSafeArea.xMax / Screen.width, _appliedSafeArea.yMax / Screen.height);
        _safeArea.offsetMin = Vector2.zero;
        _safeArea.offsetMax = Vector2.zero;
    }

    /// <summary>Drawn from two bars rather than a glyph, so it cannot depend on what the font contains.</summary>
    private static void AddCloseCross(RectTransform parent)
    {
        foreach (float angle in new[] { 45f, -45f })
        {
            Image bar = CreateImage("CrossBar", parent, Color.white);
            bar.raycastTarget = false;
            bar.rectTransform.sizeDelta = new Vector2(ChipSize * 0.5f, 6f);
            bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
        image.transform.SetParent(parent, false);
        image.color = color;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Texture2D CreateCircleTexture(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance)));
            }
        }

        texture.Apply();
        return texture;
    }
}
