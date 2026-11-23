using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

public class GameSettingsManager : MonoBehaviour
{
    [Header("UI Toggles")]
    public Toggle sfxToggle;
    public Toggle musicToggle;
    public Toggle vibrationToggle;
    [Tooltip("Reduce Motion: swaps UI scale/slide animations for short fades and stops idle loops.")]
    public Toggle reduceMotionToggle;

    [Header("UI Sliders")]
    public Slider normalSpeedSlider;
    public Slider inspectorSpeedSlider;
    public Slider lookAroundSpeedSlider;

    [Header("UI Slider Value Texts")]
    public TextMeshProUGUI normalSpeedValueText;
    public TextMeshProUGUI inspectorSpeedValueText;
    public TextMeshProUGUI lookAroundSpeedValueText;

    [Header("Close Settings")]
    public Button closeButton;
    public GameObject settingsPanel;
    
    [Header("Open Settings")]
    public Button settingsButton;

    [Header("Animation References")]
    [Tooltip("The dark background overlay image or canvas group to fade in/out.")]
    public CanvasGroup darkBg;
    [Tooltip("The main settings dialog window to scale up/down.")]
    public RectTransform mainBg;
    [Tooltip("The individual options inside the settings panel to animate sequentially.")]
    public RectTransform[] settingsOptions;

    [Header("Animation Settings")]
    [Tooltip("Unused: the panel's timings now come from Resources/UIMotionTokens so every panel feels the same. Kept so the scene's serialized values aren't lost.")]
    public float fadeDuration = 0.25f;
    public float scaleDuration = 0.35f;
    public float itemStaggerDelay = 0.05f;
    public float itemScaleDuration = 0.2f;

    private bool _isAnimating = false;
    private IdyllicFantasyNature.PlayerMovement _playerMovement;
    private IdyllicFantasyNature.CameraMovement _cameraMovement;

    private void Awake()
    {
        // By default, keep the settings panel hidden
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        InitializeSettings();
    }

    private void OnEnable()
    {
        AnimateOpen();
    }

    private void InitializeSettings()
    {
        // Load settings or set defaults
        bool sfxEnabled = PlayerPrefs.GetInt("SFXEnabled", 1) == 1;
        bool musicEnabled = PlayerPrefs.GetInt("MusicEnabled", 1) == 1;
        bool vibrationEnabled = PlayerPrefs.GetInt("VibrationEnabled", 1) == 1;
        // Apply audio settings to AudioManager
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.IsSfxMuted = !sfxEnabled;
            AudioManager.Instance.IsMusicMuted = !musicEnabled;
        }

        // Movement speeds: only override PlayerMovement when the player has actually
        // saved a value from the sliders. Otherwise keep the speeds set on the
        // PlayerMovement component in the scene rather than forcing hardcoded defaults.
        if (_playerMovement == null)
        {
            _playerMovement = FindObjectOfType<IdyllicFantasyNature.PlayerMovement>();
        }
        float normalSpeed = _playerMovement != null ? _playerMovement.MovementSpeed : 8f;
        float inspectorSpeed = _playerMovement != null ? _playerMovement.inspectorModeSpeed : 25f;
        if (PlayerPrefs.HasKey("NormalMovementSpeed"))
        {
            normalSpeed = PlayerPrefs.GetFloat("NormalMovementSpeed");
            if (_playerMovement != null) _playerMovement.SetMovementSpeed(normalSpeed);
        }
        if (PlayerPrefs.HasKey("InspectorMovementSpeed"))
        {
            inspectorSpeed = PlayerPrefs.GetFloat("InspectorMovementSpeed");
            if (_playerMovement != null) _playerMovement.SetInspectorModeSpeed(inspectorSpeed);
        }

        // Apply look around speed setting to CameraMovement
        if (_cameraMovement == null)
        {
            _cameraMovement = FindObjectOfType<IdyllicFantasyNature.CameraMovement>();
        }

        float defaultLookSpeed = _cameraMovement != null ? _cameraMovement.SensitivityMultiplier : 1f;
        float lookAroundSpeed = PlayerPrefs.GetFloat("LookAroundSpeed", defaultLookSpeed);
        lookAroundSpeed = Mathf.Clamp(lookAroundSpeed, 0.5f, 100f);

