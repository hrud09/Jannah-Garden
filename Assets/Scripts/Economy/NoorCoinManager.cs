using UnityEngine;
using System;

/// <summary>
/// Singleton manager for the Noor Coin economy.
/// Holds the player's current balance, persists it via SaveSystem,
/// and fires events whenever the balance changes.
/// </summary>
public class NoorCoinManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static NoorCoinManager Instance { get; private set; }

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired whenever the balance changes. Passes the new balance.</summary>
    public static event Action<int> OnBalanceChanged;

    // ─── Inspector Fields ─────────────────────────────────────────────────────

    [Header("Starting Balance")]
    [Tooltip("Noor Coins the player starts with on a fresh save.")]
    [SerializeField] private int startingBalance = 500;

    // ─── Private State ────────────────────────────────────────────────────────

    private const string SAVE_KEY = "NoorCoinBalance";
    private int _balance;

    // ─── Properties ───────────────────────────────────────────────────────────

    /// <summary>The player's current Noor Coin balance (read-only externally).</summary>
    public int Balance => _balance;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Enforce singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadBalance();
    }

    private void OnApplicationQuit() => SaveBalance();
    private void OnApplicationPause(bool paused) { if (paused) SaveBalance(); }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to spend <paramref name="amount"/> Noor Coins.
    /// Returns <c>true</c> and deducts the cost if the player can afford it;
    /// returns <c>false</c> and leaves the balance unchanged if they cannot.
    /// </summary>
    public bool TrySpend(int amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning($"[NoorCoinManager] TrySpend called with negative amount ({amount}). Ignored.");
            return false;
        }

        if (_balance < amount)
        {
            Debug.Log($"[NoorCoinManager] Not enough Noor Coins. Need {amount}, have {_balance}.");
            return false;
        }

        SetBalance(_balance - amount);
        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast($"-{amount} Noor Coins", Color.red);
        }
        Debug.Log($"[NoorCoinManager] Spent {amount} Noor Coins. New balance: {_balance}");
        
        // --- FLUTTER COMMUNICATION ---
        // Notify Flutter so it can deduct from Firebase
        SendCoinUpdateToFlutter(-amount);

        return true;
    }

    /// <summary>Adds <paramref name="amount"/> Noor Coins to the player's balance.</summary>
    public void Earn(int amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning($"[NoorCoinManager] Earn called with negative amount ({amount}). Ignored.");
            return;
        }

        SetBalance(_balance + amount);
        if (ToastMessageManager.Instance != null)
        {
            ToastMessageManager.Instance.ShowToast($"+{amount} Noor Coins", Color.green);
        }
        Debug.Log($"[NoorCoinManager] Earned {amount} Noor Coins. New balance: {_balance}");
        
        // --- FLUTTER COMMUNICATION ---
        // Notify Flutter so it can add to Firebase
        SendCoinUpdateToFlutter(amount);
    }

    /// <summary>
    /// Returns <c>true</c> if the player has enough coins to afford <paramref name="cost"/>.
    /// </summary>
    public bool CanAfford(int cost) => _balance >= cost;

    /// <summary>
    /// Resets the balance to the configured starting value and saves.
    /// Useful for debug / testing purposes.
    /// </summary>
    public void ResetToStartingBalance()
    {
        SetBalance(startingBalance);
        SaveBalance();
        Debug.Log($"[NoorCoinManager] Balance reset to starting value: {startingBalance}");
    }

    // ─── Flutter Communication API ────────────────────────────────────────────

    /// <summary>
    /// Flutter will call this method directly when the embedded Unity view opens,
    /// passing the user's Firebase Noor Coin balance as a string.
    /// </summary>
    public void SetInitialCoinsFromFlutter(string coinCountString)
    {
        if (int.TryParse(coinCountString, out int coinCount))
        {
            // Set balance directly without triggering Earn/Spend notifications
            _balance = Mathf.Max(0, coinCount);
            OnBalanceChanged?.Invoke(_balance);
            SaveBalance(); // Optionally keep saving locally as a fallback
            
            Debug.Log($"[NoorCoinManager] Initial balance set from Flutter/Firebase: {_balance}");
        }
        else
        {
            Debug.LogError($"[NoorCoinManager] Failed to parse coin count from Flutter: {coinCountString}");
        }
    }

    /// <summary>
    /// Sends a message back to Flutter indicating a change in the coin balance.
    /// amountChange is positive for earning, negative for spending.
    /// </summary>
    private void SendCoinUpdateToFlutter(int amountChange)
    {
        string message = $"CoinUpdate:{amountChange}";
        
        // TODO: Uncomment this when you import the flutter_unity_widget package!
        // FlutterUnityIntegration.UnityMessageManager.Instance.SendMessageToFlutter(message);
        
        Debug.Log($"[NoorCoinManager] Sent to Flutter: {message}");
    }

    // ─── Internal Helpers ─────────────────────────────────────────────────────

    private void SetBalance(int newBalance)
    {
        _balance = Mathf.Max(0, newBalance); // Never go negative
        OnBalanceChanged?.Invoke(_balance);
        SaveBalance();
    }

    private void SaveBalance()
    {
        SaveSystem.Save(SAVE_KEY, _balance);
    }

    private void LoadBalance()
    {
        if (SaveSystem.Exists(SAVE_KEY))
        {
            _balance = SaveSystem.Load<int>(SAVE_KEY);
            Debug.Log($"[NoorCoinManager] Loaded balance: {_balance} Noor Coins.");
        }
        else
        {
            _balance = startingBalance;
            Debug.Log($"[NoorCoinManager] No save found. Starting with {startingBalance} Noor Coins.");
        }

        // Notify listeners of initial balance (useful for UI that initialises later)
        OnBalanceChanged?.Invoke(_balance);
    }
}
