using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TreasureBoxStatusUI : MonoBehaviour
{
    public TreasureBoxTier tier;
    public Button showBoxButton;
    public Image statusBg;
    public TMP_Text nameText;
    public TMP_Text openedBoxCountText;
    public TMP_Text timerText;

    private void OnValidate()
    {
        UpdateColor();
    }

    private void Start()
    {
        UpdateColor();
        if (showBoxButton != null)
        {
            showBoxButton.onClick.AddListener(() =>
            {
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.TreasureBoxShow);
                if (TreasureBoxManager.Instance != null)
                {
                    TreasureBoxManager.Instance.PlayShowAnimationForTier(tier);
                }
            });
        }
    }

    private void OnDestroy()
    {
        if (showBoxButton != null)
        {
            showBoxButton.onClick.RemoveAllListeners();
        }
    }

    public void UpdateColor()
    {
        if (statusBg == null) return;

        TreasureBoxManager manager = TreasureBoxManager.Instance;
#if UNITY_EDITOR
        if (manager == null && !Application.isPlaying)
        {
            manager = FindObjectOfType<TreasureBoxManager>();
        }
#endif

        if (manager != null)
        {
            statusBg.color = manager.GetTierColor(tier);
        }
    }
}
