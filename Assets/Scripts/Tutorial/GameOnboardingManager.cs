using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using DG.Tweening;

/// <summary>
/// Drives the full first-time onboarding experience: five HUD buttons (Shop, Inspector Mode,
/// Photo Mode, Show Treasure Box, Outer Garden) start hidden and are revealed one at a time as the
/// player earns them, across three sequential flows (core shop/placement/XP loop -> photo mode /
/// inspector mode -> outer garden). The Show Treasure Box button is revealed once Flow 3 begins
/// but, unlike the other buttons, has no dedicated explanation step of its own. Flow 1 opens with
/// a movement step that highlights the joystick until the player actually moves.
///
/// Persists as a DontDestroyOnLoad singleton (via its TutorialCanvas root) so it survives the
/// Jannah Garden -> Outer Garden scene swap, which is a full LoadSceneMode.Single reload.
/// </summary>
public class GameOnboardingManager : MonoBehaviour
{
    public static GameOnboardingManager Instance { get; private set; }

    /// <summary>True whenever the tutorial's instruction panel is on screen. <see cref="ToastMessageManager"/>
    /// checks this to stay quiet while the tutorial is talking, since the two overlap near the bottom of
    /// the screen.</summary>
    public static bool IsInstructionPanelVisible { get; private set; }

    private enum OnboardingStage
    {
        NotStarted = 0,
        Flow1InProgress = 1,
        Flow2InProgress = 3,
        Flow3InProgress = 5,
        Completed = 6
    }

    private enum Flow1SubStep { None, AwaitingMovement, AwaitingShopOpen, AwaitingItemSelect, AwaitingDownload, AwaitingPlace, ShowingXPInfo }
    private enum Flow2SubStep { None, AwaitingPhoto, AwaitingPreviewClose, AwaitingInspectorTap, AwaitingInspectorExit }

    private const string StageKey = "GameOnboarding_Stage";
    private const string LegacyTutorialKey = "TutorialCompleted_ShopPlacement";
    private const string LegacyGardenSaveKey = "PlacedItemsData";
    private const string OuterGardenSceneName = "Outer Garden";

    [Header("Tutorial UI (reused TutorialCanvas hierarchy)")]
    public CanvasGroup dimOverlay;
    public RectTransform instructionPanel;
    public TMP_Text instructionText;
    public Button primaryActionButton;
    [Tooltip("Lets the player skip the remainder of the current onboarding section (jumps ahead to the next flow, or finishes onboarding if already in the last one). Shown on every tutorial panel since it's a child of instructionPanel.")]
    public Button skipButton;

    [Header("Gated Buttons (no public accessor elsewhere - wired directly)")]
    public Button inspectorModeButton;
    public Button outerGardenButton;
    [Tooltip("Hidden until the whole onboarding flow is done, so first-time players can't back out into settings mid-tutorial.")]
    public Button settingsButton;

    [Header("Sorting")]
    public int overlaySortingOrder = 999;

    [Header("Pulse Animation")]
    public float pulseScale = 1.12f;
    public float pulseDuration = 0.5f;

    [Header("Panel Slide Animation")]
    [Tooltip("How far above its resting position the panel starts (appearing) / ends up (disappearing) - far enough to clear the top of the screen.")]
    public float panelSlideOffscreenOffset = 400f;
    public float panelSlideInDuration = 0.4f;
    public float panelSlideOutDuration = 0.3f;

    [Header("Hand Pointer Animation")]
    [Tooltip("Points at whichever UI element the current step is highlighting. Hidden whenever nothing is targeted.")]
    public RectTransform handUi;
    public Vector2 handOffset = new Vector2(0f, 60f);
    public float bounceSpeed = 6f;
    public float bounceAmplitude = 15f;

    [Header("Movement Step")]
    [Tooltip("World-space distance the player must cover with the joystick before the movement step is considered done.")]
    public float movementCompletionDistance = 1.5f;
    [Tooltip("Safety cap so a player who never touches the joystick isn't stuck - advances anyway once reached.")]
    public float movementStepTimeout = 30f;

    private OnboardingStage stage;
    private Flow1SubStep flow1Sub = Flow1SubStep.None;
    private Flow2SubStep flow2Sub = Flow2SubStep.None;

    private IdyllicFantasyNature.PlayerMovement playerMovementRef;
    private bool inspectorExitListenerAdded;

    private RectTransform highlightTarget;
    private Canvas highlightCanvas;
    private GraphicRaycaster highlightRaycaster;
    private bool addedHighlightCanvas;
    private bool addedHighlightRaycaster;

    // Joystick gets its own permanent highlight (rather than HighlightUIElement's single slot) since
    // its background is normally invisible until touched - ported from TutorialManager.
    private Canvas joystickHighlightCanvas;
    private GraphicRaycaster joystickHighlightRaycaster;
    private bool joystickAddedCanvas;
    private bool joystickAddedRaycaster;

