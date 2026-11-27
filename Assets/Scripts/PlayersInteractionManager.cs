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
    PlacedItem,
    Rideable
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

    [Header("Riding")]
    [Tooltip("Offer to ride the camels (and any other GrazingAnimal marked rideable) when the player is " +
             "close to one and facing it.")]
    public bool allowRiding = true;

    [Tooltip("Shown near a rideable animal. Climbs on. Left empty, one is copied from the other " +
             "interaction buttons at start so it matches them.")]
    public Button rideInteractButton;

    [Tooltip("Shown while riding. Climbs off. Left empty, one is copied from the other interaction " +
             "buttons at start and placed just above them.")]
    public Button dismountButton;

    [Tooltip("Shown beside Dismount while riding. Watch an ad, then the animal tours the garden roads and the " +
             "jungle on its own for a few minutes; pressed again, stops the tour. Left empty, one is copied " +
             "from the ad-carrying interaction button at start.")]
    public Button autoTravelButton;

    [Tooltip("Shown beside Dismount while riding a camel in the jungle. Starts the Jungle Orb Run. Left empty, " +
             "one is copied from the other interaction buttons at start.")]
    public Button orbRunButton;

    [Tooltip("Gap between the riding buttons in their row (canvas units).")]
    public float rideButtonSpacing = 24f;

    public TreasureBox currentTargetBox = null;
    public QuestionMarkOrb currentTargetOrb = null;
    public PlaceableItem currentTargetPlaceable = null;
    public GrazingAnimal currentTargetAnimal = null;
    private AnimalRider rider;
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
            if (currentTargetAnimal != null) return InteractionTargetType.Rideable;
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

        EnsureRideButtons();
        RegisterButton(rideInteractButton, OnRideButtonClicked);
        RegisterButton(dismountButton, OnDismountButtonClicked);
        RegisterButton(autoTravelButton, OnAutoTravelButtonClicked);
        RegisterButton(orbRunButton, OnOrbRunButtonClicked);

        EnsurePlacedItemActionsUI();
    }

    private void OnDestroy()
    {
        UnregisterButton(treasureBoxInteractButton, OnTreasureBoxButtonClicked);
        UnregisterButton(orbInteractButton, OnOrbButtonClicked);
        UnregisterButton(placedItemInteractButton, OnPlacedItemButtonClicked);
        UnregisterButton(itemInteractButton, OnSharedButtonClicked);
        UnregisterButton(rideInteractButton, OnRideButtonClicked);
        UnregisterButton(dismountButton, OnDismountButtonClicked);
        UnregisterButton(autoTravelButton, OnAutoTravelButtonClicked);
        UnregisterButton(orbRunButton, OnOrbRunButtonClicked);

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

    private void OnPlacedItemButtonClicked() => BeginInteraction(CurrentTargetType);

    /// <summary>The fallback button, standing in for whichever kind has no button of its own.</summary>
    private void OnSharedButtonClicked() => BeginInteraction(CurrentTargetType);

    private void OnRideButtonClicked() => BeginInteraction(InteractionTargetType.Rideable);

    private void OnDismountButtonClicked()
    {
        if (rider != null && rider.IsRiding) rider.Dismount();
    }

    /// <summary>
    /// Auto Travel: while a tour runs, stops it. Otherwise asks for an ad (it is a paid-for extra), then
    /// plans the tour from where the animal stands and hands it the reins.
    /// </summary>
    private void OnAutoTravelButtonClicked()
    {
        if (rider == null || !rider.IsRiding) return;
        RideAutoTravel travel = RideAutoTravel.GetOrCreate(rider);

        if (travel.IsTravelling)
        {
            travel.Stop();
            return;
        }

        GrazingAnimal animal = rider.Mount;
        int minutes = Mathf.Max(1, Mathf.RoundToInt(travel.duration / 60f));

        void StartTour()
        {
            if (rider == null || !rider.IsRiding) return;
            if (JungleOrbRun.Instance != null) JungleOrbRun.Instance.Cancel();
            if (travel.Begin(rider))
                RideFeatures.Toast(RideFeatures.Loc("auto_travel.started"), ToastTone.Success);
            else
                RideFeatures.Toast(RideFeatures.Loc("auto_travel.no_route"), ToastTone.Warning);
        }

        RidePromptPanel panel = RidePromptPanel.Instance;
        if (panel == null)
        {
            RideAdGate.ShowAd(earned => { if (earned) StartTour(); });
            return;
        }

        panel.Show(
            RideFeatures.Loc("auto_travel.title"),
            RideFeatures.Loc(animal != null && animal.canFly ? "auto_travel.body_fly" : "auto_travel.body", minutes),
            RideFeatures.Loc("auto_travel.note"),
            RideFeatures.Instance != null ? RideFeatures.Instance.IconFor(animal) : null,
            RideFeatures.Loc("ride.watch_ad"), true,
            () => RideAdGate.ShowAd(earned =>
            {
                if (earned) StartTour();
                else RideFeatures.Toast(RideFeatures.Loc("ride.ad_failed"), ToastTone.Warning);
            }),
            null, null,
            null);
    }

    private void OnOrbRunButtonClicked()
    {
        if (rider != null && JungleOrbRun.Instance != null) JungleOrbRun.Instance.Offer(rider);
    }

    // ─── Riding ───────────────────────────────────────────────────────────────

    /// <summary>The player's rider, added on first use. Null until a player exists.</summary>
    private AnimalRider ResolveRider()
    {
        if (rider != null) return rider;

        var movement = FindFirstObjectByType<IdyllicFantasyNature.PlayerMovement>();
        rider = AnimalRider.GetOrCreate(movement);
        return rider;
    }

    /// <summary>
    /// Fills in the Ride and Dismount buttons when the scene doesn't have its own, by copying an
    /// existing interaction button — same canvas, sprite and size, so they look like the rest. Ride
    /// shares the others' spot (only one of those shows at a time); Dismount sits just above, because
    /// it stays up while the player rides past orbs and treasure boxes and their buttons come and go.
    /// </summary>
    private void EnsureRideButtons()
    {
        if (!allowRiding) return;

        Button template = placedItemInteractButton != null ? placedItemInteractButton
            : treasureBoxInteractButton != null ? treasureBoxInteractButton
            : orbInteractButton != null ? orbInteractButton
            : itemInteractButton;
        if (template == null) return;

        if (rideInteractButton == null)
        {
            rideInteractButton = CopyButton(template, "Ride Button", "interaction.ride_button", "ride", 0f);
        }

        if (dismountButton == null)
        {
            float height = ((RectTransform)template.transform).rect.height;
            if (height <= 0f) height = ((RectTransform)template.transform).sizeDelta.y;
            dismountButton = CopyButton(template, "Dismount Button", "interaction.dismount_button", "dismount",
                                        height * 1.2f);
        }

        float step = ((RectTransform)template.transform).rect.height;
        if (step <= 0f) step = ((RectTransform)template.transform).sizeDelta.y;

        if (autoTravelButton == null)
        {
            // The treasure box's Open button carries the little TV that marks "this plays an ad".
            Button adTemplate = treasureBoxInteractButton != null ? treasureBoxInteractButton : template;
            autoTravelButton = CopyButton(adTemplate, "Auto Travel Button", "interaction.auto_travel_button",
                                          "auto travel", step * 1.2f);
            _autoTravelLabel = autoTravelButton.GetComponentInChildren<LocalizedText>(true);
            Transform adIcon = autoTravelButton.transform.Find("Ad Icon");
            _autoTravelAdIcon = adIcon != null ? adIcon.gameObject : null;
        }

        if (orbRunButton == null)
        {
            orbRunButton = CopyButton(template, "Orb Run Button", "interaction.orb_run_button", "orb run",
                                      step * 1.2f);
        }

        _rideRowBaseY = ((RectTransform)dismountButton.transform).anchoredPosition.y;
        _rideRowBaseX = ((RectTransform)template.transform).anchoredPosition.x;
    }

    private LocalizedText _autoTravelLabel;
    private GameObject _autoTravelAdIcon;
    private bool _autoTravelShowsStop;
    private float _rideRowBaseX, _rideRowBaseY;
    private readonly System.Collections.Generic.List<Button> _rideRow = new System.Collections.Generic.List<Button>(3);

    /// <summary>
    /// Lines up whichever riding buttons are showing (Orb Run, Dismount, Auto Travel) side by side,
    /// centred where Dismount sits, so none overlaps whatever else is on screen.
    /// </summary>
    private void LayoutRideRow()
    {
        _rideRow.Clear();
        if (orbRunButton != null && orbRunButton.gameObject.activeSelf) _rideRow.Add(orbRunButton);
        if (dismountButton != null && dismountButton.gameObject.activeSelf) _rideRow.Add(dismountButton);
        if (autoTravelButton != null && autoTravelButton.gameObject.activeSelf) _rideRow.Add(autoTravelButton);
        if (_rideRow.Count == 0) return;

        float total = -rideButtonSpacing;
        foreach (Button b in _rideRow) total += ((RectTransform)b.transform).rect.width + rideButtonSpacing;

        float x = _rideRowBaseX - total * 0.5f;
        foreach (Button b in _rideRow)
        {
            var rt = (RectTransform)b.transform;
            float w = rt.rect.width;
            rt.anchoredPosition = new Vector2(x + w * rt.pivot.x, _rideRowBaseY);
            x += w + rideButtonSpacing;
        }
    }

    /// <summary>Auto Travel's label reads "stop" while a tour runs, and loses its ad mark (stopping is free).</summary>
    private void UpdateAutoTravelLabel(bool travelling)
    {
        if (autoTravelButton == null || travelling == _autoTravelShowsStop) return;
        _autoTravelShowsStop = travelling;
        if (_autoTravelLabel != null)
        {
            _autoTravelLabel.SetKey(travelling ? "interaction.auto_travel_stop" : "interaction.auto_travel_button");
        }
        if (_autoTravelAdIcon != null) _autoTravelAdIcon.SetActive(!travelling);
    }

    private static Button CopyButton(Button template, string objectName, string labelKey, string fallbackLabel,
                                     float raiseBy)
    {
        GameObject copy = Instantiate(template.gameObject, template.transform.parent);
        copy.name = objectName;
        copy.SetActive(false);

        RectTransform rect = (RectTransform)copy.transform;
        rect.anchoredPosition += new Vector2(0f, raiseBy);

        Button button = copy.GetComponent<Button>();

        // Inspector-wired clicks belong to the interaction the template was made for, not this one.
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        LocalizedText localized = copy.GetComponentInChildren<LocalizedText>(true);
        if (localized != null)
        {
            localized.SetKey(labelKey);
        }
        else
        {
            TMPro.TMP_Text label = copy.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label != null) label.text = fallbackLabel;
        }

        return button;
    }

    /// <summary>The animal the Ride button should offer, when nothing more specific is targeted.</summary>
    private GrazingAnimal FindTargetedAnimal()
    {
        if (!allowRiding) return null;
        if (currentTargetBox != null || currentTargetOrb != null || currentTargetPlaceable != null) return null;
        if (ItemPlacementManager.Instance != null && ItemPlacementManager.Instance.IsPlacing) return null;

        AnimalRider r = ResolveRider();
        return r != null ? r.FindMountable() : null;
    }

    /// <summary>
    /// Runs the interaction for <paramref name="type"/> against whatever is currently targeted. The
    /// buttons are hidden first: the panel that opens is the player's next step, not another prompt.
    /// </summary>
    private void BeginInteraction(InteractionTargetType type)
    {
        if (type == InteractionTargetType.None) return;

        isInteracting = true;
        HideAllButtons();

        switch (type)
        {
            case InteractionTargetType.TreasureBox:
                // The box plays its own cue once the manager knows whether it is openable or still
                // on its timer, so nothing is sounded here.
                OpenTargetedTreasureBox();
                break;

            case InteractionTargetType.QuestionOrb:
                if (currentTargetOrb != null)
                {
                    GameSfx.PlayAt(SoundEffect.OrbCollect, currentTargetOrb.transform);
                    currentTargetOrb.OpenQuiz();
                }
                break;

            case InteractionTargetType.PlacedItem:
                if (currentTargetPlaceable != null && PlacedItemActionsUI.Instance != null)
                {
                    PlacedItemActionsUI.Instance.Show(currentTargetPlaceable);
                }
                break;

            case InteractionTargetType.Rideable:
                AnimalRider r = ResolveRider();
                if (r != null && currentTargetAnimal != null)
                {
                    GrazingAnimal animal = currentTargetAnimal;
                    // Every few rides ask for (or offer) an ad first; see RideAdGate for the cycle.
                    RideAdGate.Request(animal, () => r.TryMount(animal));
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
            return;
        }

        // No confirmation panel in the scene: open directly. TryOpenBox runs the rewarded-ad gate itself
        // through TreasureBoxManager.AdService, so this must not show an ad of its own — doing that was
        // the same double-ad mistake the confirmation panel used to make.
        TreasureBoxManager.Instance.TryOpenBox(tier, slot);
    }

    private void Update()
    {
        if (mainCam == null)
        {
            mainCam = Camera.main;
            if (mainCam == null) return;
        }

        // Target detection (two raycasts, parent-component walks, the mount search and the button
        // layout) at ~15 Hz: a 66 ms delay in a highlight appearing is invisible, the per-frame cost on a
        // phone is not. Taps are still handled every frame.
        if (Time.unscaledTime >= _nextTargetScan)
        {
            _nextTargetScan = Time.unscaledTime + TargetScanInterval;
            PerformInteractionRaycast();
        }
        DetectOrbClick();
    }

    private const float TargetScanInterval = 1f / 15f;
    private float _nextTargetScan;

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
#if UNITY_EDITOR
        Color rayColor = didHit ? Color.green : Color.red;
        Debug.DrawRay(ray.origin, ray.direction * maxInteractionDistance, rayColor);
#endif

        // Perform raycast check
        if (didHit)
        {
            hitObject = hit.collider.gameObject;

            // Attempt to find the TreasureBox component on the hit object or its parent hierarchies.
            // GetComponentInParent already checks the object itself, so it covers both cases.
            detectedBox = hit.collider.GetComponentInParent<TreasureBox>();

            // Also check for QuestionMarkOrb
            detectedOrb = hit.collider.GetComponentInParent<QuestionMarkOrb>();
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
                GameSfx.PlayAt(SoundEffect.OrbFocus, currentTargetOrb.transform);
            }
        }

        // Placed assets are the lowest-priority target: a treasure box standing in front of a tree is
        // still what the player means.
        PlaceableItem detectedPlaceable = null;
        if (currentTargetBox == null && currentTargetOrb == null)
        {
            detectedPlaceable = FindTargetedPlaced(ray);
        }

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

        // Lowest priority of all: an animal is offered only when nothing more specific is targeted.
        GrazingAnimal detectedAnimal = FindTargetedAnimal();
        if (detectedAnimal != currentTargetAnimal)
        {
            isInteracting = false;
            currentTargetAnimal = detectedAnimal;
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
        ShowOnly(rideInteractButton, wanted);

        // Getting off is always on offer while riding, whatever else is targeted.
        bool canDismount = rider != null && rider.IsRiding && !rider.IsTransitioning;
        ShowOnly(dismountButton, canDismount ? dismountButton : null);

        bool riding = rider != null && rider.IsRiding;
        RideAutoTravel travel = riding ? rider.GetComponent<RideAutoTravel>() : null;
        bool travelling = travel != null && travel.IsTravelling;

        // Auto Travel stays on offer all ride long; it turns into Stop while a tour runs.
        ShowOnly(autoTravelButton, canDismount || travelling ? autoTravelButton : null);
        UpdateAutoTravelLabel(travelling);

        bool canOrbRun = canDismount && JungleOrbRun.Instance != null && JungleOrbRun.Instance.CanOffer(rider);
        ShowOnly(orbRunButton, canOrbRun ? orbRunButton : null);

        LayoutRideRow();

        // The altitude stick stays up for as long as the player rides something that flies, unless a
        // tour is flying it for them.
        bool canFly = canDismount && rider.Mount != null && rider.Mount.canFly && !travelling;
        SetAltitudeStick(canFly);
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

            case InteractionTargetType.Rideable:
                return rideInteractButton != null ? rideInteractButton : itemInteractButton;

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
        ShowOnly(rideInteractButton, null);
    }

    /// <summary>
    /// Looks for a placed asset the player owns (<see cref="PlaceableItem"/>) under the crosshair. Runs
    /// as its own raycast because placed items sit on the default layer, which
    /// <see cref="interactionLayerMask"/> deliberately excludes.
    /// </summary>
    private PlaceableItem FindTargetedPlaced(Ray ray)
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

        // Searches the hit object itself before walking up, so this covers both.
        PlaceableItem hitPlaceable = hit.collider.GetComponentInParent<PlaceableItem>();

        // Animals are never "managed": looking at one offers the ride button instead (FindTargetedAnimal).
        if (hitPlaceable != null && hitPlaceable.GetComponent<PlacedAnimal>() != null) return null;

        // A ghost being positioned has its PlaceableItem disabled, and an item whose placement timer is
        // still counting down isn't manageable yet — neither is a valid target.
        return (hitPlaceable != null && hitPlaceable.enabled && hitPlaceable.IsFullyPlaced) ? hitPlaceable : null;
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

        currentTargetAnimal = null;
        HideAllButtons();
        ShowOnly(dismountButton, null);
        ShowOnly(autoTravelButton, null);
        ShowOnly(orbRunButton, null);
        SetAltitudeStick(false);
    }

    private bool _altitudeStickShown;

    /// <summary>Shows or hides the flying up/down stick, sliding it in from the right like the HUD.</summary>
    private void SetAltitudeStick(bool show)
    {
        if (show == _altitudeStickShown) return;
        RideFeatures features = RideFeatures.Instance;
        if (features == null || features.altitudeStick == null) return;
        _altitudeStickShown = show;
        UIToggleMotion.SetActive(features.altitudeStick.gameObject, show, 0f, new Vector2(80f, 0f));
    }
}
