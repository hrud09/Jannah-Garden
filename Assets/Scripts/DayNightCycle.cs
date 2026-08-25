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

    [Header("Time Simulation")]
    [Tooltip("How much faster time passes compared to real life. (e.g. 2 means 2x faster, 60 means 1 real minute = 1 in-game hour)")]
    public float timeMultiplier = 1f;

    [Tooltip("The current simulated time in hours (0 to 24). You can scrub this to change time manually!")]
    [Range(0f, 24f)]
    public float currentTimeInHours;

    [Tooltip("The current simulated time in HH:MM format, visible here in the inspector.")]
    public string currentTimeString;

    [Header("UI")]
    public TextMeshProUGUI timeText;

    void Start()
    {
        // Initial setup based on current real-world time
        DateTime now = DateTime.Now;
        currentTimeInHours = now.Hour + (now.Minute / 60f) + (now.Second / 3600f);

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
        // Advance time based on Time.deltaTime and the multiplier
        // Time.deltaTime is in seconds. We convert to hours by dividing by 3600
        currentTimeInHours += (Time.deltaTime * timeMultiplier) / 3600f;

        // Loop time back around if it exceeds 24 hours
        if (currentTimeInHours >= 24f)
        {
            currentTimeInHours %= 24f;
        }

        UpdateTimeText();
    }

    void UpdateTimeText()
    {
        int hours = Mathf.FloorToInt(currentTimeInHours);
        int minutes = Mathf.FloorToInt((currentTimeInHours - hours) * 60f);
        currentTimeString = string.Format("{0:00}:{1:00}", hours, minutes);

        if (timeText != null)
        {
            timeText.text = currentTimeString;
        }
    }
}
