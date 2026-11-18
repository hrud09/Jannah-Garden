using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The one popup the riding features share — the ride ad offer, Auto Travel, and the Jungle Orb Run's
/// start and reward screens. Built from the treasure-box confirmation panel's look (cream panel, gold
/// ribbon title, inset description, sage and deep buttons, clay close button) by
/// <c>RideFeaturesBuilder</c>, and filled in per use through <see cref="Show"/>.
///
/// Like that panel, the root stays active and only its visuals are toggled, so <see cref="Instance"/>
/// always resolves.
/// </summary>
public class RidePromptPanel : MonoBehaviour
{
    private static RidePromptPanel _instance;

    public static RidePromptPanel Instance
    {
        get
        {
            if (_instance == null) _instance = FindFirstObjectByType<RidePromptPanel>(FindObjectsInactive.Include);
            return _instance;
        }
    }

    [Header("Visual roots")]
    public GameObject dim;
    public GameObject window;

    [Header("Content")]
    public TMP_Text titleText;
    public TMP_Text bodyText;
    public TMP_Text noteText;
    public Image icon;

    [Header("Buttons")]
    public Button primaryButton;
    public TMP_Text primaryLabel;
    [Tooltip("The little TV shown on the primary button when pressing it plays an ad.")]
    public GameObject primaryAdIcon;
    public Button secondaryButton;
    public TMP_Text secondaryLabel;
    public Button closeButton;

    /// <summary>True while the panel is up.</summary>
    public bool IsOpen { get; private set; }

    private Action _onPrimary, _onSecondary, _onClose;
    private UIPanelMotion _motion;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        if (primaryButton != null) primaryButton.onClick.AddListener(() => Choose(_onPrimary));
        if (secondaryButton != null) secondaryButton.onClick.AddListener(() => Choose(_onSecondary));
        if (closeButton != null) closeButton.onClick.AddListener(() => Choose(_onClose));

        SetVisible(false, animate: false);
    }

    /// <summary>
    /// Opens the panel. Any button closes it first, then runs its callback. Leave
    /// <paramref name="secondary"/> null for a single-button panel.
    /// </summary>
    /// <param name="primaryIsAd">Shows the TV icon on the primary button: pressing it plays an ad.</param>
    /// <param name="onClose">The close (X) button. Also what happens if the panel is replaced.</param>
    public void Show(string title, string body, string note, Sprite iconSprite,
                     string primary, bool primaryIsAd, Action onPrimary,
                     string secondary, Action onSecondary, Action onClose)
    {
        // A panel already up is replaced; its owner hears it was closed.
        if (IsOpen)
        {
            Action previous = _onClose;
            _onPrimary = _onSecondary = _onClose = null;
            previous?.Invoke();
        }

        SetText(titleText, title);
        SetText(bodyText, body);
        SetText(noteText, note);
        if (noteText != null) noteText.gameObject.SetActive(!string.IsNullOrEmpty(note));

        if (icon != null)
        {
            icon.sprite = iconSprite;
            icon.enabled = iconSprite != null;
            icon.preserveAspect = true;
        }

        SetText(primaryLabel, primary);
        if (primaryAdIcon != null) primaryAdIcon.SetActive(primaryIsAd);

        bool hasSecondary = !string.IsNullOrEmpty(secondary);
        if (secondaryButton != null) secondaryButton.gameObject.SetActive(hasSecondary);
        if (hasSecondary) SetText(secondaryLabel, secondary);

        _onPrimary = onPrimary;
        _onSecondary = onSecondary;
        _onClose = onClose;

        GameSfx.Play(SoundEffect.PanelOpen);
        SetVisible(true, animate: true);
    }

    /// <summary>Closes the panel without running any callback.</summary>
    public void Hide()
    {
        _onPrimary = _onSecondary = _onClose = null;
        if (IsOpen) SetVisible(false, animate: true);
    }

    private void Choose(Action action)
    {
        _onPrimary = _onSecondary = _onClose = null;
        GameSfx.Play(SoundEffect.PanelClose);
        SetVisible(false, animate: true);
        action?.Invoke();
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text == null) return;
        LocalizedRendering.SetText(text, value ?? string.Empty);
    }

    private void SetVisible(bool visible, bool animate)
    {
        IsOpen = visible;
        if (window == null) return;

        if (!animate || !Application.isPlaying)
        {
            if (_motion != null) _motion.SetVisibleImmediate(visible);
            else window.SetActive(visible);
            if (dim != null) dim.SetActive(visible);
            return;
        }

        EnsureMotion();
        if (visible)
        {
            if (dim != null) dim.SetActive(true);
            _motion.Show();
        }
        else if (_motion.IsVisible)
        {
            _motion.Hide(() => { if (!IsOpen && dim != null) dim.SetActive(false); });
        }
        else if (dim != null)
        {
            dim.SetActive(false);
        }
    }

    private void EnsureMotion()
    {
        if (_motion != null) return;
        if (!window.TryGetComponent(out _motion)) _motion = window.AddComponent<UIPanelMotion>();
        _motion.preset = UIPanelMotion.Preset.Pop;
        if (dim != null)
        {
            if (!dim.TryGetComponent(out CanvasGroup group)) group = dim.AddComponent<CanvasGroup>();
            _motion.backdrop = group;
            _motion.backdropAlphaOverride = 1f;
        }
        _motion.CaptureRest();

        if (primaryButton != null)
        {
            if (!primaryButton.TryGetComponent(out UIIdle idle)) idle = primaryButton.gameObject.AddComponent<UIIdle>();
            idle.shimmerGraphic = primaryButton.image;
            idle.SetEffects(UIIdle.Effect.Shimmer);
        }
    }
}
