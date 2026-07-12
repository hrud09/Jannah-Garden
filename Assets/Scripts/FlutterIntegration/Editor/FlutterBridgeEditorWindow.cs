using UnityEngine;
using UnityEditor;
using System;

namespace FlutterIntegration.Editor
{
    public class FlutterBridgeEditorWindow : EditorWindow
    {
        private int selectedCommandIndex = 0;
        private string[] availableCommands = new string[] { "UPDATE_USER_PROFILE", "UPDATE_COINS", "CUSTOM" };
        
        private string jsonPayloadInput = "{\n  \"userName\": \"Mahad\",\n  \"noorCoins\": 1500,\n  \"profileImagePath\": \"\"\n}";
        private string customCommandInput = "";
        private Vector2 scrollPos;

        [MenuItem("Window/Flutter Bridge Manager")]
        public static void ShowWindow()
        {
            GetWindow<FlutterBridgeEditorWindow>("Flutter Bridge");
        }

        private void OnGUI()
        {
            GUILayout.Label("Flutter -> Unity Message Simulator", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox("Use this tool to simulate incoming JSON messages from Flutter. This is useful for testing your game's UI and economy logic without needing to build and run the app on a phone.", MessageType.Info);
            EditorGUILayout.Space();

            // Command Selection
            GUILayout.Label("1. Select Command", EditorStyles.boldLabel);
            selectedCommandIndex = EditorGUILayout.Popup("Command type", selectedCommandIndex, availableCommands);

            string currentCommand = availableCommands[selectedCommandIndex];
            
            if (currentCommand == "CUSTOM")
            {
                customCommandInput = EditorGUILayout.TextField("Custom Command", customCommandInput);
                currentCommand = customCommandInput;
            }
            
            // Auto-populate template based on selection if user clicks a button
            if (GUILayout.Button("Load Default Template for Selection"))
            {
                LoadTemplate(availableCommands[selectedCommandIndex]);
            }

            EditorGUILayout.Space();

            // JSON Payload Input
            GUILayout.Label("2. Edit JSON Payload (Data Field)", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
            jsonPayloadInput = EditorGUILayout.TextArea(jsonPayloadInput, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            
            // Generate Full Wrapper JSON Preview
            GUILayout.Label("3. Full Message Preview (What Flutter sends)", EditorStyles.boldLabel);
            
            // We minify the data payload to put inside the wrapper, exactly as it happens in real scenarios
            string stringifiedData = "{}";
            try
            {
                // Simple validation check (doesn't catch all JSON errors, but enough for a preview)
                if(!string.IsNullOrEmpty(jsonPayloadInput))
                    stringifiedData = jsonPayloadInput.Replace("\n", "").Replace("\r", "").Replace(" ", ""); 
            }
            catch {}
            
            string fullJsonPreview = $"{{\n  \"command\": \"{currentCommand}\",\n  \"data\": \"{stringifiedData.Replace("\"", "\\\"")}\"\n}}";
            
            GUI.enabled = false;
            EditorGUILayout.TextArea(fullJsonPreview, GUILayout.Height(80));
            GUI.enabled = true;

            EditorGUILayout.Space();

            // Send Button
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Simulate Receive From Flutter", GUILayout.Height(40)))
            {
                SimulateMessage(currentCommand, jsonPayloadInput);
            }
            GUI.backgroundColor = Color.white;
            
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("You must be in Play Mode for the message to be received by the FlutterBridge.", MessageType.Warning);
            }
        }

        private void LoadTemplate(string commandType)
        {
            switch (commandType)
            {
                case "UPDATE_USER_PROFILE":
                    jsonPayloadInput = "{\n  \"userName\": \"PlayerName\",\n  \"noorCoins\": 1000,\n  \"profileImagePath\": \"https://example.com/image.png\"\n}";
                    break;
                case "UPDATE_COINS":
                    jsonPayloadInput = "{\n  \"newBalance\": 550\n}";
                    break;
                case "CUSTOM":
                    jsonPayloadInput = "{\n  \"key\": \"value\"\n}";
                    break;
            }
        }

        private void SimulateMessage(string command, string dataJson)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[FlutterBridgeEditor] You must enter Play Mode to test the message bridge.");
                return;
            }

            if (FlutterBridge.Instance == null)
            {
                Debug.LogError("[FlutterBridgeEditor] FlutterBridge instance not found in scene. Make sure you attached it to a GameObject.");
                return;
            }

            // Construct the final JSON message as if it came from Flutter
            FlutterMessage msg = new FlutterMessage
            {
                command = command,
                data = dataJson // Note: in real usage, this is a stringified JSON.
            };

            string finalJson = JsonUtility.ToJson(msg);
            
            Debug.Log($"[FlutterBridgeEditor] Simulating incoming message: {finalJson}");
            
            // Call the bridge directly
            FlutterBridge.Instance.ReceiveMessageFromFlutter(finalJson);
        }
    }
}
