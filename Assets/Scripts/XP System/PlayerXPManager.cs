using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using System.Collections.Generic;
using DG.Tweening;

public enum XPTaskType
{
    PlaceShopItem,
    AnswerQuestion1stTry,
    AnswerQuestionRetry,
    CompleteDhikr,
    OpenSilverBox,
    OpenGoldBox,
    OpenPlatinumBox,
    OpenDiamondBox
}

[Serializable]
public struct XPTaskReward
{
    public XPTaskType taskType;
    public float rewardAmount;
}

/// <summary>
/// Player XP manager with a nonlinear progression similar to Clash of Clans.
/// - Use AddXP(amount) to add XP; excess carries over to the next level(s).
/// - XP and level are persisted via PlayerPrefs.
/// - UI updates (TMP_Text and Slider) are handled automatically when values change.
/// </summary>
public class PlayerXPManager : MonoBehaviour
{
    public static PlayerXPManager Instance { get; private set; }

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

    [Header("Task XP Rewards")]
    [Tooltip("Configure XP rewards for different tasks here.")]
    public List<XPTaskReward> taskRewards = new List<XPTaskReward>()
    {
        new XPTaskReward { taskType = XPTaskType.PlaceShopItem, rewardAmount = 20f }, // Used as multiplier per level
        new XPTaskReward { taskType = XPTaskType.AnswerQuestion1stTry, rewardAmount = 50f },
        new XPTaskReward { taskType = XPTaskType.AnswerQuestionRetry, rewardAmount = 15f },
        new XPTaskReward { taskType = XPTaskType.CompleteDhikr, rewardAmount = 30f },
        new XPTaskReward { taskType = XPTaskType.OpenSilverBox, rewardAmount = 25f },
        new XPTaskReward { taskType = XPTaskType.OpenGoldBox, rewardAmount = 50f },
        new XPTaskReward { taskType = XPTaskType.OpenPlatinumBox, rewardAmount = 100f },
        new XPTaskReward { taskType = XPTaskType.OpenDiamondBox, rewardAmount = 200f }
    };

    [Header("UI Reference")]
    public TMP_Text xpLevelText;
    public Slider xpSlider;
    
    [Header("XP Gain Chart UI")]
    public Button xpGainChartToggleButton;
    public RectTransform xpGainChartPanel;
    public Transform xpGainChartContentParent;
    public GameObject xpChartDataPrefab;

    [Header("XP Gain Chart Animation")]
    public float chartTransitionDuration = 0.3f;
    public float chartClosedOffsetX = 500f; // Distance to move right when hidden

    private Vector2 _chartOpenedPos;
    private Vector2 _chartClosedPos;
    private bool _isChartOpen = false;
    private Coroutine _chartTransitionCoroutine;

    // SaveSystem key
    private const string SaveKey = "player_xp";

    // Cached xp required for the current level (computed)
    private float xpToNextLevel => CalculateXPToNextLevel(xpLevel);

    public event Action<int, float, float> OnXPChanged;
    // signature: (newLevel, currentXP, xpToNextLevel)

    /// <summary>Fired whenever the XP gain chart panel opens or closes. Onboarding waits for a close
    /// before covering the screen again with its own dim overlay, so it never re-hides the chart the
    /// player just opened to look at.</summary>
    public static event Action<bool> OnChartToggled;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        Load();
        UpdateUI();
        PopulateXPGainChart();

