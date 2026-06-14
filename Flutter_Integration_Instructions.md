# Flutter & Unity Integration Instructions: Noor Coins

This guide explains how to connect your Flutter app's Noor Coin balance with the Unity `Jannah-Garden` project using the `flutter_unity_widget` plugin.

## 1. Required Plugin
Ensure you have the `flutter_unity_widget` package installed in your Flutter project's `pubspec.yaml`:
```yaml
dependencies:
  flutter_unity_widget: ^2022.2.0 # Check pub.dev for the latest compatible version
```
*Note: You can find the package and full setup instructions on [pub.dev/packages/flutter_unity_widget](https://pub.dev/packages/flutter_unity_widget).*

## 2. Unity Setup
You need to import the Unity package provided by `flutter_unity_widget` into this Unity project. Once imported, you must uncomment a line of code in the `NoorCoinManager.cs` script to allow Unity to send messages back to Flutter.

1. Open `Assets/Scripts/Economy/NoorCoinManager.cs`.
2. Locate the `SendCoinUpdateToFlutter` method.
3. Uncomment the `UnityMessageManager` line:
```csharp
    private void SendCoinUpdateToFlutter(int amountChange)
    {
        string message = $"CoinUpdate:{amountChange}";
        
        // UNCOMMENT THIS LINE once the package is imported:
        FlutterUnityIntegration.UnityMessageManager.Instance.SendMessageToFlutter(message);
        
        Debug.Log($"[NoorCoinManager] Sent to Flutter: {message}");
    }
```

## 3. Flutter Implementation
In your Flutter screen that displays the Unity Widget, you need to establish two-way communication.

### A. Set up the Widget
Initialize the `UnityWidgetController` and pass callbacks for creation and messaging:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_unity_widget/flutter_unity_widget.dart';

class JannahGardenScreen extends StatefulWidget {
  @override
  _JannahGardenScreenState createState() => _JannahGardenScreenState();
}

class _JannahGardenScreenState extends State<JannahGardenScreen> {
  UnityWidgetController? _unityWidgetController;
  
  // Example: Get this from Firebase or your local state
  int userNoorCoins = 500; 

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: UnityWidget(
        onUnityCreated: onUnityCreated,
        onUnityMessage: onUnityMessage,
      ),
    );
  }
```

### B. Send Initial Coins to Unity
When Unity is ready, send the current coin balance using `.postMessage()`. The GameObject name must be **"Noor Coin Manager"**.

```dart
  void onUnityCreated(UnityWidgetController controller) {
    _unityWidgetController = controller;
    
    // Format: postMessage(GameObject_Name, Method_Name, Message_String)
    _unityWidgetController?.postMessage(
      'Noor Coin Manager', 
      'SetInitialCoinsFromFlutter', 
      userNoorCoins.toString(),
    );
  }
```

### C. Listen for Updates from Unity
When the user earns or spends coins inside Jannah Garden, Unity sends a `CoinUpdate:X` message back to Flutter. Catch this message and update your database (e.g., Firebase).

```dart
  void onUnityMessage(message) {
    print('Received message from unity: ${message.toString()}');
    
    String msg = message.toString();
    
    if (msg.startsWith('CoinUpdate:')) {
      // Extract the amount changed
      String amountString = msg.replaceFirst('CoinUpdate:', '');
      int amountChange = int.tryParse(amountString) ?? 0;
      
      if (amountChange != 0) {
        // Update local state
        setState(() {
          userNoorCoins += amountChange;
        });
        
        // TODO: Update the user's balance in Firebase/Backend
        _updateFirebaseCoins(userNoorCoins);
      }
    }
  }

  void _updateFirebaseCoins(int newBalance) {
    // Add your database update logic here
  }
}
```

## Testing Locally in Unity
If you want to test the Unity project in the editor without running it through Flutter:
1. Select the `JannahGardenManager` GameObject in your scene.
2. In the Inspector, check the **Is Debug** box.
3. Set your desired **Debug Noor Coins Amount**.
4. Press Play. The game will bypass waiting for Flutter and immediately assign your debug coins.
