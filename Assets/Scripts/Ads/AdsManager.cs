using UnityEngine;
using System.Collections;
using System;
using TMPro;

public class AdsManager : MonoBehaviour
{
    public static AdsManager Instance;

    public GameObject fakeRewardedAdPanel;
    public float fakeAdDuration = 15f;
    public TMP_Text fakeTimerCountDownText;
    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (fakeRewardedAdPanel != null)
        {
            fakeRewardedAdPanel.SetActive(false);
        }
    }

    public void ShowFakeRewardedAd(Action onComplete)
    {
        StartCoroutine(AdRoutine(onComplete));
    }

    private IEnumerator AdRoutine(Action onComplete)
    {
        if (fakeRewardedAdPanel != null)
        {
            fakeRewardedAdPanel.SetActive(true);
        }

        float remainingTime = fakeAdDuration;
        while (remainingTime > 0)
        {
            if (fakeTimerCountDownText != null)
            {
                fakeTimerCountDownText.text = Mathf.CeilToInt(remainingTime).ToString();
            }
            yield return new WaitForSeconds(1f);
            remainingTime -= 1f;
        }

        if (fakeRewardedAdPanel != null)
        {
            fakeRewardedAdPanel.SetActive(false);
        }

        onComplete?.Invoke();
    }
}