        if (xpGainChartToggleButton != null)
        {
            xpGainChartToggleButton.onClick.AddListener(ToggleXPGainChart);
        }
    }

    private void Start()
    {
        if (xpGainChartPanel != null)
        {
            // Store the position set in the inspector as the "opened" position
            _chartOpenedPos = xpGainChartPanel.anchoredPosition;
            
            // Calculate closed position (shifted to the right)
            _chartClosedPos = new Vector2(_chartOpenedPos.x + chartClosedOffsetX, _chartOpenedPos.y);
            
            // Start closed by default
            _isChartOpen = false;
            xpGainChartPanel.anchoredPosition = _chartClosedPos;
        }
    }

    private void OnEnable()
    {
        TreasureBoxManager.OnBoxOpened += HandleBoxOpened;
    }

    private void OnDisable()
    {
        TreasureBoxManager.OnBoxOpened -= HandleBoxOpened;
    }

    private void HandleBoxOpened(TreasureBoxTier tier, int slotIndex, TreasureBoxData data)
    {
        XPTaskType taskType = XPTaskType.OpenSilverBox; // Default
        switch(tier)
        {
            case TreasureBoxTier.Silver: taskType = XPTaskType.OpenSilverBox; break;
            case TreasureBoxTier.Gold: taskType = XPTaskType.OpenGoldBox; break;
            case TreasureBoxTier.Platinum: taskType = XPTaskType.OpenPlatinumBox; break;
            case TreasureBoxTier.Diamond: taskType = XPTaskType.OpenDiamondBox; break;
        }
        AddXPForTask(taskType);
    }

    private void OnDestroy()
    {
        if (xpGainChartToggleButton != null)
        {
            xpGainChartToggleButton.onClick.RemoveListener(ToggleXPGainChart);
        }
    }

    public void ToggleXPGainChart()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.XPGainChartToggle);
        if (xpGainChartPanel == null) return;

        _isChartOpen = !_isChartOpen;

        if (_chartTransitionCoroutine != null)
        {
            StopCoroutine(_chartTransitionCoroutine);
        }

        _chartTransitionCoroutine = StartCoroutine(TransitionChart(_isChartOpen ? _chartOpenedPos : _chartClosedPos));
        OnChartToggled?.Invoke(_isChartOpen);
    }

    private System.Collections.IEnumerator TransitionChart(Vector2 targetPos)
    {
        Vector2 startPos = xpGainChartPanel.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < chartTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / chartTransitionDuration);
            // Smooth step interpolation
            t = t * t * (3f - 2f * t);
            xpGainChartPanel.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            yield return null;
        }

        xpGainChartPanel.anchoredPosition = targetPos;
    }

    private void PopulateXPGainChart()
    {
        if (xpGainChartContentParent == null || xpChartDataPrefab == null) return;

        // Clear existing children (useful if called multiple times, e.g., on data refresh)
        foreach (Transform child in xpGainChartContentParent)
        {
            Destroy(child.gameObject);
        }

        foreach (var reward in taskRewards)
        {
            GameObject go = Instantiate(xpChartDataPrefab, xpGainChartContentParent);
            TMP_Text txt = go.GetComponentInChildren<TMP_Text>();
            if (txt != null)
            {
                // Format the text as "TaskName - Amount"
                txt.text = $"{reward.taskType} - {reward.rewardAmount}";
            }
        }
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

        UpdateUI(true);
        Save();

        if (leveledUp)
        {
            // optional: fire an event or play effects here
        }
    }

    /// <summary>
    /// Grants XP based on the predefined task type in the taskRewards list.
    /// </summary>
    public float AddXPForTask(XPTaskType taskType, bool showToast = true)
    {
        foreach (var reward in taskRewards)
        {
            if (reward.taskType == taskType)
            {
                if (reward.rewardAmount > 0f)
                {
                    AddXP(reward.rewardAmount);
                    Debug.Log($"[PlayerXPManager] Granted {reward.rewardAmount} XP for {taskType}.");
                    if (showToast && ToastMessageManager.Instance != null)
                    {
                        ToastMessageManager.Instance.ShowToast($"+{reward.rewardAmount} XP");
                    }
                    return reward.rewardAmount;
                }
                return 0f;
            }
        }
        Debug.LogWarning($"[PlayerXPManager] No XP reward defined for task: {taskType}");
        return 0f;
    }

    /// <summary>
    /// Grants XP when a shop item is placed, scaled by its required unlock level.
    /// </summary>
    public void AddXPForPlacingShopItem(int requiredXPLevel)
    {
        float multiplier = 0f;
        foreach (var reward in taskRewards)
        {
            if (reward.taskType == XPTaskType.PlaceShopItem)
            {
                multiplier = reward.rewardAmount;
                break;
            }
        }

        if (multiplier <= 0f)
        {
            Debug.LogWarning("[PlayerXPManager] No positive XP reward multiplier defined for PlaceShopItem task.");
            return;
        }

        // Give at least the multiplier if level is 0 or 1
        int effectiveLevel = Mathf.Max(1, requiredXPLevel);
        float amount = effectiveLevel * multiplier;
        
        if (amount > 0f)
        {
            AddXP(amount);
            Debug.Log($"[PlayerXPManager] Granted {amount} XP for placing a shop item (Level {requiredXPLevel}).");
            if (ToastMessageManager.Instance != null)
            {
                ToastMessageManager.Instance.ShowToast($"+{amount} XP");
            }
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

    private void UpdateUI(bool animate = false)
    {
        if (xpLevelText != null)
        {
            if (xpLevelText.text != $"{xpLevel}")
            {
                xpLevelText.text = $"{xpLevel}";
                if (animate)
                {
                    xpLevelText.transform.DOKill();
                    xpLevelText.transform.localScale = Vector3.one;
                    xpLevelText.transform.DOPunchScale(new Vector3(0.5f, 0.5f, 0.5f), 0.5f, 10, 1f);
                }
            }
        }

        if (xpSlider != null)
        {
            xpSlider.maxValue = xpToNextLevel;
            float targetValue = Mathf.Clamp(currentXP, 0f, xpToNextLevel);
            if (animate)
            {
                xpSlider.DOKill();
                xpSlider.DOValue(targetValue, 0.5f).SetEase(Ease.OutQuad);
            }
            else
            {
                xpSlider.value = targetValue;
            }
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