        if (_cameraMovement != null)
        {
            _cameraMovement.SetSensitivityMultiplier(lookAroundSpeed);
        }

        // Set up Toggle components
        if (sfxToggle != null)
        {
            sfxToggle.isOn = sfxEnabled;
            sfxToggle.onValueChanged.AddListener(SetSFX);
        }
        if (musicToggle != null)
        {
            musicToggle.isOn = musicEnabled;
            musicToggle.onValueChanged.AddListener(SetMusic);
        }
        if (vibrationToggle != null)
        {
            vibrationToggle.isOn = vibrationEnabled;
            vibrationToggle.onValueChanged.AddListener(SetVibration);
        }
        if (reduceMotionToggle != null)
        {
            reduceMotionToggle.isOn = UIMotion.ReduceMotion;
            reduceMotionToggle.onValueChanged.AddListener(SetReduceMotion);
        }

        // Set up Slider components
        if (normalSpeedSlider != null)
        {
            normalSpeedSlider.minValue = 1f;
            normalSpeedSlider.maxValue = 20f;
            normalSpeedSlider.SetValueWithoutNotify(normalSpeed);
            normalSpeedSlider.onValueChanged.AddListener(SetNormalMovementSpeed);
        }
        UpdateNormalSpeedText(normalSpeed);

        if (inspectorSpeedSlider != null)
        {
            inspectorSpeedSlider.minValue = 5f;
            inspectorSpeedSlider.maxValue = 50f;
            inspectorSpeedSlider.SetValueWithoutNotify(inspectorSpeed);
            inspectorSpeedSlider.onValueChanged.AddListener(SetInspectorMovementSpeed);
        }
        UpdateInspectorSpeedText(inspectorSpeed);

        // Auto-find lookAroundSpeedSlider if not explicitly assigned in inspector
        if (lookAroundSpeedSlider == null)
        {
            Transform content = transform.Find("Settings Panel/BG Image/Content") ?? transform.Find("BG Image/Content");
            if (content != null)
            {
                Transform lookOption = content.Find("Look Arround Speed Option UI") ?? content.Find("Look Around Speed Option UI");
                if (lookOption != null)
                {
                    lookAroundSpeedSlider = lookOption.GetComponentInChildren<Slider>();
                }
            }
        }

        if (lookAroundSpeedSlider != null)
        {
            lookAroundSpeedSlider.minValue = 0.5f;
            lookAroundSpeedSlider.maxValue = 100f;
            lookAroundSpeedSlider.SetValueWithoutNotify(lookAroundSpeed);
            lookAroundSpeedSlider.onValueChanged.AddListener(SetLookAroundSpeed);
        }
        UpdateLookAroundSpeedText(lookAroundSpeed);

        // Update visuals
        UpdateVisuals();

