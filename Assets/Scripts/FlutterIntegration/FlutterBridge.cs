using UnityEngine;
using System;
using System.Collections;
using System.Globalization;

namespace FlutterIntegration
{
    /// <summary>
    /// The single entry point for all Flutter ↔ Unity data traffic.
    ///
    /// The bridge is scene-independent: it bootstraps itself before the first scene loads and survives
    /// scene changes, so Flutter can reach it whether the player is in Jannah Garden, Outer Garden, or
    /// anywhere else. You do NOT need to place it in each scene by hand — a copy left in a scene simply
    /// defers to the bootstrapped one.
    ///
    /// Flutter addresses Unity objects BY GAMEOBJECT NAME, so the host object must always be called
    /// <see cref="GameObjectName"/> and the entry method <see cref="ReceiveMessageFromFlutter"/>.
    /// Both are enforced below — renaming either one breaks the Flutter side silently.
    ///
    /// Incoming payloads are cached. A scene that loads *after* Flutter pushed its data (Outer Garden,
    /// typically) reads the cache on Start rather than missing the message. See flutter_bridge_guide.md.
    /// </summary>
    public class FlutterBridge : MonoBehaviour
    {
        /// <summary>The name Flutter targets with sendToUnity(). Must not change.</summary>
        public const string GameObjectName = "Flutter Bridge";

        public static FlutterBridge Instance { get; private set; }

        // ─── Events ───────────────────────────────────────────────────────────────
        // Static so listeners can subscribe before the bridge exists, and survive scene loads.

        public static event Action<UserProfilePayload> OnUserProfileReceived;
        public static event Action<CoinUpdatePayload> OnCoinsReceived;
        public static event Action<FellowshipProfilesPayload> OnFellowshipProfilesReceived;

        // ─── Cached payloads ──────────────────────────────────────────────────────
        // Flutter may push data long before the scene that needs it is loaded, so we keep the last of
        // each. Consumers read these on Start and subscribe to the matching event for later updates.

        public static UserProfilePayload LatestUserProfile { get; private set; }
        public static FellowshipProfilesPayload LatestFellowshipProfiles { get; private set; }

        /// <summary>
        /// The last authoritative Noor Coin balance Flutter pushed, or null if none yet. Cached so a
        /// <see cref="NoorCoinManager"/> that initialises *after* Flutter's message (e.g. because the
        /// message arrived during a loading scene) can still adopt it. See <see cref="ApplyCoinBalance"/>.
        /// </summary>
        public static int? LatestCoinBalance { get; private set; }

        /// <summary>True once Flutter has sent at least one non-empty fellowship roster.</summary>
        public static bool HasFellowshipProfiles =>
            LatestFellowshipProfiles?.fellows != null && LatestFellowshipProfiles.fellows.Length > 0;

        // ─── Handshake ────────────────────────────────────────────────────────────
        // A single UNITY_READY on Start is not enough to get the coin balance: Flutter's listener may not
        // be attached yet when Unity's first frame runs, and a dropped announcement is never retried, so
        // the game sits on 0 coins forever. The handshake below re-announces until Flutter answers.

        /// <summary>How long to wait for Flutter's answer before announcing again.</summary>
        private const float HandshakeRetrySeconds = 1.5f;

        /// <summary>How many times to ask before giving up (~12s of asking).</summary>
        private const int HandshakeMaxAttempts = 8;

        private Coroutine _handshake;
        private bool _awaitingCoinBalance;

        // ─── Bootstrap ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the bridge before the first scene loads, so every scene has it regardless of which
        /// one the game boots into.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            GameObject host = new GameObject(GameObjectName);
            host.AddComponent<FlutterBridge>(); // Awake wires up Instance + DontDestroyOnLoad
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A scene shipped its own copy. The bootstrapped one already won.
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Flutter finds this object by name, so a renamed/nested host must be corrected.
            if (gameObject.name != GameObjectName) gameObject.name = GameObjectName;
            if (transform.parent != null) transform.SetParent(null);

            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Tell Flutter the bridge is live — and keep telling it until the coin balance comes back.
            // Flutter should push the user profile, coins, and the fellowship roster in response.
            BeginHandshake("game start");
        }

