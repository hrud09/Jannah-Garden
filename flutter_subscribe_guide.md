# Flutter Guide: Handling `REQUEST_SUBSCRIBE` from Unity

How to make the **Subscribe** button on the treasure box panel exit the Jannah Garden game and open
your app's subscription page.

This is a companion to [`flutter_bridge_guide.md`](flutter_bridge_guide.md) and is tailored to the
existing `unity_game_screen.dart` + `unity_bridge.dart` on the Flutter side.

---

## 1. What Unity now sends

When the player taps **Subscribe** on the treasure box panel, the Unity game fires this through the same
`Flutter Bridge` → `ReceiveMessageFromFlutter` channel you already listen to. The message that arrives in
`_onMessageFromUnity(String message)` is a standard envelope:

```json
{
  "command": "REQUEST_SUBSCRIBE",
  "data": "{\"source\":\"treasure_box\"}"
}
```

- `command` is `"REQUEST_SUBSCRIBE"` (exact, case-sensitive).
- `data` is a **stringified JSON** (like every other payload) — `jsonDecode` it a second time to read
  `source`. Today `source` is always `"treasure_box"`, but treat it as a variable so future call-sites
  (shop, daily offer, etc.) work automatically.

**Unity's only job is to signal.** It cannot pop its own `EmbedUnity` widget — only Flutter can exit the
game and navigate. So everything below happens on the Flutter side.

---

## 2. Change 1 — add the command to the switch

