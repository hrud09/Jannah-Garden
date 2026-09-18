using System;
using UnityEngine;
using TMPro;

public class DayNightCycle : MonoBehaviour
{
    [Header("Lighting")]
    public Light directionalLight;
    public float maxIntensity = 1f;

    [Header("Skyboxes")]
    public Material daySkybox;

    [Tooltip("The current real-world time in hours (0 to 24), read from the device clock.")]
    [Range(0f, 24f)]
    public float currentTimeInHours;

    [Tooltip("The current real-world time in HH:MM:SS format, visible here in the inspector.")]
    public string currentTimeString;

    [Header("UI")]
    public TextMeshProUGUI timeText;

    void Start()
    {
        // Always render as full daytime - no time-based lighting/skybox changes.
        if (directionalLight != null)
        {
            directionalLight.intensity = maxIntensity;
            directionalLight.transform.rotation = Quaternion.Euler(90f, 50f, 0f);
        }

        if (daySkybox != null)
        {
            RenderSettings.skybox = daySkybox;
            DynamicGI.UpdateEnvironment();
        }

        UpdateTimeText();
    }

    void Update()
    {
        UpdateTimeText();
    }

    void UpdateTimeText()
    {
        DateTime now = DateTime.Now;
        int hours = now.Hour;
        int minutes = now.Minute;
        int seconds = now.Second;

        currentTimeInHours = hours + (minutes / 60f) + (seconds / 3600f);
        currentTimeString = string.Format("{0:00}:{1:00}:{2:00}", hours, minutes, seconds);

        if (timeText != null)
        {
            timeText.text = currentTimeString;
        }
    }
}
