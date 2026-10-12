using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI controller for the Return to Base button.
/// Attach this to any GameObject in the garden canvas.
///
/// Pressing the button walks the player back to where they started — along the garden paths — and
/// turns them to face the way they started. The walk stops the moment the player moves the joystick,
/// drags to look around, enters Inspector Mode, or presses Move to Unlocked Area. Pressing this button
/// again mid-walk stops it too.
///
/// Assign the button in the inspector. Optionally assign:
/// - A label (TMP or legacy Text) that reads "Return to Base" / "Stop"
/// - A GameObject shown only while walking (e.g. a "Returning..." banner)
/// - A base marker Transform; otherwise base is wherever the player stood when the scene started
/// - A PlayerMovement reference (auto-finds if null)
/// </summary>
public class ReturnToBaseUI : MonoBehaviour
{
    private const string LabelKey = "interaction.return_to_base";
    private const string StopKey = "interaction.auto_walk_stop";
    private const string NoPathKey = "interaction.auto_walk_no_path";
    private const string BlockedKey = "interaction.auto_walk_blocked";

    [Header("Required")]
    [Tooltip("The button that starts (and, while walking, stops) the walk back to base.")]
    public Button returnButton;

    [Header("Button Visuals")]
    [Tooltip("TMP label on the button. Preferred: shaped correctly for Arabic and Bengali.")]
    public TMP_Text buttonLabel;
    [Tooltip("Legacy Text label on the button, if it uses UI.Text instead of TMP.")]
    public Text legacyButtonText;
    [Tooltip("Shown while the player is walking back to base. Hidden otherwise.")]
    public GameObject activeWalkLabel;

    [Header("Base (Optional)")]
    [Tooltip("Where base is, and which way to face there. Empty: wherever the player stood, facing " +
             "the way they faced, when the scene started.")]
    public Transform baseMarker;

    [Header("Player Reference (Auto-detected)")]
    [Tooltip("Reference to the PlayerMovement component. Auto-detected if null.")]
    public IdyllicFantasyNature.PlayerMovement playerMovement;

    private PlayerAutoWalker _walker;

    private void Start()
    {
        // Auto-find PlayerMovement if not assigned
        if (playerMovement == null)
        {
            playerMovement = FindObjectOfType<IdyllicFantasyNature.PlayerMovement>();
        }

        if (playerMovement == null)
        {
            Debug.LogWarning("[ReturnToBaseUI] PlayerMovement not found in scene. Return to Base button will not work.");
        }

        _walker = PlayerAutoWalker.GetOrCreate(playerMovement);

        if (_walker != null)
        {
            if (baseMarker != null) _walker.SetBase(baseMarker);

            _walker.WalkStarted += OnWalkStarted;
            _walker.WalkEnded += OnWalkEnded;
        }

        if (returnButton != null)
        {
            returnButton.onClick.AddListener(OnReturnClicked);
        }

        LocalizationManager.OnLocaleChanged += RefreshUI;

        RefreshUI();
    }

    private void OnDestroy()
    {
        if (_walker != null)
        {
            _walker.WalkStarted -= OnWalkStarted;
            _walker.WalkEnded -= OnWalkEnded;
        }

        LocalizationManager.OnLocaleChanged -= RefreshUI;
    }

    // ─── Button ──────────────────────────────────────────────────────

    private void OnReturnClicked()
    {
        if (_walker == null) return;

        // Play click sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
        }

        // Second press while already heading home stops the walk.
        if (_walker.Current == PlayerAutoWalker.Destination.Base)
        {
            _walker.Cancel();
            return;
        }

        _walker.WalkToBase();
    }

    // ─── Walker Callbacks ────────────────────────────────────────────

    private void OnWalkStarted(PlayerAutoWalker.Destination destination) => RefreshUI();

    private void OnWalkEnded(PlayerAutoWalker.Destination destination, PlayerAutoWalker.EndReason reason)
    {
        RefreshUI();

        if (destination != PlayerAutoWalker.Destination.Base) return;

        if (reason == PlayerAutoWalker.EndReason.NoPath) ShowToast(NoPathKey);
        else if (reason == PlayerAutoWalker.EndReason.Blocked) ShowToast(BlockedKey);
    }

    // ─── UI State Updates ────────────────────────────────────────────

    private void RefreshUI()
    {
        bool walking = _walker != null && _walker.Current == PlayerAutoWalker.Destination.Base;
        string text = Localize(walking ? StopKey : LabelKey);

        if (buttonLabel != null)
        {
            LocalizedRendering.SetText(buttonLabel, text);
        }

        if (legacyButtonText != null)
        {
            legacyButtonText.text = text;
        }

        if (activeWalkLabel != null)
        {
            activeWalkLabel.SetActive(walking);
        }
    }

    private static string Localize(string key) =>
        LocalizationManager.Instance != null ? LocalizationManager.Instance.Get(key) : key;

    private static void ShowToast(string key)
    {
        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast(Localize(key), Color.white);
        }
    }
}
