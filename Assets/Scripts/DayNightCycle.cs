using System;
using UnityEngine;
using TMPro;

public class DayNightCycle : MonoBehaviour
{
    [Header("Lighting")]
    public Light directionalLight;
    public float maxIntensity = 1f;
    public float minIntensity = 0f;

    [Header("Skyboxes")]
    public Material daySkybox;
    public Material nightSkybox;

    [Header("Time Simulation")]
    [Tooltip("How much faster time passes compared to real life. (e.g. 2 means 2x faster, 60 means 1 real minute = 1 in-game hour)")]
    public float timeMultiplier = 1f;

    [Tooltip("The current simulated time in hours (0 to 24). You can scrub this to change time manually!")]
    [Range(0f, 24f)]
    public float currentTimeInHours;
    
    [Tooltip("The current simulated time in HH:MM format, visible here in the inspector.")]
    public string currentTimeString;

    [Header("Transition")]
    [Tooltip("How long (in simulated hours) the skybox takes to transition between day and night.")]
    public float transitionDuration = 2f;

    [Header("UI")]
    public TextMeshProUGUI timeText;
    
    // Internal time state
    private bool isDaytime;
    private Material runtimeDaySkybox;
    private Material runtimeNightSkybox;
    private float dayOriginalExposure = 1f;
    private float nightOriginalExposure = 1f;
    
    private Color dayOriginalColor = Color.white;
    private Color nightOriginalColor = Color.white;

    // Hardcoded usual times for sunrise/sunset
    private const float sunriseHour = 6f; // 6:00 AM
    private const float sunsetHour = 18f; // 6:00 PM

    void Start()
    {
        // Initial setup based on current real-world time
        DateTime now = DateTime.Now;
        currentTimeInHours = now.Hour + (now.Minute / 60f) + (now.Second / 3600f);
        
        // Create runtime instances so we can safely modify exposure without changing your project assets
        if (daySkybox != null)
        {
            runtimeDaySkybox = new Material(daySkybox);
            if (runtimeDaySkybox.HasProperty("_Exposure"))
                dayOriginalExposure = runtimeDaySkybox.GetFloat("_Exposure");
            dayOriginalColor = GetSkyboxColor(runtimeDaySkybox);
        }
        
        if (nightSkybox != null)
        {
            runtimeNightSkybox = new Material(nightSkybox);
            if (runtimeNightSkybox.HasProperty("_Exposure"))
                nightOriginalExposure = runtimeNightSkybox.GetFloat("_Exposure");
            nightOriginalColor = GetSkyboxColor(runtimeNightSkybox);
        }

        isDaytime = currentTimeInHours >= sunriseHour && currentTimeInHours < sunsetHour;
        RenderSettings.skybox = isDaytime ? runtimeDaySkybox : runtimeNightSkybox;
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

        UpdateTimeState();
    }

    void UpdateTimeState()
    {
        // Update Light Intensity
        float intensityMultiplier = 0f;
        if (currentTimeInHours >= sunriseHour && currentTimeInHours < sunsetHour)
        {
            // It's daytime
            float dayDuration = sunsetHour - sunriseHour;
            float timeSinceSunrise = currentTimeInHours - sunriseHour;
            
            // Use a sine wave to peak intensity at noon
            intensityMultiplier = Mathf.Sin((timeSinceSunrise / dayDuration) * Mathf.PI);
        }
        else
        {
            // It's nighttime
            intensityMultiplier = 0f;
        }

        if (directionalLight != null)
        {
            // Set light intensity
            directionalLight.intensity = Mathf.Lerp(minIntensity, maxIntensity, intensityMultiplier);
            
            // Rotate the directional light to simulate sun movement
            // 6 AM (sunrise) = 0 degrees (horizon)
            // 12 PM (noon) = 90 degrees (straight down)
            // 6 PM (sunset) = 180 degrees (opposite horizon)
            float sunAngle = Mathf.Lerp(-90f, 270f, currentTimeInHours / 24f);
            directionalLight.transform.rotation = Quaternion.Euler(sunAngle, 50f, 0f); // 50 is a slight offset so shadows look better
        }

        int hours = Mathf.FloorToInt(currentTimeInHours);
        int minutes = Mathf.FloorToInt((currentTimeInHours - hours) * 60f);
        currentTimeString = string.Format("{0:00}:{1:00}", hours, minutes);

        // Update UI Time
        if (timeText != null)
        {
            timeText.text = currentTimeString;
        }

        // Switch Skybox smoothly
        UpdateSkyboxBlend();
    }

