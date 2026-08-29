# Flutter Guide: Language Switching (`SET_LOCALE`)

How to make the Flutter app tell the Jannah Garden Unity game which language to display, and keep it in
sync whenever the player changes language in the app.

This is a companion to [`flutter_bridge_guide.md`](flutter_bridge_guide.md) and is tailored to the existing
`unity_bridge.dart` + `unity_game_screen.dart` on the Flutter side (see
[`flutter_subscribe_guide.md`](flutter_subscribe_guide.md) for the shape of those two files this guide
assumes).

---

## 1. What's true today

The Unity game now has full localization support:

- **English, Arabic, and Urdu** — all 500 trivia questions, fully translated, every level.
- **Bengali** — 499 of 500 questions translated (only `L5-PR-2` is missing). The game automatically shows
  English for any question that isn't translated yet, so switching to Bengali is always safe — it never
  leaves a level empty, a missing question just reads in English until translation catches up.
- **Right-to-left layout and Arabic-script letter joining** for Arabic and Urdu — handled entirely inside
  Unity. You never send a text-direction flag, only the locale code.

**What's missing is entirely on the Flutter side: nothing currently sends the locale to Unity.** The Unity
`LocalizationManager` remembers the last language it was told about (via `PlayerPrefs`) and defaults to
English if it's never been told anything at all — so today, every player sees English (or whatever language
someone last tested with in the editor) regardless of their app language setting.

This guide is the one change needed to close that gap.

---

## 2. What Unity expects

Same envelope as every other bridge message — see `flutter_bridge_guide.md` §2 if this is your first time
touching the bridge:

```json
{
  "command": "SET_LOCALE",
  "data": "{\"localeCode\":\"ar\"}"
}
```

- `command` is `"SET_LOCALE"` (exact, case-sensitive).
- `data` is a **stringified JSON** object with one field, `localeCode`.
- `localeCode` is one of: **`"en"`, `"ar"`, `"bn"`, `"ur"`** (case-insensitive). Regional forms like
  `"ar-SA"` or `"ar_SA"` are also accepted — Unity trims everything from the `-`/`_` onward. Anything else
  unrecognised, or a message sent before Unity's `LocalizationManager` exists, is logged and ignored — the
  game just keeps whatever language it already had. It never crashes or shows a blank screen.

This is **Flutter → Unity only**. Unity never asks for the locale (no `REQUEST_LOCALE` command) — you push
it proactively, the same way you push the user profile or coin balance.

---

## 3. Change 1 — add `setLocale` to `UnityBridge`

In `unity_bridge.dart`, add one method next to your existing senders (`sendUserProfile`, `sendCoins`,
`sendFellowshipProfiles`, …):

```dart
class UnityBridge {
  // ...existing code...

  static void setLocale(String localeCode) {
    _send('SET_LOCALE', {'localeCode': localeCode});
  }
}
```

If you keep an `UnityOutgoing` (or similarly named) constants class alongside `UnityBridge` for command
strings, add it there too for consistency:

```dart
class UnityOutgoing {
  UnityOutgoing._();
  static const String setLocale = 'SET_LOCALE';
  // ...existing constants...
}
```

Purely optional — the string literal works fine either way, matching the pattern in `flutter_bridge_guide.md`.

---

## 4. Change 2 — send it on `UNITY_READY`

In `unity_game_screen.dart`, find `_pushEverythingToUnity()` (the method your `UNITY_READY` case already
calls — see `flutter_subscribe_guide.md` §5 for the full `_onMessageFromUnity` switch) and add one line:

```dart
Future<void> _pushEverythingToUnity() async {
  // ...existing pushes (profile, coins, fellowship roster)...

  UnityBridge.setLocale(_currentLocaleCode()); // 🆕 tell Unity the app's language
}
```

Where `_currentLocaleCode()` returns one of `en` / `ar` / `bn` / `ur` from whatever your app already uses to
track the active language — see §6 for how to map that.

**Why here specifically:** `UNITY_READY` fires on every cold start *and* every time the player re-enters the
game (per `flutter_bridge_guide.md` §3, the handshake retries up to 8 times over ~12s on a slow start, and
fires again on resume). Piggybacking on the same method you already use for profile/coins means the locale
is refreshed at exactly the same points, for free.

---

