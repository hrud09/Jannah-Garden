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

    private enum OnboardingStage
    {
        NotStarted = 0,
        Flow1InProgress = 1,
        Flow2InProgress = 3,
        WaitingForFlow3Trigger = 4,
        Flow3InProgress = 5,
        Completed = 6
    }

    private enum Flow1SubStep { None, AwaitingShopOpen, AwaitingItemSelect, AwaitingDownload, AwaitingPlace, AwaitingXPTap }
    private enum Flow2SubStep { None, AwaitingPhoto, AwaitingPreviewClose, AwaitingInspectorTap, AwaitingTreasureBoxTap, MinimapCallout }

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

    [Header("Sorting")]
    public int overlaySortingOrder = 999;

    [Header("Pulse Animation")]
    public float pulseScale = 1.12f;
    public float pulseDuration = 0.5f;

    private OnboardingStage stage;
    private Flow1SubStep flow1Sub = Flow1SubStep.None;
    private Flow2SubStep flow2Sub = Flow2SubStep.None;
    private bool minimapExpandedSinceWaiting;

    private RectTransform highlightTarget;
    private Canvas highlightCanvas;
    private GraphicRaycaster highlightRaycaster;
    private bool addedHighlightCanvas;
    private bool addedHighlightRaycaster;

    private Tween pulseTween;
    private RectTransform pulseTarget;

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
        if (instructionPanel != null) instructionPanel.gameObject.SetActive(false);

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

        bool flow2Done = stage >= OnboardingStage.WaitingForFlow3Trigger;
        SetActive(inspectorModeButton, flow2Done);
        SetActive(GetTreasureBoxShowButton(), flow2Done);

        SetActive(outerGardenButton, stage >= OnboardingStage.Flow3InProgress);
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
                break; // WaitingForFlow3Trigger / Completed - free play
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
        MinimapBehaviour.OnExpanded += HandleMinimapExpanded;
        MinimapBehaviour.OnCollapsed += HandleMinimapCollapsed;
        TreasureBoxManager.OnBoxOpened += HandleTreasureBoxOpened;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void UnsubscribeEvents()
    {
        InGameShopManager.OnShopOpened -= HandleShopOpened;
        InGameShopManager.OnShopClosed -= HandleShopClosed;
        InGameShopManager.OnShopItemUsed -= HandleShopItemUsed;
        ItemPlacementManager.OnItemPlaced -= HandleItemPlaced;
        PhotoModeManager.OnPreviewClosed -= HandlePhotoPreviewClosed;
        MinimapBehaviour.OnExpanded -= HandleMinimapExpanded;
        MinimapBehaviour.OnCollapsed -= HandleMinimapCollapsed;
        TreasureBoxManager.OnBoxOpened -= HandleTreasureBoxOpened;
        SceneManager.sceneLoaded -= HandleSceneLoaded;

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
        ShowDimAndPanel(false);
        instructionPanel?.gameObject.SetActive(true);
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
        ShowDimAndPanel(true);
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
        ShowDimAndPanel(false);
        flow1Sub = Flow1SubStep.None;

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
        ShowDimAndPanel(false);
        instructionPanel?.gameObject.SetActive(true);
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
        flow2Sub = Flow2SubStep.AwaitingTreasureBoxTap;

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

        ShowDimAndPanel(false);
        instructionPanel?.gameObject.SetActive(true);
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
        instructionPanel?.gameObject.SetActive(false);
        SetStage(OnboardingStage.WaitingForFlow3Trigger);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Passive wait -> Flow 3 - Outer Garden
    // ═══════════════════════════════════════════════════════════════════════

    private void HandleMinimapExpanded()
    {
        if (stage == OnboardingStage.WaitingForFlow3Trigger) minimapExpandedSinceWaiting = true;
    }

    private void HandleMinimapCollapsed()
    {
        if (stage == OnboardingStage.WaitingForFlow3Trigger && minimapExpandedSinceWaiting)
        {
            TriggerFlow3();
        }
    }

    private void HandleTreasureBoxOpened(TreasureBoxTier tier, int slotIndex, TreasureBoxData data)
    {
        if (stage == OnboardingStage.WaitingForFlow3Trigger)
        {
            TriggerFlow3();
        }
    }

    private void TriggerFlow3()
    {
        if (stage != OnboardingStage.WaitingForFlow3Trigger) return;

        SetStage(OnboardingStage.Flow3InProgress);
        BeginOuterGardenStep();
    }

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
        instructionPanel?.gameObject.SetActive(false);

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
        instructionPanel?.gameObject.SetActive(false);
        SetStage(OnboardingStage.Completed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UI helpers
    // ═══════════════════════════════════════════════════════════════════════

    private void ShowDimAndPanel(bool show)
    {
        if (dimOverlay != null)
        {
            dimOverlay.gameObject.SetActive(show);
            dimOverlay.blocksRaycasts = show;
        }
        if (instructionPanel != null) instructionPanel.gameObject.SetActive(show);
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