        /// <summary>
        /// flutter_embed_unity keeps a single Unity instance alive: leaving the game pauses the player
        /// instead of tearing it down, so <see cref="Start"/> never runs a second time. Resume is
        /// therefore the only signal that the player "started the game" again — re-handshake so the
        /// balance comes back fresh from Firebase rather than being whatever was on screen when they left
        /// (they may have spent or earned coins in the app in between).
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (!paused) BeginHandshake("game resumed");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Announces UNITY_READY and asks for the coin balance, repeating until Flutter answers with one
        /// (or the attempts run out). Restarts any handshake already in flight.
        /// </summary>
        private void BeginHandshake(string reason)
        {
            _awaitingCoinBalance = true;

            if (_handshake != null) StopCoroutine(_handshake);
            _handshake = StartCoroutine(HandshakeUntilCoinsArrive(reason));
        }

        private IEnumerator HandshakeUntilCoinsArrive(string reason)
        {
            for (int attempt = 1; attempt <= HandshakeMaxAttempts; attempt++)
            {
                Debug.Log($"[FlutterBridge] Handshake ({reason}) attempt {attempt}/{HandshakeMaxAttempts} — announcing {FlutterCommands.UnityReady} and requesting the Noor Coin balance.");

                SendMessageToFlutterApp(FlutterCommands.UnityReady, new EmptyPayload());
                SendMessageToFlutterApp(FlutterCommands.RequestCoinBalance, new EmptyPayload());

                // Realtime: a paused/slowed game (timeScale 0 behind a panel) must still retry.
                yield return new WaitForSecondsRealtime(HandshakeRetrySeconds);

                if (!_awaitingCoinBalance)
                {
                    _handshake = null;
                    yield break;
                }
            }

            Debug.LogWarning($"[FlutterBridge] Flutter sent no Noor Coin balance after {HandshakeMaxAttempts} attempts — the game is running on its local balance. Check that the Flutter side answers {FlutterCommands.UnityReady} / {FlutterCommands.RequestCoinBalance} with UPDATE_COINS or UPDATE_USER_PROFILE.");
            _handshake = null;
        }

        // ─── Flutter → Unity ──────────────────────────────────────────────────────

