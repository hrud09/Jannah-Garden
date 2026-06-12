using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[System.Serializable]
public class DhikrList
{
    public string[] dhikrs;
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

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
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

        if (dhikrPanel != null) 
        {
            dhikrPanel.SetActive(true);
            dhikrPanel.transform.DOKill();
            dhikrPanel.transform.localScale = Vector3.zero;
            dhikrPanel.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack);
        }

        if (allDhikrs != null && allDhikrs.dhikrs != null && allDhikrs.dhikrs.Length > 0)
        {
            int randomIndex = UnityEngine.Random.Range(0, allDhikrs.dhikrs.Length);
            targetCount = UnityEngine.Random.Range(minDhikrCount, maxDhikrCount + 1);
            currentCount = 0;

            string dhikrName = allDhikrs.dhikrs[randomIndex];
            if (dhikrTextUI != null)
                dhikrTextUI.text = $"Dhikr \"{dhikrName}\" for {targetCount} times";
            
            UpdateCountUI();

            if (dhikrTextUI != null)
            {
                dhikrTextUI.transform.DOKill();
                dhikrTextUI.transform.localScale = Vector3.zero;
                dhikrTextUI.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetDelay(0.2f);
            }
        }
        
        CheckSubmitCondition();
    }

    void LoadDhikrs()
    {
        if (dhikrFile != null)
        {
            allDhikrs = JsonUtility.FromJson<DhikrList>(dhikrFile.text);
        }
        else
        {
            Debug.LogError("Dhikr JSON file not assigned in the inspector!");
        }
    }

    void IncrementCount()
    {
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
    }

    void DecrementCount()
    {
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
            countTextUI.text = currentCount.ToString();
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

            CloseDhikrDramatically();
        }
    }

    public void CloseDhikrDramatically()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.DhikrClose);
        StopAllCoroutines();

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
}
