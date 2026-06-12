using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // Assuming you are using TextMeshPro for UI text

[System.Serializable]
public class QuestionData
{
    public string questionText;
    public string[] options;
    public int correctAnswerIndex;
}

[System.Serializable]
public class QuestionList
{
    public QuestionData[] questions;
}

public class MCQManager : MonoBehaviour
{
    public static MCQManager Instance;

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
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        LoadQuestions();
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

        if (quizPanel != null) quizPanel.SetActive(false);
        if (countDownToHidePanel != null) countDownToHidePanel.gameObject.SetActive(false);
        if (blurredBG != null) blurredBG.SetActive(false);
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
            int randomIndex = UnityEngine.Random.Range(0, allQuestions.questions.Length);
            ShowQuestion(randomIndex);
        }
    }

    void LoadQuestions()
    {
        if (questionFile != null)
        {
            allQuestions = JsonUtility.FromJson<QuestionList>(questionFile.text);
        }
        else
        {
            Debug.LogError("Question JSON file not assigned in the inspector!");
        }
    }

    public void ShowQuestion(int index)
    {
        if (allQuestions == null || allQuestions.questions == null || index < 0 || index >= allQuestions.questions.Length)
            return;

        currentQuestionIndex = index;
        selectedOptionIndex = -1;
        currentQuestionAttempts = 0;
        
        if (countDownToHidePanel != null) 
            countDownToHidePanel.gameObject.SetActive(false);

        QuestionData qData = allQuestions.questions[index];

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

        questionTextUI.text = qData.questionText;

        // Juicy animation for question text
        questionTextUI.transform.DOKill();
        questionTextUI.transform.localScale = Vector3.zero;
        questionTextUI.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetDelay(0.2f);

        for (int i = 0; i < optionButtons.Length; i++)
        {
            if (i < qData.options.Length)
            {
                optionButtons[i].gameObject.SetActive(true);
                optionTextsUI[i].text = qData.options[currentShuffledIndices[i]];
                
                // Reset appearance and interaction
                optionButtons[i].image.sprite = defaultSprite;
                optionButtons[i].interactable = true;

                // Juicy animation for option buttons
                optionButtons[i].transform.DOKill();
                optionButtons[i].transform.localScale = Vector3.zero;
                optionButtons[i].transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetDelay(0.3f + (i * 0.1f));
            }
            else
            {
                optionButtons[i].gameObject.SetActive(false);
            }
        }

        if (submitButton != null)
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
                string toastMsg = "";
                if (coinsEarned > 0) toastMsg += $"<color=#FFD700>+{coinsEarned} Noor Coins</color> ";
                if (coinsEarned > 0 && xpEarned > 0) toastMsg += "& ";
                if (xpEarned > 0) toastMsg += $"<color=#00FFFF>+{xpEarned} XP</color>";
                
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
                ToastMessageManager.Instance.ShowToast("Opps! Review the correct answer and try again to earn your Noor Coins.", Color.red);
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
}
