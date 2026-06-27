using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace JannahGarden.Tutorial
{
    /// <summary>
    /// Manages a step‑by‑step tutorial that guides the player through picking an item from the shop
    /// and placing it in the world. Attach this component to a GameObject in the scene and wire up the
    /// required UI references in the inspector.
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        // ======================
        // ==== UI References ====
        // ======================
        [Header("Panels / Overlays")]
        [Tooltip("The shop UI panel that holds all shop items.")]
        public GameObject shopPanel;

        [Tooltip("The button that opens the shop. If the shop is opened by another means, just leave this null.")]
        public Button openShopButton;

        [Header("Shop Item Selection")]
        [Tooltip("Reference to the specific shop item button that should be selected during the tutorial.")]
        public Button tutorialShopItemButton;

        [Header("Placement Area")]
        [Tooltip("Transform that defines where the placed item should appear (e.g., a ground transform).")]
        public Transform placementRoot;

        [Header("Visual Feedback")]
        [Tooltip("UI Text that displays tutorial instructions.")]
        public Text tutorialMessageText;

        [Tooltip("Optional Image used to highlight a UI element (e.g., a glowing overlay)." )]
        public Image highlightImage;

        // ======================
        // ==== Runtime State ====
        // ======================
        private bool _shopOpen => shopPanel != null && shopPanel.activeSelf;
        private bool _itemPlaced;

        private void Start()
        {
            // Begin the tutorial automatically when the scene starts.
            StartCoroutine(RunTutorial());
        }

        /// <summary>
        /// Core coroutine that runs the tutorial steps in order.
        /// </summary>
        private IEnumerator RunTutorial()
        {
            // Step 1 – Prompt player to open the shop.
            ShowMessage("Welcome! Let's start by opening the shop.");
            Highlight(openShopButton?.gameObject);
            // Wait until the shop panel is visible.
            yield return WaitUntil(() => _shopOpen);
            ClearHighlight();

            // Step 2 – Instruct the player to select a specific item.
            ShowMessage("Great! Choose the highlighted item from the shop.");
            Highlight(tutorialShopItemButton?.gameObject);
            // Wait until the player clicks the tutorial button.
            bool itemSelected = false;
            void OnItemClicked()
            {
                itemSelected = true;
            }
            tutorialShopItemButton.onClick.AddListener(OnItemClicked);
            yield return WaitUntil(() => itemSelected);
            tutorialShopItemButton.onClick.RemoveListener(OnItemClicked);
            ClearHighlight();

            // Step 3 – Guide the player to place the item.
            ShowMessage("Now place the item on the ground.");
            // In many projects the placement logic lives in another script. Here we simply listen for a
            // static event that the placement system fires when an object is instantiated.
            _itemPlaced = false;
            PlaceableItem.OnItemPlaced += OnItemPlaced;
            yield return WaitUntil(() => _itemPlaced);
            PlaceableItem.OnItemPlaced -= OnItemPlaced;
            ClearHighlight();

            // Step 4 – Completion.
            ShowMessage("Congratulations! You have successfully placed the item. Tutorial complete.");
            yield return new WaitForSeconds(3f);
            HideMessage();
        }

        /// <summary>
        /// Callback that the PlaceableItem script should invoke when an item is placed.
        /// </summary>
        private void OnItemPlaced(PlaceableItem placedItem)
        {
            // You could add extra validation (e.g., check placedItem type) here.
            _itemPlaced = true;
        }

        // ---------------------------------------------------------------------
        // Helper UI methods
        // ---------------------------------------------------------------------
        private void ShowMessage(string msg)
        {
            if (tutorialMessageText != null)
            {
                tutorialMessageText.text = msg;
                tutorialMessageText.gameObject.SetActive(true);
            }
        }

        private void HideMessage()
        {
            if (tutorialMessageText != null)
                tutorialMessageText.gameObject.SetActive(false);
        }

        private void Highlight(GameObject target)
        {
            if (highlightImage == null || target == null) return;
            // Position the highlight image over the target UI element.
            var rect = target.GetComponent<RectTransform>();
            if (rect != null)
            {
                highlightImage.rectTransform.position = rect.position;
                highlightImage.rectTransform.sizeDelta = rect.rect.size;
                highlightImage.gameObject.SetActive(true);
            }
        }

        private void ClearHighlight()
        {
            if (highlightImage != null)
                highlightImage.gameObject.SetActive(false);
        }

        /// <summary>
        /// Simple coroutine helper that yields each frame until the supplied predicate returns true.
        /// </summary>
        private IEnumerator WaitUntil(System.Func<bool> predicate)
        {
            while (!predicate())
                yield return null;
        }
    }
}
