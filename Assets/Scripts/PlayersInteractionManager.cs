using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The kind of thing the crosshair is resting on. Each kind gets its own on-screen button, so the
/// prompt reads "open" in front of a treasure box and "answer" in front of an orb instead of one
/// button meaning three different things.
/// </summary>
public enum InteractionTargetType
{
    None,
    TreasureBox,
    QuestionOrb,
    PlacedItem
}

public class PlayersInteractionManager : MonoBehaviour
{
    public static PlayersInteractionManager Instance { get; private set; }

    [Header("Interaction Settings")]
    [Tooltip("Maximum distance at which the player can look at and interact with the treasure box.")]
    public float maxInteractionDistance = 10f;

    [Tooltip("Optional crosshair RectTransform to project raycast from. If null, projects from the screen center.")]
    public RectTransform crosshairRect;

    [Tooltip("Physics layers to include in the raycast check.")]
    public LayerMask interactionLayerMask = ~0; // Default to everything

    [Header("Placed Item Management")]
    [Tooltip("Let the player look at something they have placed and choose to move it or return it to " +
             "the Asset Store.")]
    public bool allowPlacedItemManagement = true;

    [Tooltip("Layers to search for placed items. Separate from the mask above because placed assets sit " +
             "on the default layer, not the treasure box / orb layers.")]
    public LayerMask placedItemLayerMask = ~0;

    [Header("Interaction Buttons")]
    [Tooltip("Shown while looking at a treasure box. Opens the box.")]
    public Button treasureBoxInteractButton;

    [Tooltip("Shown while looking at a question mark orb. Opens the quiz.")]
    public Button orbInteractButton;

    [Tooltip("Shown while looking at an asset the player has placed. Opens the move / return options.")]
    public Button placedItemInteractButton;

    [Tooltip("Used for any target kind left without a button of its own above. A scene wired to this one " +
             "button behaves exactly as it did before the per-kind buttons existed.")]
    public Button itemInteractButton;

    public TreasureBox currentTargetBox = null;
    public QuestionMarkOrb currentTargetOrb = null;
    public PlaceableItem currentTargetPlaceable = null;
    private Camera mainCam;

    private bool isInteracting = false;

    /// <summary>
    /// What the crosshair is on right now. A treasure box standing in front of a tree is still what the
    /// player means, so the order here is the priority order the raycasts already use.
    /// </summary>
    public InteractionTargetType CurrentTargetType
    {
        get
        {
            if (currentTargetBox != null) return InteractionTargetType.TreasureBox;
            if (currentTargetOrb != null) return InteractionTargetType.QuestionOrb;
            if (currentTargetPlaceable != null) return InteractionTargetType.PlacedItem;
            return InteractionTargetType.None;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogWarning("[PlayersInteractionManager] Main Camera not found. Please tag your camera as 'MainCamera'.");
        }

        // Every button starts hidden and knows the single thing it does. The shared button is the odd
        // one out: it has to work out at click time which of the three it is standing in for.
        RegisterButton(treasureBoxInteractButton, OnTreasureBoxButtonClicked);
        RegisterButton(orbInteractButton, OnOrbButtonClicked);
        RegisterButton(placedItemInteractButton, OnPlacedItemButtonClicked);
        RegisterButton(itemInteractButton, OnSharedButtonClicked);

        EnsurePlacedItemActionsUI();
    }

