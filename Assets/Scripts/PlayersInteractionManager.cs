using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

    public TreasureBox currentTargetBox = null;
    public QuestionMarkOrb currentTargetOrb = null;
    public PlaceableItem currentTargetPlaceable = null;
    private Camera mainCam;

    public Button itemInteractButton;
    private bool isInteracting = false;

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

        // Disable the interaction button by default
        if (itemInteractButton != null)
        {
            itemInteractButton.gameObject.SetActive(false);
            itemInteractButton.onClick.AddListener(OnInteractButtonClicked);
        }

        EnsurePlacedItemActionsUI();
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

    private void OnInteractButtonClicked()
    {
        isInteracting = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ItemInteract);
        if (itemInteractButton != null)
        {
            itemInteractButton.gameObject.SetActive(false);
        }

        if (currentTargetBox != null && TreasureBoxManager.Instance != null)
        {
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
        else if (currentTargetOrb != null)
        {
            currentTargetOrb.OpenQuiz();
        }
        else if (currentTargetPlaceable != null && PlacedItemActionsUI.Instance != null)
        {
            PlacedItemActionsUI.Instance.Show(currentTargetPlaceable);
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

        // Activate the itemInteractButton if a treasure box, orb or placed item is targeted
        if (itemInteractButton != null)
        {
            bool shouldShow = (currentTargetBox != null || currentTargetOrb != null || currentTargetPlaceable != null)
                              && !isInteracting;
            if (ToastMessageManager.Instance != null && ToastMessageManager.Instance.IsShowing)
            {
                shouldShow = false;
            }
            itemInteractButton.gameObject.SetActive(shouldShow);
        }
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
                    OnInteractButtonClicked();
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

        if (itemInteractButton != null)
        {
            itemInteractButton.gameObject.SetActive(false);
        }
    }
}
