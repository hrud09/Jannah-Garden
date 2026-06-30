using UnityEngine;
using UnityEngine.UI;

public class PlayersInteractionManager : MonoBehaviour
{
    public static PlayersInteractionManager Instance { get; private set; }

    [Header("Interaction Settings")]
    [Tooltip("Maximum distance at which the player can look at and interact with the treasure box.")]
    public float maxInteractionDistance = 10f;

    [Tooltip("Optional crosshair RectTransform to project raycast from. If null, projects from the screen center.")]
    public RectTransform crosshairRect;

    [Tooltip("Physics layers to include in the raycast check.")]
    public LayerMask interactionLayerMask = ~0; // Default to everything

    public TreasureBox currentTargetBox = null;
    public QuestionMarkOrb currentTargetOrb = null;
    private Camera mainCam;

    public Button itemInteractButton;
    private bool isInteracting = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogWarning("[PlayersInteractionManager] Main Camera not found. Please tag your camera as 'MainCamera'.");
        }

        // Disable the interaction button by default
        if (itemInteractButton != null)
        {
            itemInteractButton.gameObject.SetActive(false);
            itemInteractButton.onClick.AddListener(OnInteractButtonClicked);
        }
    }

    private void OnInteractButtonClicked()
    {
        isInteracting = true;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ItemInteract);
        if (itemInteractButton != null)
        {
            itemInteractButton.gameObject.SetActive(false);
        }

        if (currentTargetBox != null && TreasureBoxManager.Instance != null)
        {
            TreasureBoxTier tier = currentTargetBox.tier;
            int slot = currentTargetBox.slotIndex;

            if (TreasureBoxConfirmationPanel.Instance != null)
            {
                TreasureBoxConfirmationPanel.Instance.Show(tier, slot);
            }
            else if (AdsManager.Instance != null)
            {
                AdsManager.Instance.ShowFakeRewardedAd(() => 
                {
                    if (TreasureBoxManager.Instance != null)
                    {
                        TreasureBoxManager.Instance.TryOpenBox(tier, slot);
                    }
                });
            }
            else
            {
                TreasureBoxManager.Instance.TryOpenBox(tier, slot);
            }
        }
        else if (currentTargetOrb != null)
        {
            currentTargetOrb.OpenQuiz();
        }
    }

    private void Update()
    {
        if (mainCam == null)
        {
            mainCam = Camera.main;
            if (mainCam == null) return;
        }

        PerformInteractionRaycast();
    }

    /// <summary>
    /// Casts a ray from the camera through the crosshair (or center screen) to detect TreasureBox colliders.
    /// </summary>
    private void PerformInteractionRaycast()
    {
        Ray ray;
        if (crosshairRect != null)
        {
            ray = mainCam.ScreenPointToRay(crosshairRect.position);
        }
        else
        {
            // Fallback to center screen viewport ray
            ray = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        }

        RaycastHit hit;
        TreasureBox detectedBox = null;
        QuestionMarkOrb detectedOrb = null;
        GameObject hitObject = null;
        
        bool didHit = Physics.Raycast(ray, out hit, maxInteractionDistance, interactionLayerMask);

        // Draw the ray in the Editor/Scene view (and Game view if Gizmos are enabled)
        // Colors the ray green when it hits an object, and red when it hits nothing
        Color rayColor = didHit ? Color.green : Color.red;
        Debug.DrawRay(ray.origin, ray.direction * maxInteractionDistance, rayColor);

        // Perform raycast check
        if (didHit)
        {
            hitObject = hit.collider.gameObject;

            // Attempt to find the TreasureBox component on the hit object or its parent hierarchies
            detectedBox = hit.collider.GetComponent<TreasureBox>();
            if (detectedBox == null)
            {
                detectedBox = hit.collider.GetComponentInParent<TreasureBox>();
            }


            // Also check for QuestionMarkOrb
            detectedOrb = hit.collider.GetComponent<QuestionMarkOrb>();
            if (detectedOrb == null)
            {
                detectedOrb = hit.collider.GetComponentInParent<QuestionMarkOrb>();
            }
        }

        // Handle highlighting transitions for TreasureBox
        if (detectedBox != currentTargetBox)
        {
            isInteracting = false;
            // Disable outline on the previous box
            if (currentTargetBox != null)
            {
                currentTargetBox.SetOutline(false);
            }

            // Update current target and enable outline on the new box
            currentTargetBox = detectedBox;
            if (currentTargetBox != null)
            {
                currentTargetBox.SetOutline(true);
            }
        }

        // Handle highlighting transitions for QuestionMarkOrb
        if (detectedOrb != currentTargetOrb)
        {
            isInteracting = false;
            if (currentTargetOrb != null)
            {
                currentTargetOrb.SetFocus(false);
            }

            currentTargetOrb = detectedOrb;
            if (currentTargetOrb != null)
            {
                currentTargetOrb.SetFocus(true);
            }
        }

     
        // Activate the itemInteractButton if a treasure box or orb is targeted, otherwise deactivate it
        if (itemInteractButton != null)
        {
            bool shouldShow = (currentTargetBox != null || currentTargetOrb != null) && !isInteracting;
            if (ToastMessageManager.Instance != null && ToastMessageManager.Instance.IsShowing)
            {
                shouldShow = false;
            }
            itemInteractButton.gameObject.SetActive(shouldShow);
        }
    }

    private void OnDisable()
    {
        // Turn off any active outlines when the script is disabled
        if (currentTargetBox != null)
        {
            currentTargetBox.SetOutline(false);
            currentTargetBox = null;
        }

        if (currentTargetOrb != null)
        {
            currentTargetOrb.SetFocus(false);
            currentTargetOrb = null;
        }

        if (itemInteractButton != null)
        {
            itemInteractButton.gameObject.SetActive(false);
        }
    }
}