        // Close button
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseSettings);
        }

        // Settings button
        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(OpenSettings);
        }
    }

    // ---------------------------------------------------------------- open / close motion
    //
    // Open: the dim fades up while the window springs in from below with a squash-and-stretch
    // landing and a small tilt that rights itself; the rows then cascade in with an alternating
    // wobble, and the close button spins in last. Close: a quick anticipation swell, then the
    // window tips and shrinks away while the rows collapse in reverse. Everything runs on
    // unscaled time and collapses to plain fades under Reduce Motion.

    private Sequence _panelSequence;
    private Vector2 _mainBgRestPos;
    private bool _mainBgRestCaptured;
    private Vector3 _closeButtonRestScale = Vector3.one;
    private bool _closeButtonRestCaptured;

    private CanvasGroup MainBgGroup
    {
        get
        {
            if (mainBg == null) return null;
            CanvasGroup g = mainBg.GetComponent<CanvasGroup>();
            return g != null ? g : mainBg.gameObject.AddComponent<CanvasGroup>();
        }
    }

    /// <summary>The close button, unless it is already one of the cascading rows.</summary>
    private Transform CloseButtonTransform
    {
        get
        {
            if (closeButton == null) return null;
            if (settingsOptions != null && System.Array.IndexOf(settingsOptions, closeButton.transform as RectTransform) >= 0) return null;
            return closeButton.transform;
        }
    }

    private void CaptureRestPose()
    {
        // Only capture while nothing is mid-flight, so an interrupted tween can't become the new rest.
        if (_panelSequence != null && _panelSequence.IsActive()) return;
        if (mainBg != null && !_mainBgRestCaptured)
        {
            _mainBgRestPos = mainBg.anchoredPosition;
            _mainBgRestCaptured = true;
        }
        Transform cb = CloseButtonTransform;
        if (cb != null && !_closeButtonRestCaptured)
        {
            _closeButtonRestScale = cb.localScale == Vector3.zero ? Vector3.one : cb.localScale;
            _closeButtonRestCaptured = true;
        }
    }

    private void KillPanelSequence()
    {
        if (_panelSequence != null && _panelSequence.IsActive()) _panelSequence.Kill();
        _panelSequence = null;
        if (mainBg != null) mainBg.DOKill();
        if (darkBg != null) darkBg.DOKill();
        if (settingsOptions != null)
            foreach (var o in settingsOptions) if (o != null) o.DOKill();
        Transform cb = CloseButtonTransform;
        if (cb != null) cb.DOKill();
    }

    private void ResetToRestPose()
    {
        if (mainBg != null)
        {
            mainBg.localScale = Vector3.one;
            mainBg.localRotation = Quaternion.identity;
            if (_mainBgRestCaptured) mainBg.anchoredPosition = _mainBgRestPos;
            CanvasGroup g = mainBg.GetComponent<CanvasGroup>();
            if (g != null) g.alpha = 1f;
        }
        if (settingsOptions != null)
            foreach (var o in settingsOptions)
                if (o != null) { o.localScale = Vector3.one; o.localRotation = Quaternion.identity; }
        Transform cb = CloseButtonTransform;
        if (cb != null) { cb.localScale = _closeButtonRestScale; cb.localRotation = Quaternion.identity; }
    }

    private Sequence NewPanelSequence()
    {
        GameObject link = settingsPanel != null ? settingsPanel : gameObject;
        _panelSequence = DOTween.Sequence().SetUpdate(true).SetLink(link);
        return _panelSequence;
    }

    private void AnimateOpen()
    {
        CaptureRestPose();
        KillPanelSequence();
        ResetToRestPose();

        Sequence seq = NewPanelSequence();
        CanvasGroup windowGroup = MainBgGroup;

        if (darkBg != null)
        {
            darkBg.alpha = 0f;
            seq.Insert(0f, darkBg.DOFade(1f, 0.22f).SetEase(Ease.OutQuad));
        }

        if (UIMotion.ReduceMotion)
        {
            if (windowGroup != null)
            {
                windowGroup.alpha = 0f;
                seq.Insert(0f, windowGroup.DOFade(1f, UIMotion.Tokens.reducedFade.duration).SetEase(Ease.Linear));
            }
            seq.OnComplete(() => _panelSequence = null);
            return;
        }

        float landAt = 0f;
        if (mainBg != null)
        {
            // Start small, low and tilted, then spring up.
            mainBg.localScale = new Vector3(0.55f, 0.45f, 1f);
            mainBg.localRotation = Quaternion.Euler(0f, 0f, UIMotion.IsRightToLeft ? 6f : -6f);
            mainBg.anchoredPosition = _mainBgRestPos + new Vector2(0f, -70f);
            if (windowGroup != null)
            {
                windowGroup.alpha = 0f;
                seq.Insert(0f, windowGroup.DOFade(1f, 0.12f).SetEase(Ease.OutQuad));
            }

            seq.Insert(0f, mainBg.DOAnchorPos(_mainBgRestPos, 0.34f).SetEase(Ease.OutBack, 1.6f));
            seq.Insert(0f, mainBg.DOLocalRotate(Vector3.zero, 0.5f).SetEase(Ease.OutElastic, 1.1f, 0.35f));

            // Stretch tall on the way up, squash wide on landing, rebound, settle.
            Sequence squash = DOTween.Sequence()
                .Append(mainBg.DOScale(new Vector3(0.96f, 1.08f, 1f), 0.16f).SetEase(Ease.OutQuad))
                .Append(mainBg.DOScale(new Vector3(1.08f, 0.93f, 1f), 0.09f).SetEase(Ease.InOutSine))
                .Append(mainBg.DOScale(new Vector3(0.98f, 1.03f, 1f), 0.09f).SetEase(Ease.InOutSine))
                .Append(mainBg.DOScale(Vector3.one, 0.14f).SetEase(Ease.OutBack, 2f));
            seq.Insert(0f, squash);
            landAt = 0.16f;
        }

        // Rows cascade in with an alternating wobble once the window has landed.
        int shown = 0;
        if (settingsOptions != null)
        {
            for (int i = 0; i < settingsOptions.Length; i++)
            {
                RectTransform option = settingsOptions[i];
                if (option == null || !option.gameObject.activeSelf) continue;

                float at = landAt + 0.05f + shown * 0.045f;
                float tilt = (shown % 2 == 0 ? -1f : 1f) * 9f;
                option.localScale = Vector3.zero;
                option.localRotation = Quaternion.Euler(0f, 0f, tilt);
                seq.Insert(at, option.DOScale(Vector3.one, 0.32f).SetEase(Ease.OutBack, 2.2f));
                seq.Insert(at, option.DOLocalRotate(Vector3.zero, 0.4f).SetEase(Ease.OutBack, 2.5f));
                shown++;
            }
        }

        // The close button spins in last, like a seal on the window.
        Transform cb = CloseButtonTransform;
        if (cb != null)
        {
            float at = landAt + 0.1f + shown * 0.045f;
            cb.localScale = Vector3.zero;
            cb.localRotation = Quaternion.Euler(0f, 0f, -120f);
            seq.Insert(at, cb.DOScale(_closeButtonRestScale, 0.3f).SetEase(Ease.OutBack, 2.6f));
            seq.Insert(at, cb.DOLocalRotate(Vector3.zero, 0.35f).SetEase(Ease.OutBack, 1.8f));
        }

        seq.OnComplete(() => _panelSequence = null);
    }

    private void AnimateClose(TweenCallback onDone)
    {
        CaptureRestPose();
        KillPanelSequence();

        Sequence seq = NewPanelSequence();
        CanvasGroup windowGroup = MainBgGroup;

        if (UIMotion.ReduceMotion)
        {
            float d = UIMotion.Tokens.reducedFade.duration;
            if (windowGroup != null) seq.Insert(0f, windowGroup.DOFade(0f, d).SetEase(Ease.Linear));
            if (darkBg != null) seq.Insert(0f, darkBg.DOFade(0f, d).SetEase(Ease.Linear));
            seq.OnComplete(() => { _panelSequence = null; onDone(); ResetToRestPose(); });
            return;
        }

        // Close button and rows collapse in reverse order, quickly.
        int count = 0;
        Transform cb = CloseButtonTransform;
        if (cb != null)
        {
            seq.Insert(0f, cb.DOScale(Vector3.zero, 0.12f).SetEase(Ease.InBack, 2f));
            seq.Insert(0f, cb.DOLocalRotate(new Vector3(0f, 0f, 90f), 0.12f).SetEase(Ease.InQuad));
            count++;
        }
        if (settingsOptions != null)
        {
            for (int i = settingsOptions.Length - 1; i >= 0; i--)
            {
                RectTransform option = settingsOptions[i];
                if (option == null || !option.gameObject.activeSelf) continue;
                seq.Insert(Mathf.Min(count * 0.015f, 0.08f), option.DOScale(Vector3.zero, 0.13f).SetEase(Ease.InBack, 1.8f));
                count++;
            }
        }

        if (mainBg != null)
        {
            // Anticipation: a quick swell, then tip over and shrink away.
            float tip = UIMotion.IsRightToLeft ? -5f : 5f;
            seq.Insert(0f, mainBg.DOScale(new Vector3(1.05f, 1.05f, 1f), 0.07f).SetEase(Ease.OutQuad));
            seq.Insert(0.07f, mainBg.DOScale(new Vector3(0.6f, 0.55f, 1f), 0.18f).SetEase(Ease.InBack, 1.6f));
            seq.Insert(0.07f, mainBg.DOLocalRotate(new Vector3(0f, 0f, tip), 0.18f).SetEase(Ease.InQuad));
            seq.Insert(0.07f, mainBg.DOAnchorPos(_mainBgRestPos + new Vector2(0f, -40f), 0.18f).SetEase(Ease.InQuad));
            if (windowGroup != null) seq.Insert(0.13f, windowGroup.DOFade(0f, 0.12f).SetEase(Ease.InQuad));
        }

        if (darkBg != null) seq.Insert(0.06f, darkBg.DOFade(0f, 0.22f).SetEase(Ease.InQuad));

        seq.OnComplete(() =>
        {
            _panelSequence = null;
            onDone();
            ResetToRestPose(); // hidden now; leave everything at rest for the next open
        });
    }

    // Direct Set Methods
    public void SetSFX(bool enabled)
    {
        PlayerPrefs.SetInt("SFXEnabled", enabled ? 1 : 0);
        PlayerPrefs.Save();
        GameEvents.SettingChanged("SFX", enabled ? "On" : "Off");
        
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.IsSfxMuted = !enabled;
        }

        PlayToggleSound(enabled);
        UpdateVisuals();
    }

    public void SetMusic(bool enabled)
    {
        PlayerPrefs.SetInt("MusicEnabled", enabled ? 1 : 0);
        PlayerPrefs.Save();
        GameEvents.SettingChanged("Music", enabled ? "On" : "Off");
        
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.IsMusicMuted = !enabled;
        }

        PlayToggleSound(enabled);
        UpdateVisuals();
    }

    public void SetVibration(bool enabled)
    {
        PlayerPrefs.SetInt("VibrationEnabled", enabled ? 1 : 0);
        PlayerPrefs.Save();
        GameEvents.SettingChanged("Vibration", enabled ? "On" : "Off");

        PlayToggleSound(enabled);
        TriggerVibrationFeedback();
        UpdateVisuals();
    }

    public void SetReduceMotion(bool enabled)
    {
        UIMotion.ReduceMotion = enabled;
        GameEvents.SettingChanged("ReduceMotion", enabled ? "On" : "Off");

        PlayToggleSound(enabled);
        UpdateVisuals();
    }

    public void SetNormalMovementSpeed(float value)
    {
        PlayerPrefs.SetFloat("NormalMovementSpeed", value);
        PlayerPrefs.Save();

        UpdateNormalSpeedText(value);

        if (_playerMovement != null)
        {
            _playerMovement.SetMovementSpeed(value);
        }
    }

    public void SetInspectorMovementSpeed(float value)
    {
        PlayerPrefs.SetFloat("InspectorMovementSpeed", value);
        PlayerPrefs.Save();

        UpdateInspectorSpeedText(value);

        if (_playerMovement != null)
        {
            _playerMovement.SetInspectorModeSpeed(value);
        }
    }

    public void SetLookAroundSpeed(float value)
    {
        float clampedValue = Mathf.Clamp(value, 0.5f, 100f);
        PlayerPrefs.SetFloat("LookAroundSpeed", clampedValue);
        PlayerPrefs.Save();

        UpdateLookAroundSpeedText(clampedValue);

        if (_cameraMovement == null)
        {
            _cameraMovement = FindObjectOfType<IdyllicFantasyNature.CameraMovement>();
        }

        if (_cameraMovement != null)
        {
            _cameraMovement.SetSensitivityMultiplier(clampedValue);
        }
    }

    private void UpdateNormalSpeedText(float value)
    {
        if (normalSpeedValueText != null)
        {
            normalSpeedValueText.text = value.ToString("0.#");
        }
    }

    private void UpdateInspectorSpeedText(float value)
    {
        if (inspectorSpeedValueText != null)
        {
            inspectorSpeedValueText.text = value.ToString("0.#");
        }
    }

    private void UpdateLookAroundSpeedText(float value)
    {
        if (lookAroundSpeedValueText != null)
        {
            lookAroundSpeedValueText.text = value.ToString("0.#");
        }
    }

    private void UpdateVisuals()
    {
        bool sfxEnabled = PlayerPrefs.GetInt("SFXEnabled", 1) == 1;
        bool musicEnabled = PlayerPrefs.GetInt("MusicEnabled", 1) == 1;
        bool vibrationEnabled = PlayerPrefs.GetInt("VibrationEnabled", 1) == 1;

        if (sfxToggle != null) sfxToggle.SetIsOnWithoutNotify(sfxEnabled);
        if (musicToggle != null) musicToggle.SetIsOnWithoutNotify(musicEnabled);
        if (vibrationToggle != null) vibrationToggle.SetIsOnWithoutNotify(vibrationEnabled);
        if (reduceMotionToggle != null) reduceMotionToggle.SetIsOnWithoutNotify(UIMotion.ReduceMotion);

        if (normalSpeedSlider != null) UpdateNormalSpeedText(normalSpeedSlider.value);
        else UpdateNormalSpeedText(PlayerPrefs.GetFloat("NormalMovementSpeed", _playerMovement != null ? _playerMovement.MovementSpeed : 8f));

        if (inspectorSpeedSlider != null) UpdateInspectorSpeedText(inspectorSpeedSlider.value);
        else UpdateInspectorSpeedText(PlayerPrefs.GetFloat("InspectorMovementSpeed", _playerMovement != null ? _playerMovement.inspectorModeSpeed : 25f));

        if (lookAroundSpeedSlider != null) UpdateLookAroundSpeedText(lookAroundSpeedSlider.value);
        else UpdateLookAroundSpeedText(PlayerPrefs.GetFloat("LookAroundSpeed", 1f));
    }

    public void TriggerVibrationFeedback()
    {
        bool vibrationEnabled = PlayerPrefs.GetInt("VibrationEnabled", 1) == 1;
        if (vibrationEnabled)
        {
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }
    }

    /// <summary>
    /// Sounds a switch flipping. The two directions are separate clips so the player can hear
    /// which way a toggle went without looking at it.
    ///
    /// The SFX toggle is the exception: turning sound off cannot announce itself (the mute has
    /// already landed by the time this runs), so only the on-direction is audible there, and it
    /// doubles as the confirmation that sound is back.
    /// </summary>
    private static void PlayToggleSound(bool enabled)
        => GameSfx.Play(enabled ? SoundEffect.ToggleOn : SoundEffect.ToggleOff);

    private float _settingsOpenedAt = -1f;

    public void OpenSettings()
    {
        _settingsOpenedAt = Time.realtimeSinceStartup;
        GameEvents.Design(new[] { "Settings", "Opened" });
        _isAnimating = false; // An open interrupts a close in progress; AnimateOpen kills its sequence
        GameSfx.Play(SoundEffect.PanelOpen);
        if (darkBg != null)
        {
            darkBg.alpha = 0f; // Set alpha to 0 first to prevent any visual flickering before the fade starts
        }
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            // This component lives on the always-active holder, so its OnEnable does not fire when
            // only the child panel switches on: run the entrance here, or the dim stays at the 0
            // alpha set above and nothing animates in.
            if (settingsPanel != gameObject && isActiveAndEnabled) AnimateOpen();
        }
        else
        {
            gameObject.SetActive(true);
        }
    }

    public void CloseSettings()
    {
        if (_isAnimating) return;
        _isAnimating = true;

        if (_settingsOpenedAt >= 0f)
        {
            GameEvents.FeatureTime("Settings", Time.realtimeSinceStartup - _settingsOpenedAt);
            _settingsOpenedAt = -1f;
        }

        GameSfx.Play(SoundEffect.PanelClose);

        GameObject panel = settingsPanel != null ? settingsPanel : gameObject;
        if (panel.activeInHierarchy) AnimateClose(FinishClose);
        else FinishClose();
    }

    private void FinishClose()
    {
        _isAnimating = false;
        
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
