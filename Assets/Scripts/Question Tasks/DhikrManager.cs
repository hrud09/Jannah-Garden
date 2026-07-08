using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[System.Serializable]
public class LocalizedDhikrData
{
    public JannahGarden.Localization.Language language;
    public string[] dhikrs;
}

[System.Serializable]
public class DhikrList
{
    public LocalizedDhikrData[] translations;

    public string[] GetDhikrs(JannahGarden.Localization.Language lang)
    {
        if (translations == null) return new string[0];
        foreach (var t in translations)
        {
            if (t.language == lang) return t.dhikrs;
        }
        foreach (var t in translations)
        {
            if (t.language == JannahGarden.Localization.Language.English) return t.dhikrs;
        }
        return translations.Length > 0 ? translations[0].dhikrs : new string[0];
    }
}

public class DhikrManager : MonoBehaviour
{
    public static DhikrManager Instance;

    [Header("UI References")]
    public GameObject dhikrPanel;
    public Button closeButton;
    public GameObject blurredBG;
    public TextMeshProUGUI dhikrTextUI;
    public TextMeshProUGUI countTextUI;
    public Button plusButton;
    public Button minusButton;
    public Button submitButton;

    [Header("Data")]
    public TextAsset dhikrFile;
    public int minDhikrCount = 33;
    public int maxDhikrCount = 100;

    private DhikrList allDhikrs;
    private QuestionMarkOrb currentOrb;
    private int currentCount = 0;
    private int targetCount = 33;
    private int currentDhikrIndex = 0;
    private string currentDhikrName = "";

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void OnEnable()
    {
        JannahGarden.Localization.LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
    }

    void OnDisable()
    {
        JannahGarden.Localization.LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
    }

    void HandleLanguageChanged(JannahGarden.Localization.Language newLang)
    {
        LoadDhikrs();
        if (dhikrPanel != null && dhikrPanel.activeSelf)
        {
            if (allDhikrs != null)
            {
                string[] currentDhikrs = allDhikrs.GetDhikrs(newLang);
                if (currentDhikrs != null && currentDhikrIndex < currentDhikrs.Length)
                {
                    currentDhikrName = currentDhikrs[currentDhikrIndex];
                    if (dhikrTextUI != null && currentCount < targetCount)
                    {
                        string template = JannahGarden.Localization.LocalizationManager.Instance.GetTranslation("Dhikr \"{0}\" for {1} times", "Dhikr \"{0}\" for {1} times");
                        SetText(dhikrTextUI, string.Format(template, currentDhikrName, targetCount));
                    }
                }
            }
        }
    }

    void Start()
    {
        LoadDhikrs();
        if (submitButton != null)
        {
            submitButton.onClick.AddListener(SubmitDhikr);
            submitButton.interactable = false;
        }
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(CloseDhikrDramatically);
        }
        if (plusButton != null)
        {
            plusButton.onClick.AddListener(IncrementCount);
        }
        if (minusButton != null)
        {
            minusButton.onClick.AddListener(DecrementCount);
        }