    private void OnDestroy()
    {
        UnregisterButton(treasureBoxInteractButton, OnTreasureBoxButtonClicked);
        UnregisterButton(orbInteractButton, OnOrbButtonClicked);
        UnregisterButton(placedItemInteractButton, OnPlacedItemButtonClicked);
        UnregisterButton(itemInteractButton, OnSharedButtonClicked);

        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Hides a button and hooks up what it does. Assigning the same button to two fields — the shared
    /// button also left in the treasure box slot, say — would otherwise fire its interaction twice.
    /// </summary>
    private void RegisterButton(Button button, UnityAction handler)
    {
        if (button == null) return;

        button.gameObject.SetActive(false);
        button.onClick.RemoveListener(handler);
        button.onClick.AddListener(handler);
    }

    private void UnregisterButton(Button button, UnityAction handler)
    {
        if (button == null) return;
        button.onClick.RemoveListener(handler);
    }

    /// <summary>
    /// Makes sure something in the scene can show the relocate / return options. The UI builds itself
    /// when it has no references, so adding the component is all it takes — but a designed panel already
    /// placed in the scene wins, exactly like <c>JannahGardenManager</c> does with the tutorial.
    /// </summary>
    private void EnsurePlacedItemActionsUI()
    {
        if (!allowPlacedItemManagement) return;
        if (PlacedItemActionsUI.Instance != null) return;

        // Instance is only assigned from Awake, so one sitting under a disabled parent has not
        // registered yet — include inactive objects in the search before adding another.
        if (FindFirstObjectByType<PlacedItemActionsUI>(FindObjectsInactive.Include) != null) return;

        gameObject.AddComponent<PlacedItemActionsUI>();
    }

    // ─── Button handlers ──────────────────────────────────────────────────────

    private void OnTreasureBoxButtonClicked() => BeginInteraction(InteractionTargetType.TreasureBox);

    private void OnOrbButtonClicked() => BeginInteraction(InteractionTargetType.QuestionOrb);

    private void OnPlacedItemButtonClicked() => BeginInteraction(InteractionTargetType.PlacedItem);

    /// <summary>The fallback button, standing in for whichever kind has no button of its own.</summary>
    private void OnSharedButtonClicked() => BeginInteraction(CurrentTargetType);

    /// <summary>
    /// Runs the interaction for <paramref name="type"/> against whatever is currently targeted. The
    /// buttons are hidden first: the panel that opens is the player's next step, not another prompt.
    /// </summary>
    private void BeginInteraction(InteractionTargetType type)
    {
        if (type == InteractionTargetType.None) return;

        isInteracting = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ItemInteract);
        HideAllButtons();

        switch (type)
        {
            case InteractionTargetType.TreasureBox:
                OpenTargetedTreasureBox();
                break;

            case InteractionTargetType.QuestionOrb:
                if (currentTargetOrb != null) currentTargetOrb.OpenQuiz();
                break;

            case InteractionTargetType.PlacedItem:
                if (currentTargetPlaceable != null && PlacedItemActionsUI.Instance != null)
                {
                    PlacedItemActionsUI.Instance.Show(currentTargetPlaceable);
                }
                break;
        }
    }

    private void OpenTargetedTreasureBox()
    {
        if (currentTargetBox == null || TreasureBoxManager.Instance == null) return;

        TreasureBoxTier tier = currentTargetBox.tier;
        int slot = currentTargetBox.slotIndex;

        if (TreasureBoxConfirmationPanel.Instance != null)
        {
            TreasureBoxConfirmationPanel.Instance.Show(tier, slot);
        }
        else if (AdsManager.Instance != null)
        {
            AdsManager.Instance.ShowRewardedAd(() =>
            {
                if (TreasureBoxManager.Instance != null)
                {
                    TreasureBoxManager.Instance.TryOpenBox(tier, slot);
                }
            }, "treasure_box");
        }
        else
        {
            TreasureBoxManager.Instance.TryOpenBox(tier, slot);
        }
    }

    private void Update()
    {
        if (mainCam == null)
        {
            mainCam = Camera.main;
            if (mainCam == null) return;
        }

        PerformInteractionRaycast();
        DetectOrbClick();
    }