    private Tween pulseTween;
    private RectTransform pulseTarget;

    private Coroutine handPointerCoroutine;

    private Tween panelSlideTween;
    private Vector2 instructionPanelShownPos;
    private bool instructionPanelShownPosCaptured;

    /// <summary>The last state actually requested, updated immediately (not deferred to the hide tween's
    /// completion) - see <see cref="SetInstructionPanelVisible"/>.</summary>
    private bool panelTargetShown;

    private bool photoTapListenerAdded;
    private bool inspectorTapListenerAdded;
    private bool outerGardenTapListenerAdded;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Only TutorialCanvas (this object's direct parent) should survive the scene swap.
        // transform.root would resolve to "All Canvas", the shared organizational parent of every
        // HUD canvas in the scene - DontDestroyOnLoad-ing that dragged the whole Jannah Garden HUD
        // (currency, timer, action buttons, etc.) into Outer Garden along with the tutorial overlay.
        // DontDestroyOnLoad also requires a root object, so detach TutorialCanvas first.
        Transform tutorialCanvas = transform.parent != null ? transform.parent : transform;
        tutorialCanvas.SetParent(null, true);
        DontDestroyOnLoad(tutorialCanvas.gameObject);
    }

    private void Start()
    {
        if (dimOverlay != null) dimOverlay.gameObject.SetActive(false);
        SetInstructionPanelVisible(false);
        EnsureHandUiSetup();
        StopHandPointerAnimation();

        if (skipButton != null)
        {
            skipButton.onClick.RemoveAllListeners();
            skipButton.onClick.AddListener(SkipCurrentSection);
        }

        RunMigrationCheckIfNeeded();

        stage = (OnboardingStage)PlayerPrefs.GetInt(StageKey, (int)OnboardingStage.NotStarted);

        SubscribeEvents();
        ApplyButtonVisibilityForStage();
        ResumeFromStage();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnsubscribeEvents();
        RestoreHighlightSorting();
        RestoreJoystickHighlight();
        StopPulse();
        StopHandPointerAnimation();
        panelSlideTween?.Kill();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Migration / persistence
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Existing players (already carrying the old tutorial's completion flag, or already holding a
    /// placed garden) must never suddenly lose 5 HUD buttons - grandfather them straight to Completed.
    /// </summary>
    private void RunMigrationCheckIfNeeded()
    {
        if (PlayerPrefs.HasKey(StageKey)) return;

        bool legacyDone = PlayerPrefs.GetInt(LegacyTutorialKey, 0) == 1;
        bool hasExistingGarden = SaveSystem.Exists(LegacyGardenSaveKey);

        if (legacyDone || hasExistingGarden)
        {
            PlayerPrefs.SetInt(StageKey, (int)OnboardingStage.Completed);
            PlayerPrefs.Save();
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor-only test helper - see the "Tools/Reset Onboarding Tutorial" menu item. Sets the
    /// stage directly (rather than deleting the key) so <see cref="RunMigrationCheckIfNeeded"/> doesn't
    /// immediately re-grandfather it back to Completed on next Play because a garden save already exists.</summary>
    public static void ResetForTesting()
    {
        PlayerPrefs.SetInt(StageKey, (int)OnboardingStage.NotStarted);
        PlayerPrefs.Save();
    }
#endif

    private void SetStage(OnboardingStage newStage)
    {
        stage = newStage;
        PlayerPrefs.SetInt(StageKey, (int)stage);
        PlayerPrefs.Save();
        ApplyButtonVisibilityForStage();
    }

    /// <summary>A button becomes permanently visible once its own step has begun - derived purely from
    /// the coarse stage, so this alone reproduces the "hidden until earned" state on every resume.</summary>
    private void ApplyButtonVisibilityForStage()
    {
        SetActive(InGameShopManager.Instance != null ? InGameShopManager.Instance.openCloseButton : null,
            stage >= OnboardingStage.Flow1InProgress);

        SetActive(PhotoModeManager.Instance != null ? PhotoModeManager.Instance.photoButton : null,
            stage >= OnboardingStage.Flow2InProgress);

        bool flow2Done = stage >= OnboardingStage.Flow3InProgress;
        SetActive(inspectorModeButton, flow2Done);
        SetActive(GetTreasureBoxShowButton(), flow2Done);

        SetActive(outerGardenButton, stage >= OnboardingStage.Flow3InProgress);

        SetActive(settingsButton, stage >= OnboardingStage.Completed);
    }

    private void ResumeFromStage()
    {
        switch (stage)
        {
            case OnboardingStage.NotStarted:
                ShowIntroPanel();
                break;
            case OnboardingStage.Flow1InProgress:
                BeginShopOpenStep();
                break;
            case OnboardingStage.Flow2InProgress:
                BeginPhotoModeStep();
                break;
            case OnboardingStage.Flow3InProgress:
                BeginOuterGardenStep();
                break;
            default:
                break; // Completed - free play
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Events
    // ═══════════════════════════════════════════════════════════════════════

    private void SubscribeEvents()
    {
        InGameShopManager.OnShopOpened += HandleShopOpened;
        InGameShopManager.OnShopClosed += HandleShopClosed;
        InGameShopManager.OnShopItemUsed += HandleShopItemUsed;
        ItemPlacementManager.OnItemPlaced += HandleItemPlaced;
        PhotoModeManager.OnPreviewClosed += HandlePhotoPreviewClosed;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void UnsubscribeEvents()
    {
        InGameShopManager.OnShopOpened -= HandleShopOpened;
        InGameShopManager.OnShopClosed -= HandleShopClosed;
        InGameShopManager.OnShopItemUsed -= HandleShopItemUsed;
        ItemPlacementManager.OnItemPlaced -= HandleItemPlaced;
        PhotoModeManager.OnPreviewClosed -= HandlePhotoPreviewClosed;
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (inspectorExitListenerAdded && playerMovementRef != null)
        {
            playerMovementRef.OnInspectorModeChanged -= HandleInspectorModeChanged;
        }

        if (photoTapListenerAdded)
        {
            Button photo = PhotoModeManager.Instance != null ? PhotoModeManager.Instance.photoButton : null;
            if (photo != null) photo.onClick.RemoveListener(HandlePhotoButtonTapped);
        }
        if (inspectorTapListenerAdded && inspectorModeButton != null)
        {
            inspectorModeButton.onClick.RemoveListener(HandleInspectorButtonTapped);
        }
        if (outerGardenTapListenerAdded && outerGardenButton != null)
        {
            outerGardenButton.onClick.RemoveListener(HandleOuterGardenButtonTapped);
        }
    }

    private static Button GetTreasureBoxShowButton()
    {
        return TreasureBoxManager.Instance != null && TreasureBoxManager.Instance.treasureBoxStatusUi != null
            ? TreasureBoxManager.Instance.treasureBoxStatusUi.showBoxButton
            : null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Flow 1 - Core loop
    // ═══════════════════════════════════════════════════════════════════════

    private void ShowIntroPanel()
    {
        ShowDimAndPanel(true);
        LocalizationManager loc = LocalizationManager.Instance;
        SetInstructionText($"<color=#FFD35C><size=120%>{loc.Get("onboarding.welcome_title")}</size></color>\n\n"
            + loc.Get("onboarding.welcome_body"));
        ConfigurePrimaryButton(loc.Get("onboarding.start_button"), () =>
        {
            SetStage(OnboardingStage.Flow1InProgress);
            BeginMovementStep();
        });
    }

    private void BeginMovementStep()
    {
        flow1Sub = Flow1SubStep.AwaitingMovement;
        HidePrimaryButton();
        // Non-blocking: the player needs the joystick underneath to actually be usable.
        ShowDimAndPanel(true, blockRaycasts: false);
        SetInstructionText(LocalizationManager.Instance.Get("tutorial.step_basic_movement"));

        Joystick joystick = FindFirstObjectByType<Joystick>();
        if (joystick == null)
        {
            Debug.LogWarning("[GameOnboardingManager] Joystick not found; skipping movement step.");
            CompleteMovementStep();
            return;
        }

        RectTransform joystickRect = joystick.GetComponent<RectTransform>();
        HighlightJoystickPermanently(joystickRect);

        RectTransform pointerTarget = joystick.Handle != null ? joystick.Handle : joystickRect;
        StartHandPointerAnimation(pointerTarget);

        if (playerMovementRef == null)
        {
            playerMovementRef = FindFirstObjectByType<IdyllicFantasyNature.PlayerMovement>();
        }

        StartCoroutine(WaitForMovementRoutine(playerMovementRef != null ? playerMovementRef.transform : null));
    }

    private IEnumerator WaitForMovementRoutine(Transform playerTransform)
    {
        Vector3 startPos = playerTransform != null ? playerTransform.position : Vector3.zero;
        float elapsed = 0f;

        while (flow1Sub == Flow1SubStep.AwaitingMovement && elapsed < movementStepTimeout)
        {
            if (playerTransform != null
                && Vector3.Distance(playerTransform.position, startPos) >= movementCompletionDistance)
            {
                break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (flow1Sub != Flow1SubStep.AwaitingMovement) yield break; // superseded (e.g. skipped)

        CompleteMovementStep();
    }

    private void CompleteMovementStep()
    {
        if (stage != OnboardingStage.Flow1InProgress) return;

        RestoreJoystickHighlight();
        StopHandPointerAnimation();
        flow1Sub = Flow1SubStep.None;

        BeginShopOpenStep();
    }

    private void BeginShopOpenStep()
    {
        flow1Sub = Flow1SubStep.AwaitingShopOpen;
        HidePrimaryButton();
        ShowDimAndPanel(true);
        SetInstructionText(LocalizationManager.Instance.Get("onboarding.step_open_shop"));

        Button shopButton = InGameShopManager.Instance != null ? InGameShopManager.Instance.openCloseButton : null;
        SetActive(shopButton, true);
        if (shopButton != null)
        {
            RectTransform rect = shopButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
            StartHandPointerAnimation(rect);
        }
    }

    private void HandleShopOpened()
    {
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.AwaitingShopOpen) return;

        StopPulse();
        RestoreHighlightSorting();
        StopHandPointerAnimation();
        HideDimOverlayOnly();
        SetInstructionText(LocalizationManager.Instance.Get("onboarding.step_select_item"));
        StartCoroutine(SelectFirstShopItemRoutine());
    }

    private void HandleShopClosed()
    {
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.AwaitingItemSelect) return;

        StopPulse();
        RestoreHighlightSorting();
        StopHandPointerAnimation();
        UnblockAllShopCards();
        BeginShopOpenStep();
    }

    private IEnumerator SelectFirstShopItemRoutine()
    {
        flow1Sub = Flow1SubStep.AwaitingItemSelect;

        List<ShopItemUI> spawned = null;
        float elapsed = 0f;
        while ((spawned == null || spawned.Count == 0) && elapsed < 5f)
        {
            if (InGameShopManager.Instance != null) spawned = InGameShopManager.Instance.GetSpawnedShopItemUIs();
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (flow1Sub != Flow1SubStep.AwaitingItemSelect) yield break; // shop closed again before items were ready

        if (spawned == null || spawned.Count == 0)
        {
            Debug.LogWarning("[GameOnboardingManager] No shop items spawned; skipping item-selection lock.");
            yield break;
        }

        ShopItemUI chosen = spawned.FirstOrDefault(u => u != null && u.ItemData != null
            && u.ItemData.itemCategory == ShopItemCategory.PlantsAndGardens);
        if (chosen == null) chosen = spawned.FirstOrDefault(u => u != null);
        if (chosen == null) yield break;

        foreach (var ui in spawned)
        {
            if (ui != null) ui.SetInteractionBlocked(ui != chosen);
        }

        if (chosen.purchaseButton != null)
        {
            RectTransform rect = chosen.purchaseButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
            StartHandPointerAnimation(rect);
        }
    }

    private void HandleShopItemUsed(ShopItemData data)
    {
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.AwaitingItemSelect) return;

        StopPulse();
        RestoreHighlightSorting();
        StopHandPointerAnimation();
        UnblockAllShopCards();

        flow1Sub = Flow1SubStep.AwaitingDownload;
        SetInstructionText(LocalizationManager.Instance.Get("onboarding.step_downloading"));
        StartCoroutine(WaitForPlaceButtonReady());
    }

    private void UnblockAllShopCards()
    {
        if (InGameShopManager.Instance == null) return;
        foreach (var ui in InGameShopManager.Instance.GetSpawnedShopItemUIs())
        {
            if (ui != null) ui.SetInteractionBlocked(false);
        }
    }

    private IEnumerator WaitForPlaceButtonReady()
    {
        Button place = null;
        float elapsed = 0f;
        while (elapsed < 15f)
        {
            place = ItemPlacementManager.Instance != null ? ItemPlacementManager.Instance.placeButton : null;
            if (place != null && place.gameObject.activeInHierarchy) break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (flow1Sub != Flow1SubStep.AwaitingDownload) yield break; // superseded

        if (place == null || !place.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("[GameOnboardingManager] Place button never became ready; skipping placement highlight.");
            yield break;
        }

        flow1Sub = Flow1SubStep.AwaitingPlace;
        ShowDimAndPanel(true, blockRaycasts: false);
        SetInstructionText(LocalizationManager.Instance.Get("onboarding.step_place_item"));

        RectTransform rect = place.GetComponent<RectTransform>();
        HighlightUIElement(rect);
        PulseButton(rect);
        StartHandPointerAnimation(rect);
    }

    private void HandleItemPlaced(PlaceableItem placedItem)
    {
        if (placedItem == null || placedItem.sourceKind != PlacedItemSource.ShopItem) return;
        if (ItemPlacementManager.Instance != null && ItemPlacementManager.Instance.IsRelocating) return;
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.AwaitingPlace) return;

        StopPulse();
        RestoreHighlightSorting();
        StopHandPointerAnimation();
        flow1Sub = Flow1SubStep.ShowingXPInfo;

        Button xpButton = PlayerXPManager.Instance != null ? PlayerXPManager.Instance.xpGainChartToggleButton : null;
        ShowDimAndPanel(true);
        SetInstructionText(LocalizationManager.Instance.Get("onboarding.step_xp_info"));
        if (xpButton != null)
        {
            RectTransform rect = xpButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
        }

        ConfigurePrimaryButton(LocalizationManager.Instance.Get("onboarding.got_it_button"), HandleXPInfoContinue);
    }

    private void HandleXPInfoContinue()
    {
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.ShowingXPInfo) return;

        StopPulse();
        RestoreHighlightSorting();
        flow1Sub = Flow1SubStep.None;
        SetInstructionPanelVisible(false);

        SetStage(OnboardingStage.Flow2InProgress);
        BeginPhotoModeStep();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Flow 2 - Photo Mode -> Inspector Mode
    // ═══════════════════════════════════════════════════════════════════════

    private void BeginPhotoModeStep()
    {
        flow2Sub = Flow2SubStep.AwaitingPhoto;
        HidePrimaryButton();
        ShowDimAndPanel(true, blockRaycasts: false);
        SetInstructionText(LocalizationManager.Instance.Get("onboarding.step_take_photo"));

        Button photoButton = PhotoModeManager.Instance != null ? PhotoModeManager.Instance.photoButton : null;
        SetActive(photoButton, true);
        if (photoButton != null)
        {
            RectTransform rect = photoButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
            StartHandPointerAnimation(rect);
            if (!photoTapListenerAdded)
            {
                photoButton.onClick.AddListener(HandlePhotoButtonTapped);
                photoTapListenerAdded = true;
            }
        }
    }

    private void HandlePhotoButtonTapped()
    {
        if (stage != OnboardingStage.Flow2InProgress || flow2Sub != Flow2SubStep.AwaitingPhoto) return;

        StopPulse();
        RestoreHighlightSorting();
        StopHandPointerAnimation();
        flow2Sub = Flow2SubStep.AwaitingPreviewClose;
        SetInstructionText(LocalizationManager.Instance.Get("onboarding.step_share_photo"));
        HideDimOverlayOnly();
    }

    private void HandlePhotoPreviewClosed()
    {
        if (stage != OnboardingStage.Flow2InProgress || flow2Sub != Flow2SubStep.AwaitingPreviewClose) return;

        flow2Sub = Flow2SubStep.AwaitingInspectorTap;
        ShowDimAndPanel(true);
        SetInstructionText(LocalizationManager.Instance.Get("onboarding.step_inspector_mode"));

        SetActive(inspectorModeButton, true);
        if (inspectorModeButton != null)
        {
            RectTransform rect = inspectorModeButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
            StartHandPointerAnimation(rect);
            if (!inspectorTapListenerAdded)
            {
                inspectorModeButton.onClick.AddListener(HandleInspectorButtonTapped);
                inspectorTapListenerAdded = true;
            }
        }
    }

    private void HandleInspectorButtonTapped()
    {
        if (stage != OnboardingStage.Flow2InProgress || flow2Sub != Flow2SubStep.AwaitingInspectorTap) return;

        StopPulse();
        RestoreHighlightSorting();
        StopHandPointerAnimation();
        flow2Sub = Flow2SubStep.AwaitingInspectorExit;

        // Inspector mode's fly controls and camera drag both need raycasts to reach the world/joystick,
        // so don't leave a blocking dim overlay up while the player finds their way back to the ground.
        HideDimOverlayOnly();
        SetInstructionText(LocalizationManager.Instance.Get("onboarding.step_return_ground"));

        if (playerMovementRef == null)
        {
            playerMovementRef = FindFirstObjectByType<IdyllicFantasyNature.PlayerMovement>();
        }

        if (playerMovementRef == null)
        {
            Debug.LogWarning("[GameOnboardingManager] PlayerMovement not found; skipping inspector-exit wait.");
            CompleteFlow2();
            return;
        }

        if (!inspectorExitListenerAdded)
        {
            playerMovementRef.OnInspectorModeChanged += HandleInspectorModeChanged;
            inspectorExitListenerAdded = true;
        }

        // Toggling the button just fired ToggleInspectorMode(), so this is normally still true - but
        // guard the (unlikely) case where it's already back off by the time we check.
        if (!playerMovementRef.IsInspectorMode)
        {
            CompleteFlow2();
        }
    }

    private void HandleInspectorModeChanged(bool isInspectorMode)
    {
        if (isInspectorMode) return; // only care about coming back down
        if (stage != OnboardingStage.Flow2InProgress || flow2Sub != Flow2SubStep.AwaitingInspectorExit) return;

        CompleteFlow2();
    }

    private void CompleteFlow2()
    {
        if (stage != OnboardingStage.Flow2InProgress || flow2Sub != Flow2SubStep.AwaitingInspectorExit) return;

        flow2Sub = Flow2SubStep.None;
        SetInstructionPanelVisible(false);

        SetStage(OnboardingStage.Flow3InProgress);
        BeginOuterGardenStep();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Flow 3 - Outer Garden
    // ═══════════════════════════════════════════════════════════════════════

    private void BeginOuterGardenStep()
    {
        HidePrimaryButton();
        ShowDimAndPanel(true);
        SetInstructionText(LocalizationManager.Instance.Get("onboarding.step_outer_garden"));

        SetActive(outerGardenButton, true);
        if (outerGardenButton != null)
        {
            RectTransform rect = outerGardenButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
            StartHandPointerAnimation(rect);
            if (!outerGardenTapListenerAdded)
            {
                outerGardenButton.onClick.AddListener(HandleOuterGardenButtonTapped);
                outerGardenTapListenerAdded = true;
            }
        }
    }

    private void HandleOuterGardenButtonTapped()
    {
        if (stage != OnboardingStage.Flow3InProgress) return;

        StopPulse();
        RestoreHighlightSorting();
        StopHandPointerAnimation();
        ShowDimAndPanel(false);

        if (JannahGardenManager.Instance != null) JannahGardenManager.Instance.LoadOuterGarden();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (stage == OnboardingStage.Flow3InProgress && scene.name == OuterGardenSceneName)
        {
            ShowOuterGardenIntro();
        }
    }

    private void ShowOuterGardenIntro()
    {
        ShowDimAndPanel(true);
        LocalizationManager loc = LocalizationManager.Instance;
        SetInstructionText($"<color=#FFD35C><size=120%>{loc.Get("onboarding.outer_garden_title")}</size></color>\n\n"
            + loc.Get("onboarding.outer_garden_body"));
        ConfigurePrimaryButton(loc.Get("tutorial.button_finish"), HandleOuterGardenIntroFinished);
    }

    private void HandleOuterGardenIntroFinished()
    {
        ShowDimAndPanel(false);
        SetStage(OnboardingStage.Completed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UI helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <param name="blockRaycasts">When false, the dim overlay still shows visually but lets touches/
    /// clicks pass through to the world underneath — needed for the placement step, where the player
    /// must be able to drag-look and use the joystick to find a spot. <see cref="CameraMovement"/> and
    /// the joystick both gate on <c>EventSystem.IsPointerOverGameObject</c>, which a raycast-blocking
    /// full-screen overlay would otherwise always satisfy.</param>
    private void ShowDimAndPanel(bool show, bool blockRaycasts = true)
    {
        if (dimOverlay != null)
        {
            dimOverlay.gameObject.SetActive(show);
            dimOverlay.blocksRaycasts = show && blockRaycasts;
        }
        SetInstructionPanelVisible(show);
    }

    /// <summary>The one place instructionPanel's active state is set, so <see cref="IsInstructionPanelVisible"/>
    /// can never drift out of sync with what's actually on screen. Slides the panel in from above its
    /// authored resting position when showing, and back up off-screen before deactivating when hiding.</summary>
    private void SetInstructionPanelVisible(bool show)
    {
        IsInstructionPanelVisible = show;
        if (instructionPanel == null) return;

        CaptureInstructionPanelShownPosIfNeeded();

        // Idempotent: a step that only needs to change the dim overlay (see HideDimOverlayOnly) leaves
        // the panel already at/heading to the requested state, and re-playing the slide would just
        // flicker it out and back in for no reason. Compared against the *requested* state rather than
        // gameObject.activeSelf, since activeSelf only flips once a hide tween finishes - a show request
        // arriving mid-hide would otherwise look like a no-op and let the in-flight hide keep going.
        if (show == panelTargetShown) return;
        panelTargetShown = show;

        panelSlideTween?.Kill();
        Vector2 offscreenPos = instructionPanelShownPos + new Vector2(0f, panelSlideOffscreenOffset);

        if (show)
        {
            instructionPanel.gameObject.SetActive(true);
            instructionPanel.anchoredPosition = offscreenPos;
            panelSlideTween = instructionPanel
                .DOAnchorPos(instructionPanelShownPos, panelSlideInDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);
            return;
        }

        RectTransform panel = instructionPanel;
        panelSlideTween = panel
            .DOAnchorPos(offscreenPos, panelSlideOutDuration)
            .SetEase(Ease.InBack)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                if (panel != null) panel.gameObject.SetActive(false);
            });
    }

    /// <summary>Captures the panel's authored resting position and initial active state the first time
    /// it's touched, so the slide animation always returns to wherever it was actually placed in the
    /// scene and <see cref="panelTargetShown"/> starts from the truth rather than assuming hidden.</summary>
    private void CaptureInstructionPanelShownPosIfNeeded()
    {
        if (instructionPanelShownPosCaptured || instructionPanel == null) return;
        instructionPanelShownPos = instructionPanel.anchoredPosition;
        panelTargetShown = instructionPanel.gameObject.activeSelf;
        instructionPanelShownPosCaptured = true;
    }

    /// <summary>Turns off just the dim overlay, leaving the instruction panel (if any) up. Used where a
    /// full-screen game element (the XP chart) needs to be visible without the tutorial's own dimming.</summary>
    private void HideDimOverlayOnly()
    {
        if (dimOverlay != null)
        {
            dimOverlay.gameObject.SetActive(false);
            dimOverlay.blocksRaycasts = false;
        }
    }

    private void SetInstructionText(string text)
    {
        if (instructionText != null) LocalizedRendering.SetText(instructionText, text);
    }

    private void ConfigurePrimaryButton(string label, System.Action onClick)
    {
        if (primaryActionButton == null) return;

        primaryActionButton.gameObject.SetActive(true);
        TMP_Text label_ = primaryActionButton.GetComponentInChildren<TMP_Text>();
        if (label_ != null) LocalizedRendering.SetText(label_, label);

        primaryActionButton.onClick.RemoveAllListeners();
        primaryActionButton.onClick.AddListener(() =>
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
            onClick?.Invoke();
        });
    }

    private void HidePrimaryButton()
    {
        if (primaryActionButton != null) primaryActionButton.gameObject.SetActive(false);
    }

    /// <summary>Jumps straight to the next flow (or finishes onboarding if already in the last one),
    /// bypassing whatever gameplay action the current section was waiting on. Mirrors the same
    /// stage transitions used when a section completes normally (see <see cref="HandleXPInfoContinue"/>
    /// and <see cref="CompleteFlow2"/>) so skipped players end up in an identical state to
    /// players who finished the section the intended way.</summary>
    private void SkipCurrentSection()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);

        StopPulse();
        RestoreHighlightSorting();
        RestoreJoystickHighlight();
        StopHandPointerAnimation();

        switch (stage)
        {
            case OnboardingStage.NotStarted:
            case OnboardingStage.Flow1InProgress:
                flow1Sub = Flow1SubStep.None;
                UnblockAllShopCards();
                SetInstructionPanelVisible(false);
                SetStage(OnboardingStage.Flow2InProgress);
                BeginPhotoModeStep();
                break;

            case OnboardingStage.Flow2InProgress:
                flow2Sub = Flow2SubStep.None;
                SetInstructionPanelVisible(false);
                SetStage(OnboardingStage.Flow3InProgress);
                BeginOuterGardenStep();
                break;

            default:
                ShowDimAndPanel(false);
                SetStage(OnboardingStage.Completed);
                break;
        }
    }

    private static void SetActive(Component component, bool active)
    {
        if (component == null) return;
        component.gameObject.SetActive(active);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Highlight punch-through (raises the target above the dim overlay, ported from TutorialManager)
    // ═══════════════════════════════════════════════════════════════════════

    private void HighlightUIElement(RectTransform target)
    {
        RestoreHighlightSorting();
        if (target == null) return;

        highlightTarget = target;

        highlightCanvas = target.GetComponent<Canvas>();
        if (highlightCanvas == null)
        {
            highlightCanvas = target.gameObject.AddComponent<Canvas>();
            addedHighlightCanvas = true;
        }
        else
        {
            addedHighlightCanvas = false;
        }
        highlightCanvas.overrideSorting = true;
        highlightCanvas.sortingOrder = overlaySortingOrder + 1;

        highlightRaycaster = target.GetComponent<GraphicRaycaster>();
        if (highlightRaycaster == null)
        {
            highlightRaycaster = target.gameObject.AddComponent<GraphicRaycaster>();
            addedHighlightRaycaster = true;
        }
        else
        {
            addedHighlightRaycaster = false;
        }
    }

    private void RestoreHighlightSorting()
    {
        if (highlightTarget == null) return;

        if (addedHighlightRaycaster && highlightRaycaster != null) Destroy(highlightRaycaster);
        if (addedHighlightCanvas && highlightCanvas != null) Destroy(highlightCanvas);
        else if (highlightCanvas != null) highlightCanvas.overrideSorting = false;

        highlightTarget = null;
        highlightCanvas = null;
        highlightRaycaster = null;
        addedHighlightCanvas = false;
        addedHighlightRaycaster = false;
    }

    private void HighlightJoystickPermanently(RectTransform target)
    {
        if (target == null || joystickHighlightCanvas != null) return;

        Joystick joystick = target.GetComponentInParent<Joystick>();
        if (joystick != null)
        {
            joystick.KeepBackgroundVisible = true;
            if (joystick.Background != null)
            {
                joystick.Background.gameObject.SetActive(true);
            }
        }

        joystickHighlightCanvas = target.GetComponent<Canvas>();
        if (joystickHighlightCanvas == null)
        {
            joystickHighlightCanvas = target.gameObject.AddComponent<Canvas>();
            joystickAddedCanvas = true;
        }
        else
        {
            joystickAddedCanvas = false;
        }
        joystickHighlightCanvas.overrideSorting = true;
        joystickHighlightCanvas.sortingOrder = overlaySortingOrder + 1;

        joystickHighlightRaycaster = target.GetComponent<GraphicRaycaster>();
        if (joystickHighlightRaycaster == null)
        {
            joystickHighlightRaycaster = target.gameObject.AddComponent<GraphicRaycaster>();
            joystickAddedRaycaster = true;
        }
        else
        {
            joystickAddedRaycaster = false;
        }
    }

    private void RestoreJoystickHighlight()
    {
        if (joystickHighlightCanvas == null) return;

        Joystick joystick = joystickHighlightCanvas.GetComponentInParent<Joystick>();
        if (joystick != null)
        {
            joystick.KeepBackgroundVisible = false;
            if (joystick is FloatingJoystick || joystick is DynamicJoystick)
            {
                if (joystick.Background != null)
                {
                    joystick.Background.gameObject.SetActive(false);
                }
            }
            else if (joystick is VariableJoystick variableJoystick)
            {
                variableJoystick.SetMode(variableJoystick.Mode);
            }
        }

        if (joystickAddedRaycaster && joystickHighlightRaycaster != null) Destroy(joystickHighlightRaycaster);
        if (joystickAddedCanvas) Destroy(joystickHighlightCanvas);
        else joystickHighlightCanvas.overrideSorting = false;

        joystickHighlightCanvas = null;
        joystickHighlightRaycaster = null;
        joystickAddedCanvas = false;
        joystickAddedRaycaster = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Pulse animation
    // ═══════════════════════════════════════════════════════════════════════

    private void PulseButton(RectTransform target)
    {
        StopPulse();
        if (target == null) return;

        pulseTarget = target;
        pulseTarget.localScale = Vector3.one;
        pulseTween = target.DOScale(pulseScale, pulseDuration)
            .SetLoops(-1, LoopType.Yoyo)
            .SetEase(Ease.InOutSine)
            .SetUpdate(true);
    }

    private void StopPulse()
    {
        if (pulseTween != null)
        {
            pulseTween.Kill();
            pulseTween = null;
        }
        if (pulseTarget != null)
        {
            pulseTarget.localScale = Vector3.one;
            pulseTarget = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Hand pointer (ported from TutorialManager, reuses the scene's TutorialHand)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>One-time setup so the hand renders above whatever HighlightUIElement raises (overlaySortingOrder + 1)
    /// and never eats the tap meant for the button underneath it.</summary>
    private void EnsureHandUiSetup()
    {
        if (handUi == null) return;

        // Canvas.overrideSorting silently fails to take effect (and sortingOrder reads back as the
        // inherited/default value) while the GameObject is inactive - handUi starts disabled in the
        // scene, so the override has to be applied with it briefly active or it never actually applies,
        // leaving the hand stuck at TutorialCanvas's own sortingOrder (i.e. behind whatever's highlighted).
        bool wasActive = handUi.gameObject.activeSelf;
        if (!wasActive) handUi.gameObject.SetActive(true);

        Canvas handCanvas = handUi.GetComponent<Canvas>();
        if (handCanvas == null) handCanvas = handUi.gameObject.AddComponent<Canvas>();
        handCanvas.overrideSorting = true;
        handCanvas.sortingOrder = overlaySortingOrder + 2;

        foreach (var img in handUi.GetComponentsInChildren<Image>(true))
        {
            img.raycastTarget = false;
        }

        if (!wasActive) handUi.gameObject.SetActive(false);
    }

    private void StartHandPointerAnimation(RectTransform target)
    {
        StopHandPointerAnimation();
        if (handUi == null || target == null) return;

        handUi.gameObject.SetActive(true);
        handPointerCoroutine = StartCoroutine(AnimateHandRoutine(target));
    }

    private void StopHandPointerAnimation()
    {
        if (handPointerCoroutine != null)
        {
            StopCoroutine(handPointerCoroutine);
            handPointerCoroutine = null;
        }
        if (handUi != null)
        {
            handUi.gameObject.SetActive(false);
        }
    }

    private IEnumerator AnimateHandRoutine(RectTransform target)
    {
        while (target != null && handUi != null)
        {
            Vector3 targetWorldPos = target.position;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, targetWorldPos);

            RectTransform parentRect = handUi.parent as RectTransform;
            if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, null, out Vector2 localPoint))
            {
                float bounce = Mathf.Sin(Time.time * bounceSpeed) * bounceAmplitude;
                handUi.anchoredPosition = localPoint + handOffset + new Vector2(0f, bounce);
            }

            yield return null;
        }

        if (handUi != null) handUi.gameObject.SetActive(false);
    }
}
