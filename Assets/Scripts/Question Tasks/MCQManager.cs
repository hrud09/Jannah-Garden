using System.Collections;
using System.Collections.Generic;
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

        // Setup button listeners
        for (int i = 0; i < optionButtons.Length; i++)
        {
            int index = i; // Local copy for closure
            optionButtons[i].onClick.AddListener(() => SelectOption(index));
        }

        if (quizPanel != null) quizPanel.SetActive(false);
        if (countDownToHidePanel != null) countDownToHidePanel.gameObject.SetActive(false);
    }

    public void StartQuiz()
    {
        if (quizPanel != null) quizPanel.SetActive(true);
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
        
        if (countDownToHidePanel != null) 
            countDownToHidePanel.gameObject.SetActive(false);

        QuestionData qData = allQuestions.questions[index];

        questionTextUI.text = qData.questionText;

        for (int i = 0; i < optionButtons.Length; i++)
        {
            if (i < qData.options.Length)
            {
                optionButtons[i].gameObject.SetActive(true);
                optionTextsUI[i].text = qData.options[i];
                
                // Reset appearance and interaction
                optionButtons[i].image.sprite = defaultSprite;
                optionButtons[i].interactable = true;
            }
            else
            {
                optionButtons[i].gameObject.SetActive(false);
            }
        }

        if (submitButton != null)
            submitButton.interactable = false;
    }

    public void SelectOption(int optionIndex)
    {
        selectedOptionIndex = optionIndex;
        
        // Update visual to selected
        for (int i = 0; i < optionButtons.Length; i++)
        {
            if (i == optionIndex)
            {
                optionButtons[i].image.sprite = selectedSprite;
            }
            else
            {
                optionButtons[i].image.sprite = defaultSprite;
            }
        }

        if (submitButton != null)
            submitButton.interactable = true;
        else
            CheckAnswer(); // If there is no submit button, check immediately upon selection
    }

    public void CheckAnswer()
    {
        if (selectedOptionIndex == -1) return; // No option selected

        QuestionData qData = allQuestions.questions[currentQuestionIndex];
        
        // Disable all buttons to prevent changing answer
        foreach(var btn in optionButtons)
            btn.interactable = false;

        if (submitButton != null)
            submitButton.interactable = false;

        // Visual feedback
        if (selectedOptionIndex == qData.correctAnswerIndex)
        {
            // Selected answer is correct
            optionButtons[selectedOptionIndex].image.sprite = correctSprite;
            
            // Award Noor Coins
            if (NoorCoinManager.Instance != null && QuestionMarkOrbManager.Instance != null)
            {
                NoorCoinManager.Instance.Earn(QuestionMarkOrbManager.Instance.rewardCoins);
            }
        }
        else
        {
            // Selected answer is wrong
            optionButtons[selectedOptionIndex].image.sprite = wrongSprite;
            
            // Also show the correct answer
            optionButtons[qData.correctAnswerIndex].image.sprite = correctSprite;
        }

        StartCoroutine(HideQuizAfterDelay(5f));
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

        if (quizPanel != null) quizPanel.SetActive(false);
        
        if (countDownToHidePanel != null)
        {
            countDownToHidePanel.gameObject.SetActive(false);
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
