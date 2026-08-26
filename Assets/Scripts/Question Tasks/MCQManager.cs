using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // Assuming you are using TextMeshPro for UI text

[System.Serializable]
public class QuestionData
{
    public string id;
    public int level;
    public string category;

    public int correctAnswerIndex;
    public string questionText;
    public string[] options;

    public string scholarReviewFlag;
    public string notes;
    public bool isScholarReviewed;
}

[System.Serializable]
public class QuestionList
{
    public QuestionData[] questions;
}

public class MCQManager : MonoBehaviour
{
    private static MCQManager _instance;
    public static MCQManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<MCQManager>(true);
            }
            return _instance;
        }
    }

    [Header("UI References")]
    public GameObject quizPanel; // The main UI panel containing the quiz
    public Button closeButton; // Button to close the quiz dramatically
    public GameObject blurredBG; // The blurred background behind the quiz panel
    public TextMeshProUGUI questionTextUI;
    public Button[] optionButtons;
    public TextMeshProUGUI[] optionTextsUI;
    public Button submitButton; // Optional: A button to submit the selected answer
    public TMP_Text countDownToHidePanel;

    [Header("Button Sprites")]
    public Sprite defaultSprite;
    public Sprite selectedSprite;
    public Sprite correctSprite;
    public Sprite wrongSprite;

    [Header("Data")]
    [Tooltip("Optional explicit override. Leave empty to auto-load Resources/questions_{locale}.txt for " +
             "the active locale (falling back to questions_en if that locale has no file yet).")]
    public TextAsset questionFile;

    private QuestionList allQuestions;
    private int currentQuestionIndex = 0;
    private int selectedOptionIndex = -1;
    private QuestionMarkOrb currentOrb;
    private int[] currentShuffledIndices;
    private int currentCorrectOptionIndex;
    private int currentQuestionAttempts = 0;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // Hide the panel visuals here rather than in Start so they never render for a
        // frame. Only these children are toggled — this GameObject stays active.
        if (quizPanel != null) quizPanel.SetActive(false);
        if (countDownToHidePanel != null) countDownToHidePanel.gameObject.SetActive(false);
        if (blurredBG != null) blurredBG.SetActive(false);
    }

    void Start()
    {
        LoadQuestions();
        LocalizationManager.OnLocaleChanged += LoadQuestions;
        if (submitButton != null)
        {
            submitButton.onClick.AddListener(CheckAnswer);
            submitButton.interactable = false;
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseQuizDramatically);
        }

        // Setup button listeners
        for (int i = 0; i < optionButtons.Length; i++)
        {
            int index = i; // Local copy for closure
            optionButtons[i].onClick.AddListener(() => SelectOption(index));
        }
    }

    void OnDestroy()
    {
        LocalizationManager.OnLocaleChanged -= LoadQuestions;
    }

    public void StartQuiz(QuestionMarkOrb orb = null)
    {
        currentOrb = orb;
        if (blurredBG != null) blurredBG.SetActive(true);

        if (quizPanel != null) 
        {
            quizPanel.SetActive(true);
            quizPanel.transform.DOKill();
            quizPanel.transform.localScale = Vector3.zero;
            quizPanel.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);
        }
        if (allQuestions != null && allQuestions.questions != null && allQuestions.questions.Length > 0)
        {
            int targetLevel = 1;
            if (PlayerXPManager.Instance != null)
            {
                targetLevel = PlayerXPManager.Instance.xpLevel;
            }

            int questionIndex = GetRandomQuestionIndexByLevel(targetLevel);
            
            // Fallback to any random question if no questions exist for the player's exact level
            if (questionIndex == -1)
            {
                questionIndex = UnityEngine.Random.Range(0, allQuestions.questions.Length);
            }

            ShowQuestion(questionIndex, true);
        }
    }

    void LoadQuestions()
    {
        TextAsset asset = questionFile != null ? questionFile : LocalizationManager.LoadLocalizedTextAsset("questions");

        if (asset != null)
        {
            allQuestions = JsonUtility.FromJson<QuestionList>(asset.text);
            if (questionFile == null) FillMissingQuestionsFromEnglish();
        }
        else
        {
            Debug.LogError("Question JSON file not assigned and no 'questions_{locale}' found in Resources!");
        }
    }

    /// <summary>
    /// Translation work lands incrementally, so a locale's questions_{locale}.txt can legitimately contain
    /// fewer than the full 500 entries (e.g. Bengali currently covers Levels 1-4 only). Rather than leaving
    /// every other level with zero questions for that locale, fill in any id missing from the active file
    /// using the English original, so every level always has a full question pool while translations catch up.
    /// </summary>
    void FillMissingQuestionsFromEnglish()
    {
        if (LocalizationManager.Instance != null && LocalizationManager.Instance.CurrentLocale == AppLocale.en) return;
        if (allQuestions?.questions == null) return;

        TextAsset englishAsset = Resources.Load<TextAsset>("questions_en");
        if (englishAsset == null) return;

        QuestionList englishQuestions = JsonUtility.FromJson<QuestionList>(englishAsset.text);
        if (englishQuestions?.questions == null) return;

        var presentIds = new HashSet<string>();
        foreach (var q in allQuestions.questions)
        {
            if (!string.IsNullOrEmpty(q.id)) presentIds.Add(q.id);
        }

        List<QuestionData> merged = null;
        foreach (var enQ in englishQuestions.questions)
        {
            if (presentIds.Contains(enQ.id)) continue;
            merged ??= new List<QuestionData>(allQuestions.questions);
            merged.Add(enQ);
        }

        if (merged != null) allQuestions.questions = merged.ToArray();
    }

    public QuestionData[] GetQuestionsByLevel(int level)
    {
        if (allQuestions == null || allQuestions.questions == null) return new QuestionData[0];
        List<QuestionData> filtered = new List<QuestionData>();
        foreach(var q in allQuestions.questions)
        {
            if (q.level == level) filtered.Add(q);
        }
        return filtered.ToArray();
    }

    public int GetRandomQuestionIndexByLevel(int level)
    {
        if (allQuestions == null || allQuestions.questions == null) return -1;
        List<int> indices = new List<int>();
        for (int i = 0; i < allQuestions.questions.Length; i++)
        {
            if (allQuestions.questions[i].level == level) indices.Add(i);
        }
        if (indices.Count > 0)
        {
            return indices[UnityEngine.Random.Range(0, indices.Count)];
        }
        return -1;
    }

    public void ShowQuestion(int index)
    {
        ShowQuestion(index, true);
    }

    public void ShowQuestion(int index, bool reshuffle)
    {
        if (allQuestions == null || allQuestions.questions == null || index < 0 || index >= allQuestions.questions.Length)
            return;

        currentQuestionIndex = index;
        
        if (countDownToHidePanel != null) 
            countDownToHidePanel.gameObject.SetActive(false);

        QuestionData qData = allQuestions.questions[index];

        if (qData.options == null) return;

        if (reshuffle)
        {
            selectedOptionIndex = -1;
            currentQuestionAttempts = 0;

            currentShuffledIndices = new int[qData.options.Length];
            for (int i = 0; i < qData.options.Length; i++) currentShuffledIndices[i] = i;

            // Shuffle options
            for (int i = 0; i < currentShuffledIndices.Length; i++)
            {
                int temp = currentShuffledIndices[i];
                int rand = UnityEngine.Random.Range(i, currentShuffledIndices.Length);
                currentShuffledIndices[i] = currentShuffledIndices[rand];
                currentShuffledIndices[rand] = temp;
            }

            // Find the correct option index after shuffle
            for (int i = 0; i < currentShuffledIndices.Length; i++)
            {
                if (currentShuffledIndices[i] == qData.correctAnswerIndex)
                {
                    currentCorrectOptionIndex = i;
                    break;
                }
            }

            // Juicy animation for question text on new question load
            questionTextUI.transform.DOKill();
            questionTextUI.transform.localScale = Vector3.zero;
            questionTextUI.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetDelay(0.2f);
        }

        SetText(questionTextUI, qData.questionText, QuestionShapedTextTopPadding);

        for (int i = 0; i < optionButtons.Length; i++)
        {
            if (i < qData.options.Length)
            {
                optionButtons[i].gameObject.SetActive(true);
                SetText(optionTextsUI[i], qData.options[currentShuffledIndices[i]], QuestionShapedTextTopPadding);
                
                if (reshuffle)
                {
                    // Reset appearance and interaction
                    optionButtons[i].image.sprite = defaultSprite;
                    optionButtons[i].interactable = true;

                    // Juicy animation for option buttons
                    optionButtons[i].transform.DOKill();
                    optionButtons[i].transform.localScale = Vector3.zero;
                    optionButtons[i].transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetDelay(0.3f + (i * 0.1f));
                }
            }
            else
            {
                optionButtons[i].gameObject.SetActive(false);
            }
        }

        if (reshuffle && submitButton != null)
        {
            submitButton.interactable = false;
            submitButton.gameObject.SetActive(true);
            submitButton.transform.DOKill();
            submitButton.transform.localScale = Vector3.zero;
            submitButton.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetDelay(0.3f + (qData.options.Length * 0.1f));
        }
    }

    public void SelectOption(int optionIndex)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
        selectedOptionIndex = optionIndex;
        
        // Update visual to selected
        for (int i = 0; i < optionButtons.Length; i++)
        {
            optionButtons[i].transform.DOKill();
            if (i == optionIndex)
            {
                optionButtons[i].image.sprite = selectedSprite;
                optionButtons[i].transform.localScale = Vector3.one;
                optionButtons[i].transform.DOPunchScale(new Vector3(0.05f, 0.05f, 0.05f), 0.2f, 5, 1);
            }
            else
            {
                optionButtons[i].image.sprite = defaultSprite;
                optionButtons[i].transform.localScale = Vector3.one;
            }
        }

        if (submitButton != null)
            submitButton.interactable = true;
        else
            CheckAnswer(); // If there is no submit button, check immediately upon selection
    }

    public void CheckAnswer()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.QuizSubmit);
        if (selectedOptionIndex == -1) return; // No option selected

        QuestionData qData = allQuestions.questions[currentQuestionIndex];
        
        // Disable all buttons to prevent changing answer
        foreach(var btn in optionButtons)
            btn.interactable = false;

        if (submitButton != null)
            submitButton.interactable = false;

        // Visual feedback
        if (selectedOptionIndex == currentCorrectOptionIndex)
        {
            // Selected answer is correct
            currentQuestionAttempts++;
            optionButtons[selectedOptionIndex].image.sprite = correctSprite;
            optionButtons[selectedOptionIndex].transform.DOKill();
            optionButtons[selectedOptionIndex].transform.localScale = Vector3.one;
            optionButtons[selectedOptionIndex].transform.DOPunchScale(new Vector3(0.1f, 0.1f, 0.1f), 0.4f, 10, 1);
            
            // Show congratulations message
            if (questionTextUI != null)
            {
                SetText(questionTextUI, LocalizationManager.Instance.Get("quiz.correct"), QuestionShapedTextTopPadding);
                questionTextUI.transform.DOKill();
                questionTextUI.transform.localScale = Vector3.one;
                questionTextUI.transform.DOPunchScale(new Vector3(0.15f, 0.15f, 0.15f), 0.5f, 10, 1f);
            }

            // Award Noor Coins and XP together
            int coinsEarned = 0;
            float xpEarned = 0f;

            if (NoorCoinManager.Instance != null && QuestionMarkOrbManager.Instance != null)
            {
                coinsEarned = QuestionMarkOrbManager.Instance.rewardCoins;
                NoorCoinManager.Instance.Earn(coinsEarned, false);
            }
            if (PlayerXPManager.Instance != null)
            {
                XPTaskType xpTask = currentQuestionAttempts == 1 ? XPTaskType.AnswerQuestion1stTry : XPTaskType.AnswerQuestionRetry;
                xpEarned = PlayerXPManager.Instance.AddXPForTask(xpTask, false);
            }

            if (ToastMessageManager.Instance != null && (coinsEarned > 0 || xpEarned > 0))
            {
                LocalizationManager loc = LocalizationManager.Instance;
                string toastMsg = "";
                if (coinsEarned > 0)
                {
                    toastMsg += loc.Get("reward.coins", coinsEarned) + " ";
                }
                if (coinsEarned > 0 && xpEarned > 0)
                {
                    toastMsg += loc.Get("reward.and") + " ";
                }
                if (xpEarned > 0)
                {
                    toastMsg += loc.Get("reward.xp", xpEarned);
                }

                ToastMessageManager.Instance.ShowToast(toastMsg.Trim(), Color.white);
            }

            if (currentOrb != null && QuestionMarkOrbManager.Instance != null)
            {
                QuestionMarkOrbManager.Instance.OnOrbOpened(currentOrb);
                currentOrb = null;
            }

            StartCoroutine(HideQuizAfterDelay(5f));
        }
        else
        {
            // Selected answer is wrong
            currentQuestionAttempts++;
            optionButtons[selectedOptionIndex].image.sprite = wrongSprite;
            optionButtons[selectedOptionIndex].transform.DOKill();
            optionButtons[selectedOptionIndex].transform.localScale = Vector3.one;
            optionButtons[selectedOptionIndex].transform.DOShakePosition(0.4f, 10f, 20, 90f, false, true);
            
            // Also show the correct answer
            optionButtons[currentCorrectOptionIndex].image.sprite = correctSprite;
            optionButtons[currentCorrectOptionIndex].transform.DOKill();
            optionButtons[currentCorrectOptionIndex].transform.localScale = Vector3.one;
            optionButtons[currentCorrectOptionIndex].transform.DOPunchScale(new Vector3(0.1f, 0.1f, 0.1f), 0.4f, 10, 1);
            
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast(
                    LocalizationManager.Instance.Get("quiz.wrong"), Color.red);
            }
            
            StartCoroutine(ResetQuizAfterDelay(5f));
        }
    }

    public void CloseQuizDramatically()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.QuizClose);
        StopAllCoroutines();

        if (quizPanel != null) 
        {
            quizPanel.transform.DOKill();
            quizPanel.transform.DOPunchScale(new Vector3(0.05f, 0.05f, 0.05f), 0.2f, 5, 1).OnComplete(() => {
                quizPanel.transform.DOScale(0f, 0.3f).SetEase(Ease.InBack).OnComplete(() => {
                    quizPanel.SetActive(false);
                    if (countDownToHidePanel != null) countDownToHidePanel.gameObject.SetActive(false);
                    if (blurredBG != null) blurredBG.SetActive(false);
                    currentOrb = null;
                });
            });
        }
        else if (blurredBG != null)
        {
            blurredBG.SetActive(false);
            currentOrb = null;
        }
    }

    private IEnumerator ResetQuizAfterDelay(float delay)
    {
        if (countDownToHidePanel != null) countDownToHidePanel.gameObject.SetActive(true);

        float remainingTime = delay;
        while (remainingTime > 0)
        {
            if (countDownToHidePanel != null) countDownToHidePanel.text = Mathf.CeilToInt(remainingTime).ToString();
            yield return new WaitForSeconds(1f);
            remainingTime -= 1f;
        }

        if (countDownToHidePanel != null) countDownToHidePanel.gameObject.SetActive(false);
        ShowQuestion(currentQuestionIndex);
    }

    private IEnumerator HideQuizAfterDelay(float delay)
    {
        if (countDownToHidePanel != null)
        {
            countDownToHidePanel.gameObject.SetActive(true);
        }

        float remainingTime = delay;
        while (remainingTime > 0)
        {
            if (countDownToHidePanel != null)
            {
                countDownToHidePanel.text = Mathf.CeilToInt(remainingTime).ToString();
            }
            yield return new WaitForSeconds(1f);
            remainingTime -= 1f;
        }

        if (quizPanel != null) 
        {
            quizPanel.transform.DOKill();
            quizPanel.transform.DOScale(0f, 0.3f).SetEase(Ease.InBack).OnComplete(() => {
                quizPanel.SetActive(false);
                if (countDownToHidePanel != null)
                {
                    countDownToHidePanel.gameObject.SetActive(false);
                }
                if (blurredBG != null)
                {
                    blurredBG.SetActive(false);
                }
            });
        }
        else if (blurredBG != null)
        {
            blurredBG.SetActive(false);
        }
    }

    // Call this method from a "Next" button in the UI
    public void NextQuestion()
    {
        if (currentQuestionIndex + 1 < allQuestions.questions.Length)
        {
            ShowQuestion(currentQuestionIndex + 1);
        }
        else
        {
            Debug.Log("Quiz Completed!");
            // Handle quiz completion logic here
        }
    }

    // The question label and each MCQ option label get 50 units of top padding on their shaped
    // (Bengali) child by default, so the shaped glyphs don't render flush against the panel's top
    // edge the way plain TMP's own vertical centering would allow. Opt-in per call rather than baked
    // into the shared wrapper, since not every SetText caller (e.g. countdown text) wants it.
    private const float QuestionShapedTextTopPadding = 50f;

    private void SetText(TextMeshProUGUI tmpTextUI, string text, float shapedTopPadding = 0f)
    {
        if (tmpTextUI == null) return;

        if (LocalizationManager.Instance == null) { tmpTextUI.text = text; return; }

        LocalizedRendering.SetText(tmpTextUI, text, LocalizationManager.Instance.CurrentLocale, shapedTopPadding);
    }
}
