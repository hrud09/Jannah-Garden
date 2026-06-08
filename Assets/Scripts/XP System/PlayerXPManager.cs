using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player XP manager with a nonlinear progression similar to Clash of Clans.
/// - Use AddXP(amount) to add XP; excess carries over to the next level(s).
/// - XP and level are persisted via PlayerPrefs.
/// - UI updates (TMP_Text and Slider) are handled automatically when values change.
/// </summary>
public class PlayerXPManager : MonoBehaviour
{
    [Header("Player Data")]
    [Tooltip("Current player level (starts at 1)")]
    public int xpLevel = 1;

    [Tooltip("Current XP accumulated towards the next level")]
    public float currentXP = 0f;

    [Header("XP Curve Settings")]
    [Tooltip("Base XP for level 1 -> 2 (tunable)")]
    [SerializeField] private float baseXP = 100f;

    [Tooltip("Exponent applied to level to get nonlinear growth (1.5 is typical)")]
    [SerializeField] private float levelExponent = 1.5f;

    [Header("UI Reference")]
    public TMP_Text xpLevelText;
    public Slider xpSlider;

    // SaveSystem key
    private const string SaveKey = "player_xp";

    // Cached xp required for the current level (computed)
    private float xpToNextLevel => CalculateXPToNextLevel(xpLevel);

    public event Action<int, float, float> OnXPChanged;
    // signature: (newLevel, currentXP, xpToNextLevel)

    private void Awake()
    {
        Load();
        UpdateUI();
    }

    /// <summary>
    /// Adds xp to the player. Will handle level ups and carryover.
    /// </summary>
    public void AddXP(float amount)
    {
        if (amount <= 0f) return;

        currentXP += amount;
        bool leveledUp = false;

        // handle multiple level ups if enough xp
        while (currentXP >= xpToNextLevel)
        {
            currentXP -= xpToNextLevel;
            xpLevel++;
            leveledUp = true;
        }

        UpdateUI();
        Save();

        if (leveledUp)
        {
            // optional: fire an event or play effects here
        }
    }

    /// <summary>
    /// Calculates xp required to reach the next level from the given level.
    /// This uses a nonlinear formula similar to mobile progression games: base * level^exponent
    /// </summary>
    private float CalculateXPToNextLevel(int level)
    {
        // ensure level is at least 1
        var l = Mathf.Max(1, level);
        // Use Mathf.Pow for nonlinear growth. This produces low early requirements and faster ramp.
        return Mathf.Floor(baseXP * Mathf.Pow(l, levelExponent));
    }

    /// <summary>
    /// Returns the normalized progress [0..1] towards the next level.
    /// </summary>
    public float GetProgressNormalized()
    {
        var next = xpToNextLevel;
        if (next <= 0f) return 0f;
        return Mathf.Clamp01(currentXP / next);
    }

    /// <summary>
    /// Resets XP and level to defaults (level 1, 0 xp).
    /// </summary>
    public void ResetProgress(bool save = true)
    {
        xpLevel = 1;
        currentXP = 0f;
        UpdateUI();
        if (save) Save();
    }

    private void UpdateUI()
    {
        if (xpLevelText != null)
        {
            xpLevelText.text = $"Level {xpLevel}";
        }

        if (xpSlider != null)
        {
            xpSlider.maxValue = xpToNextLevel;
            xpSlider.value = Mathf.Clamp(currentXP, 0f, xpToNextLevel);
        }

        OnXPChanged?.Invoke(xpLevel, currentXP, xpToNextLevel);
    }

    private void Save()
    {
        var data = new PlayerXPData
        {
            level = xpLevel,
            currentXP = currentXP
        };

        SaveSystem.Save(SaveKey, data);
    }

    private void Load()
    {
        var data = SaveSystem.Load<PlayerXPData>(SaveKey);
        if (data != null)
        {
            xpLevel = Mathf.Max(1, data.level);
            currentXP = Mathf.Max(0f, data.currentXP);
        }
        else
        {
            // clamp values to sensible ranges
            xpLevel = Mathf.Max(1, xpLevel);
            currentXP = Mathf.Max(0f, currentXP);
        }
    }

    [System.Serializable]
    private class PlayerXPData
    {
        public int level;
        public float currentXP;
    }

    // Debug helpers accessible from the inspector context menu
    [ContextMenu("Add 50 XP (Debug)")]
    private void DebugAdd50() => AddXP(50f);

    [ContextMenu("Reset XP (Debug)")]
    private void DebugReset() => ResetProgress(true);
}