        if (dhikrPanel != null) dhikrPanel.SetActive(false);
        if (blurredBG != null) blurredBG.SetActive(false);
    }

    public void StartDhikr(QuestionMarkOrb orb = null)
    {
        currentOrb = orb;
        if (blurredBG != null) blurredBG.SetActive(true);

        if (plusButton != null) plusButton.interactable = true;
        if (minusButton != null) minusButton.interactable = true;

        if (dhikrPanel != null) 
        {
            dhikrPanel.SetActive(true);
            dhikrPanel.transform.DOKill();
            dhikrPanel.transform.localScale = Vector3.zero;
            dhikrPanel.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);
        }

        if (allDhikrs != null)
        {
            string[] currentDhikrs = allDhikrs.GetDhikrs(JannahGarden.Localization.LocalizationManager.CurrentLanguage);
            if (currentDhikrs != null && currentDhikrs.Length > 0)
            {
                currentDhikrIndex = UnityEngine.Random.Range(0, currentDhikrs.Length);
                targetCount = UnityEngine.Random.Range(minDhikrCount, maxDhikrCount + 1);
                currentCount = 0;

                currentDhikrName = currentDhikrs[currentDhikrIndex];
                if (dhikrTextUI != null)
                {
                    string template = JannahGarden.Localization.LocalizationManager.Instance.GetTranslation("Dhikr \"{0}\" for {1} times", "Dhikr \"{0}\" for {1} times");
                    SetText(dhikrTextUI, string.Format(template, currentDhikrName, targetCount));
                }
                
                UpdateCountUI();

                if (dhikrTextUI != null)
                {
                    dhikrTextUI.transform.DOKill();
                    dhikrTextUI.transform.localScale = Vector3.zero;
                    dhikrTextUI.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetDelay(0.2f);
                }
            }
        }
        
        CheckSubmitCondition();
    }

    void LoadDhikrs()
    {
        if (dhikrFile == null)
        {
            dhikrFile = Resources.Load<TextAsset>("dhikrs");
        }

        if (dhikrFile != null)
        {
            allDhikrs = JsonUtility.FromJson<DhikrList>(dhikrFile.text);
        }
        else
        {
            Debug.LogError("Dhikr JSON file not assigned and 'dhikrs' not found in Resources!");
        }
    }

    void IncrementCount()
    {
        if (currentCount >= targetCount) return;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.DhikrIncrement);
        currentCount++;
        UpdateCountUI();
        CheckSubmitCondition();
        
        if (countTextUI != null)
        {
            countTextUI.transform.DOKill();
            countTextUI.transform.localScale = Vector3.one;
            countTextUI.transform.DOPunchScale(new Vector3(0.1f, 0.1f, 0.1f), 0.1f);
        }

        if (currentCount >= targetCount)
        {
            if (plusButton != null) plusButton.interactable = false;
            if (minusButton != null) minusButton.interactable = false;
            if (submitButton != null) submitButton.interactable = false;
            StartCoroutine(AutoSubmitRoutine());
        }
    }

    private IEnumerator AutoSubmitRoutine()
    {
        yield return new WaitForSeconds(1.5f);
        SubmitDhikr();
    }

    void DecrementCount()
    {
        if (currentCount >= targetCount) return;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.DhikrDecrement);
        if (currentCount > 0)
        {
            currentCount--;
            UpdateCountUI();
            CheckSubmitCondition();
            
            if (countTextUI != null)
            {
                countTextUI.transform.DOKill();
                countTextUI.transform.localScale = Vector3.one;
                countTextUI.transform.DOPunchScale(new Vector3(0.1f, 0.1f, 0.1f), 0.1f);
            }
        }
    }

    void UpdateCountUI()
    {
        if (countTextUI != null)
        {
            SetText(countTextUI, currentCount.ToString());
        }
    }

    void CheckSubmitCondition()
    {
        if (submitButton != null)
        {
            submitButton.interactable = currentCount >= targetCount;
        }
    }

    public void SubmitDhikr()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.DhikrSubmit);
        if (currentCount >= targetCount)
        {
            if (submitButton != null) submitButton.interactable = false;

            int coinsEarned = 0;
            float xpEarned = 0f;

            if (NoorCoinManager.Instance != null && QuestionMarkOrbManager.Instance != null)
            {
                coinsEarned = QuestionMarkOrbManager.Instance.rewardCoins;
                NoorCoinManager.Instance.Earn(coinsEarned, false); // Suppress default toast
            }
            if (PlayerXPManager.Instance != null)
            {
                xpEarned = PlayerXPManager.Instance.AddXPForTask(XPTaskType.CompleteDhikr, false); // Suppress default toast
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

            StartCoroutine(CongratsAndCloseDhikrRoutine());
        }
    }

    private IEnumerator CongratsAndCloseDhikrRoutine()
    {
        if (plusButton != null) plusButton.interactable = false;
        if (minusButton != null) minusButton.interactable = false;
        if (submitButton != null) submitButton.interactable = false;

        if (dhikrTextUI != null)
        {
            SetText(dhikrTextUI, JannahGarden.Localization.LocalizationManager.Instance.GetTranslation("<color=#FFD700>Congratulations!</color>\nDhikr Completed!", "<color=#FFD700>Congratulations!</color>\nDhikr Completed!"));
            dhikrTextUI.transform.DOKill();
            dhikrTextUI.transform.localScale = Vector3.one;
            dhikrTextUI.transform.DOPunchScale(new Vector3(0.15f, 0.15f, 0.15f), 0.5f, 10, 1f);
        }

        if (countTextUI != null)
        {
            SetText(countTextUI, JannahGarden.Localization.LocalizationManager.Instance.GetTranslation("Masha'Allah!", "Masha'Allah!"));
            countTextUI.transform.DOKill();
            countTextUI.transform.localScale = Vector3.one;
            countTextUI.transform.DOPunchScale(new Vector3(0.1f, 0.1f, 0.1f), 0.5f, 10, 1f);
        }

        yield return new WaitForSeconds(3.0f);

        CloseDhikrDramaticallyInternal();
    }

    public void CloseDhikrDramatically()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.DhikrClose);
        StopAllCoroutines();
        CloseDhikrDramaticallyInternal();
    }

    private void CloseDhikrDramaticallyInternal()
    {
        if (dhikrPanel != null) 
        {
            dhikrPanel.transform.DOKill();
            dhikrPanel.transform.DOPunchScale(new Vector3(0.05f, 0.05f, 0.05f), 0.2f, 5, 1).OnComplete(() => {
                dhikrPanel.transform.DOScale(0f, 0.3f).SetEase(Ease.InBack).OnComplete(() => {
                    dhikrPanel.SetActive(false);
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

    private void SetText(TextMeshProUGUI tmpTextUI, string text)
    {
        if (tmpTextUI == null) return;
        
        var localizedText = tmpTextUI.GetComponent<JannahGarden.Localization.LocalizedText>();
        if (localizedText != null)
        {
            localizedText.SetDynamicText(text);
        }
        else
        {
            tmpTextUI.text = text;
        }
    }
}
