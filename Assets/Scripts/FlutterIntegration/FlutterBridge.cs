using UnityEngine;
using System;

namespace FlutterIntegration
{
    public class FlutterBridge : MonoBehaviour
    {
        public static FlutterBridge Instance { get; private set; }

        private void Awake()
        {
            // Ensure this acts as a singleton if needed
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// This method is called directly by the Flutter side via the UnityWidgetController.
        /// </summary>
        /// <param name="jsonMessage">The JSON string sent from Flutter.</param>
        public void ReceiveMessageFromFlutter(string jsonMessage)
        {
            Debug.Log($"[FlutterBridge] Received Raw Message: {jsonMessage}");

            try
            {
                // Parse the base command wrapper
                FlutterMessage message = JsonUtility.FromJson<FlutterMessage>(jsonMessage);

                if (string.IsNullOrEmpty(message.command))
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
                case "UPDATE_USER_PROFILE":
                    UserProfilePayload profile = JsonUtility.FromJson<UserProfilePayload>(dataJson);
                    Debug.Log($"[FlutterBridge] Updating Profile -> Name: {profile.userName}, Coins: {profile.noorCoins}");
                    // TODO: Route this to your GameSettingsManager or UI Manager
                    break;

                case "UPDATE_COINS":
                    CoinUpdatePayload coinData = JsonUtility.FromJson<CoinUpdatePayload>(dataJson);
                    Debug.Log($"[FlutterBridge] Updating Coins -> New Balance: {coinData.newBalance}");
                    // TODO: Route this to your Economy system
                    break;

                default:
                    Debug.LogWarning($"[FlutterBridge] Unhandled command: {command}");
                    break;
            }
        }

        /// <summary>
        /// Sends a message back to Flutter using flutter_embed_unity's SendToFlutter.
        /// </summary>
        public void SendMessageToFlutterApp(string command, object payloadData)
        {
            try
            {
                // We serialize the payload into a string first
                string payloadJson = JsonUtility.ToJson(payloadData);

                // Construct the wrapper message
                FlutterMessage msg = new FlutterMessage
                {
                    command = command,
                    data = payloadJson
                };

                string finalJson = JsonUtility.ToJson(msg);

                // Call the flutter_embed_unity plugin method to send string data back to Flutter
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
