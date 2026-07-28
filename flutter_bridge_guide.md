# Flutter ↔ Unity Bridge Guide

How the Flutter app talks to the Jannah Garden Unity game, and what you (the Flutter developer) need to
implement on your side.

The current goal: **push the fellowship roster** — a list of other users — from Flutter into Unity, so the
game can spawn their profile cards around the Outer Garden map.

---

## 1. The contract in one picture

```
Flutter                                             Unity
───────                                             ─────
sendToUnity(                                        FlutterBridge.ReceiveMessageFromFlutter(json)
  "Flutter Bridge",          ── json string ──▶       └─ parses FlutterMessage
  "ReceiveMessageFromFlutter",                        └─ switches on `command`
  jsonString)                                         └─ caches + fires an event

onUnityMessage(json)         ◀── json string ──     SendToFlutter.Send(json)
```

Three things are **hardcoded on the Unity side and must match exactly**:

| Thing | Value |
|---|---|
| Target GameObject name | `Flutter Bridge` (with the space) |
| Target method name | `ReceiveMessageFromFlutter` |
| Message format | A JSON `{command, data}` envelope — see below |

The Unity bridge creates itself before the first scene loads and survives scene changes, so it is reachable
in **every** scene (Jannah Garden, Outer Garden, …). You do not need to know which scene the player is in.

---

## 2. The message envelope

Every message in **both** directions is this two-field envelope:

```json
{
  "command": "UPDATE_FELLOWSHIP_PROFILES",
  "data": "{\"fellows\":[...]}"
}
```

⚠️ **`data` is a JSON *string*, not a nested JSON object.** This is the single most common mistake. Unity's
`JsonUtility` cannot deserialize an arbitrary nested object, so the payload must be encoded (stringified)
and placed in `data` as text. In Dart:

```dart
final envelope = jsonEncode({
  'command': 'UPDATE_FELLOWSHIP_PROFILES',
  'data': jsonEncode(payload),   // ← note the SECOND jsonEncode
});
```

Getting this wrong fails silently — Unity logs `Error parsing Flutter message` or ends up with empty fields.

---

## 3. Commands

### Flutter → Unity

| Command | Payload (the object you stringify into `data`) |
|---|---|
| `UPDATE_USER_PROFILE` | `{ userName, noorCoins, profileImagePath }` — the *local* player |
| `UPDATE_COINS` | `{ newBalance }` — authoritative coin balance |
| `UPDATE_FELLOWSHIP_PROFILES` | `{ fellows: [...] }` — the roster of *other* users |

### Unity → Flutter

| Command | Meaning | What you should do |
|---|---|---|
| `UNITY_READY` | The bridge is alive and listening. **Sent more than once** — see below. | Push the user profile, coins, and the fellowship roster. |
| `REQUEST_COIN_BALANCE` | The game started (or the player re-entered it) and needs the player's Noor Coin balance. Payload: `{}`. | Send `UPDATE_COINS` (or `UPDATE_USER_PROFILE`, which also carries `noorCoins`). |
| `REQUEST_FELLOWSHIP_PROFILES` | A scene needs the roster and has none cached. | Send `UPDATE_FELLOWSHIP_PROFILES`. |
| `REQUEST_SUBSCRIBE` | The player tapped **Subscribe** on the treasure box panel. Payload: `{ source }`. | **Leave the Unity game** (pop/dismiss the `UnityWidget`) and navigate to your app's subscribe page. |
| `REQUEST_EXIT_GAME` | The player confirmed **Exit** on the exit panel. Payload: `{ source }` (currently `"exit_panel"`). | **Leave the Unity game** (pop/dismiss the `UnityWidget`) and show your app's home screen. Do *not* close the app. |

#### Handling `REQUEST_COIN_BALANCE` (and repeated `UNITY_READY`)

**This is what makes the coin balance show up in the game.** Unity has no wallet of its own — until Flutter
sends a balance, the garden runs on 0 coins, so nothing in the shop is affordable.

On every game start the bridge announces `UNITY_READY` *and* `REQUEST_COIN_BALANCE` together, then repeats
the pair every 1.5s — up to 8 times, ~12s — until a balance arrives. It retries because a single
announcement is easy to miss: Unity's first frame can run before your `onMessageFromUnity` handler is
attached, and a dropped message used to leave the player on 0 coins for the whole session.

Two consequences for your side:

- **Both handlers must be idempotent.** You will receive `UNITY_READY` several times in a row on a slow
  start, and again on every resume. Answering each one with the current balance is correct and cheap;
  just don't do anything with side effects (analytics events, wallet writes) in that handler.
- **Answer as soon as you can, even from cache.** The retries stop the moment Unity sees a balance, so a
  fast local answer followed by the fresh Firebase value is better than waiting for the network.

```dart
case 'REQUEST_COIN_BALANCE':
  // Answer immediately from whatever you already have...
  UnityBridge.sendCoins(user.coins);
  // ...then push the authoritative value when Firebase responds.
  UnityBridge.sendCoins(await fetchCoinBalance());
  break;
```

