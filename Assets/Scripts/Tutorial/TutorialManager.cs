using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum TutorialStep
{
    NotStarted,
    Welcome,
    BasicMovement,
    BasicLooking,
    OpenShop,
    SelectItem,
    PlaceItem,
    Completed
}

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    [Header("Persistence Settings")]
    private const string TUTORIAL_PREF_KEY = "TutorialCompleted_ShopPlacement";
    public bool forceStartTutorial = false; // Set to true in inspector to force play for testing
    public bool isTutorialActive { get; private set; } = false;

    [Header("UI Reference Overrides")]
    [Tooltip("If null, will look for a child image or dynamically build a pointing hand UI.")]
    public RectTransform handUi;
    [Tooltip("Overlay image that dims the rest of the screen. If null, dynamically built.")]
    public CanvasGroup dimOverlay;
    [Tooltip("Text display container for tutorial dialog instructions. If null, dynamically built.")]
    public RectTransform instructionPanel;
    [Tooltip("TMP text display element. If null, dynamically built.")]
    public TMP_Text instructionText;
    [Tooltip("Action button for advancing dialogues (Welcome/Complete). If null, dynamically built.")]
    public Button nextStepButton;
    [Tooltip("Button to skip the tutorial entirely. If null, dynamically built.")]
    public Button skipButton;

    [Header("Sorting Settings")]
    [Tooltip("Sorting order for the tutorial canvas. Target elements will be set to this order + 1 to appear highlighted.")]
    public int tutorialSortingOrder = 999;

    [Header("Hand Pointer Animation")]
    public Vector2 handOffset = new Vector2(0f, 60f); // Default offset above target
    public float bounceSpeed = 6f;
    public float bounceAmplitude = 15f;

    // Current tutorial state
    private TutorialStep currentStep = TutorialStep.NotStarted;
    private RectTransform activeHighlightTarget;
    private Canvas activeHighlightCanvas;
    private GraphicRaycaster activeHighlightRaycaster;
    private bool addedCanvasComponent = false;
    private bool addedRaycasterComponent = false;
    private Coroutine handPointerCoroutine;
    private Canvas createdCanvas; // Canvas created dynamically if references are null

    // Persistent joystick highlight override fields
    private Canvas joystickHighlightCanvas;
    private GraphicRaycaster joystickHighlightRaycaster;
    private bool joystickAddedCanvas = false;
    private bool joystickAddedRaycaster = false;

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
        // Subscribe to events from Shop and Placement managers
        SubscribeEvents();

        // Check if tutorial has already been completed
        bool completedBefore = PlayerPrefs.GetInt(TUTORIAL_PREF_KEY, 0) == 1;

        if (forceStartTutorial || !completedBefore)
        {
            StartTutorial();
        }
        else
        {
            // Clean/disable UI components just in case
            DeactivateTutorialUI();
        }
    }

    private void Update()
    {
        // Support debug restart
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame && Keyboard.current.ctrlKey.isPressed)
        {
            Debug.Log("[TutorialManager] Force restarting tutorial via debug keyboard shortcut.");
            StartTutorial();
        }
#else
        if (Input.GetKeyDown(KeyCode.T) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            Debug.Log("[TutorialManager] Force restarting tutorial via debug keyboard shortcut.");
            StartTutorial();
        }
