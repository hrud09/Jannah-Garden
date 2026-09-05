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

    public string[] GetDhikrs()
    {
        return dhikrs ?? new string[0];
    }
}

public class DhikrManager : MonoBehaviour
{
    private static DhikrManager _instance;
    public static DhikrManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<DhikrManager>(true);
            }
            return _instance;
        }
    }

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
    [Tooltip("Optional explicit override. Leave empty to auto-load Resources/dhikrs_{locale}.txt for the " +
             "active locale (falling back to dhikrs_en if that locale has no file yet).")]
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
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // Hide the panel visuals here rather than in Start so they never render for a
        // frame. Only these children are toggled — this GameObject stays active.
        if (dhikrPanel != null) dhikrPanel.SetActive(false);
        if (blurredBG != null) blurredBG.SetActive(false);
    }

    void Start()
    {
        LoadDhikrs();
        LocalizationManager.OnLocaleChanged += LoadDhikrs;
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
    }

    void OnDestroy()
    {
        LocalizationManager.OnLocaleChanged -= LoadDhikrs;
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
            string[] currentDhikrs = allDhikrs.GetDhikrs();
            if (currentDhikrs != null && currentDhikrs.Length > 0)
            {
                currentDhikrIndex = UnityEngine.Random.Range(0, currentDhikrs.Length);
                targetCount = UnityEngine.Random.Range(minDhikrCount, maxDhikrCount + 1);
                currentCount = 0;

                currentDhikrName = currentDhikrs[currentDhikrIndex];
                if (dhikrTextUI != null)
                {
                    SetText(dhikrTextUI, LocalizationManager.Instance.Get("dhikr.instruction", currentDhikrName, targetCount));
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
        TextAsset asset = dhikrFile != null ? dhikrFile : LocalizationManager.LoadLocalizedTextAsset("dhikrs");

        if (asset != null)
        {
            allDhikrs = JsonUtility.FromJson<DhikrList>(asset.text);
        }
        else
        {
            Debug.LogError("Dhikr JSON file not assigned and no 'dhikrs_{locale}' found in Resources!");
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
                LocalizationManager loc = LocalizationManager.Instance;
                string toastMsg = "";
                if (coinsEarned > 0) toastMsg += loc.Get("reward.coins_colored", coinsEarned) + " ";
                if (coinsEarned > 0 && xpEarned > 0) toastMsg += loc.Get("reward.and") + " ";
                if (xpEarned > 0) toastMsg += loc.Get("reward.xp_colored", xpEarned);

                ToastMessageManager.Instance.ShowToast(toastMsg.Trim(), Color.white);
            }

            if (currentOrb != null && QuestionMarkOrbManager.Instance != null)
            {
                QuestionMarkOrbManager.Instance.OnOrbOpened(currentOrb);
                currentOrb = null;
            }

            if (plusButton != null) plusButton.interactable = false;
            if (minusButton != null) minusButton.interactable = false;

            CloseDhikrDramaticallyInternal();
        }
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

    // Both the dhikr instruction label and the count label get 100 units of top padding on their
    // shaped (Bengali) child by default, so the shaped glyphs don't render flush against the panel's
    // top edge the way plain TMP's own vertical centering would allow.
    private const float ShapedTextTopPadding = 100f;

    private void SetText(TextMeshProUGUI tmpTextUI, string text)
    {
        if (tmpTextUI == null) return;

        if (LocalizationManager.Instance == null) { tmpTextUI.text = text; return; }

        LocalizedRendering.SetText(tmpTextUI, text, LocalizationManager.Instance.CurrentLocale, ShapedTextTopPadding);
    }
}