In `unity_game_screen.dart`, inside `_onMessageFromUnity`, add one case to `switch (command)` (next to the
existing `EXIT` case, since it's conceptually "exit + go somewhere"):

```dart
switch (command) {
  case 'UNITY_READY':
    await _pushEverythingToUnity();
    break;

  case 'REQUEST_FELLOWSHIP_PROFILES':
    await _sendFellowshipRoster();
    break;

  // ⬇⬇ নতুন: treasure box প্যানেলে Subscribe চাপলে গেম থেকে বেরিয়ে subscribe পেজে যাই
  case 'REQUEST_SUBSCRIBE':
    await _handleSubscribeRequest(env); // env = পুরো decoded envelope
    break;

  case 'EXIT':
  case 'exit':
    if (mounted) Navigator.of(context).pop();
    break;

  default:
    debugPrint('[Unity] Unhandled command: $command');
}
```

> ⚠️ Small refactor needed: currently `final env = jsonDecode(...)` lives inside the try block and only
> `command` is kept. To pass `env` into the handler, hoist `env` so it's visible in the switch. See the
> full updated method in §5.

---

## 3. Change 2 — the handler method

Add this to `_UnityGameScreenState` (next to `_handleLegacyMessage`):

```dart
/// Unity-র treasure box প্যানেলে "Subscribe" চাপলে আসে (REQUEST_SUBSCRIBE)।
/// payload: data (stringified) = { "source": "treasure_box" }
///
/// কাজ দুটো: (১) গেম থেকে বেরোনো — EmbedUnity widget pop করা, এবং
/// (২) অ্যাপের subscribe পেজে নিয়ে যাওয়া। Unity নিজে থেকে বের হতে পারে না,
/// তাই এই navigation সম্পূর্ণ Flutter-এর দায়িত্ব।
Future<void> _handleSubscribeRequest(Map<String, dynamic> env) async {
  // source বের করি — analytics ও ভবিষ্যতে পেজ কাস্টমাইজেশনের জন্য।
  String source = 'unknown';
  try {
    final rawData = env['data'];
    if (rawData is String && rawData.isNotEmpty) {
      final data = jsonDecode(rawData) as Map<String, dynamic>;
      source = (data['source'] as String?) ?? 'unknown';
    }
  } catch (e) {
    debugPrint('[Unity] subscribe payload parse error: $e');
  }

  debugPrint('[Unity → Flutter] REQUEST_SUBSCRIBE (source=$source)');
  if (!mounted) return;

  // pop-এর পরে `context` invalid হতে পারে, তাই আগেই NavigatorState ধরে রাখি।
  final navigator = Navigator.of(context);

  // গেম থেকে বেরিয়ে সরাসরি subscribe পেজে replace করি।
  // pushReplacement হওয়ায় UnityGameScreen dispose হবে → landscape→portrait
  // ও system UI restore আপনা-আপনি চলবে (dispose()-এ যা আছে)।
  navigator.pushReplacementNamed('/subscribe', arguments: source);
}
```

### Why `pushReplacementNamed` and not `pop()` + `pushNamed()`?

`pushReplacementNamed` swaps the Unity screen for the subscribe screen in a single transition — no flash of
the underlying screen, and `UnityGameScreen.dispose()` still runs (so the orientation reset to portrait and
the `immersiveSticky` → `manual` restore both fire).

If you'd rather the user land back on the game after subscribing, use the pop-then-push variant instead:

```dart
navigator.pop();                                  // গেম বন্ধ
navigator.pushNamed('/subscribe', arguments: source);
```

Pick based on the UX you want:

- **`pushReplacementNamed`** → after subscribing, Back goes to whatever was *before* the game (game is gone).
- **`pop` + `pushNamed`** → same net stack, game removed, subscribe on top.

Either way the game is fully torn down — the Unity engine unloads and orientation returns to portrait.

---

## 4. Change 3 — make sure the `/subscribe` route exists

If you use named routes, register it in your `MaterialApp`:

```dart
MaterialApp(
  // ...
  routes: {
    '/subscribe': (context) => const SubscribePage(),
    // ...
  },
);
```

Read the `source` inside the page:

```dart
class SubscribePage extends StatelessWidget {
  const SubscribePage({super.key});

  @override
  Widget build(BuildContext context) {
    final source = ModalRoute.of(context)?.settings.arguments as String?;
    // source == 'treasure_box' — চাইলে analytics/copy কাস্টমাইজ করুন।
    return Scaffold(/* আপনার subscription UI */);
  }
}
```

**If you don't use named routes**, skip the route registration and navigate directly:

```dart
navigator.pushReplacement(
  MaterialPageRoute(
    builder: (_) => SubscribePage(source: source),
  ),
);
```

---

## 5. Full updated `_onMessageFromUnity` (drop-in replacement)

Complete method with `env` hoisted so the handler can access `data`:

```dart
// ── Unity → Flutter ────────────────────────────────────────────────────────
Future<void> _onMessageFromUnity(String message) async {
  debugPrint('[Unity → Flutter] $message');
  if (!mounted) return;

  // envelope নয় (plain string) → legacy shape।
  if (!message.startsWith('{')) {
    await _handleLegacyMessage(message);
    return;
  }

  // নতুন envelope: { command, data }
  Map<String, dynamic> env;
  String? command;
  try {
    env = jsonDecode(message) as Map<String, dynamic>;
    command = env['command'] as String?;
  } catch (e) {
    debugPrint('[Unity] envelope parse error: $e');
    return;
  }

  switch (command) {
    case 'UNITY_READY':
      await _pushEverythingToUnity();
      break;

    case 'REQUEST_FELLOWSHIP_PROFILES':
      await _sendFellowshipRoster();
      break;

    case 'REQUEST_SUBSCRIBE':
      await _handleSubscribeRequest(env);
      break;

    case 'EXIT':
    case 'exit':
      if (mounted) Navigator.of(context).pop();
      break;

    default:
      debugPrint('[Unity] Unhandled command: $command');
  }
}
```

*(Only change from the original: `final env` → hoisted `Map<String, dynamic> env` declared before the try,
so it's in scope for the switch.)*

---

## 6. Optional — add the incoming command as a constant

For consistency with the outgoing constants in `unity_bridge.dart`, document the incoming command:

```dart
// unity_bridge.dart — ইনকামিং কমান্ড (Unity → Flutter), শুধু রেফারেন্সের জন্য
class UnityIncoming {
  UnityIncoming._();
  static const String unityReady = 'UNITY_READY';
  static const String requestFellowshipProfiles = 'REQUEST_FELLOWSHIP_PROFILES';
  static const String requestSubscribe = 'REQUEST_SUBSCRIBE';
  static const String exit = 'EXIT';
}
```

Purely optional — the string literals work fine.

---

## 7. Already handled correctly (no action needed)

- **Orientation & system UI** — `dispose()` already resets portrait + restores the status bar. Both
  navigation options above tear down `UnityGameScreen`, so this runs automatically.
- **`mounted` guard** — the handler checks `mounted` before touching `Navigator`.
- **Payload double-decode** — the handler `jsonDecode`s `data` a second time, matching the protocol.

---

## 8. Testing checklist

1. **In the Unity Editor first** (before a full build): open **Window → Flutter Bridge Manager**, and
   confirm the Subscribe button on the treasure box panel logs
   `[FlutterBridge] Sent to Flutter: {"command":"REQUEST_SUBSCRIBE",...}` in the Console. This proves the
   Unity side fires. *(The `subscribeButton` field must be assigned in the panel's Inspector, or the tap
   won't fire — worth double-checking.)*
2. **On device**, tap Subscribe in the treasure box panel → confirm:
   - `[Unity → Flutter] {"command":"REQUEST_SUBSCRIBE"...}` prints,
   - the game closes,
   - the screen rotates back to portrait,
   - the subscribe page appears with `source == 'treasure_box'`.
3. **Back navigation** from the subscribe page behaves the way you intended (per your `pushReplacement`
   vs `pop`+`push` choice).

---

## 9. Command reference (Unity → Flutter)

| Command | Payload (`data`, stringified) | What Flutter does |
|---|---|---|
| `UNITY_READY` | `{}` | Push profile, coins, fellowship roster. |
| `REQUEST_FELLOWSHIP_PROFILES` | `{}` | Send `UPDATE_FELLOWSHIP_PROFILES`. |
| `REQUEST_SUBSCRIBE` | `{ "source": "treasure_box" }` | Exit the Unity widget and open the subscribe page. |

The `source` field currently only ever holds `"treasure_box"`. If the subscribe page should also know
*which reward/tier* prompted the tap, the Unity-side `SubscribeRequestPayload` can be extended with
`rewardName` / `tier` — ask the Unity developer and update §3's parsing accordingly.
