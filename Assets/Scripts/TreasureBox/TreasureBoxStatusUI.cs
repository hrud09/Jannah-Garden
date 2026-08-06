using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TreasureBoxStatusUI : MonoBehaviour
{
    public TreasureBoxTier tier;
    public Button showBoxButton;

    [Header("Visuals")]
    [Tooltip("Chest image swapped per tier. Sprite comes from TreasureBoxData.tierIcon.")]
    public Image boxIcon;
    [Tooltip("Optional background tinted with the tier colour. Leave empty to keep the authored colour.")]
    public Image statusBg;

    [Header("Texts")]
    [Tooltip("Optional — the compact HUD layout has no name label.")]
    public TMP_Text nameText;
    public TMP_Text openedBoxCountText;
    public TMP_Text timerText;

    private void OnValidate()
    {
        UpdateVisuals();
    }

    private void Start()
    {
        UpdateVisuals();
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

    /// <summary>
    /// Switches the panel to a tier and refreshes every tier-driven visual.
    /// </summary>
    public void SetTier(TreasureBoxTier newTier)
    {
        tier = newTier;
        UpdateVisuals();
    }

    public void UpdateVisuals()
    {
        UpdateColor();
        UpdateIcon();
    }

    public void UpdateColor()
    {
        if (statusBg == null) return;

        TreasureBoxManager manager = GetManager();
        if (manager != null)
        {
            statusBg.color = manager.GetTierColor(tier);
        }
    }

    public void UpdateIcon()
    {
        if (boxIcon == null) return;

        TreasureBoxManager manager = GetManager();
        if (manager == null) return;

        TreasureBoxData data = manager.GetBoxData(tier);
        if (data == null || data.tierIcon == null) return;

        if (boxIcon.sprite != data.tierIcon)
        {
            boxIcon.sprite = data.tierIcon;
        }
    }

    private TreasureBoxManager GetManager()
    {
        TreasureBoxManager manager = TreasureBoxManager.Instance;
#if UNITY_EDITOR
        if (manager == null && !Application.isPlaying)
        {
            manager = FindObjectOfType<TreasureBoxManager>();
        }
#endif
        return manager;
    }
}
