using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI controller for the Move to Unlocked Area button.
/// Attach this to any GameObject in the garden canvas.
///
/// Pressing the button walks the player, along the garden paths, to the unlocked area nearest to them
/// by road (not as the crow flies) — skipping the one they are already standing in — and stops at the
/// heart of it. The walk stops the moment the player moves the joystick, drags to look around, enters
/// Inspector Mode, or presses Return to Base. Pressing this button again mid-walk stops it too.
///
/// Assign the button in the inspector. Optionally assign:
/// - A label (TMP or legacy Text) that reads "Go to Unlocked Area" / "Stop"
/// - A GameObject shown only while walking (e.g. a "Walking..." banner)
/// - A PlayerMovement reference (auto-finds if null)
/// </summary>
public class MoveToUnlockedAreaUI : MonoBehaviour
{
    private const string LabelKey = "interaction.move_to_unlocked_area";
    private const string StopKey = "interaction.auto_walk_stop";
    private const string ArrivedKey = "interaction.auto_walk_arrived_area";
    private const string AlreadyHereKey = "interaction.auto_walk_already_in_area";
    private const string NoPathKey = "interaction.auto_walk_no_path";
    private const string BlockedKey = "interaction.auto_walk_blocked";

    [Header("Required")]
    [Tooltip("The button that starts (and, while walking, stops) the walk to the nearest unlocked area.")]
    public Button moveButton;

    [Header("Button Visuals")]
    [Tooltip("TMP label on the button. Preferred: shaped correctly for Arabic and Bengali.")]
    public TMP_Text buttonLabel;
    [Tooltip("Legacy Text label on the button, if it uses UI.Text instead of TMP.")]
    public Text legacyButtonText;
    [Tooltip("Shown while the player is walking to an unlocked area. Hidden otherwise.")]
    public GameObject activeWalkLabel;

    [Header("Feedback")]
    [Tooltip("Show a toast naming the area when the player arrives.")]
    public bool announceArrival = true;

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
            Debug.LogWarning("[MoveToUnlockedAreaUI] PlayerMovement not found in scene. Move to Unlocked Area button will not work.");
        }

        _walker = PlayerAutoWalker.GetOrCreate(playerMovement);

        if (_walker != null)
        {
            _walker.WalkStarted += OnWalkStarted;
            _walker.WalkEnded += OnWalkEnded;
        }

        if (moveButton != null)
        {
            moveButton.onClick.AddListener(OnMoveClicked);
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

    private void OnMoveClicked()
    {
        if (_walker == null) return;

        // Play click sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
        }

        // Second press while already on the way stops the walk.
        if (_walker.Current == PlayerAutoWalker.Destination.UnlockedArea)
        {
            _walker.Cancel();
            return;
        }

        _walker.WalkToNearestUnlockedArea();
    }

    // ─── Walker Callbacks ────────────────────────────────────────────

    private void OnWalkStarted(PlayerAutoWalker.Destination destination) => RefreshUI();

    private void OnWalkEnded(PlayerAutoWalker.Destination destination, PlayerAutoWalker.EndReason reason)
    {
        RefreshUI();

        if (destination != PlayerAutoWalker.Destination.UnlockedArea) return;

        switch (reason)
        {
            case PlayerAutoWalker.EndReason.Arrived:
                if (announceArrival) AnnounceArrival();
                break;

            case PlayerAutoWalker.EndReason.NoPath:
                // Nowhere to go because they are already standing in the only area they own.
                ShowToast(Localize(_walker.IsAlreadyInOnlyUnlockedArea() ? AlreadyHereKey : NoPathKey));
                break;

            case PlayerAutoWalker.EndReason.Blocked:
                ShowToast(Localize(BlockedKey));
                break;
        }
    }

    private void AnnounceArrival()
    {
        GardenAreaManager areas = GardenAreaManager.Instance;
        if (areas == null || areas.map == null || _walker.TargetArea == null) return;

        string areaName = areas.map.DisplayName(_walker.TargetArea);
        ShowToast(LocalizationManager.Instance != null
            ? LocalizationManager.Instance.Get(ArrivedKey, areaName)
            : areaName);
    }

    // ─── UI State Updates ────────────────────────────────────────────

    private void RefreshUI()
    {
        bool walking = _walker != null && _walker.Current == PlayerAutoWalker.Destination.UnlockedArea;
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

    private static void ShowToast(string message)
    {
        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast(message, Color.white);
        }
    }
}
