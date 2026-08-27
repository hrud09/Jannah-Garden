using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The panel that opens when the player interacts with example garden dressing scattered by the
/// Environment Generator (<see cref="PrePlacedAsset"/>) — decoration nobody owns, placed just to show
/// what a finished garden looks like. Tells the player it is a Shop item and offers a shortcut to the
/// Shop, instead of the relocate/return options <see cref="PlacedItemActionsUI"/> offers for items the
/// player actually placed.
/// </summary>
public class PrePlacedAssetInfoPanel : MonoBehaviour
{
    public static PrePlacedAssetInfoPanel Instance { get; private set; }

    [Header("Panel")]
    public GameObject panel;
    public TMP_Text messageText;

    [Header("Buttons")]
    public Button openShopButton;
    public Button closeButton;

    public bool IsOpen => panel != null && panel.activeSelf;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (openShopButton != null) openShopButton.onClick.AddListener(OnOpenShopClicked);
        if (closeButton != null) closeButton.onClick.AddListener(Hide);

        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (openShopButton != null) openShopButton.onClick.RemoveListener(OnOpenShopClicked);
        if (closeButton != null) closeButton.onClick.RemoveListener(Hide);

        if (Instance == this) Instance = null;
    }

    public void Show()
    {
        if (panel != null) panel.SetActive(true);

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    private void OnOpenShopClicked()
    {
        Hide();

        if (InGameShopManager.Instance != null) InGameShopManager.Instance.SetShopOpen(true, true);
    }
}
