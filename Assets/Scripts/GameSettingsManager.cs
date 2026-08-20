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
        float normalSpeed = PlayerPrefs.GetFloat("NormalMovementSpeed", 8f);
        float inspectorSpeed = PlayerPrefs.GetFloat("InspectorMovementSpeed", 25f);

        // Apply audio settings to AudioManager
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.IsSfxMuted = !sfxEnabled;
            AudioManager.Instance.IsMusicMuted = !musicEnabled;
        }

        // Apply movement speed settings to PlayerMovement
        if (_playerMovement == null)
        {
            _playerMovement = FindObjectOfType<IdyllicFantasyNature.PlayerMovement>();
        }
        if (_playerMovement != null)
        {
            _playerMovement.SetMovementSpeed(normalSpeed);
            _playerMovement.SetInspectorModeSpeed(inspectorSpeed);
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

    private void AnimateOpen()
    {
        _isAnimating = true;

        // Reset scales and alpha
        if (settingsOptions != null && settingsOptions.Length > 0)
        {
            for (int i = 0; i < settingsOptions.Length; i++)
            {
                if (settingsOptions[i] != null)
                {
                    settingsOptions[i].localScale = Vector3.zero;
                }
            }
        }

        if (mainBg != null)
        {
            mainBg.localScale = Vector3.one;
        }

        if (darkBg != null)
        {
            darkBg.alpha = 0f;
            darkBg.DOKill();
            darkBg.DOFade(1f, fadeDuration)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    _isAnimating = false;
                    AnimateOptions();
                });
        }
        else
        {
            _isAnimating = false;
            AnimateOptions();
        }
    }

    private void AnimateOptions()
    {
        if (settingsOptions != null && settingsOptions.Length > 0)
        {
            for (int i = 0; i < settingsOptions.Length; i++)
            {
                var option = settingsOptions[i];
                if (option == null) continue;

                option.DOKill();
                option.DOScale(Vector3.one, itemScaleDuration)
                    .SetEase(Ease.OutBack)
                    .SetDelay(i * itemStaggerDelay)
                    .SetUpdate(true);
            }
        }
    }

    // Direct Set Methods
    public void SetSFX(bool enabled)
    {
        PlayerPrefs.SetInt("SFXEnabled", enabled ? 1 : 0);
        PlayerPrefs.Save();
        
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.IsSfxMuted = !enabled;
        }

        PlayClickSound();
        UpdateVisuals();
    }

    public void SetMusic(bool enabled)
    {
        PlayerPrefs.SetInt("MusicEnabled", enabled ? 1 : 0);
        PlayerPrefs.Save();
        
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.IsMusicMuted = !enabled;
        }

        PlayClickSound();
        UpdateVisuals();
    }

    public void SetVibration(bool enabled)
    {
        PlayerPrefs.SetInt("VibrationEnabled", enabled ? 1 : 0);
        PlayerPrefs.Save();

        PlayClickSound();
        TriggerVibrationFeedback();
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

        if (normalSpeedSlider != null) UpdateNormalSpeedText(normalSpeedSlider.value);
        else UpdateNormalSpeedText(PlayerPrefs.GetFloat("NormalMovementSpeed", 8f));

        if (inspectorSpeedSlider != null) UpdateInspectorSpeedText(inspectorSpeedSlider.value);
        else UpdateInspectorSpeedText(PlayerPrefs.GetFloat("InspectorMovementSpeed", 25f));

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

    private void PlayClickSound()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
        }
    }

    public void OpenSettings()
    {
        _isAnimating = false; // Reset animation state to ensure the show animation always plays
        PlayClickSound();
        if (darkBg != null)
        {
            darkBg.alpha = 0f; // Set alpha to 0 first to prevent any visual flickering before the fade starts
        }
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
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

        PlayClickSound();

        // Stagger hide options
        if (settingsOptions != null && settingsOptions.Length > 0)
        {
            for (int i = 0; i < settingsOptions.Length; i++)
            {
                var option = settingsOptions[i];
                if (option == null) continue;

                option.DOKill();
                option.DOScale(Vector3.zero, itemScaleDuration)
                    .SetEase(Ease.InBack)
                    .SetUpdate(true);
            }
        }

        if (darkBg != null)
        {
            darkBg.DOKill();
            darkBg.DOFade(0f, fadeDuration)
                .SetUpdate(true)
                .OnComplete(FinishClose);
        }
        else
        {
            FinishClose();
        }
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