#endif
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
        RestoreHighlightSorting();
        RestoreJoystickHighlight();
    }

    private void SubscribeEvents()
    {
        InGameShopManager.OnShopOpened += HandleShopOpened;
        InGameShopManager.OnShopClosed += HandleShopClosed;
        InGameShopManager.OnShopItemUsed += HandleShopItemUsed;
        ItemPlacementManager.OnItemPlaced += HandleItemPlaced;
    }

    private void UnsubscribeEvents()
    {
        InGameShopManager.OnShopOpened -= HandleShopOpened;
        InGameShopManager.OnShopClosed -= HandleShopClosed;
        InGameShopManager.OnShopItemUsed -= HandleShopItemUsed;
        ItemPlacementManager.OnItemPlaced -= HandleItemPlaced;
    }

    /// <summary>
    /// Initiates the tutorial sequence.
    /// </summary>
    public void StartTutorial()
    {
        isTutorialActive = true;
        currentStep = TutorialStep.Welcome;
        
        // Ensure UI elements exist, build dynamic fallbacks if needed
        EnsureUIRequirements();

        // Set initial state
        SetStep(TutorialStep.Welcome);
    }

    /// <summary>
    /// Skips the tutorial sequence and saves the completed state.
    /// </summary>
    public void SkipTutorial()
    {
        if (!isTutorialActive) return;
        
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
        
        Debug.Log("[TutorialManager] Tutorial skipped by the player.");
        CompleteAndSaveTutorial();
    }

    /// <summary>
    /// Completes the tutorial sequence, saves progress, and cleans up overlays.
    /// </summary>
    public void CompleteAndSaveTutorial()
    {
        isTutorialActive = false;
        currentStep = TutorialStep.Completed;
        
        PlayerPrefs.SetInt(TUTORIAL_PREF_KEY, 1);
        PlayerPrefs.Save();

        DeactivateTutorialUI();
        RestoreHighlightSorting();
        RestoreJoystickHighlight();

        Debug.Log("[TutorialManager] Tutorial successfully completed and saved.");
    }

    private void SetStep(TutorialStep step)
    {
        currentStep = step;
        RestoreHighlightSorting();

        if (step != TutorialStep.BasicMovement)
        {
            RestoreJoystickHighlight();
        }

        if (!isTutorialActive) return;

        // Ensure panels are active
        if (dimOverlay != null)
        {
            dimOverlay.gameObject.SetActive(true);
            dimOverlay.blocksRaycasts = (step != TutorialStep.BasicLooking);
        }
        if (instructionPanel != null) instructionPanel.gameObject.SetActive(true);

        switch (step)
        {
            case TutorialStep.Welcome:
                ShowWelcomeStep();
                break;
            case TutorialStep.BasicMovement:
                ShowBasicMovementStep();
                break;
            case TutorialStep.BasicLooking:
                ShowBasicLookingStep();
                break;
            case TutorialStep.OpenShop:
                ShowOpenShopStep();
                break;
            case TutorialStep.SelectItem:
                ShowSelectItemStep();
                break;
            case TutorialStep.PlaceItem:
                ShowPlaceItemStep();
                break;
            case TutorialStep.Completed:
                ShowCompleteStep();
                break;
        }
    }

    #region Step Implementations

    private void ShowWelcomeStep()
    {
        if (instructionText != null)
        {
            instructionText.text = "<color=#FFB300><size=120%>Welcome to Jannah Garden!</color></size>\n\nLet's learn how to grow your garden. Tap <b>Start</b> to begin the tutorial!";
        }

        if (nextStepButton != null)
        {
            nextStepButton.gameObject.SetActive(true);
            var txt = nextStepButton.GetComponentInChildren<TMP_Text>();
            if (txt != null) txt.text = "Start";

            nextStepButton.onClick.RemoveAllListeners();
            nextStepButton.onClick.AddListener(() => {
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
                SetStep(TutorialStep.BasicMovement);
            });
        }

        if (skipButton != null) skipButton.gameObject.SetActive(true);

        // Hide hand during intro
        StopHandPointerAnimation();
    }

    private void ShowBasicMovementStep()
    {
        if (instructionText != null)
        {
            instructionText.text = "Use the <b>Virtual Joystick</b> on the left side of the screen to move your character around.";
        }

        if (nextStepButton != null)
        {
            nextStepButton.gameObject.SetActive(true);
            var txt = nextStepButton.GetComponentInChildren<TMP_Text>();
            if (txt != null) txt.text = "Next";

            nextStepButton.onClick.RemoveAllListeners();
            nextStepButton.onClick.AddListener(() => {
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
                SetStep(TutorialStep.BasicLooking);
            });
        }

        if (skipButton != null) skipButton.gameObject.SetActive(true);

        // Find the joystick and highlight it
        Joystick joystick = FindObjectOfType<Joystick>();
        if (joystick != null)
        {
            RectTransform joystickRect = joystick.GetComponent<RectTransform>();
            HighlightJoystickPermanently(joystickRect);
            
            // Point specifically to the joystick's handle if available, otherwise fallback to the base
            RectTransform pointerTarget = joystick.Handle != null ? joystick.Handle : joystickRect;
            StartHandPointerAnimation(pointerTarget);
        }
        else
        {
            Debug.LogWarning("[TutorialManager] Joystick reference not found in the scene.");
            StopHandPointerAnimation();
        }
    }

    private void ShowBasicLookingStep()
    {
        if (instructionText != null)
        {
            instructionText.text = "Swipe/drag anywhere on the screen to look around and control the camera view.";
        }

        if (nextStepButton != null)
        {
            nextStepButton.gameObject.SetActive(true);
            var txt = nextStepButton.GetComponentInChildren<TMP_Text>();
            if (txt != null) txt.text = "Next";

            nextStepButton.onClick.RemoveAllListeners();
            nextStepButton.onClick.AddListener(() => {
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
                SetStep(TutorialStep.OpenShop);
            });
        }

        if (skipButton != null) skipButton.gameObject.SetActive(true);

        // Looking around doesn't target a specific UI button
        StopHandPointerAnimation();
        RestoreHighlightSorting();
    }

    private void ShowOpenShopStep()
    {
        if (instructionText != null)
        {
            instructionText.text = "First, tap the <b>Shop Button</b> in the corner to open the Garden Shop.";
        }

        if (nextStepButton != null) nextStepButton.gameObject.SetActive(false); // Player must click shop button instead
        if (skipButton != null) skipButton.gameObject.SetActive(true);

        // Highlight the shop open button
        if (InGameShopManager.Instance != null && InGameShopManager.Instance.openCloseButton != null)
        {
            RectTransform shopBtnRect = InGameShopManager.Instance.openCloseButton.GetComponent<RectTransform>();
            HighlightUIElement(shopBtnRect);
            StartHandPointerAnimation(shopBtnRect);
        }
        else
        {
            Debug.LogWarning("[TutorialManager] OpenShop: shop open button reference missing. Advancing step.");
            SetStep(TutorialStep.SelectItem);
        }
    }

    private void ShowSelectItemStep()
    {
        if (instructionText != null)
        {
            instructionText.text = "Great! Now select the first item in the shop and click <b>Purchase</b> using Noor Coins.";
        }

        if (nextStepButton != null) nextStepButton.gameObject.SetActive(false);
        if (skipButton != null) skipButton.gameObject.SetActive(true);

        // Polling loop to wait for shop panel dynamic items to spawn
        StartCoroutine(HighlightFirstShopItemRoutine());
    }

    private IEnumerator HighlightFirstShopItemRoutine()
    {
        StopHandPointerAnimation();
        
        List<ShopItemUI> spawnedItems = null;
        float elapsed = 0f;
        while ((spawnedItems == null || spawnedItems.Count == 0) && elapsed < 5f)
        {
            if (InGameShopManager.Instance != null)
            {
                spawnedItems = InGameShopManager.Instance.GetSpawnedShopItemUIs();
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (spawnedItems != null && spawnedItems.Count > 0)
        {
            ShopItemUI firstItem = spawnedItems[0];
            if (firstItem != null && firstItem.purchaseButton != null)
            {
                RectTransform itemRect = firstItem.GetComponent<RectTransform>();
                RectTransform purchaseBtnRect = firstItem.purchaseButton.GetComponent<RectTransform>();
                HighlightUIElement(itemRect);
                StartHandPointerAnimation(purchaseBtnRect);
                yield break;
            }
        }

        Debug.LogWarning("[TutorialManager] Could not find shop purchase button. Skipping selection step.");
        SetStep(TutorialStep.PlaceItem);
    }

    private void ShowPlaceItemStep()
    {
        if (instructionText != null)
        {
            instructionText.text = "The placement preview is visible at the center of the screen.\n\nLook around using camera controls and tap <b>Place</b> when ready!";
        }

        if (nextStepButton != null) nextStepButton.gameObject.SetActive(false);
        if (skipButton != null) skipButton.gameObject.SetActive(true);

        // Highlight place button in ItemPlacementManager
        if (ItemPlacementManager.Instance != null && ItemPlacementManager.Instance.placeButton != null)
        {
            RectTransform placeBtnRect = ItemPlacementManager.Instance.placeButton.GetComponent<RectTransform>();
            HighlightUIElement(placeBtnRect);
            StartHandPointerAnimation(placeBtnRect);
        }
        else
        {
            Debug.LogWarning("[TutorialManager] Place button reference missing. Skipping placement step.");
            SetStep(TutorialStep.Completed);
        }
    }

    private void ShowCompleteStep()
    {
        if (instructionText != null)
        {
            instructionText.text = "<color=#4CAF50><size=120%>Perfect Sequence!</color></size>\n\nYou have successfully placed your first plant! Grow your garden by completing quizzes and purchasing items. Click <b>Finish</b> to play!";
        }

        if (nextStepButton != null)
        {
            nextStepButton.gameObject.SetActive(true);
            var txt = nextStepButton.GetComponentInChildren<TMP_Text>();
            if (txt != null) txt.text = "Finish";

            nextStepButton.onClick.RemoveAllListeners();
            nextStepButton.onClick.AddListener(() => {
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
                CompleteAndSaveTutorial();
            });
        }

        if (skipButton != null) skipButton.gameObject.SetActive(false);

        StopHandPointerAnimation();
    }

    #endregion

    #region Event Handlers

    private void HandleShopOpened()
    {
        if (isTutorialActive && currentStep == TutorialStep.OpenShop)
        {
            SetStep(TutorialStep.SelectItem);
        }
    }

    private void HandleShopClosed()
    {
        // If the player accidentally closes shop during the purchase step, bring them back to shop open
        if (isTutorialActive && currentStep == TutorialStep.SelectItem)
        {
            SetStep(TutorialStep.OpenShop);
        }
    }

    private void HandleShopItemUsed(ShopItemData itemData)
    {
        if (isTutorialActive && currentStep == TutorialStep.SelectItem)
        {
            SetStep(TutorialStep.PlaceItem);
        }
    }

    private void HandleItemPlaced(PlaceableItem placedItem)
    {
        if (isTutorialActive && currentStep == TutorialStep.PlaceItem)
        {
            SetStep(TutorialStep.Completed);
        }
    }

    #endregion

    #region Highlight/Overlay Utility

    private void HighlightUIElement(RectTransform target)
    {
        RestoreHighlightSorting();

        if (target == null) return;

        activeHighlightTarget = target;

        // Apply temporary canvas sorting override
        activeHighlightCanvas = target.GetComponent<Canvas>();
        if (activeHighlightCanvas == null)
        {
            activeHighlightCanvas = target.gameObject.AddComponent<Canvas>();
            addedCanvasComponent = true;
        }
        else
        {
            addedCanvasComponent = false;
        }

        activeHighlightCanvas.overrideSorting = true;
        activeHighlightCanvas.sortingOrder = tutorialSortingOrder + 1;

        // Apply temporary graphic raycaster so buttons remain clickable in the sorting override
        activeHighlightRaycaster = target.GetComponent<GraphicRaycaster>();
        if (activeHighlightRaycaster == null)
        {
            activeHighlightRaycaster = target.gameObject.AddComponent<GraphicRaycaster>();
            addedRaycasterComponent = true;
        }
        else
        {
            addedRaycasterComponent = false;
        }
    }

    private void RestoreHighlightSorting()
    {
        if (activeHighlightTarget != null)
        {
            if (addedRaycasterComponent && activeHighlightRaycaster != null)
            {
                Destroy(activeHighlightRaycaster);
            }
            if (addedCanvasComponent && activeHighlightCanvas != null)
            {
                Destroy(activeHighlightCanvas);
            }
            else if (activeHighlightCanvas != null)
            {
                activeHighlightCanvas.overrideSorting = false;
            }

            activeHighlightTarget = null;
            activeHighlightCanvas = null;
            activeHighlightRaycaster = null;
            addedCanvasComponent = false;
            addedRaycasterComponent = false;
        }
    }

    private void DeactivateTutorialUI()
    {
        if (dimOverlay != null) dimOverlay.gameObject.SetActive(false);
        if (instructionPanel != null) instructionPanel.gameObject.SetActive(false);
        if (handUi != null) handUi.gameObject.SetActive(false);
        StopHandPointerAnimation();
    }

    #endregion

    #region Hand Pointer Animation

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
            
            // Calculate screen point of target
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, targetWorldPos);
            
            // Translate to local point inside hand parent RectTransform
            RectTransform parentRect = handUi.parent as RectTransform;
            if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, null, out Vector2 localPoint))
            {
                // Bouncing sine animation offset
                float bounce = Mathf.Sin(Time.time * bounceSpeed) * bounceAmplitude;
                handUi.anchoredPosition = localPoint + handOffset + new Vector2(0f, bounce);
            }

            yield return null;
        }
        
        if (handUi != null) handUi.gameObject.SetActive(false);
    }

    #endregion

    #region Dynamic UI Builder (Fallback)

    /// <summary>
    /// Assures all necessary UI variables exist in the scene.
    /// If references are missing from inspector, looks for children or dynamically builds them.
    /// </summary>
    private void EnsureUIRequirements()
    {
        // 1. Try to find existing structures inside this GameObject or Children
        if (handUi == null) handUi = transform.Find("HandUI") as RectTransform;
        if (dimOverlay == null) dimOverlay = GetComponentInChildren<CanvasGroup>();
        if (instructionPanel == null) instructionPanel = transform.Find("InstructionPanel") as RectTransform;
        if (instructionText == null && instructionPanel != null) instructionText = instructionPanel.GetComponentInChildren<TMP_Text>();

        // Ensure the skip button click listener is registered if it is pre-assigned
        if (skipButton != null)
        {
            skipButton.onClick.RemoveAllListeners();
            skipButton.onClick.AddListener(SkipTutorial);
        }

        // 2. If elements are still null, build them dynamically to ensure robustness
        if (dimOverlay == null || instructionPanel == null || handUi == null)
        {
            GameObject canvasObj = new GameObject("TutorialCanvas");
            createdCanvas = canvasObj.AddComponent<Canvas>();
            createdCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            createdCanvas.sortingOrder = tutorialSortingOrder;
            
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            
            // Keep object persistent and structured
            canvasObj.transform.SetParent(this.transform);

            // A. Create Dim Overlay
            if (dimOverlay == null)
            {
                GameObject overlay = new GameObject("DimOverlay", typeof(RectTransform));
                overlay.transform.SetParent(canvasObj.transform, false);
                
                RectTransform rect = overlay.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;

                Image img = overlay.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0.7f); // Sleek dark overlay

                dimOverlay = overlay.AddComponent<CanvasGroup>();
                dimOverlay.blocksRaycasts = true;
                dimOverlay.interactable = true;
            }

            // B. Create Instruction Panel
            if (instructionPanel == null)
            {
                GameObject panel = new GameObject("InstructionPanel", typeof(RectTransform));
                panel.transform.SetParent(canvasObj.transform, false);
                
                instructionPanel = panel.GetComponent<RectTransform>();
                instructionPanel.anchorMin = new Vector2(0.5f, 0f); // Positioned at bottom center
                instructionPanel.anchorMax = new Vector2(0.5f, 0f);
                instructionPanel.pivot = new Vector2(0.5f, 0f);
                instructionPanel.anchoredPosition = new Vector2(0f, 50f);
                instructionPanel.sizeDelta = new Vector2(420f, 180f);

                // Premium UI Design Panel styling (Deep Slate color)
                Image panelBg = panel.AddComponent<Image>();
                panelBg.color = new Color(0.12f, 0.16f, 0.22f, 0.96f); // Premium dark teal slate

                // Add Shadow/Outline
                Outline outline = panel.AddComponent<Outline>();
                outline.effectColor = new Color(0.3f, 0.4f, 0.5f, 0.5f);
                outline.effectDistance = new Vector2(2f, -2f);

                // Text UI child
                GameObject textObj = new GameObject("InstructionText", typeof(RectTransform));
                textObj.transform.SetParent(instructionPanel.transform, false);
                
                RectTransform textRect = textObj.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(20f, 50f); // Padding for buttons at bottom
                textRect.offsetMax = new Vector2(-20f, -20f);

                instructionText = textObj.AddComponent<TextMeshProUGUI>();
                instructionText.fontSize = 18;
                instructionText.color = Color.white;
                instructionText.alignment = TextAlignmentOptions.Center;
                instructionText.enableWordWrapping = true;

                // Action Next/Start Button
                GameObject btnObj = new GameObject("NextButton", typeof(RectTransform));
                btnObj.transform.SetParent(instructionPanel.transform, false);
                
                RectTransform btnRect = btnObj.GetComponent<RectTransform>();
                btnRect.anchorMin = new Vector2(0.7f, 0f);
                btnRect.anchorMax = new Vector2(0.7f, 0f);
                btnRect.pivot = new Vector2(0.5f, 0f);
                btnRect.anchoredPosition = new Vector2(0f, 12f);
                btnRect.sizeDelta = new Vector2(100f, 32f);

                Image btnImg = btnObj.AddComponent<Image>();
                btnImg.color = new Color(0.15f, 0.68f, 0.37f); // Emerald green

                nextStepButton = btnObj.AddComponent<Button>();
                
                GameObject btnTextObj = new GameObject("Text", typeof(RectTransform));
                btnTextObj.transform.SetParent(btnObj.transform, false);
                RectTransform btnTextRect = btnTextObj.GetComponent<RectTransform>();
                btnTextRect.anchorMin = Vector2.zero;
                btnTextRect.anchorMax = Vector2.one;
                btnTextRect.sizeDelta = Vector2.zero;
                
                TMP_Text btnText = btnTextObj.AddComponent<TextMeshProUGUI>();
                btnText.fontSize = 14;
                btnText.color = Color.white;
                btnText.alignment = TextAlignmentOptions.Center;
                btnText.text = "Next";

                // Action Skip Button
                GameObject skipObj = new GameObject("SkipButton", typeof(RectTransform));
                skipObj.transform.SetParent(instructionPanel.transform, false);
                
                RectTransform skipRect = skipObj.GetComponent<RectTransform>();
                skipRect.anchorMin = new Vector2(0.3f, 0f);
                skipRect.anchorMax = new Vector2(0.3f, 0f);
                skipRect.pivot = new Vector2(0.5f, 0f);
                skipRect.anchoredPosition = new Vector2(0f, 12f);
                skipRect.sizeDelta = new Vector2(100f, 32f);

                Image skipImg = skipObj.AddComponent<Image>();
                skipImg.color = new Color(0.75f, 0.22f, 0.16f); // Soft red

                skipButton = skipObj.AddComponent<Button>();
                skipButton.onClick.AddListener(SkipTutorial);

                GameObject skipTextObj = new GameObject("Text", typeof(RectTransform));
                skipTextObj.transform.SetParent(skipObj.transform, false);
                RectTransform skipTextRect = skipTextObj.GetComponent<RectTransform>();
                skipTextRect.anchorMin = Vector2.zero;
                skipTextRect.anchorMax = Vector2.one;
                skipTextRect.sizeDelta = Vector2.zero;
                
                TMP_Text skipText = skipTextObj.AddComponent<TextMeshProUGUI>();
                skipText.fontSize = 14;
                skipText.color = Color.white;
                skipText.alignment = TextAlignmentOptions.Center;
                skipText.text = "Skip";
            }

            // C. Create Animated Hand Pointer UI
            if (handUi == null)
            {
                GameObject handObj = new GameObject("TutorialHand", typeof(RectTransform));
                handObj.transform.SetParent(canvasObj.transform, false);
                
                handUi = handObj.GetComponent<RectTransform>();
                handUi.anchorMin = Vector2.zero;
                handUi.anchorMax = Vector2.zero;
                handUi.pivot = new Vector2(0.5f, 1f); // Point from top center
                handUi.sizeDelta = new Vector2(40f, 40f);

                // Make a geometric triangle pointing down as a high-fidelity visual cursor
                Image handImg = handObj.AddComponent<Image>();
                handImg.color = new Color(0.95f, 0.77f, 0.06f); // Golden yellow
                
                // Add a rotated sub-square to make the arrow look like an arrowhead pointing down
                GameObject arrowhead = new GameObject("Head", typeof(RectTransform));
                arrowhead.transform.SetParent(handObj.transform, false);
                RectTransform headRect = arrowhead.GetComponent<RectTransform>();
                headRect.anchorMin = new Vector2(0.5f, 0f);
                headRect.anchorMax = new Vector2(0.5f, 0f);
                headRect.pivot = new Vector2(0.5f, 0.5f);
                headRect.anchoredPosition = new Vector2(0f, 0f);
                headRect.sizeDelta = new Vector2(25f, 25f);
                headRect.localRotation = Quaternion.Euler(0f, 0f, 45f); // Diamond orientation

                Image headImg = arrowhead.AddComponent<Image>();
                headImg.color = new Color(0.95f, 0.77f, 0.06f);

                // Shaft
                GameObject shaft = new GameObject("Shaft", typeof(RectTransform));
                shaft.transform.SetParent(handObj.transform, false);
                RectTransform shaftRect = shaft.GetComponent<RectTransform>();
                shaftRect.anchorMin = new Vector2(0.5f, 1f);
                shaftRect.anchorMax = new Vector2(0.5f, 1f);
                shaftRect.pivot = new Vector2(0.5f, 1f);
                shaftRect.anchoredPosition = new Vector2(0f, 0f);
                shaftRect.sizeDelta = new Vector2(10f, 30f);

                Image shaftImg = shaft.AddComponent<Image>();
                shaftImg.color = new Color(0.95f, 0.77f, 0.06f);
            }
        }

        // Ensure the hand UI renders on top of highlighted elements (sorting order + 2) and doesn't block raycasts
        if (handUi != null)
        {
            Canvas handCanvas = handUi.GetComponent<Canvas>();
            if (handCanvas == null)
            {
                handCanvas = handUi.gameObject.AddComponent<Canvas>();
            }
            handCanvas.overrideSorting = true;
            handCanvas.sortingOrder = tutorialSortingOrder + 2;

            Image[] handImages = handUi.GetComponentsInChildren<Image>(true);
            foreach (var img in handImages)
            {
                img.raycastTarget = false;
            }
        }
    }

    private void HighlightJoystickPermanently(RectTransform target)
    {
        if (target == null || joystickHighlightCanvas != null) return;

        // Find the Joystick component on target or parent/children
        Joystick joystick = target.GetComponentInParent<Joystick>();
        if (joystick != null)
        {
            joystick.KeepBackgroundVisible = true;
            if (joystick.Background != null)
            {
                joystick.Background.gameObject.SetActive(true);
            }
        }

        // Apply temporary canvas sorting override
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
        joystickHighlightCanvas.sortingOrder = tutorialSortingOrder + 1;

        // Apply temporary graphic raycaster so buttons remain clickable in the sorting override
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
        if (joystickHighlightCanvas != null)
        {
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

            if (joystickAddedRaycaster && joystickHighlightRaycaster != null)
            {
                Destroy(joystickHighlightRaycaster);
            }
            if (joystickAddedCanvas)
            {
                Destroy(joystickHighlightCanvas);
            }
            else
            {
                joystickHighlightCanvas.overrideSorting = false;
            }

            joystickHighlightCanvas = null;
            joystickHighlightRaycaster = null;
            joystickAddedCanvas = false;
            joystickAddedRaycaster = false;
        }
    }

    #endregion
}
