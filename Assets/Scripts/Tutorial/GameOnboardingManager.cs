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
/// inspector mode / treasure box / minimap -> outer garden). Replaces <see cref="TutorialManager"/>,
/// which highlights always-visible buttons instead of gating them.
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

    private enum Flow1SubStep { None, AwaitingShopOpen, AwaitingItemSelect, AwaitingDownload, AwaitingPlace, AwaitingXPTap, AwaitingXPChartClose }
    private enum Flow2SubStep { None, AwaitingPhoto, AwaitingPreviewClose, AwaitingInspectorTap, AwaitingInspectorExit, AwaitingTreasureBoxTap, MinimapCallout }

    private const string StageKey = "GameOnboarding_Stage";
    private const string LegacyTutorialKey = "TutorialCompleted_ShopPlacement";
    private const string LegacyGardenSaveKey = "PlacedItemsData";
    private const string OuterGardenSceneName = "Outer Garden";

    [Header("Tutorial UI (reused TutorialCanvas hierarchy)")]
    public CanvasGroup dimOverlay;
    public RectTransform instructionPanel;
    public TMP_Text instructionText;
    public Button primaryActionButton;

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

    private Tween pulseTween;
    private RectTransform pulseTarget;

    private Tween panelSlideTween;
    private Vector2 instructionPanelShownPos;
    private bool instructionPanelShownPosCaptured;

    /// <summary>The last state actually requested, updated immediately (not deferred to the hide tween's
    /// completion) - see <see cref="SetInstructionPanelVisible"/>.</summary>
    private bool panelTargetShown;

    private bool xpTapListenerAdded;
    private bool photoTapListenerAdded;
    private bool inspectorTapListenerAdded;
    private bool treasureBoxTapListenerAdded;
    private bool outerGardenTapListenerAdded;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(transform.root.gameObject);
    }

    private void Start()
    {
        if (dimOverlay != null) dimOverlay.gameObject.SetActive(false);
        SetInstructionPanelVisible(false);

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
        StopPulse();
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
        PlayerXPManager.OnChartToggled += HandleXPChartToggled;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void UnsubscribeEvents()
    {
        InGameShopManager.OnShopOpened -= HandleShopOpened;
        InGameShopManager.OnShopClosed -= HandleShopClosed;
        InGameShopManager.OnShopItemUsed -= HandleShopItemUsed;
        ItemPlacementManager.OnItemPlaced -= HandleItemPlaced;
        PhotoModeManager.OnPreviewClosed -= HandlePhotoPreviewClosed;
        PlayerXPManager.OnChartToggled -= HandleXPChartToggled;
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (inspectorExitListenerAdded && playerMovementRef != null)
        {
            playerMovementRef.OnInspectorModeChanged -= HandleInspectorModeChanged;
        }

        if (xpTapListenerAdded)
        {
            Button xp = PlayerXPManager.Instance != null ? PlayerXPManager.Instance.xpGainChartToggleButton : null;
            if (xp != null) xp.onClick.RemoveListener(HandleXPButtonTapped);
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
        if (treasureBoxTapListenerAdded)
        {
            Button box = GetTreasureBoxShowButton();
            if (box != null) box.onClick.RemoveListener(HandleTreasureBoxButtonTapped);
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
        SetInstructionText("<color=#FFD35C><size=120%>Welcome to Jannah Garden!</size></color>\n\n"
            + "Grow your own piece of paradise: place trees, fountains and sacred decor, earn XP, "
            + "open treasure boxes and explore the Outer Garden. Let's get started!");
        ConfigurePrimaryButton("Start Tutorial", () =>
        {
            SetStage(OnboardingStage.Flow1InProgress);
            BeginShopOpenStep();
        });
    }

    private void BeginShopOpenStep()
    {
        flow1Sub = Flow1SubStep.AwaitingShopOpen;
        HidePrimaryButton();
        ShowDimAndPanel(true);
        SetInstructionText("Tap the Shop button to open the Garden Shop!");

        Button shopButton = InGameShopManager.Instance != null ? InGameShopManager.Instance.openCloseButton : null;
        SetActive(shopButton, true);
        if (shopButton != null)
        {
            RectTransform rect = shopButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
        }
    }

    private void HandleShopOpened()
    {
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.AwaitingShopOpen) return;

        StopPulse();
        RestoreHighlightSorting();
        HideDimOverlayOnly();
        SetInstructionText("Select your first item below!");
        StartCoroutine(SelectFirstShopItemRoutine());
    }

    private void HandleShopClosed()
    {
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.AwaitingItemSelect) return;

        StopPulse();
        RestoreHighlightSorting();
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
        }
    }

    private void HandleShopItemUsed(ShopItemData data)
    {
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.AwaitingItemSelect) return;

        StopPulse();
        RestoreHighlightSorting();
        UnblockAllShopCards();

        flow1Sub = Flow1SubStep.AwaitingDownload;
        SetInstructionText("Downloading your item — hang tight!");
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
        SetInstructionText("Move around and tap Place to plant it!");

        RectTransform rect = place.GetComponent<RectTransform>();
        HighlightUIElement(rect);
        PulseButton(rect);
    }

    private void HandleItemPlaced(PlaceableItem placedItem)
    {
        if (placedItem == null || placedItem.sourceKind != PlacedItemSource.ShopItem) return;
        if (ItemPlacementManager.Instance != null && ItemPlacementManager.Instance.IsRelocating) return;
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.AwaitingPlace) return;

        StopPulse();
        RestoreHighlightSorting();
        flow1Sub = Flow1SubStep.AwaitingXPTap;

        Button xpButton = PlayerXPManager.Instance != null ? PlayerXPManager.Instance.xpGainChartToggleButton : null;
        ShowDimAndPanel(true);
        SetInstructionText("Nice! Tap the XP button to see your progress!");
        if (xpButton != null)
        {
            RectTransform rect = xpButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
            if (!xpTapListenerAdded)
            {
                xpButton.onClick.AddListener(HandleXPButtonTapped);
                xpTapListenerAdded = true;
            }
        }
    }

    private void HandleXPButtonTapped()
    {
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.AwaitingXPTap) return;

        StopPulse();
        RestoreHighlightSorting();

        // The chart opens right where this dim overlay sits (sortingOrder 999) and takes a moment to
        // slide in - drop just the overlay so it never covers the chart the player just asked to see.
        // Flow 2 doesn't start until PlayerXPManager reports the chart closed again (HandleXPChartToggled).
        HideDimOverlayOnly();
        SetInstructionText("Check out your XP chart, then close it to continue!");

        flow1Sub = Flow1SubStep.AwaitingXPChartClose;
    }

    private void HandleXPChartToggled(bool isOpen)
    {
        if (isOpen) return;
        if (stage != OnboardingStage.Flow1InProgress || flow1Sub != Flow1SubStep.AwaitingXPChartClose) return;

        flow1Sub = Flow1SubStep.None;
        SetInstructionPanelVisible(false);

        SetStage(OnboardingStage.Flow2InProgress);
        BeginPhotoModeStep();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Flow 2 - Photo Mode -> Inspector Mode -> Treasure Box -> Minimap callout
    // ═══════════════════════════════════════════════════════════════════════

    private void BeginPhotoModeStep()
    {
        flow2Sub = Flow2SubStep.AwaitingPhoto;
        HidePrimaryButton();
        ShowDimAndPanel(true);
        SetInstructionText("Snap a photo of your garden!");

        Button photoButton = PhotoModeManager.Instance != null ? PhotoModeManager.Instance.photoButton : null;
        SetActive(photoButton, true);
        if (photoButton != null)
        {
            RectTransform rect = photoButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
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
        flow2Sub = Flow2SubStep.AwaitingPreviewClose;
        SetInstructionText("Share or save it, then close the preview!");
        HideDimOverlayOnly();
    }

    private void HandlePhotoPreviewClosed()
    {
        if (stage != OnboardingStage.Flow2InProgress || flow2Sub != Flow2SubStep.AwaitingPreviewClose) return;

        flow2Sub = Flow2SubStep.AwaitingInspectorTap;
        ShowDimAndPanel(true);
        SetInstructionText("Try Inspector Mode to fly around and admire your garden!");

        SetActive(inspectorModeButton, true);
        if (inspectorModeButton != null)
        {
            RectTransform rect = inspectorModeButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
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
        flow2Sub = Flow2SubStep.AwaitingInspectorExit;

        // Inspector mode's fly controls and camera drag both need raycasts to reach the world/joystick,
        // so don't leave a blocking dim overlay up while the player finds their way back to the ground.
        HideDimOverlayOnly();
        SetInstructionText("Come back down to the ground to continue!");

        if (playerMovementRef == null)
        {
            playerMovementRef = FindFirstObjectByType<IdyllicFantasyNature.PlayerMovement>();
        }

        if (playerMovementRef == null)
        {
            Debug.LogWarning("[GameOnboardingManager] PlayerMovement not found; skipping inspector-exit wait.");
            BeginTreasureBoxStep();
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
            BeginTreasureBoxStep();
        }
    }

    private void HandleInspectorModeChanged(bool isInspectorMode)
    {
        if (isInspectorMode) return; // only care about coming back down
        if (stage != OnboardingStage.Flow2InProgress || flow2Sub != Flow2SubStep.AwaitingInspectorExit) return;

        BeginTreasureBoxStep();
    }

    private void BeginTreasureBoxStep()
    {
        flow2Sub = Flow2SubStep.AwaitingTreasureBoxTap;
        ShowDimAndPanel(true);

        Button box = GetTreasureBoxShowButton();
        SetInstructionText("Open a Treasure Box for bonus rewards!");
        ConfigurePrimaryButton("Next", HandleTreasureBoxStepAdvance);

        SetActive(box, true);
        if (box != null)
        {
            RectTransform rect = box.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
            if (!treasureBoxTapListenerAdded)
            {
                box.onClick.AddListener(HandleTreasureBoxButtonTapped);
                treasureBoxTapListenerAdded = true;
            }
        }
    }

    private void HandleTreasureBoxButtonTapped()
    {
        HandleTreasureBoxStepAdvance();
    }

    private void HandleTreasureBoxStepAdvance()
    {
        if (stage != OnboardingStage.Flow2InProgress || flow2Sub != Flow2SubStep.AwaitingTreasureBoxTap) return;

        StopPulse();
        RestoreHighlightSorting();
        flow2Sub = Flow2SubStep.MinimapCallout;

        HideDimOverlayOnly();
        SetInstructionText("Check your minimap to find the way to the Treasure Box!");
        ConfigurePrimaryButton("Got it", HandleFlow2Complete);

        if (TreasureBoxManager.Instance != null)
        {
            TreasureBoxManager.Instance.PlayShowAnimationForTier(TreasureBoxManager.Instance.GetUpcomingTier());
        }
    }

    private void HandleFlow2Complete()
    {
        if (flow2Sub != Flow2SubStep.MinimapCallout) return;

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
        SetInstructionText("Explore the Outer Garden!");

        SetActive(outerGardenButton, true);
        if (outerGardenButton != null)
        {
            RectTransform rect = outerGardenButton.GetComponent<RectTransform>();
            HighlightUIElement(rect);
            PulseButton(rect);
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
        SetInstructionText("<color=#FFD35C><size=120%>Welcome to the Outer Garden!</size></color>\n\n"
            + "This is your wider world beyond the main garden — explore further, discover hidden "
            + "sights, and find more inspiration for what to bring home and plant. Wander freely and "
            + "enjoy the view!");
        ConfigurePrimaryButton("Finish", HandleOuterGardenIntroFinished);
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
        if (instructionText != null) instructionText.text = text;
    }

    private void ConfigurePrimaryButton(string label, System.Action onClick)
    {
        if (primaryActionButton == null) return;

        primaryActionButton.gameObject.SetActive(true);
        TMP_Text label_ = primaryActionButton.GetComponentInChildren<TMP_Text>();
        if (label_ != null) label_.text = label;

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
}