    /// <summary>
    /// Casts a ray from the camera through the crosshair (or center screen) to detect TreasureBox colliders.
    /// </summary>
    private void PerformInteractionRaycast()
    {
        Ray ray;
        if (crosshairRect != null)
        {
            ray = mainCam.ScreenPointToRay(crosshairRect.position);
        }
        else
        {
            // Fallback to center screen viewport ray
            ray = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        }

        RaycastHit hit;
        TreasureBox detectedBox = null;
        QuestionMarkOrb detectedOrb = null;
        GameObject hitObject = null;

        bool didHit = Physics.Raycast(ray, out hit, maxInteractionDistance, interactionLayerMask);

        // Draw the ray in the Editor/Scene view (and Game view if Gizmos are enabled)
        // Colors the ray green when it hits an object, and red when it hits nothing
        Color rayColor = didHit ? Color.green : Color.red;
        Debug.DrawRay(ray.origin, ray.direction * maxInteractionDistance, rayColor);

        // Perform raycast check
        if (didHit)
        {
            hitObject = hit.collider.gameObject;

            // Attempt to find the TreasureBox component on the hit object or its parent hierarchies
            detectedBox = hit.collider.GetComponent<TreasureBox>();
            if (detectedBox == null)
            {
                detectedBox = hit.collider.GetComponentInParent<TreasureBox>();
            }


            // Also check for QuestionMarkOrb
            detectedOrb = hit.collider.GetComponent<QuestionMarkOrb>();
            if (detectedOrb == null)
            {
                detectedOrb = hit.collider.GetComponentInParent<QuestionMarkOrb>();
            }
        }

        // Handle highlighting transitions for TreasureBox
        if (detectedBox != currentTargetBox)
        {
            isInteracting = false;
            // Disable outline on the previous box
            if (currentTargetBox != null)
            {
                currentTargetBox.SetOutline(false);
            }

            // Update current target and enable outline on the new box
            currentTargetBox = detectedBox;
            if (currentTargetBox != null)
            {
                currentTargetBox.SetOutline(true);
            }
        }

        // Handle highlighting transitions for QuestionMarkOrb
        if (detectedOrb != currentTargetOrb)
        {
            isInteracting = false;
            if (currentTargetOrb != null)
            {
                currentTargetOrb.SetFocus(false);
            }

            currentTargetOrb = detectedOrb;
            if (currentTargetOrb != null)
            {
                currentTargetOrb.SetFocus(true);
            }
        }

        // Placed assets are the lowest-priority target: a treasure box standing in front of a tree is
        // still what the player means.
        PlaceableItem detectedPlaceable = (currentTargetBox == null && currentTargetOrb == null)
            ? FindTargetedPlacedItem(ray)
            : null;

        if (detectedPlaceable != currentTargetPlaceable)
        {
            isInteracting = false;
            if (currentTargetPlaceable != null)
            {
                currentTargetPlaceable.SetHighlight(false);
            }

            currentTargetPlaceable = detectedPlaceable;
            if (currentTargetPlaceable != null)
            {
                currentTargetPlaceable.SetHighlight(true);
            }
        }

        UpdateInteractionButtons();
    }

    /// <summary>
    /// Shows the one button that belongs to whatever is targeted, and hides the rest. Only ever one is
    /// on screen, which is why they can all share the same spot on the canvas.
    /// </summary>
    private void UpdateInteractionButtons()
    {
        InteractionTargetType target = CurrentTargetType;

        // Nothing to prompt while the interaction just fired, or while a toast is using the screen.
        if (isInteracting) target = InteractionTargetType.None;
        if (ToastMessageManager.Instance != null && ToastMessageManager.Instance.IsShowing)
        {
            target = InteractionTargetType.None;
        }

        Button wanted = GetButtonFor(target);

        ShowOnly(treasureBoxInteractButton, wanted);
        ShowOnly(orbInteractButton, wanted);
        ShowOnly(placedItemInteractButton, wanted);
        ShowOnly(itemInteractButton, wanted);
    }

    /// <summary>
    /// The button for a target kind, falling back to the shared one so a scene that has not been split
    /// into per-kind buttons — or has only had some of them wired — still prompts the player.
    /// </summary>
    private Button GetButtonFor(InteractionTargetType type)
    {
        switch (type)
        {
            case InteractionTargetType.TreasureBox:
                return treasureBoxInteractButton != null ? treasureBoxInteractButton : itemInteractButton;

            case InteractionTargetType.QuestionOrb:
                return orbInteractButton != null ? orbInteractButton : itemInteractButton;

            case InteractionTargetType.PlacedItem:
                return placedItemInteractButton != null ? placedItemInteractButton : itemInteractButton;

            default:
                return null;
        }
    }