"Game start" also means **re-entry**. With `flutter_embed_unity` the Unity player is paused, not destroyed,
when the widget goes away, so Unity's `Start` never runs twice. The bridge instead re-handshakes on resume
— which is exactly what you want, since the player may have spent or earned coins in the app while the
garden was in the background.

#### Handling `REQUEST_EXIT_GAME`

This is the exit button inside the garden. Unity used to call `Application.Quit()` here, which killed the
whole host process and closed the Flutter app with it — so it no longer does. Unity cannot dismiss its own
widget, so exiting is entirely your side's job:

```dart
case 'REQUEST_EXIT_GAME':
  // Pop the route that hosts the EmbedUnity widget — back to the app, app stays running.
  Navigator.of(context).popUntil((r) => r.isFirst);
  break;
```

If the game is *not* on a pushed route (e.g. it is a tab or the body of your home page), switch away from
it instead of popping — hide/replace the widget so the player lands somewhere in the app.

Unity keeps running in the background once the widget is gone; that is expected with
`flutter_embed_unity` (single Unity instance, paused rather than torn down). The pause is also what makes
the game save its state, so nothing is lost when the player re-enters the garden.

#### Handling `REQUEST_SUBSCRIBE`

Unity cannot pop its own `UnityWidget` — only Flutter can. When you receive this command, dismiss the
Unity screen and route to the subscription page:

```dart
case 'REQUEST_SUBSCRIBE':
  final data = jsonDecode(envelope['data'] as String) as Map<String, dynamic>;
  final source = data['source'] as String?; // e.g. "treasure_box"
  // Close the Unity view and go to your subscribe screen.
  Navigator.of(context).popUntil((r) => r.isFirst); // or however you exit Unity
  Navigator.of(context).pushNamed('/subscribe', arguments: source);
  break;
```

The `source` field (currently `"treasure_box"`) tells you what prompted the request, in case you want to
tailor the subscribe screen or log analytics.

---

## 4. The fellowship payload (the important one)

`data` for `UPDATE_FELLOWSHIP_PROFILES`, before stringifying:

```json
{
  "fellows": [
    {
      "userId": "u_1001",
      "userName": "Ahmad Rahman",
      "memberSince": "2024-03-15",
      "noorCoins": 1250,
      "profileImagePath": "https://cdn.example.com/avatars/ahmad.png"
    },
    {
      "userId": "u_1002",
      "userName": "Fatima Zahra",
      "memberSince": "2023-11-02",
      "noorCoins": 4820,
      "profileImagePath": ""
    }
  ]
}
```

### Field rules

| Field | Type | Rules |
|---|---|---|
| `userId` | string | Stable unique id. Not displayed. |
| `userName` | string | Displayed on the card. Keep it short — long names overflow the card. |
| `memberSince` | string | **ISO-8601 date: `YYYY-MM-DD`.** Rendered as "Member since Mar 2024". Anything else falls back to "Member since —". |
| `noorCoins` | int | Must be a JSON **number**, not a quoted string. `1250` ✅ · `"1250"` ❌ |
| `profileImagePath` | string | An `http(s)` URL to an image, or `""`. Unity downloads and caches it. Never send Base64. |

### Constraints that matter

- **The wrapper key must be `fellows`.** A bare top-level array (`[{...}, {...}]`) will **not** parse —
  Unity's `JsonUtility` cannot read a top-level array. It must be an object with a `fellows` key.
- **Field names are case-sensitive** and must match exactly. An unknown or misspelled field is silently
  dropped and arrives as `0` / `null` — no error.
- **Omit nothing.** A missing field deserializes to a default (empty string / `0`), not an error.
- **Send as many fellows as you like.** Unity shows at most `maxProfiles` (default 8) of them, limited also
  by the number of spawn points placed in the scene, and picks the subset randomly.
- Avatars should be **small** (≈128–256 px). Every card downloads its own; they are cached by URL, so
  reusing a URL across fellows costs one download.

---

## 5. Dart implementation

### Send the roster

```dart
import 'dart:convert';
import 'package:flutter_embed_unity/flutter_embed_unity.dart';

class UnityBridge {
  static const _gameObject = 'Flutter Bridge';
  static const _method = 'ReceiveMessageFromFlutter';

  static void _send(String command, Map<String, dynamic> payload) {
    final envelope = jsonEncode({
      'command': command,
      'data': jsonEncode(payload), // data must be a STRING
    });
    sendToUnity(_gameObject, _method, envelope);
  }

  static void sendFellowshipProfiles(List<Fellow> fellows) {
    _send('UPDATE_FELLOWSHIP_PROFILES', {
      'fellows': fellows.map((f) => f.toJson()).toList(),
    });
  }

  static void sendUserProfile(String userName, int noorCoins, String avatarUrl) {
    _send('UPDATE_USER_PROFILE', {
      'userName': userName,
      'noorCoins': noorCoins,
      'profileImagePath': avatarUrl,
    });
  }

  static void sendCoins(int newBalance) {
    _send('UPDATE_COINS', {'newBalance': newBalance});
  }
}

class Fellow {
  final String userId;
  final String userName;
  final DateTime memberSince;
  final int noorCoins;
  final String profileImagePath;

  Fellow({
    required this.userId,
    required this.userName,
    required this.memberSince,
    required this.noorCoins,
    required this.profileImagePath,
  });

  Map<String, dynamic> toJson() => {
        'userId': userId,
        'userName': userName,
        // YYYY-MM-DD — Unity parses this with an invariant culture.
        'memberSince': memberSince.toIso8601String().split('T').first,
        'noorCoins': noorCoins,
        'profileImagePath': profileImagePath,
      };
}
```

