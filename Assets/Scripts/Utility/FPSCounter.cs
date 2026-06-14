using UnityEngine;
using TMPro;

public class FPSCounter : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The TextMeshProUGUI component to display the FPS. If left blank, it will try to get it from the same GameObject.")]
    [SerializeField] private TextMeshProUGUI fpsText;

    [Header("Settings")]
    [Tooltip("How often in seconds the FPS text updates.")]
    [SerializeField] private float updateInterval = 0.5f;

    private float timeAccumulator = 0f;
    private int frameCount = 0;

    private void Awake()
    {
        if (fpsText == null)
        {
            fpsText = GetComponent<TextMeshProUGUI>();
        }
    }

    private void Update()
    {
        // Use unscaledDeltaTime to get the real time elapsed regardless of game time scale (e.g., if game is paused)
        timeAccumulator += Time.unscaledDeltaTime;
        frameCount++;

        if (timeAccumulator >= updateInterval)
        {
            // Calculate accurate average FPS over the interval
            float currentFps = frameCount / timeAccumulator;
            
            if (fpsText != null)
            {
                fpsText.text = $"FPS: {Mathf.RoundToInt(currentFps)}";
            }

            // Reset counters for the next interval
            timeAccumulator = 0f;
            frameCount = 0;
        }
    }
}