## 5. Change 3 — send it whenever the player changes language

This is the part that makes switching language *while already in the garden* work, not just at the next
cold start. Find wherever your app currently reacts to a language change — commonly one of:

- A `Locale` setter on a `ChangeNotifier`/`Provider`/`Riverpod` locale controller
- A callback on a language-picker widget in Settings
- An `AppLocalizations`/`intl` delegate switch

Call `UnityBridge.setLocale(...)` there too:

```dart
void onLanguageChanged(String newLocaleCode) {
  // ...whatever you already do to update the app's own locale...
  setState(() => _appLocale = newLocaleCode);

  UnityBridge.setLocale(newLocaleCode); // 🆕 keep Unity in sync
}
```

This works **even while the Unity view is open and the player is mid-game** — `flutter_embed_unity` keeps a
single Unity instance alive (paused, not destroyed) whenever its widget isn't visible, so the message still
reaches the running game. Unity reloads its UI strings and trivia question bank live; no restart, no
re-entering the garden, no loading screen.

If your app currently only changes language from a screen where the Unity widget definitely isn't mounted,
this still matters: the point of this call site is that Unity has no way to notice your app's language
changed on its own — it only ever knows because you told it.

---

## 6. Mapping your app's language identifiers to Unity's locale codes

Unity only understands the four codes in §2. If your app tracks language as a Dart `Locale` object, an
enum, or a different string convention, map it once in a small helper rather than scattering the mapping
across call sites:

```dart
String unityLocaleCodeFor(Locale locale) {
  switch (locale.languageCode) {
    case 'ar': return 'ar';
    case 'bn': return 'bn';
    case 'ur': return 'ur';
    default:   return 'en'; // anything else — Unity's own fallback is also English, so this just avoids relying on it
  }
}
```

Use this same helper for both call sites in §4 and §5 so they can never drift out of sync with each other.

---

## 7. Already handled correctly on the Unity side (no action needed)

- **Persistence.** Unity remembers the last locale via `PlayerPrefs` and applies it on its very first frame,
  before your `UNITY_READY` handler even runs — so a returning player briefly sees their *last* language,
  not a flash of English, while §4's message is in flight.
- **RTL layout and Arabic-script shaping** for Arabic and Urdu — mirrored UI, joined letterforms. Nothing
  for Flutter to send or configure.
- **Partial-translation safety.** Bengali questions that aren't translated yet fall back to English
  automatically. Switching to `bn` never produces a blank or broken level.
- **Unrecognised or early input.** An unknown locale code, or `SET_LOCALE` arriving before Unity's
  `LocalizationManager` exists, is logged and ignored rather than crashing.

---

## 8. Testing checklist

1. **In the Unity Editor first** (before a full build): open **Window → Flutter Bridge Manager**, pick
   `SET_LOCALE` (or build the envelope by hand — `{"command":"SET_LOCALE","data":"{\"localeCode\":\"ar\"}"}`
   — if it isn't in the template list), and hit *Simulate Receive From Flutter*. Confirm the Console logs
   `[FlutterBridge] Locale -> ar` and that any open trivia question / UI text updates immediately and
   right-aligns.
2. **On device**, with the language wired up per §4–§5:
   - Cold-start the app in each of the four languages and confirm the garden's UI text and any trivia
     question match.
   - While the garden is open, change language in Settings without closing the game, and confirm the UI and
     an open (or newly opened) trivia question switch live.
   - Specifically check Arabic and Urdu render right-to-left and Bengali does not.
   - In Bengali, every question should read in Bengali except one (`L5-PR-2`, a Level 5 Prophets question),
     which shows readable English — this is expected today (see §1), not a bug.
3. **Backgrounding.** Put the app in the background mid-game, change the system/app language, and resume —
   confirm the next `UNITY_READY` handshake (§4) picks up the new language.

---

## 9. Command reference

| Direction | Command | Payload (`data`, stringified) | Notes |
|---|---|---|---|
| Flutter → Unity | `SET_LOCALE` | `{ "localeCode": "en" \| "ar" \| "bn" \| "ur" }` | Send on `UNITY_READY` and on every in-app language change. No response is sent back. |

There is no `REQUEST_LOCALE` or any Unity → Flutter direction for this feature — it is purely Flutter
telling Unity, proactively, whenever the language is or becomes known.