        /// <summary>
        /// Called directly by Flutter via sendToUnity("Flutter Bridge", "ReceiveMessageFromFlutter", json).
        /// The name of this method is part of the contract — do not rename it.
        /// </summary>
        /// <param name="jsonMessage">A JSON <see cref="FlutterMessage"/> whose `data` is a JSON *string*.</param>
        public void ReceiveMessageFromFlutter(string jsonMessage)
        {
            Debug.Log($"[FlutterBridge] Received Raw Message: {jsonMessage}");

            try
            {
                FlutterMessage message = JsonUtility.FromJson<FlutterMessage>(jsonMessage);

                if (message == null || string.IsNullOrEmpty(message.command))
                {
                    Debug.LogWarning("[FlutterBridge] Received a message with no command.");
                    return;
                }

                HandleCommand(message.command, message.data);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FlutterBridge] Error parsing Flutter message: {e.Message}");
            }
        }

        private void HandleCommand(string command, string dataJson)
        {
            switch (command)
            {
                case FlutterCommands.UpdateUserProfile:
                {
                    UserProfilePayload profile = JsonUtility.FromJson<UserProfilePayload>(dataJson);
                    if (profile == null) break;

                    LatestUserProfile = profile;
                    Debug.Log($"[FlutterBridge] Profile -> Name: {profile.userName}, Coins: {profile.noorCoins}");

                    ApplyCoinBalance(profile.noorCoins);
                    OnUserProfileReceived?.Invoke(profile);
                    break;
                }

                case FlutterCommands.UpdateCoins:
                {
                    CoinUpdatePayload coinData = JsonUtility.FromJson<CoinUpdatePayload>(dataJson);
                    if (coinData == null) break;

                    Debug.Log($"[FlutterBridge] Coins -> New Balance: {coinData.newBalance}");

                    ApplyCoinBalance(coinData.newBalance);
                    OnCoinsReceived?.Invoke(coinData);
                    break;
                }

                case FlutterCommands.IAPPurchaseResult:
                {
                    IAPPurchaseResultPayload result = JsonUtility.FromJson<IAPPurchaseResultPayload>(dataJson);
                    if (result == null) break;

                    Debug.Log($"[FlutterBridge] IAP -> Product: {result.productId}, Success: {result.success}");

                    if (IAPManager.Instance == null)
                    {
                        Debug.LogWarning("[FlutterBridge] No IAPManager in this scene — purchase result dropped.");
                        break;
                    }

                    IAPManager.Instance.HandlePurchaseResult(result.productId, result.success, result.message);
                    break;
                }

                case FlutterCommands.UpdateFellowshipProfiles:
                {
                    FellowshipProfilesPayload roster = JsonUtility.FromJson<FellowshipProfilesPayload>(dataJson);

                    if (roster?.fellows == null)
                    {
                        Debug.LogWarning("[FlutterBridge] Fellowship payload had no 'fellows' array — ignoring.");
                        break;
                    }

                    LatestFellowshipProfiles = roster;
                    Debug.Log($"[FlutterBridge] Fellowship -> {roster.fellows.Length} profile(s) received.");

                    // Fires only if a listener exists; otherwise the roster waits in the cache for the
                    // next scene that wants it.
                    OnFellowshipProfilesReceived?.Invoke(roster);
                    break;
                }

                default:
                    Debug.LogWarning($"[FlutterBridge] Unhandled command: {command}");
                    break;
            }
        }

        /// <summary>Pushes an authoritative balance from Flutter into the game's economy.</summary>
        private void ApplyCoinBalance(int balance)
        {
            // Cache first — unconditionally — so the value survives even when no manager exists yet. A
            // NoorCoinManager that spawns in a later scene reads this on Awake (see NoorCoinManager.Awake).
            LatestCoinBalance = balance;

            // Flutter answered, so the handshake can stop asking.
            _awaitingCoinBalance = false;

            if (NoorCoinManager.Instance == null)
            {
                Debug.LogWarning("[FlutterBridge] No NoorCoinManager yet — balance cached; it will be applied when the manager loads.");
                return;
            }

            NoorCoinManager.Instance.SetInitialCoinsFromFlutter(balance.ToString(CultureInfo.InvariantCulture));
        }

        // ─── Unity → Flutter ──────────────────────────────────────────────────────

        /// <summary>
        /// Asks Flutter to (re)send the fellowship roster. Called by
        /// <see cref="FellowshipVisualizationManager"/> when it loads into a scene with nothing cached.
        /// </summary>
        public void RequestFellowshipProfiles()
        {
            SendMessageToFlutterApp(FlutterCommands.RequestFellowshipProfiles, new EmptyPayload());
        }

        /// <summary>
        /// Asks Flutter for the player's authoritative Noor Coin balance. The handshake already does this
        /// on every game start; call it directly when something loads late and finds nothing cached (see
        /// <see cref="NoorCoinManager"/>).
        /// </summary>
        public void RequestCoinBalance()
        {
            SendMessageToFlutterApp(FlutterCommands.RequestCoinBalance, new EmptyPayload());
        }

        /// <summary>
        /// Sends a message back to Flutter using flutter_embed_unity's SendToFlutter.
        /// </summary>
        public void SendMessageToFlutterApp(string command, object payloadData)
        {
            try
            {
                // The payload is serialized to a string first, then nested inside the wrapper — this is
                // the same shape Flutter sends to us.
                string payloadJson = JsonUtility.ToJson(payloadData);

                FlutterMessage msg = new FlutterMessage
                {
                    command = command,
                    data = payloadJson
                };

                string finalJson = JsonUtility.ToJson(msg);

                SendToFlutter.Send(finalJson);

                Debug.Log($"[FlutterBridge] Sent to Flutter: {finalJson}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FlutterBridge] Error constructing message for Flutter: {e.Message}");
            }
        }
    }
}
