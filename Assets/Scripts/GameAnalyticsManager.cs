using UnityEngine;
using GameAnalyticsSDK;

/// <summary>
/// Wrapper for integrating GameAnalytics SDK into Jannah Garden.
/// Automatically handles initialization, session tracking, economy, and progression events.
/// </summary>
public class GameAnalyticsManager : MonoBehaviour
{
    public static GameAnalyticsManager Instance { get; private set; }

    private int _lastXPLevel = -1;
    private int _lastCoinBalance = -1;

    private bool _isInitialized = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Initialize GameAnalytics SDK in Awake so it is ready before OnApplicationPause or Start runs
        GameAnalytics.Initialize();
        _isInitialized = true;
    }

    private void Start()
    {
        if (_isInitialized)
        {
            // Game Start Event
            GameAnalytics.NewDesignEvent("Game:Start");
        }

        // Hook up to existing managers for Progression and Economy
        if (PlayerXPManager.Instance != null)
        {
            _lastXPLevel = PlayerXPManager.Instance.xpLevel;
            PlayerXPManager.Instance.OnXPChanged += OnXPChanged;
        }

        if (NoorCoinManager.Instance != null)
        {
            _lastCoinBalance = NoorCoinManager.Instance.Balance;
            NoorCoinManager.OnBalanceChanged += OnCoinBalanceChanged;
        }
    }

    private void OnDestroy()
    {
        if (PlayerXPManager.Instance != null)
        {
            PlayerXPManager.Instance.OnXPChanged -= OnXPChanged;
        }

        NoorCoinManager.OnBalanceChanged -= OnCoinBalanceChanged;
    }

    private void OnApplicationQuit()
    {
        if (_isInitialized)
        {
            GameAnalytics.NewDesignEvent("Game:Quit");
        }
    }

    private void OnApplicationPause(bool pause)
    {
        if (!_isInitialized) return;

        if (pause)
        {
            GameAnalytics.NewDesignEvent("Game:Pause");
        }
        else
        {
            GameAnalytics.NewDesignEvent("Game:Resume");
        }
    }

    // ─── Progression Events (XP System) ──────────────────────────────────────

    private void OnXPChanged(int newLevel, float currentXP, float xpToNextLevel)
    {
        if (!_isInitialized) return;

        if (_lastXPLevel != -1 && newLevel > _lastXPLevel)
        {
            // Player leveled up
            GameAnalytics.NewProgressionEvent(GAProgressionStatus.Complete, "PlayerLevel", $"Level_{newLevel}");
        }
        _lastXPLevel = newLevel;
    }

    // ─── Economy Events (Noor Coins) ─────────────────────────────────────────

    private void OnCoinBalanceChanged(int newBalance)
    {
        if (!_isInitialized) return;

        if (_lastCoinBalance == -1)
        {
            _lastCoinBalance = newBalance;
            return;
        }

        int diff = newBalance - _lastCoinBalance;
        if (diff > 0)
        {
            // Player earned coins
            GameAnalytics.NewResourceEvent(GAResourceFlowType.Source, "NoorCoin", diff, "Gameplay", "Earned");
        }
        else if (diff < 0)
        {
            // Player spent coins
            GameAnalytics.NewResourceEvent(GAResourceFlowType.Sink, "NoorCoin", Mathf.Abs(diff), "Gameplay", "Spent");
        }

        _lastCoinBalance = newBalance;
    }
}
