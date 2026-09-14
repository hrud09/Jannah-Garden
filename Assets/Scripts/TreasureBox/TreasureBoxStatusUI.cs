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

    [Header("4 Slot Stage Boxes UI")]
    [Tooltip("Root container holding the 4 boxes display.")]
    public GameObject boxesContainer;
    [Tooltip("Array of 4 box display items representing slots or stages.")]
    public GameObject[] slotBoxItems = new GameObject[4];
    [Tooltip("Overlay blur/lock GameObjects corresponding to each of the 4 boxes.")]
    public GameObject[] slotBoxLockOverlays = new GameObject[4];

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
        UpdateSlotBoxes();
    }

    public void UpdateSlotBoxes()
    {
        TreasureBoxManager manager = GetManager();
        bool showBoxes = manager != null && manager.HasShownChestOnce;

        if (boxesContainer != null)
        {
            boxesContainer.SetActive(showBoxes);
        }

        if (slotBoxItems != null)
        {
            for (int i = 0; i < slotBoxItems.Length; i++)
            {
                if (slotBoxItems[i] != null)
                {
                    slotBoxItems[i].SetActive(showBoxes);
                }

                if (showBoxes && slotBoxLockOverlays != null && i < slotBoxLockOverlays.Length && slotBoxLockOverlays[i] != null)
                {
                    bool isUnlocked = false;
                    if (manager != null)
                    {
                        // Check if slot or tier is unlocked & ready
                        if (i < TreasureBoxManager.SLOTS_PER_TIER)
                        {
                            isUnlocked = manager.IsTierUnlocked(tier) && manager.IsSlotAvailable(tier, i);
                        }
                        else
                        {
                            // 4th box / stage box unlocked if entire tier set is complete
                            TreasureBoxTierState state = manager.GetTierState(tier);
                            isUnlocked = state != null && state.IsSetComplete;
                        }
                    }

                    // Lock/blur overlay active if NOT unlocked
                    slotBoxLockOverlays[i].SetActive(!isUnlocked);
                }
            }
        }
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
