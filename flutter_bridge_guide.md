# Flutter ↔ Unity Bridge Guide

How the Flutter app talks to the Jannah Garden Unity game, and what you (the Flutter developer) need to
implement on your side.

Two things carry most of the traffic:

- **The garden state** (§5) — every asset the player has placed. Unity has no Firebase SDK, so this bridge
  is the only way a player's Jannah Garden survives a reinstall or a change of device.
- **The fellowship roster** (§4) — a list of other users, pushed from Flutter into Unity so the game can
  spawn their profile cards around the Outer Garden map.

---

## 🆕 Urgent: OOM crash ~4–5 minutes into a session, on both Android and iOS

**The embedded game has been getting killed by the OS a few minutes into a session, on both real Android
devices and iOS** — not a Unity exception, no error screen; the OS just terminates the process for memory.
Full writeup in **§10**.

**Android — one attribute needed on your side**, `android:largeHeap="true"` on the `<application>` tag in
`android/app/src/main/AndroidManifest.xml`:

```xml
<application
    android:label="Amal"
    android:name="${applicationName}"
    android:icon="@mipmap/launcher_icon"
    android:extractNativeLibs="true"
    android:largeHeap="true">   <!-- ← add this -->
```

**iOS — nothing needed on your side.** iOS has no equivalent flag; there's no way to ask the OS for more
memory headroom. The fix there is entirely a Unity-side change (item meshes were being cached forever, plus
now reacting to the OS's low-memory warning directly) that lands in the next `unityLibrary` export. See
**§10** for the full picture and how to confirm it's fixed on each platform.

The Android flag is unrelated to the existing `EnableImpeller=false` entry already in that file (that one's
for a Vulkan shadow-rendering crash) — don't touch that one, this is an independent addition right below it.

---

## 🆕 New requirement: language switching (`SET_LOCALE`)

**Unity now supports English, Arabic, Bengali, and Urdu — trivia questions and UI text — but nothing on the
Flutter side sends the locale yet.** Until this is implemented, the game always shows its last cached
language and never learns when the player changes language in the app. This is the one outstanding piece
of work needed to make in-app language switching actually reach the game. Full details in §3 under
**"Handling `SET_LOCALE`"**, Dart snippet in §6 — short version:

1. Add one method to `UnityBridge`:
   ```dart
   static void setLocale(String localeCode) {
     _send('SET_LOCALE', {'localeCode': localeCode});
   }
   ```
2. Call it in **two places**:
   - Right after handling `UNITY_READY` (send the app's current language).
   - Anywhere the player changes language in your app's settings, even while the Unity view is open.
3. Locale codes: `en`, `ar`, `bn`, `ur`.

Nothing else changes on your side — no new UI, no new state to store. See §3/§6 for the full writeup,
including current translation coverage and RTL notes.

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
| `UPDATE_AD_AVAILABILITY` | `{ rewardedReady }` — whether you have a rewarded ad loaded right now |
| `REWARDED_AD_RESULT` | `{ status, source }` — how the ad you were asked for ended |
| `UPDATE_GARDEN_STATE` | `{ hasData, savedAtUnix, revision, items: [...] }` — the player's garden, out of Firebase |
| `PHOTO_ACTION_RESULT` | `{ action, success, message }` — how a photo share or gallery save ended. Optional |
| `SET_LOCALE` 🆕 | `{ localeCode }` — the app's active language. **Not yet implemented on the Flutter side — see below.** |

### Unity → Flutter

| Command | Meaning | What you should do |
|---|---|---|
| `UNITY_READY` | The bridge is alive and listening. **Sent more than once** — see below. | Push the user profile, coins, and the fellowship roster. |
| `REQUEST_COIN_BALANCE` | The game started (or the player re-entered it) and needs the player's Noor Coin balance. Payload: `{}`. | Send `UPDATE_COINS` (or `UPDATE_USER_PROFILE`, which also carries `noorCoins`). |
| `REQUEST_FELLOWSHIP_PROFILES` | A scene needs the roster and has none cached. | Send `UPDATE_FELLOWSHIP_PROFILES`. |
| `REQUEST_SUBSCRIBE` | The player tapped **Subscribe** on the treasure box panel. Payload: `{ source }`. | **Leave the Unity game** (pop/dismiss the `UnityWidget`) and navigate to your app's subscribe page. |
| `REQUEST_EXIT_GAME` | The player confirmed **Exit** on the exit panel. Payload: `{ source }` (currently `"exit_panel"`). | **Leave the Unity game** (pop/dismiss the `UnityWidget`) and show your app's home screen. Do *not* close the app. |
| `REQUEST_REWARDED_AD` | The player asked for something that costs an ad. Payload: `{ source }` (`"treasure_box"`, `"shop_item"`). | Show a rewarded ad **over** the Unity view, then answer with `REWARDED_AD_RESULT`. |
| `REQUEST_GARDEN_STATE` | The garden loaded and needs the player's saved assets. Payload: `{}`. | Read the garden from Firestore and answer with `UPDATE_GARDEN_STATE`. Answer **even when there is nothing saved** — send `hasData: false`. |
| `SAVE_GARDEN_STATE` | The player placed, moved or returned something. Payload: the same garden shape. | Write it to Firestore under the signed-in user. |
| `REQUEST_SHARE_PHOTO` | **No longer sent on a phone** — see below. Payload: `{ filePath, caption, source, width, height }`. | Nothing. Implement only if you want to override the game's own share sheet. |
| `REQUEST_SAVE_PHOTO` | **No longer sent at all** — see below. Same payload. | Nothing. |

#### Photos are handled by the game now

**You do not need to implement anything for the Share and Save buttons.** The game talks to the phone
directly: the system share sheet on both platforms, MediaStore on Android and PHPhotoLibrary on iOS. It
also declares its own FileProvider and asks for its own permissions, all injected at build time — see
`Assets/Scripts/Native/NativePhotoService.cs`.

One thing is still yours: **iOS reads the usage description from the host app's `Info.plist`, not
Unity's.** Add `NSPhotoLibraryAddUsageDescription` to your Runner's `Info.plist` or iOS will terminate
the app the first time a player taps **Save**:

```xml
<key>NSPhotoLibraryAddUsageDescription</key>
<string>Jannah Garden saves the photos you take of your garden to your gallery.</string>
```

The two commands remain in the protocol as a fallback. `REQUEST_SHARE_PHOTO` is sent only when the game
is running somewhere with no share sheet of its own — the Unity Editor, or a desktop build — so on a
real device you will never see it. `REQUEST_SAVE_PHOTO` is never sent any more. If you handle them
anyway, `PHOTO_ACTION_RESULT` still works exactly as before.

Two things worth knowing if you do:

- **The image never travels through the bridge.** A full-screen PNG is several megabytes; base64 in a
  JSON envelope would stall both sides. `filePath` points inside `persistentDataPath` — app-private
  storage on both platforms, which the Flutter side of the same app can read directly. The file is
  already written and closed by the time the message arrives.
- **The game keeps only the last few photos.** Old ones are deleted as new ones are taken, so do not
  hold a path and expect it to still be there later — act on it while the message is fresh.

#### Handling `REQUEST_REWARDED_AD`

**The game has no ad SDK.** Unity used to ship its own copy of Google Mobile Ads; on iOS that made `dyld`
kill the app at launch (`_GADAdLoaderAdTypeNative` not found), because the Unity plugin referenced
native-ads symbols the final binary never linked. The plugin is gone, and it must stay gone — two GMA
instances in one process also means two initialisations and two UMP consent flows.

So ads are yours entirely. Unity only says *when* one is due, and waits:

```dart
case 'REQUEST_REWARDED_AD':
  final data = jsonDecode(envelope['data'] as String) as Map<String, dynamic>;
  final source = data['source'] as String? ?? 'game'; // "treasure_box" | "shop_item"

  final ad = _rewardedAd; // pre-loaded, see UPDATE_AD_AVAILABILITY below
  if (ad == null) {
    UnityBridge.sendRewardedAdResult(status: 'unavailable', source: source);
    break;
  }

  ad.show(onUserEarnedReward: (_, __) => _earned = true);
  // On dismiss / failure:
  //   sendRewardedAdResult(status: _earned ? 'rewarded' : 'dismissed', source: source)
  //   sendRewardedAdResult(status: 'failed', source: source)
  break;
```

Three things the game depends on:

- **Always answer.** `status` is one of `rewarded` | `dismissed` | `failed` | `unavailable`. The game is
  frozen at `timeScale = 0` from the moment it asks until your answer arrives — it will unfreeze itself
  after 120s as a last resort, but the player watches a dead screen until then. Answer on *every* exit
  path, including the error ones.
- **Do not send coins in this message.** `REWARDED_AD_RESULT` carries no amount, on purpose. If the reward
  is coins, credit the wallet server-side and push the new balance as `UPDATE_COINS` — one source of truth.
- **`dismissed` is not an error.** The player closed the ad early: no reward, no error message. The game
  resumes normally.

#### Handling `UPDATE_AD_AVAILABILITY`

Ads take a few seconds to load, so pre-load one and tell the game whether it has an ad to offer. Push
`{ "rewardedReady": true|false }` whenever that changes — after a load succeeds, and again after the ad is
consumed. The game defaults to `false` and, while it is false, plays its built-in placeholder timer panel
instead of asking you for an ad, so a stale `false` costs the player a real ad rather than breaking
anything.

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

#### 🆕 Handling `SET_LOCALE` (language switching) — REQUIRED, not yet implemented

This is the **only** command that drives the game's language — there is no separate settings screen inside
Unity, and no other way for the game to learn what language the player wants.

```dart
case 'onLanguageChanged': // wherever your app's language-change hook lives
  UnityBridge.setLocale(newLocaleCode); // 'en' | 'ar' | 'bn' | 'ur'
  break;
```

```dart
static void setLocale(String localeCode) {
  _send('SET_LOCALE', {'localeCode': localeCode});
}
```

Send it in exactly two places:

1. **Once, right after you see `UNITY_READY`** — alongside your `UPDATE_USER_PROFILE`/`UPDATE_COINS` reply.
   Unity remembers the last locale it was set to (via `PlayerPrefs`) and shows that on its very first frame,
   *before* Flutter's first message arrives — so if the player changed their language in your app's settings
   while the Unity view was closed, Unity is still showing the old one until you send this.
2. **Every time the player changes the language inside your app**, even while the Unity view is currently
   open/paused. The game reloads its UI strings and trivia questions live — no restart, no re-entering the
   garden required.

Locale codes: **`en`, `ar`, `bn`, `ur`** (case-insensitive; `"ar-SA"` / `"ar_SA"`-style regional codes are
also accepted — Unity trims at the `-`/`_`). Anything else, or anything sent before Unity's
`LocalizationManager` exists, is logged and silently ignored — the game keeps whatever language it already
had rather than crashing or blanking out.

Two things worth knowing about current content coverage (as of this writing):

- **Arabic and Urdu are fully translated** — all 500 trivia questions, every level.
- **Bengali is partially translated** (Levels 1–3 complete, Level 4 partial). The game automatically fills
  any untranslated question with its English text rather than leaving a level empty, so switching to `bn`
  is always safe — some questions will just read in English until translation catches up. No Flutter-side
  handling needed for this; it's entirely a Unity-side fallback.
- Arabic and Urdu are both right-to-left. Bengali and English are left-to-right. Unity handles this
  entirely on its own (mirrored layout, joined Arabic-script letterforms) — you only ever send the locale
  code, never a text-direction flag.

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

## 5. The garden payload (what keeps a player's garden across devices)

Everything the player places — trees, buildings, decorations, treasure box rewards — lives in the garden
state. Unity has no Firebase SDK, so **this is the only route the garden has off the device**. Without it
a player who reinstalls, or signs in on a new phone, starts from bare terrain.

Same shape in both directions: Unity sends it as `SAVE_GARDEN_STATE`, you send it back as
`UPDATE_GARDEN_STATE`.

```json
{
  "hasData": true,
  "savedAtUnix": 1754380800,
  "revision": 12,
  "items": [
    {
      "uniqueId": "3f2a…",
      "prefabName": "Date Palm Tree",
      "posX": 118.4, "posY": 3.02, "posZ": -44.9,
      "rotX": 0, "rotY": 0.7071, "rotZ": 0, "rotW": 0.7071,
      "remainingDuration": 214.5,
      "totalDuration": 360,
      "sourceItemId": "shop_date_palm",
      "sourceKind": 1
    }
  ]
}
```

### Field rules

| Field | Type | Rules |
|---|---|---|
| `hasData` | bool | **On the way back to Unity**, `false` means "this account has no garden stored yet" — Unity then keeps whatever is on the device and seeds Firestore from it. An account whose garden is genuinely empty must send `true` with `items: []`, otherwise deleting your last tree can never sync. |
| `savedAtUnix` | int | Unix **seconds**, UTC. Drives both how much growth to fast-forward and which copy wins. See the warning below. |
| `revision` | int | Save counter. Only used to break a tie when both copies report the same second. Store and return it unchanged. |
| `items` | array | May be empty. Never null. |
| `uniqueId` | string | Stable per placement. Moving an item keeps its id — treat the array as a full replacement, not a merge. |
| `prefabName` | string | The prefab Unity respawns. **Store and return it byte-for-byte**; an item whose name does not match a prefab in the build is dropped with a warning. |
| `posX/Y/Z`, `rotX/Y/Z/W` | number | World position and rotation quaternion. Flat floats on purpose, so they map onto a Firestore document with no converter. Must be JSON **numbers**, not quoted strings. |
| `remainingDuration` | number | Seconds of growth left at `savedAtUnix`. Unity ages it by the time since — do not adjust it yourself. |
| `totalDuration` | number | How long this item takes to fully grow. |
| `sourceItemId` | string | Which shop/inventory item it was bought from, so returning it refunds the right thing. May be `""`. |
| `sourceKind` | int | `0` unknown, `1` shop item, `2` inventory (treasure box) item. |

### ⚠️ `savedAtUnix` is a device clock

Unity stamps it from the phone it is running on, and the newer of the two copies wins. A phone with a
badly-set clock therefore wins every comparison, forever — including against a garden the player built on
another device.

**Overwrite it with a server timestamp on write, and return that value on read.** In Firestore:

```dart
await doc.set({
  ...garden,
  'savedAtUnix': FieldValue.serverTimestamp(),   // then read it back as epoch seconds
});
```

That one change makes the comparison authoritative rather than best-effort. Everything else works either
way.

### When Unity sends and asks

- **Asks once per cold start.** `REQUEST_GARDEN_STATE` goes out when the placement manager loads and has
  nothing cached. Answer it even when there is nothing stored (`hasData: false`) — a dropped answer means
  the player's other-device garden never arrives.
- **Sends a few seconds after any change.** Placing, moving or returning an item queues a
  `SAVE_GARDEN_STATE` about 3s later, so a player planting five things in a row costs one write, not five.
- **Sends immediately on pause and quit.** Leaving the garden flushes whatever is pending. This is the
  save that matters — treat it as the durable one.

Unity keeps its own local copy too, so the garden is on screen from the first frame; your snapshot arrives
a moment later and replaces it only if it is newer. Nothing is lost if Firestore is slow or unreachable.

---

## 6. Dart implementation

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

  // 🆕 REQUIRED for language switching — see §3, "Handling SET_LOCALE".
  static void setLocale(String localeCode) {
    _send('SET_LOCALE', {'localeCode': localeCode});
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
        UnityBridge.setLocale(currentAppLocaleCode); // 🆕 'en' | 'ar' | 'bn' | 'ur' — see §3, SET_LOCALE
        break;

      case 'REQUEST_COIN_BALANCE':
        // Without this the garden runs on 0 coins. Answer from cache first, then refresh.
        UnityBridge.sendCoins(user.coins);
        UnityBridge.sendCoins(await fetchCoinBalance());
        break;

      case 'REQUEST_FELLOWSHIP_PROFILES':
        UnityBridge.sendFellowshipProfiles(await fetchFellows());
        break;

      case 'REQUEST_GARDEN_STATE':
        // Without this the player's garden never follows them to a new device.
        UnityBridge.sendGardenState(await fetchGarden());
        break;

      case 'SAVE_GARDEN_STATE':
        // `data` is the garden, already a JSON string — store it as-is.
        await saveGarden(jsonDecode(envelope['data'] as String) as Map<String, dynamic>);
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

### Store and return the garden

The garden is opaque to your side — it is Unity's own scene data. Store the fields you were given and hand
them back unchanged; the only value worth rewriting is `savedAtUnix` (see §5).

```dart
// Unity → Firestore
Future<void> saveGarden(Map<String, dynamic> garden) async {
  await FirebaseFirestore.instance
      .collection('users').doc(uid)
      .collection('game').doc('jannah_garden')
      .set({
        ...garden,
        // Overwrite the device clock so the newest-copy-wins comparison is trustworthy.
        'savedAtUnix': DateTime.now().toUtc().millisecondsSinceEpoch ~/ 1000,
      });
}

// Firestore → Unity
Future<Map<String, dynamic>> fetchGarden() async {
  final snap = await FirebaseFirestore.instance
      .collection('users').doc(uid)
      .collection('game').doc('jannah_garden')
      .get();

  // No document yet: say so explicitly. Unity keeps the on-device garden and seeds Firestore from it.
  if (!snap.exists) return {'hasData': false, 'items': <dynamic>[], 'savedAtUnix': 0, 'revision': 0};

  return {...snap.data()!, 'hasData': true};
}
```

Add `sendGardenState` alongside the other senders in `UnityBridge`:

```dart
static const String cmdUpdateGardenState = 'UPDATE_GARDEN_STATE';

static void sendGardenState(Map<String, dynamic> garden) {
  _send(cmdUpdateGardenState, garden);
}
```

> **Note on the legacy coin message.** Unity's `NoorCoinManager` currently sends the *plain string*
> `CoinUpdate:{amount}` back to Flutter when the player earns or spends, rather than the JSON envelope.
> Handle both shapes in `onMessageFromUnity`: if the message does not start with `{`, treat it as the
> legacy `CoinUpdate:` format.

---

## 7. When to send what

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

The garden works like coins — **Unity asks, then keeps you updated.** Answer `REQUEST_GARDEN_STATE` once
per session (including with `hasData: false`), and write every `SAVE_GARDEN_STATE` you receive. You never
need to push `UPDATE_GARDEN_STATE` unprompted; if you do — say the player's garden changed elsewhere —
Unity adopts it only when its `savedAtUnix` is newer than what the device holds, and never while an item is
mid-placement.

---

## 8. Testing without a phone

Unity has a built-in simulator: **Window → Flutter Bridge Manager**. In Play Mode, pick
`UPDATE_FELLOWSHIP_PROFILES`, click *Load Default Template for Selection*, edit the JSON, and hit *Simulate
Receive From Flutter*. It runs the exact same code path as a real message from Flutter.

Use it to confirm the payload shape before wiring up the Flutter side.

---

## 9. Troubleshooting

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
| Garden is empty on a new device | `REQUEST_GARDEN_STATE` was never answered, or was answered with `hasData: false` when a garden did exist. |
| Some placed items are missing after a restore | `Failed to find placeable prefab named: X` in the log — `prefabName` was altered in storage, or the item's prefab is not reachable from the loaded scene. |
| Firebase keeps losing the newest garden | Both copies are being compared on device clocks. Stamp `savedAtUnix` server-side on write and return that value on read (§5). |
| The garden reverts to an old state | You answered `REQUEST_GARDEN_STATE` with a stale cached document whose `savedAtUnix` was newer than the device's. Read through to Firestore. |
| The app dies when the player taps **Save** on iOS | `NSPhotoLibraryAddUsageDescription` is missing from the host app's `Info.plist`. iOS terminates rather than refuses. |
| Sharing a photo does nothing | The game handles this itself now; look for `[NativePhoto]` in the device log rather than for a bridge message. |
| The shared photo file is missing | You held the path and used it later. The game keeps only the most recent photos and deletes the rest as new ones are taken. |
| The shared photo has the HUD in it | Not possible from the game's side — every canvas is switched off for the captured frame. Check you are not re-capturing the screen yourself in Flutter. |
| 🆕 Game shows the wrong language on first open | `SET_LOCALE` was never sent after `UNITY_READY`. Unity shows its last cached language (from `PlayerPrefs`) until you send one. |
| 🆕 Language doesn't change while the garden is open | Confirm you call `UnityBridge.setLocale(...)` from your app's language-change handler, not only at startup — Unity applies it live, no restart needed. |
| 🆕 Some trivia questions show English while others are in the new language | Expected for Bengali right now — translation is partial (Levels 1–3 done, Level 4 partial); untranslated questions fall back to English on purpose rather than being missing. |
| 🆕 Game (device only, not Editor) crashes with no warning ~4–5 minutes into a session | Not a Unity exception — the OS is killing the process for memory. See §10. |

---

## 10. 🆕 Android OOM crash after a few minutes of play

### What was happening

Every shop/treasure-box item is a Remote Addressable — its 3D prefab downloads the first time a player
previews or places it. `AddressableItemLoader` on the Unity side cached each one **for the rest of the
process's life** and never freed it — deliberate at the time, since nothing evicted it.

That stopped being safe once the game's assets got as heavy as they are now: the Meshy AI item meshes run
roughly 30 MB per bundle apiece even after compression. Browsing, buying, or returning even a handful of
different items in one sitting can pull several hundred MB of mesh/texture data into memory that's never
released — on top of the scene itself, **and on top of the Flutter/Impeller engine running in the same
process** (this isn't a standalone Unity app; `flutter_embed_unity` runs both engines in one process, so
they're competing for the same memory ceiling). On a mid-range Android device that combination can exceed
the OS's per-app memory allowance around the 4–5 minute mark, and Android silently kills the process — which
shows up to the player as "the game just closes," not as any kind of error screen or Unity log.

### The fix has two parts, both needed

**1. Unity side (done, not yours to touch).** `AddressableItemLoader` now keeps a bounded cache — the least-
recently-used item prefab gets released once too many unused ones pile up, while anything actually standing
in the player's garden (or mid-placement) is protected from eviction. This ships automatically the next time
`android/unityLibrary` gets re-exported and dropped into this repo — nothing for you to do here beyond
picking up that new export when it's ready.

**2. Flutter side (yours) — `android:largeHeap="true"`.** Add it to the `<application>` tag in
`android/app/src/main/AndroidManifest.xml` (shown in the callout above). This raises Android's declared
memory class for the process, giving the combined Flutter+Unity heap more headroom before the OS decides
to kill it.

**Why both:** `largeHeap` affects the Java/ART heap and the OS's overall memory bookkeeping for the process;
it does **not** touch Unity's native mesh/texture memory, which is where the actual growth was happening.
Ship it without the Unity-side fix and you've only bought a bit more runway before the same crash returns
with a slightly heavier session. Ship the Unity fix without `largeHeap` and it's still a meaningful
improvement, but you're leaving free headroom on the table for no cost — it's a one-line, no-downside change.

### How to verify, once both sides have landed

Install a release build on a device, play past the 5-minute mark (browse/place/return several different
shop items rather than sitting idle — that's the pattern that was triggering it), and watch for a kill
signal while it's running:

```bash
adb logcat -c && adb logcat | grep -iE "lowmemorykiller|OutOfMemory|Unity.*Fatal"
```

A `lowmemorykiller` (or similar) line naming this app's process confirms it was memory pressure. No output
and the app still standing at 10+ minutes is the fix working.

### 🆕 It also happens on iOS — and `largeHeap` does nothing there

Confirmed the same crash occurs on iOS builds too. That changes the priority of the two fixes:

- **`android:largeHeap` is Android-only.** There is no iOS equivalent — iOS gives every app a hard,
  device-dependent memory ceiling (roughly 1–3 GB depending on the model, less on older/cheaper phones)
  and there is no manifest flag, entitlement, or Info.plist key that raises it. So on iOS, the manifest
  change above buys nothing; **the Unity-side fix is the entire fix.**
- Also just added on the Unity side: the game now listens for the OS's low-memory warning (iOS
  `didReceiveMemoryWarning`, Android `onTrimMemory` — Unity surfaces both as one `Application.lowMemory`
  event) and immediately drops every downloaded item prefab it isn't actively using, rather than waiting
  for the next placement/return. Nothing for you to wire up — it's automatic once you're on the updated
  `unityLibrary` export.
- **Nothing is needed in `ios/Runner/Info.plist` for this.** No permission, no key. If you want to confirm
  it's actually a memory kill (rather than a genuine crash) on an iOS device, look for a **Jetsam** /
  `EXC_RESOURCE (RESOURCE_TYPE_MEMORY)` entry in the crash log — either from Xcode's **Window → Organizer →
  Crashes** for a TestFlight build, or on the device itself under **Settings → Privacy & Security →
  Analytics & Improvements → Analytics Data** (look for a report named after this app/`Runner`).
- Worth testing on an **older or lower-RAM device** (e.g. an iPhone SE-class phone) if you have one —
  iOS's ceiling is stricter there and has no override, so it'll hit this fastest and is the most convincing
  proof the fix actually holds.
