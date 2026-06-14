# Flutter Integration: Noor Coins (flutter_embed_unity)

This guide provides the exact Flutter code you need to communicate with the `Jannah-Garden` Unity project.

*(Note: The Unity side of this integration has already been completely configured for you.)*

## Overview
You need to establish two-way communication using the `FlutterEmbed` widget:
1. **Sending Coins to Unity:** Send the initial Firebase balance when Unity is ready.
2. **Receiving Coins from Unity:** Listen for spending/earning events to update Firebase.

---

## 1. Setting up the FlutterEmbed Widget

Initialize the `FlutterEmbed` widget in your screen and provide the `onMessageFromUnity` callback so you can listen for updates.

```dart
import 'package:flutter/material.dart';
import 'package:flutter_embed_unity/flutter_embed_unity.dart';

class JannahGardenScreen extends StatefulWidget {
  @override
  _JannahGardenScreenState createState() => _JannahGardenScreenState();
}

class _JannahGardenScreenState extends State<JannahGardenScreen> {
  // Example: Get this from Firebase or your local state
  int userNoorCoins = 500; 

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: FlutterEmbed(
        onMessageFromUnity: _onMessageFromUnity,
      ),
    );
  }
```

---

## 2. Sending Initial Coins to Unity

To send coins from Flutter to Unity, you use the top-level `sendToUnity` function provided by the plugin. 

**Important details for the Unity project:**
* **GameObject Name:** `"Noor Coin Manager"`
* **Method Name:** `"SetInitialCoinsFromFlutter"`

*You can trigger this using a button, or listen to an event that tells you Unity is ready, depending on your app's flow.*

```dart
  void sendInitialCoinsToUnity() {
    // Format: sendToUnity('GameObject_Name', 'Method_Name', 'Message_String')
    sendToUnity(
      'Noor Coin Manager', 
      'SetInitialCoinsFromFlutter', 
      userNoorCoins.toString(),
    );
  }
```

---

## 3. Accepting Coin Updates from Unity

When the user earns or spends coins inside Jannah Garden, Unity automatically sends a string message formatted as `CoinUpdate:X` (where X is the positive or negative amount changed) back to Flutter.

Catch this message in your `onMessageFromUnity` callback to update your local state and database (e.g., Firebase).

```dart
  void _onMessageFromUnity(String message) {
    print('Received message from Unity: $message');
    
    // Check if the message is a Noor Coin update
    if (message.startsWith('CoinUpdate:')) {
      // Extract the amount changed
      String amountString = message.replaceFirst('CoinUpdate:', '');
      int amountChange = int.tryParse(amountString) ?? 0;
      
      if (amountChange != 0) {
        // Update local state
        setState(() {
          userNoorCoins += amountChange;
        });
        
        // TODO: Update the user's new balance in Firebase/Backend
        _updateFirebaseCoins(userNoorCoins);
      }
    }
  }

  void _updateFirebaseCoins(int newBalance) {
    // Add your database update logic here
    print('Updating Firebase with new balance: $newBalance');
  }
}
```
