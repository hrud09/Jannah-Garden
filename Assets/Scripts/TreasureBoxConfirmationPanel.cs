using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TreasureBoxConfirmationPanel : MonoBehaviour
{
    public static TreasureBoxConfirmationPanel Instance { get; private set; }

    [Header("UI Fields")]
    public TMP_Text nameText;
    public TMP_Text descriptionText;
    public Image rewardIcon;

    [Header("Buttons")]
    public Button watchAdButton;
    public Button subscribeButton;
    public Button closeButton;

    private TreasureBoxTier _tier;
    private int _slotIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Setup button listeners
        if (watchAdButton != null)
        {
            watchAdButton.onClick.AddListener(OnWatchAdClicked);
        }
        if (subscribeButton != null)
        {
            subscribeButton.onClick.AddListener(OnSubscribeClicked);
        }
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseClicked);
        }

        // Initially hide the panel
        gameObject.SetActive(false);
    }

    public void Show(TreasureBoxTier tier, int slotIndex)
    {
        _tier = tier;
        _slotIndex = slotIndex;

        if (TreasureBoxManager.Instance != null)
        {
            TreasureBoxRewardItemData reward = TreasureBoxManager.Instance.GetCurrentCycleReward(tier);
            if (reward != null)
            {
                if (nameText != null) nameText.text = reward.itemName;
                if (descriptionText != null) descriptionText.text = reward.itemDescription;
                if (rewardIcon != null)
                {
                    rewardIcon.sprite = reward.itemIcon;
                    rewardIcon.gameObject.SetActive(reward.itemIcon != null);
                }
            }
            else
            {
                if (nameText != null) nameText.text = tier.ToString() + " Box";
                if (descriptionText != null) descriptionText.text = "Open this box to get rewards!";
                if (rewardIcon != null) rewardIcon.gameObject.SetActive(false);
            }
        }

        gameObject.SetActive(true);
    }

    private void OnWatchAdClicked()
    {
        gameObject.SetActive(false);
        if (AdsManager.Instance != null)
        {
            AdsManager.Instance.ShowFakeRewardedAd(() =>
            {
                if (TreasureBoxManager.Instance != null)
                {
                    TreasureBoxManager.Instance.TryOpenBox(_tier, _slotIndex);
                }
            });
        }
        else
        {
            if (TreasureBoxManager.Instance != null)
            {
                TreasureBoxManager.Instance.TryOpenBox(_tier, _slotIndex);
            }
        }
    }

    private void OnSubscribeClicked()
    {
        gameObject.SetActive(false);
        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast("You need to subscribe from the Amal Apps.");
        }
    }

    private void OnCloseClicked()
    {
        gameObject.SetActive(false);
    }
}