    void UpdateSkyboxBlend()
    {
        if (runtimeDaySkybox == null || runtimeNightSkybox == null) return;

        // Calculate transition boundaries
        float sunriseStart = sunriseHour - (transitionDuration / 2f);
        float sunriseEnd = sunriseHour + (transitionDuration / 2f);
        
        float sunsetStart = sunsetHour - (transitionDuration / 2f);
        float sunsetEnd = sunsetHour + (transitionDuration / 2f);

        if (currentTimeInHours > sunriseStart && currentTimeInHours < sunriseEnd)
        {
            // Transitioning from Night to Day (Sunrise)
            float t = Mathf.InverseLerp(sunriseStart, sunriseEnd, currentTimeInHours);
            Color blendedColor = Color.Lerp(nightOriginalColor, dayOriginalColor, t);
            
            if (t < 0.5f)
            {
                RenderSettings.skybox = runtimeNightSkybox;
                SetSkyboxColor(runtimeNightSkybox, blendedColor);
                // Dim to 30% exposure to mask the swap, rather than pitch black
                float fade = Mathf.Lerp(nightOriginalExposure, nightOriginalExposure * 0.3f, t * 2f); 
                if (runtimeNightSkybox.HasProperty("_Exposure")) runtimeNightSkybox.SetFloat("_Exposure", fade);
            }
            else
            {
                RenderSettings.skybox = runtimeDaySkybox;
                SetSkyboxColor(runtimeDaySkybox, blendedColor);
                float fade = Mathf.Lerp(dayOriginalExposure * 0.3f, dayOriginalExposure, (t - 0.5f) * 2f); 
                if (runtimeDaySkybox.HasProperty("_Exposure")) runtimeDaySkybox.SetFloat("_Exposure", fade);
            }
            DynamicGI.UpdateEnvironment(); 
        }
        else if (currentTimeInHours > sunsetStart && currentTimeInHours < sunsetEnd)
        {
            // Transitioning from Day to Night (Sunset)
            float t = Mathf.InverseLerp(sunsetStart, sunsetEnd, currentTimeInHours);
            Color blendedColor = Color.Lerp(dayOriginalColor, nightOriginalColor, t);
            
            if (t < 0.5f)
            {
                RenderSettings.skybox = runtimeDaySkybox;
                SetSkyboxColor(runtimeDaySkybox, blendedColor);
                float fade = Mathf.Lerp(dayOriginalExposure, dayOriginalExposure * 0.3f, t * 2f);
                if (runtimeDaySkybox.HasProperty("_Exposure")) runtimeDaySkybox.SetFloat("_Exposure", fade);
            }
            else
            {
                RenderSettings.skybox = runtimeNightSkybox;
                SetSkyboxColor(runtimeNightSkybox, blendedColor);
                float fade = Mathf.Lerp(nightOriginalExposure * 0.3f, nightOriginalExposure, (t - 0.5f) * 2f);
                if (runtimeNightSkybox.HasProperty("_Exposure")) runtimeNightSkybox.SetFloat("_Exposure", fade);
            }
            DynamicGI.UpdateEnvironment();
        }
        else if (currentTimeInHours >= sunriseEnd && currentTimeInHours <= sunsetStart)
        {
            // Full Day
            RenderSettings.skybox = runtimeDaySkybox;
            SetSkyboxColor(runtimeDaySkybox, dayOriginalColor);
            if (runtimeDaySkybox.HasProperty("_Exposure")) runtimeDaySkybox.SetFloat("_Exposure", dayOriginalExposure);
        }
        else
        {
            // Full Night
            RenderSettings.skybox = runtimeNightSkybox;
            SetSkyboxColor(runtimeNightSkybox, nightOriginalColor);
            if (runtimeNightSkybox.HasProperty("_Exposure")) runtimeNightSkybox.SetFloat("_Exposure", nightOriginalExposure);
        }
    }

    Color GetSkyboxColor(Material mat)
    {
        if (mat.HasProperty("_Tint")) return mat.GetColor("_Tint");
        if (mat.HasProperty("_SkyTint")) return mat.GetColor("_SkyTint"); // Used in procedural skyboxes
        return Color.white;
    }

    void SetSkyboxColor(Material mat, Color col)
    {
        if (mat.HasProperty("_Tint")) mat.SetColor("_Tint", col);
        if (mat.HasProperty("_SkyTint")) mat.SetColor("_SkyTint", col);
    }
}
