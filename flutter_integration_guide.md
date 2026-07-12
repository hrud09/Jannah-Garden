# Flutter to Unity Integration Guide

This document explains how to set up your Flutter application to communicate with the Unity Game (Jannah Garden) using the structured JSON bridge.

## 1. Overview
The Unity project now expects messages in a specific JSON format. We avoid sending raw unstructured strings. 

Every message you send from Flutter to Unity (and vice versa) should follow this structure:
```json
{
  "command": "YOUR_COMMAND_NAME",
  "data": "{\"inner_key\":\"inner_value\"}" 
}
```
*Note: The `data` field is itself a stringified JSON object of the actual payload.*

## 2. Sending Data from Flutter to Unity

Use the `UnityWidgetController` to send messages to the `FlutterBridge` GameObject.

### Dart Setup (Flutter)
Create matching models in your Dart code to easily encode your data.

```dart
import 'dart:convert';
import 'package:flutter_embed_unity/flutter_embed_unity.dart'; // Adjust import based on your widget package

class FlutterMessage {
  final String command;
  final String data;

  FlutterMessage({required this.command, required this.data});

  Map<String, dynamic> toJson() => {
    'command': command,
    'data': data,
  };
}

class UserProfilePayload {
  final String userName;
  final int noorCoins;
  final String profileImagePath;

  UserProfilePayload({
    required this.userName,
    required this.noorCoins,
    required this.profileImagePath,
  });

  Map<String, dynamic> toJson() => {
    'userName': userName,
    'noorCoins': noorCoins,
    'profileImagePath': profileImagePath,
  };
}
```

### Sending the Message

When you want to update the user's profile inside the game:

```dart
void sendProfileToUnity() {
  // 1. Create the specific payload data
  final profileData = UserProfilePayload(
    userName: "Mahad",
    noorCoins: 1500,
    profileImagePath: "/path/to/local/cached/image.jpg", // OR a secure HTTPS URL
  );

  // 2. Stringify the inner payload
  final payloadJsonStr = jsonEncode(profileData.toJson());

  // 3. Create the wrapper message
  final message = FlutterMessage(
    command: "UPDATE_USER_PROFILE",
    data: payloadJsonStr,
  );

  // 4. Stringify the outer wrapper
  final finalJsonStr = jsonEncode(message.toJson());

  // 5. Send to Unity!
  // Send the message to the GameObject named "FlutterBridge" and call the "ReceiveMessageFromFlutter" method
  sendToUnity(
    "FlutterBridge", 
    "ReceiveMessageFromFlutter", 
    finalJsonStr
  );
}
```

## 3. Receiving Data from Unity in Flutter

When Unity needs to tell Flutter that something happened (e.g., the user spent coins in the game shop), it will send a message back using the same JSON structure.

You can listen for this using the `onMessage` callback on your `UnityWidget`.

```dart
UnityWidget(
  onUnityMessage: (message) {
    print("Received from Unity: $message");
    
    try {
      final decodedMessage = jsonDecode(message);
      final command = decodedMessage['command'];
      final dataStr = decodedMessage['data'];
      
      switch (command) {
        case "COIN_SPEND_REQUEST":
          final data = jsonDecode(dataStr);
          final int amountToSpend = data['amount'];
          // 1. Validate with your backend
          // 2. Deduct coins in Flutter state
          // 3. Send UPDATE_COINS command back to Unity
          break;
          
        default:
          print("Unknown command from Unity: $command");
      }
    } catch (e) {
      print("Failed to parse message from Unity: $e");
    }
  },
);
```

## 4. Best Practices for Images

**Do NOT send images as Base64 strings.** This will cause massive memory spikes and frame drops in Unity.

**Recommended Approaches:**
1. **Send the URL:** Send the URL in the `UserProfilePayload`. The Unity game will use `UnityWebRequestTexture` to download it asynchronously.
2. **Local Cache:** Download the image in Flutter. Save it to the device's Documents directory. Send the absolute file path to Unity. Unity will read the file directly using `File.ReadAllBytes(path)`.

## 5. Security & State

- **Flutter is the Source of Truth:** Unity should only visually represent data. The actual "saving" and validation of Noor Coins should happen on the Flutter side and your database.
- Unity tells Flutter "I want to spend 50 coins". Flutter checks if the user has 50 coins, deducts them, and tells Unity "Your new balance is 100".