### Receive Unity's requests

```dart
EmbedUnity(
  onMessageFromUnity: (String message) {
    final envelope = jsonDecode(message) as Map<String, dynamic>;
    final command = envelope['command'] as String?;

    switch (command) {
      case 'UNITY_READY':
        // The bridge is up. Push everything the game needs.
        // Arrives repeatedly until Unity has a coin balance — keep this handler side-effect free.
        UnityBridge.sendUserProfile(user.name, user.coins, user.avatarUrl);
        UnityBridge.sendFellowshipProfiles(await fetchFellows());
        break;

      case 'REQUEST_COIN_BALANCE':
        // Without this the garden runs on 0 coins. Answer from cache first, then refresh.
        UnityBridge.sendCoins(user.coins);
        UnityBridge.sendCoins(await fetchCoinBalance());
        break;

      case 'REQUEST_FELLOWSHIP_PROFILES':
        UnityBridge.sendFellowshipProfiles(await fetchFellows());
        break;

      case 'REQUEST_EXIT_GAME':
        // The player tapped Exit in the garden. Close the Unity screen only —
        // never exit the app; Unity no longer calls Application.Quit().
        Navigator.of(context).popUntil((r) => r.isFirst);
        break;
    }
  },
)
```

> **Note on the legacy coin message.** Unity's `NoorCoinManager` currently sends the *plain string*
> `CoinUpdate:{amount}` back to Flutter when the player earns or spends, rather than the JSON envelope.
> Handle both shapes in `onMessageFromUnity`: if the message does not start with `{`, treat it as the
> legacy `CoinUpdate:` format.

---

## 6. When to send what

The bridge is alive before any scene loads, but **the Outer Garden scene — where the profile cards appear —
usually loads much later**, after the player walks there from Jannah Garden.

You do not need to time this. Unity **caches** the last roster it received, and the Outer Garden reads that
cache when it loads. So:

- **Send the roster once, as soon as you get `UNITY_READY`.** That is enough.
- Send it again whenever the roster changes (a new fellow joins, coins change). If the Outer Garden is
  loaded at that moment, the cards refresh live; if not, the new roster waits in the cache.
- If Unity ever sends `REQUEST_FELLOWSHIP_PROFILES`, it means the scene loaded with an empty cache — just
  answer it with the roster.

Coins work the other way round: **Unity asks, and keeps asking.** Send `UPDATE_COINS` in reply to
`UNITY_READY` / `REQUEST_COIN_BALANCE`, and again any time the balance changes on your side (a purchase, a
reward, a `CoinUpdate:` message the game sent you). Unity caches the last balance it received, so a manager
that loads in a later scene still gets it.

If Unity gets no roster within 3 seconds of asking, it falls back to placeholder dummy profiles (this is a
development convenience and can be turned off for release).

---

## 7. Testing without a phone

Unity has a built-in simulator: **Window → Flutter Bridge Manager**. In Play Mode, pick
`UPDATE_FELLOWSHIP_PROFILES`, click *Load Default Template for Selection*, edit the JSON, and hit *Simulate
Receive From Flutter*. It runs the exact same code path as a real message from Flutter.

Use it to confirm the payload shape before wiring up the Flutter side.

---

## 8. Troubleshooting

| Symptom | Cause |
|---|---|
| `Error parsing Flutter message` | `data` was sent as a nested object instead of a stringified JSON string. |
| Cards show blank names / `0` coins | Field names misspelled or wrong case; `noorCoins` sent as `"1250"` instead of `1250`. |
| `Fellowship payload had no 'fellows' array` | You sent a top-level array instead of `{"fellows": [...]}`. |
| `Unhandled command: X` | The command string does not match one in the table above (case-sensitive). |
| Nothing happens at all | The GameObject name or method name is wrong. Must be `Flutter Bridge` / `ReceiveMessageFromFlutter`. |
| Player has 0 Noor Coins in the game | Flutter never answered `UNITY_READY` / `REQUEST_COIN_BALANCE` with `UPDATE_COINS` or `UPDATE_USER_PROFILE`. Unity logs `Flutter sent no Noor Coin balance after 8 attempts` after ~12s of asking. |
| Coins are stale after returning to the garden | Your `UNITY_READY` / `REQUEST_COIN_BALANCE` handler replies from a cached user object that was not refreshed. Unity re-asks on every resume; answer with the current balance. |
| All cards show "Member since —" | `memberSince` is not `YYYY-MM-DD`. |
| Avatars never appear | `profileImagePath` is not a reachable `http(s)` URL, or the device has no network. Unity logs the failure and keeps the default avatar. |
