using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlaceableItem : MonoBehaviour
{
    [Header("Placement Time Settings")]
    public float placementDuration = 60f; // Total placement duration in seconds
    public float remainingDuration = 60f; // Remaining time in seconds

    [HideInInspector]
    public string uniqueId;
    [HideInInspector]
    public string prefabName;

    [Header("UI References (Optional)")]
    public TMP_Text timerText;
    public GameObject timerHolder;

    private bool isTracking = false;

    /// <summary>
    /// Initializes tracking values for this item.
    /// </summary>
    public void Initialize(string id, float totalDur, float remainingDur)
    {
        this.uniqueId = id;
        this.placementDuration = totalDur;
        this.remainingDuration = remainingDur;
        this.isTracking = true;
    }

    private void Start()
    {
        // Make the timerHolder face the camera
        if (timerHolder != null && timerHolder.GetComponent<Billboard>() == null)
        {
            timerHolder.AddComponent<Billboard>();
        }

        // Auto-detect a Text Mesh Pro text field in children if not assigned
        if (timerText == null)
        {
            timerText = GetComponentInChildren<TMP_Text>();
        }

        // Dynamically create a floating world-space billboard timer if missing
        if (timerText == null)
        {
            CreateFloatingTimerUI();
        }
    }

    private void CreateFloatingTimerUI()
    {
        // Create Canvas container
        GameObject canvasGo = new GameObject("FloatingTimerCanvas");
        canvasGo.transform.SetParent(this.transform);
        canvasGo.transform.localPosition = new Vector3(0, 3.5f, 0); // Positioned above the model
        canvasGo.transform.localRotation = Quaternion.identity;

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10;

        RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(3, 1);

        // Create TMP Text container
        GameObject textGo = new GameObject("TimerText");
        textGo.transform.SetParent(canvasGo.transform);
        textGo.transform.localPosition = Vector3.zero;
        textGo.transform.localRotation = Quaternion.identity;

        timerText = textGo.AddComponent<TextMeshPro>();
        timerText.fontSize = 4;
        timerText.alignment = TextAlignmentOptions.Center;
        timerText.color = Color.yellow;

        // Apply a Billboard effect to rotate towards the camera
        textGo.AddComponent<Billboard>();
    }

    private void Update()
    {
        if (!isTracking) return;

        remainingDuration -= Time.deltaTime;

        if (remainingDuration <= 0f)
        {
            remainingDuration = 0f;
            if (timerText != null)
            {
                timerText.text = "Completed!";
            }
        }
        else
        {
            if (timerText != null)
            {
                int minutes = Mathf.FloorToInt(remainingDuration / 60f);
                int seconds = Mathf.FloorToInt(remainingDuration % 60f);
                timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            }
        }
    }
}

/// <summary>
/// Simple helper behavior to rotate text towards the camera.
/// </summary>
public class Billboard : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            transform.LookAt(transform.position + mainCam.transform.rotation * Vector3.forward,
                             mainCam.transform.rotation * Vector3.up);
        }
    }
}
