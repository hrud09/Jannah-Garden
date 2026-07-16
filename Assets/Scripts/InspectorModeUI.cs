using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// UI controller for the Inspector Mode toggle button.
/// Attach this to any GameObject in the Outer Garden canvas.
/// 
/// Assign the toggle button in the inspector. Optionally assign:
/// - Up/Down buttons for mobile vertical movement
/// - A label to display "INSPECTOR MODE" when active
/// - A PlayerMovement reference (auto-finds if null)
/// </summary>
public class InspectorModeUI : MonoBehaviour
{
    [Header("Required")]
    [Tooltip("The button that toggles Inspector Mode on/off.")]
    public Button toggleButton;

    [Header("Button Visuals")]
    [Tooltip("Text component on the toggle button to update its label.")]
    public Text toggleButtonText;
    [Tooltip("Label shown when Inspector Mode is active (e.g. a floating 'INSPECTOR MODE' text). Hidden when inactive.")]
    public GameObject activeModeLabel;

    [Header("Optional — Button Labels")]
    [Tooltip("Text to display when Inspector Mode is OFF (clicking will turn it ON).")]
    public string normalModeText = "Inspector Mode: OFF";
    [Tooltip("Text to display when Inspector Mode is ON (clicking will turn it OFF).")]
    public string inspectorModeText = "Inspector Mode: ON";

    [Header("Optional — Mobile Vertical Controls")]
    [Tooltip("Hold to fly up in Inspector Mode. Hidden when not in Inspector Mode.")]
    public Button flyUpButton;
    [Tooltip("Hold to fly down in Inspector Mode. Hidden when not in Inspector Mode.")]
    public Button flyDownButton;

    [Header("Player Reference (Auto-detected)")]
    [Tooltip("Reference to the PlayerMovement component. Auto-detected if null.")]
    public IdyllicFantasyNature.PlayerMovement playerMovement;

    // Camera reference for pitch unlock
    private IdyllicFantasyNature.CameraMovement _cameraMovement;

    private void Start()
    {
        // Auto-find PlayerMovement if not assigned
        if (playerMovement == null)
        {
            playerMovement = FindObjectOfType<IdyllicFantasyNature.PlayerMovement>();
        }

        if (playerMovement == null)
        {
            Debug.LogWarning("[InspectorModeUI] PlayerMovement not found in scene. Inspector Mode button will not work.");
        }

        // Auto-find CameraMovement
        _cameraMovement = FindObjectOfType<IdyllicFantasyNature.CameraMovement>();

        // Setup toggle button
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(OnToggleClicked);
        }

        // Setup vertical control buttons (mobile)
        SetupVerticalButton(flyUpButton, 1f);
        SetupVerticalButton(flyDownButton, -1f);

        // Subscribe to mode changes for UI updates
        if (playerMovement != null)
        {
            playerMovement.OnInspectorModeChanged += OnModeChanged;
        }

        // Initialize UI state
        UpdateUI(false);
    }

    private void OnDestroy()
    {
        if (playerMovement != null)
        {
            playerMovement.OnInspectorModeChanged -= OnModeChanged;
        }
    }

    // ─── Toggle Button ───────────────────────────────────────────────

    private void OnToggleClicked()
    {
        if (playerMovement == null) return;

        // Play click sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
        }

        playerMovement.ToggleInspectorMode();
    }

    // ─── Mode Changed Callback ───────────────────────────────────────

    private void OnModeChanged(bool isInspectorMode)
    {
        UpdateUI(isInspectorMode);

        // Unlock/lock camera pitch
        if (_cameraMovement != null)
        {
            _cameraMovement.unlockFullPitch = isInspectorMode;
        }
    }

    // ─── UI State Updates ────────────────────────────────────────────

    private void UpdateUI(bool isInspectorMode)
    {
        // Update button text
        if (toggleButtonText != null)
        {
            toggleButtonText.text = isInspectorMode ? inspectorModeText : normalModeText;
        }

        // Show/hide active mode label
        if (activeModeLabel != null)
        {
            activeModeLabel.SetActive(isInspectorMode);
        }

        // Show/hide vertical control buttons
        if (flyUpButton != null)
        {
            flyUpButton.gameObject.SetActive(isInspectorMode);
        }
        if (flyDownButton != null)
        {
            flyDownButton.gameObject.SetActive(isInspectorMode);
        }
    }

    // ─── Vertical Button Helpers (Hold to move up/down) ──────────────

    private void SetupVerticalButton(Button button, float direction)
    {
        if (button == null) return;

        // Hide by default
        button.gameObject.SetActive(false);

        // Add EventTrigger for hold-to-move behavior
        EventTrigger trigger = button.gameObject.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = button.gameObject.AddComponent<EventTrigger>();
        }

        // PointerDown → start moving
        EventTrigger.Entry pointerDown = new EventTrigger.Entry();
        pointerDown.eventID = EventTriggerType.PointerDown;
        pointerDown.callback.AddListener((data) =>
        {
            if (playerMovement != null)
            {
                playerMovement.SetInspectorVerticalInput(direction);
            }
        });
        trigger.triggers.Add(pointerDown);

        // PointerUp → stop moving
        EventTrigger.Entry pointerUp = new EventTrigger.Entry();
        pointerUp.eventID = EventTriggerType.PointerUp;
        pointerUp.callback.AddListener((data) =>
        {
            if (playerMovement != null)
            {
                playerMovement.SetInspectorVerticalInput(0f);
            }
        });
        trigger.triggers.Add(pointerUp);

        // PointerExit → stop moving (safety)
        EventTrigger.Entry pointerExit = new EventTrigger.Entry();
        pointerExit.eventID = EventTriggerType.PointerExit;
        pointerExit.callback.AddListener((data) =>
        {
            if (playerMovement != null)
            {
                playerMovement.SetInspectorVerticalInput(0f);
            }
        });
        trigger.triggers.Add(pointerExit);
    }
}
