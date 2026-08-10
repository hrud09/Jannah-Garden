using UnityEngine;
using UnityEditor;
using System;

namespace FlutterIntegration.Editor
{
    public class FlutterBridgeEditorWindow : EditorWindow
    {
        private int selectedCommandIndex = 0;
        private string[] availableCommands = new string[]
        {
            FlutterCommands.UpdateUserProfile,
            FlutterCommands.UpdateCoins,
            FlutterCommands.UpdateFellowshipProfiles,
            FlutterCommands.UpdateGardenState,
            FlutterCommands.PhotoActionResult,
            "CUSTOM"
        };
        
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
            
            // We minify the data payload to put inside the wrapper, exactly as it happens in real scenarios.
            // Only whitespace *between* tokens is collapsed — stripping spaces outright would corrupt
            // string values like "Ahmad Rahman".
            string stringifiedData = "{}";
            if (!string.IsNullOrEmpty(jsonPayloadInput))
            {
                stringifiedData = jsonPayloadInput
                    .Replace("\r", "")
                    .Replace("\n", "")
                    .Replace("\t", "");
            }
            
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
                case FlutterCommands.UpdateUserProfile:
                    jsonPayloadInput = "{\n  \"userName\": \"PlayerName\",\n  \"noorCoins\": 1000,\n  \"profileImagePath\": \"https://example.com/image.png\"\n}";
                    break;
                case FlutterCommands.UpdateCoins:
                    jsonPayloadInput = "{\n  \"newBalance\": 550\n}";
                    break;
                case FlutterCommands.UpdateFellowshipProfiles:
                    jsonPayloadInput =
                        "{\n" +
                        "  \"fellows\": [\n" +
                        "    {\n" +
                        "      \"userId\": \"u_1001\",\n" +
                        "      \"userName\": \"Ahmad Rahman\",\n" +
                        "      \"memberSince\": \"2024-03-15\",\n" +
                        "      \"noorCoins\": 1250,\n" +
                        "      \"profileImagePath\": \"https://example.com/avatars/ahmad.png\"\n" +
                        "    },\n" +
                        "    {\n" +
                        "      \"userId\": \"u_1002\",\n" +
                        "      \"userName\": \"Fatima Zahra\",\n" +
                        "      \"memberSince\": \"2023-11-02\",\n" +
                        "      \"noorCoins\": 4820,\n" +
                        "      \"profileImagePath\": \"\"\n" +
                        "    }\n" +
                        "  ]\n" +
                        "}";
                    break;
                case FlutterCommands.UpdateGardenState:
                    // savedAtUnix is intentionally 0 here: the placement manager only adopts a snapshot
                    // that is newer than the device's, so replace it with a current Unix timestamp to
                    // watch the garden actually rebuild.
                    jsonPayloadInput =
                        "{\n" +
                        "  \"hasData\": true,\n" +
                        "  \"savedAtUnix\": 0,\n" +
                        "  \"revision\": 1,\n" +
                        "  \"items\": [\n" +
                        "    {\n" +
                        "      \"uniqueId\": \"11111111-1111-1111-1111-111111111111\",\n" +
                        "      \"prefabName\": \"Date Palm Tree\",\n" +
                        "      \"posX\": 0, \"posY\": 0, \"posZ\": 0,\n" +
                        "      \"rotX\": 0, \"rotY\": 0, \"rotZ\": 0, \"rotW\": 1,\n" +
                        "      \"remainingDuration\": 0,\n" +
                        "      \"totalDuration\": 360,\n" +
                        "      \"sourceItemId\": \"\",\n" +
                        "      \"sourceKind\": 1\n" +
                        "    }\n" +
                        "  ]\n" +
                        "}";
                    break;
                case FlutterCommands.PhotoActionResult:
                    jsonPayloadInput =
                        "{\n" +
                        "  \"action\": \"save\",\n" +
                        "  \"success\": true,\n" +
                        "  \"message\": \"\"\n" +
                        "}";
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