    private static void ShowOnly(Button button, Button wanted)
    {
        if (button == null) return;

        bool shouldShow = button == wanted;
        if (button.gameObject.activeSelf != shouldShow)
        {
            button.gameObject.SetActive(shouldShow);
        }
    }

    private void HideAllButtons()
    {
        ShowOnly(treasureBoxInteractButton, null);
        ShowOnly(orbInteractButton, null);
        ShowOnly(placedItemInteractButton, null);
        ShowOnly(itemInteractButton, null);
    }

    /// <summary>
    /// Looks for a placed asset under the crosshair. Runs as its own raycast because placed items sit on
    /// the default layer, which <see cref="interactionLayerMask"/> deliberately excludes.
    /// </summary>
    private PlaceableItem FindTargetedPlacedItem(Ray ray)
    {
        if (!allowPlacedItemManagement) return null;

        // Nothing to offer while an item is already following the crosshair, or while the options for
        // another item are open.
        if (ItemPlacementManager.Instance != null && ItemPlacementManager.Instance.IsPlacing) return null;
        if (PlacedItemActionsUI.Instance != null && PlacedItemActionsUI.Instance.IsOpen) return null;

        if (!Physics.Raycast(ray, out RaycastHit hit, maxInteractionDistance, placedItemLayerMask,
                             QueryTriggerInteraction.Ignore))
        {
            return null;
        }

        PlaceableItem placeable = hit.collider.GetComponent<PlaceableItem>();
        if (placeable == null)
        {
            placeable = hit.collider.GetComponentInParent<PlaceableItem>();
        }

        // A ghost being positioned has its PlaceableItem disabled — it is not a target.
        return (placeable != null && placeable.enabled) ? placeable : null;
    }

    private void DetectOrbClick()
    {
        if (isInteracting) return;
        if (ToastMessageManager.Instance != null && ToastMessageManager.Instance.IsShowing) return;

        bool clicked = false;
        Vector2 pointerPosition = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
        {
            clicked = true;
            pointerPosition = Pointer.current.position.ReadValue();
        }
#else
        if (Input.GetMouseButtonDown(0))
        {
            clicked = true;
            pointerPosition = Input.mousePosition;
        }
#endif

        if (clicked)
        {
            // Avoid clicking through UI elements
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                {
                    return;
                }
#if ENABLE_INPUT_SYSTEM
                if (Touchscreen.current != null && Touchscreen.current.touches.Count > 0)
                {
                    int touchId = Touchscreen.current.touches[0].touchId.ReadValue();
                    if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(touchId))
                    {
                        return;
                    }
                }
#else
                if (Input.touchCount > 0)
                {
                    int touchId = Input.GetTouch(0).fingerId;
                    if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(touchId))
                    {
                        return;
                    }
                }
#endif
            }

            Ray ray = mainCam.ScreenPointToRay(pointerPosition);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, maxInteractionDistance, interactionLayerMask))
            {
                QuestionMarkOrb clickedOrb = hit.collider.GetComponent<QuestionMarkOrb>();
                if (clickedOrb == null)
                {
                    clickedOrb = hit.collider.GetComponentInParent<QuestionMarkOrb>();
                }

                if (clickedOrb != null)
                {
                    // Clicked on the orb! Set it as the current target and trigger interaction.
                    currentTargetBox = null;
                    currentTargetOrb = clickedOrb;
                    BeginInteraction(InteractionTargetType.QuestionOrb);
                }
            }
        }
    }

    private void OnDisable()
    {
        // Turn off any active outlines when the script is disabled
        if (currentTargetBox != null)
        {
            currentTargetBox.SetOutline(false);
            currentTargetBox = null;
        }

        if (currentTargetOrb != null)
        {
            currentTargetOrb.SetFocus(false);
            currentTargetOrb = null;
        }

        if (currentTargetPlaceable != null)
        {
            currentTargetPlaceable.SetHighlight(false);
            currentTargetPlaceable = null;
        }

        HideAllButtons();
    }
}
