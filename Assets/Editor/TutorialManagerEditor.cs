using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

[CustomEditor(typeof(TutorialManager))]
public class TutorialManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw default inspector fields
        DrawDefaultInspector();

        TutorialManager tutorialManager = (TutorialManager)target;

        GUILayout.Space(15);
        GUI.backgroundColor = new Color(0.15f, 0.68f, 0.37f); // Premium Emerald Green
        if (GUILayout.Button("Create & Assign UI References", GUILayout.Height(35)))
        {
            CreateTutorialUI(tutorialManager);
        }
        GUI.backgroundColor = Color.white;
    }

    private void CreateTutorialUI(TutorialManager manager)
    {
        // 1. Check if a TutorialCanvas child already exists
        Transform existingCanvas = manager.transform.Find("TutorialCanvas");
        if (existingCanvas != null)
        {
            bool deleteExisting = EditorUtility.DisplayDialog(
                "Tutorial Canvas Already Exists",
                "A child GameObject named 'TutorialCanvas' was found under the TutorialManager. Do you want to delete it and create a fresh one?",
                "Yes, Delete & Recreate",
                "Cancel"
            );
            if (deleteExisting)
            {
                Undo.DestroyObjectImmediate(existingCanvas.gameObject);
            }
            else
            {
                return;
            }
        }
        else
        {
            // If no child canvas exists, but some references are assigned, double check before overwriting
            bool hasAnyReference = manager.handUi != null || manager.dimOverlay != null || 
                                   manager.instructionPanel != null || manager.instructionText != null || 
                                   manager.nextStepButton != null || manager.skipButton != null;
            if (hasAnyReference)
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Overwrite UI References",
                    "Some UI references on the Tutorial Manager are already assigned. Do you want to clear them and create a new Tutorial Canvas?",
                    "Yes, Overwrite",
                    "Cancel"
                );
                if (!overwrite)
                {
                    return;
                }
            }
        }

        // 2. Start recording undo operations
        Undo.IncrementCurrentGroup();
        int groupIndex = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Tutorial UI");

        Undo.RecordObject(manager, "Assign Tutorial UI References");

        // 3. Create Tutorial Canvas
        GameObject canvasObj = new GameObject("TutorialCanvas");
        canvasObj.transform.SetParent(manager.transform, false);
        Undo.RegisterCreatedObjectUndo(canvasObj, "Create Tutorial Canvas");

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = manager.tutorialSortingOrder;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // 4. Create Dim Overlay
        GameObject overlayObj = new GameObject("DimOverlay", typeof(RectTransform));
        overlayObj.transform.SetParent(canvasObj.transform, false);
        
        RectTransform overlayRect = overlayObj.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.sizeDelta = Vector2.zero;
        overlayRect.anchoredPosition = Vector2.zero;

        Image overlayImage = overlayObj.AddComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.7f); // Sleek semi-transparent dark overlay

        CanvasGroup overlayGroup = overlayObj.AddComponent<CanvasGroup>();
        overlayGroup.blocksRaycasts = true;
        overlayGroup.interactable = true;
        manager.dimOverlay = overlayGroup;

        // 5. Create Instruction Panel
        GameObject panelObj = new GameObject("InstructionPanel", typeof(RectTransform));
        panelObj.transform.SetParent(canvasObj.transform, false);
        
        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f); // Bottom center
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 50f);
        panelRect.sizeDelta = new Vector2(420f, 180f);

        Image panelBg = panelObj.AddComponent<Image>();
        panelBg.color = new Color(0.12f, 0.16f, 0.22f, 0.96f); // Premium dark teal slate

        Outline outline = panelObj.AddComponent<Outline>();
        outline.effectColor = new Color(0.3f, 0.4f, 0.5f, 0.5f);
        outline.effectDistance = new Vector2(2f, -2f);
        manager.instructionPanel = panelRect;

        // 6. Create Instruction Text (inside Panel)
        GameObject textObj = new GameObject("InstructionText", typeof(RectTransform));
        textObj.transform.SetParent(panelRect, false);
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(20f, 50f); // Leaves 50px at the bottom for buttons
        textRect.offsetMax = new Vector2(-20f, -20f);

        TextMeshProUGUI instText = textObj.AddComponent<TextMeshProUGUI>();
        instText.fontSize = 18;
        instText.color = Color.white;
        instText.alignment = TextAlignmentOptions.Center;
        instText.enableWordWrapping = true;
        instText.text = "Welcome to Jannah Garden!";
        manager.instructionText = instText;

        // 7. Create Skip Button (inside Panel)
        GameObject skipBtnObj = new GameObject("SkipButton", typeof(RectTransform));
        skipBtnObj.transform.SetParent(panelRect, false);
        
        RectTransform skipBtnRect = skipBtnObj.GetComponent<RectTransform>();
        skipBtnRect.anchorMin = new Vector2(0.3f, 0f);
        skipBtnRect.anchorMax = new Vector2(0.3f, 0f);
        skipBtnRect.pivot = new Vector2(0.5f, 0f);
        skipBtnRect.anchoredPosition = new Vector2(0f, 12f);
        skipBtnRect.sizeDelta = new Vector2(100f, 32f);

        Image skipImg = skipBtnObj.AddComponent<Image>();
        skipImg.color = new Color(0.75f, 0.22f, 0.16f); // Soft red

        Button skipBtn = skipBtnObj.AddComponent<Button>();
        manager.skipButton = skipBtn;

        // Skip Button Text
        GameObject skipTxtObj = new GameObject("Text", typeof(RectTransform));
        skipTxtObj.transform.SetParent(skipBtnRect, false);
        
        RectTransform skipTxtRect = skipTxtObj.GetComponent<RectTransform>();
        skipTxtRect.anchorMin = Vector2.zero;
        skipTxtRect.anchorMax = Vector2.one;
        skipTxtRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI skipTxt = skipTxtObj.AddComponent<TextMeshProUGUI>();
        skipTxt.fontSize = 14;
        skipTxt.color = Color.white;
        skipTxt.alignment = TextAlignmentOptions.Center;
        skipTxt.text = "Skip";

        // 8. Create Next Button (inside Panel)
        GameObject nextBtnObj = new GameObject("NextButton", typeof(RectTransform));
        nextBtnObj.transform.SetParent(panelRect, false);
        
        RectTransform nextBtnRect = nextBtnObj.GetComponent<RectTransform>();
        nextBtnRect.anchorMin = new Vector2(0.7f, 0f);
        nextBtnRect.anchorMax = new Vector2(0.7f, 0f);
        nextBtnRect.pivot = new Vector2(0.5f, 0f);
        nextBtnRect.anchoredPosition = new Vector2(0f, 12f);
        nextBtnRect.sizeDelta = new Vector2(100f, 32f);

        Image nextImg = nextBtnObj.AddComponent<Image>();
        nextImg.color = new Color(0.15f, 0.68f, 0.37f); // Emerald green

        Button nextBtn = nextBtnObj.AddComponent<Button>();
        manager.nextStepButton = nextBtn;

        // Next Button Text
        GameObject nextTxtObj = new GameObject("Text", typeof(RectTransform));
        nextTxtObj.transform.SetParent(nextBtnRect, false);
        
        RectTransform nextTxtRect = nextTxtObj.GetComponent<RectTransform>();
        nextTxtRect.anchorMin = Vector2.zero;
        nextTxtRect.anchorMax = Vector2.one;
        nextTxtRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI nextTxt = nextTxtObj.AddComponent<TextMeshProUGUI>();
        nextTxt.fontSize = 14;
        nextTxt.color = Color.white;
        nextTxt.alignment = TextAlignmentOptions.Center;
        nextTxt.text = "Next";

        // 9. Create Hand Pointer UI
        GameObject handObj = new GameObject("TutorialHand", typeof(RectTransform));
        handObj.transform.SetParent(canvasObj.transform, false);
        
        RectTransform handRect = handObj.GetComponent<RectTransform>();
        handRect.anchorMin = Vector2.zero;
        handRect.anchorMax = Vector2.zero;
        handRect.pivot = new Vector2(0.5f, 1f); // Point from top center
        handRect.sizeDelta = new Vector2(40f, 40f);

        // Add Canvas component for sorting override so it renders on top of highlighted elements (which are at sortingOrder + 1)
        Canvas handCanvas = handObj.AddComponent<Canvas>();
        handCanvas.overrideSorting = true;
        handCanvas.sortingOrder = manager.tutorialSortingOrder + 2;

        Image handImg = handObj.AddComponent<Image>();
        handImg.color = new Color(0.95f, 0.77f, 0.06f); // Golden yellow
        handImg.raycastTarget = false;
        manager.handUi = handRect;

        // Hand UI Head
        GameObject arrowhead = new GameObject("Head", typeof(RectTransform));
        arrowhead.transform.SetParent(handRect, false);
        
        RectTransform headRect = arrowhead.GetComponent<RectTransform>();
        headRect.anchorMin = new Vector2(0.5f, 0f);
        headRect.anchorMax = new Vector2(0.5f, 0f);
        headRect.pivot = new Vector2(0.5f, 0.5f);
        headRect.anchoredPosition = Vector2.zero;
        headRect.sizeDelta = new Vector2(25f, 25f);
        headRect.localRotation = Quaternion.Euler(0f, 0f, 45f); // Rotated square

        Image headImg = arrowhead.AddComponent<Image>();
        headImg.color = new Color(0.95f, 0.77f, 0.06f);
        headImg.raycastTarget = false;

        // Hand UI Shaft
        GameObject shaft = new GameObject("Shaft", typeof(RectTransform));
        shaft.transform.SetParent(handRect, false);
        
        RectTransform shaftRect = shaft.GetComponent<RectTransform>();
        shaftRect.anchorMin = new Vector2(0.5f, 1f);
        shaftRect.anchorMax = new Vector2(0.5f, 1f);
        shaftRect.pivot = new Vector2(0.5f, 1f);
        shaftRect.anchoredPosition = Vector2.zero;
        shaftRect.sizeDelta = new Vector2(10f, 30f);

        Image shaftImg = shaft.AddComponent<Image>();
        shaftImg.color = new Color(0.95f, 0.77f, 0.06f);
        shaftImg.raycastTarget = false;

        // 10. Collapse and save undo group
        Undo.CollapseUndoOperations(groupIndex);

        // 11. Mark manager and scene as dirty for saving
        EditorUtility.SetDirty(manager);
        if (!Application.isPlaying)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }

        Debug.Log("[TutorialManagerEditor] Successfully created and assigned tutorial UI elements. Use Ctrl+Z to undo.");
        EditorUtility.DisplayDialog(
            "Success",
            "Tutorial Canvas and UI elements created successfully as children of TutorialManager!\n\nAll references have been automatically assigned.",
            "OK"
        );
    }
}
