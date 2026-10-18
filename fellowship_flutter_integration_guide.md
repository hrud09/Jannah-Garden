# Fellowship cards — what to change in the Flutter app

**For:** the Flutter developer on the `amal` repo.
**About:** the profile cards of other users that float around the Outer Garden.
**You do not need to touch Unity.** Unity is already updated and waiting for these changes.

---

## The short version

There are **6 tasks**. Do them in order — task 1 is the big one.

| # | Task | Where | Size |
|---|---|---|---|
| **1** | Send the **upline** too, not just the downline | `functions/index.js` + Dart | ~40 lines |
| **2** | Send `level`, `relation`, `isDirect`, `referralCount` per fellow | `unity_bridge.dart` | ~10 lines |
| **3** | Send `totalCount`, `directCount`, `truncated` | `unity_bridge.dart` | ~5 lines |
| **4** | Cache the roster — stop calling the Cloud Function 3× per app open | `unity_game_screen.dart` | ~25 lines |
| **5** | On failure, send an empty list instead of sending nothing | `unity_game_screen.dart` | ~8 lines |
| **6** | Fix the dead Exit button (`EXIT` → `REQUEST_EXIT_GAME`) | `unity_bridge.dart` | 2 lines |

Everything degrades safely. If you ship only task 2, nothing breaks — you just don't get task 1's benefit.

---

## Task 1 — Both sides of the chain

### The problem, in one picture

```
   A  invites  B  invites  C

   A's garden:  B, C        ✅ works today
   B's garden:  C           ⚠️  B cannot see A
   C's garden:  (empty)     ❌ C sees nothing at all
```

`getReferralNetwork` returns **downline only** — the people below you. So a brand-new user who was
just invited by a friend opens the garden and finds it completely empty, even though the friend who
brought them in is one step away.

### What it should be

```
   B's garden:  A (above), C (below)
   C's garden:  B (above), A (above)
```

Every person in a chain sees the people they are connected to — **in both directions**.

### Step 1a — server: return the upline

Each user document already stores `uplineUids`, an ordered array:

```
uplineUids[0] = the person who invited me
uplineUids[1] = the person who invited them
uplineUids[2] = ...and so on
```

So the upline needs no query at all — just a direct fetch by ID. Add this to `getReferralNetwork`
in `functions/index.js`, just before the `return`:

```js
// ── upline: the people ABOVE the target in the chain ─────────────────────
// uplineUids is already ordered nearest-first, so this is a plain getAll of
// a handful of documents - no query, no index, effectively free.
// Capped at 5: the game shows ~8 cards, and nobody feels connected to the
// person who invited the person who invited the person who invited them.
const MAX_UPLINE_SHOWN = 5;

let upline = [];
try {
  const targetDoc = await db.collection("users").doc(targetUid).get();
  const chain = Array.isArray(targetDoc.get("uplineUids"))
    ? targetDoc.get("uplineUids").slice(0, MAX_UPLINE_SHOWN)
    : [];

  if (chain.length > 0) {
    const refs = chain.map((id) => db.collection("users").doc(id));
    const docs = await db.getAll(...refs);

    upline = docs
      .filter((d) => d.exists)
      .map((d) => {
        const u = d.data() || {};
        return {
          uid: d.id,
          displayName: u.displayName || "",
          photoUrl: u.photoUrl || "",
          noorCoins: u.noorCoins || 0,
          referralCount: u.referralCount || 0,
          // level 1 = invited me directly, 2 = invited them, ...
          level: chain.indexOf(d.id) + 1,
          joinedAt: u.createdAt ? u.createdAt.toMillis() : null,
        };
      })
      .sort((a, b) => a.level - b.level);
  }
} catch (e) {
  console.warn("[getReferralNetwork] upline failed", e && e.message);
  // Non-fatal: the downline is still worth returning on its own.
}
```

and add one key to the returned object:

```js
return {
  targetUid,
  directOnly,
  directCount,
  totalCount: trueTotal !== null ? trueTotal : members.length,
  returnedCount: members.length,
  truncated: trueTotal !== null && trueTotal > members.length,
  totalNoorCoins: members.reduce((s, m) => s + (m.noorCoins || 0), 0),
  members,
  upline,   // ← NEW. Existing callers ignore it; nothing else changes.
};
```

This is additive — the admin dashboard and every other caller keep working untouched.

### Step 1b — Dart: merge both lists

In `unity_game_screen.dart`, where you map the response:

```dart
final members = (res.data['members'] as List?) ?? const [];
final uplineRaw = (res.data['upline'] as List?) ?? const [];

final fellows = <Fellow>[
  // Upline first - these are the people the player definitely knows.
  ...uplineRaw
      .whereType<Map>()
      .map((m) => Map<String, dynamic>.from(m))
      .map((m) => Fellow.fromNetworkMember(m, relation: 'upline')),

  ...members
      .whereType<Map>()
      .map((m) => Map<String, dynamic>.from(m))
      .map((m) => Fellow.fromNetworkMember(m, relation: 'downline')),
];
```

### What Unity does with it

- The inviter's card is shown **first**, and is **guaranteed a spot** — it can never be pushed off
  the end by the player's own 500 referrals.
- Order becomes: *your inviter → people you invited → your inviter's inviter → ...*
- If the card prefab is given an optional label, the inviter's card reads **"Invited you"** and your
  direct referrals read **"You invited"**. Translations are already in for en/bn/ar/ur.

---

## Task 2 — Send the chain info per fellow

Right now `getReferralNetwork` returns `level`, `isDirect` and `referralCount`, and the Dart mapper
throws them away. Unity needs them to decide which 8 of up to 500 fellows to show.

Replace the `Fellow` class in `lib/features/game/unity_bridge.dart`:

```dart
class Fellow {
  final String userId;
  final String userName;
  final String memberSince;
  final int noorCoins;
  final String profileImagePath;

  // ── NEW ──
  /// Steps away along the chain. Always positive; `relation` says which way.
  /// 1 = next to me (my inviter, or someone I invited).
  final int level;

  /// 'upline'   = this person invited me
  /// 'downline' = I invited this person
  final String relation;

  final bool isDirect;      // I invited them myself
  final int referralCount;  // how many they have invited

  const Fellow({
    required this.userId,
    required this.userName,
    required this.memberSince,
    required this.noorCoins,
    required this.profileImagePath,
    this.level = 0,
    this.relation = 'downline',
    this.isDirect = false,
    this.referralCount = 0,
  });

  /// Works for BOTH lists - pass relation: 'upline' or 'downline'.
  factory Fellow.fromNetworkMember(
    Map<String, dynamic> m, {
    required String relation,
  }) {
    final level = (m['level'] as num?)?.toInt() ?? 0;
    return Fellow(
      userId: (m['uid'] ?? '').toString(),
      userName: _trimName(m['displayName']),
      memberSince: _isoFromMillis(m['joinedAt']),
      noorCoins: (m['noorCoins'] as num?)?.round() ?? 0,
      profileImagePath: _asHttpUrl(m['photoUrl']),
      level: level,
      relation: relation,
      // Only meaningful downline. An upline person did not "get invited by me".
      isDirect: relation == 'downline' && (m['isDirect'] == true || level == 1),
      referralCount: (m['referralCount'] as num?)?.toInt() ?? 0,
    );
  }

  Map<String, dynamic> toUnityJson() => {
        'userId': userId,
        'userName': userName,
        'memberSince': memberSince,
        'noorCoins': noorCoins,
        'profileImagePath': profileImagePath,
        // ── NEW ──
        'level': level,
        'relation': relation,
        'isDirect': isDirect,
        'referralCount': referralCount,
      };

  // _trimName / _isoFromMillis / _asHttpUrl stay exactly as they are.
}
```

> Keep the old `Fellow.fromReferralNetworkMember` as a thin wrapper if anything else calls it:
> `factory Fellow.fromReferralNetworkMember(Map<String, dynamic> m) => Fellow.fromNetworkMember(m, relation: 'downline');`

**Three rules for these fields:**
1. Names are **case-sensitive**. `userName` works, `username` is silently ignored by Unity.
2. `level` and `referralCount` must be JSON **numbers**, `isDirect` a JSON **bool**. `"1"` and
   `"true"` as strings are read as 0 and false.
3. `relation` must be exactly `"upline"` or `"downline"` (lower case preferred; Unity compares
   case-insensitively). Anything else is treated as downline.

---

## Task 3 — Send the real totals

`fellows.length` is a page, not a count — the server caps the list at 500. Unity wants the real
numbers so the garden can show "+N more" later.

```dart
static void sendFellowshipProfiles(
  List<Fellow> fellows, {
  int totalCount = 0,
  int directCount = 0,
  bool truncated = false,
}) {
  _send(cmdUpdateFellowshipProfiles, {
    'fellows': fellows.map((f) => f.toUnityJson()).toList(),
    'totalCount': totalCount,
    'directCount': directCount,
    'truncated': truncated,
  });
}
```

Call site:

```dart
UnityBridge.sendFellowshipProfiles(
  fellows,
  totalCount: (res.data['totalCount'] as num?)?.toInt() ?? fellows.length,
  directCount: (res.data['directCount'] as num?)?.toInt() ?? 0,
  truncated: res.data['truncated'] == true,
);
```

`0` means "not sent" — Unity falls back to `fellows.length`.

---

## Task 4 — Cache the roster (this one costs money today)

### The problem

Unity does **not** send `UNITY_READY` once. It repeats it **every 1.5 seconds, up to 8 times**,
until the coin balance comes back — and starts over on every app resume.

Your handler calls `getReferralNetwork` on **every single one**:

```dart
case UnityIncoming.unityReady:
  await _pushEverythingToUnity();   // ← Cloud Function call, every time
```

`_pushedOnce` is set inside that method but never checked at the top, so it guards nothing. For a
player with a 5,000-person chain that is thousands of extra Firestore reads per app open.

> Do **not** fix this by ignoring repeat `UNITY_READY`s. Unity repeats them precisely because it
> has not got the coin balance yet — swallowing them leaves the game showing 0 coins.
> Cache the expensive call; keep answering the cheap ones.

### The fix

```dart
// ── roster cache ──────────────────────────────────────────────────────────
static const Duration _rosterTtl = Duration(minutes: 2);
List<Fellow>? _cachedFellows;
Map<String, dynamic>? _cachedMeta;
DateTime? _fetchedAt;
Future<void>? _inFlight;

bool get _rosterIsFresh =>
    _cachedFellows != null &&
    _fetchedAt != null &&
    DateTime.now().difference(_fetchedAt!) < _rosterTtl;

Future<void> _sendFellowshipRoster({bool force = false}) async {
  // Fresh cache? Just re-send it. Unity ignores an unchanged roster, so this
  // is free and still fixes a scene that loaded late.
  if (!force && _rosterIsFresh) {
    _emitCached();
    return;
  }
  // Already fetching? Wait for that one instead of starting a second.
  if (_inFlight != null) {
    await _inFlight;
    return;
  }
  _inFlight = _fetchAndSend();
  try {
    await _inFlight;
  } finally {
    _inFlight = null;
  }
}

Future<void> _fetchAndSend() async {
  try {
    final callable = FirebaseFunctions.instanceFor(region: 'us-central1')
        .httpsCallable('getReferralNetwork');

    final res = await callable
        .call<Map<String, dynamic>>({})
        .timeout(const Duration(seconds: 8));

    // …build `fellows` from res.data['upline'] + res.data['members'] (Task 1b)…

    _cachedFellows = fellows;
    _cachedMeta = Map<String, dynamic>.from(res.data);
    _fetchedAt = DateTime.now();
    _emitCached();
  } catch (e) {
    debugPrint('[Unity] getReferralNetwork failed: $e');
    _emitEmptyIfNothingCached();   // Task 5
  }
}

void _emitCached() {
  final fellows = _cachedFellows;
  if (fellows == null) return;
  final meta = _cachedMeta ?? const {};
  UnityBridge.sendFellowshipProfiles(
    fellows,
    totalCount: (meta['totalCount'] as num?)?.toInt() ?? fellows.length,
    directCount: (meta['directCount'] as num?)?.toInt() ?? 0,
    truncated: meta['truncated'] == true,
  );
}
```

### Optional: also make the call cheaper

`functions/index.js` says the game is meant to pass `directOnly: true` — it turns a full-chain
`array-contains` scan into a single index lookup. With Task 1 in place you would get: your inviter
(and up to 4 above them) + the people you invited personally. That is already a full garden for
most players.

Your call — `{}` (full chain) also works fine, it is just more expensive.

---

## Task 5 — "No fellows" and "the call failed" are different

Today a failure sends **nothing**, so the game cannot tell "still loading" from "nothing to show"
and waits forever.

```dart
/// Fetch failed and we have no cache: say "empty" rather than nothing.
/// An empty list is a real answer - Unity renders an empty garden for it.
void _emitEmptyIfNothingCached() {
  if (_cachedFellows != null) {
    _emitCached();            // stale data beats no data
    return;
  }
  UnityBridge.sendFellowshipProfiles(const []);
}
```

### Timing — please keep these three in sync

| | Value |
|---|---|
| Unity re-asks for the roster after | **4 s** |
| Unity stops waiting after | **12 s** |
| Your callable timeout | **8 s** |

There is room for exactly one retry inside the 12 s. If you change your timeout past 12 s, tell me
and I'll raise Unity's.

> *Historical note:* Unity used to give up after **3 seconds** — before your 8 s call could possibly
> return. That alone was why real rosters often never appeared. Already fixed on the Unity side.

---

## Task 6 — The in-game Exit button is dead

Not fellowship, but it is one line and players hit it.

Unity sends `REQUEST_EXIT_GAME`. The app listens for `'EXIT'`, which Unity never sends. So the exit
button inside the game does nothing. Unity deliberately will not call `Application.Quit()` — that
would close the whole Amal app — so popping the route is the only way out.

```dart
// unity_bridge.dart
static const String requestExitGame = 'REQUEST_EXIT_GAME';
```

```dart
// unity_game_screen.dart
case UnityIncoming.requestExitGame:
case 'EXIT':
case 'exit':      // keep both for older compiled builds
  if (mounted) Navigator.of(context).pop();
  break;
```

---

## The data behind the cards

Each card shows exactly four things: **name**, **"Member since ..."**, **Noor Coins**, **avatar**.
Two of them silently break if the `users` document is incomplete.

**`photoUrl` → the avatar**
- Must be `http://` or `https://`. Unity downloads it with **no auth headers** — a Firebase Storage
  URL with `?alt=media&token=...` works; a signed/expiring URL or anything needing a bearer token
  does not.
- Never Base64.
- **Please check it is actually written to `users/{uid}`.** The server reads the document, not
  Firebase Auth. If it only lives in Auth, every card shows the default avatar.
- Prefer a small image. Google photo URLs: append `=s128-c`. Storage: keep a thumbnail.

**`createdAt` → "Member since Mar 2024"**
- Missing `createdAt` → card reads "Member since —". Worth a backfill if many old accounts lack it.

**Names** — nothing to do, just so you know:
- Send them exactly as typed. Do not transliterate.
- Unity picks the font from the name's own script, so a Bengali name renders in Bengali even when
  the game's UI is in English, and Arabic names are joined and right-to-left.
- Your 40-character trim is right; past ~20 characters the card shrinks the text noticeably.
- Known gap, not fixable from Flutter: Bengali conjuncts don't join (ক্ষ shows as ক + ্ + ষ). Needs
  a Unity prefab change; tracked on that side.

---

## Testing without a device

**Window → Flutter Bridge Manager** in the Unity editor → pick `UPDATE_FELLOWSHIP_PROFILES` →
*Load Default Template for Selection* → paste → *Simulate Receive From Flutter*.

A roster that covers every edge case — the inviter, a direct referral, a deep one, a missing
avatar, a missing join date, a nameless account, Bengali and Arabic names:

```json
{
  "fellows": [
    { "userId": "u0", "userName": "মাহাদি হাসান", "memberSince": "2023-06-01", "noorCoins": 9100, "profileImagePath": "https://example.com/a.jpg", "level": 1, "relation": "upline",   "isDirect": false, "referralCount": 12 },
    { "userId": "u1", "userName": "عبد الرحمن",   "memberSince": "2025-01-02", "noorCoins": 300,  "profileImagePath": "",                            "level": 1, "relation": "downline", "isDirect": true,  "referralCount": 0 },
    { "userId": "u2", "userName": "Tareq Khan",   "memberSince": "",           "noorCoins": 90,   "profileImagePath": "",                            "level": 3, "relation": "downline", "isDirect": false, "referralCount": 1 },
    { "userId": "u3", "userName": "",             "memberSince": "2023-11-30", "noorCoins": 15,   "profileImagePath": "",                            "level": 2, "relation": "upline",   "isDirect": false, "referralCount": 3 }
  ],
  "totalCount": 412,
  "directCount": 1,
  "truncated": true
}
```

**Expected:** `u0` (your inviter) first, then `u1`, then `u3`, then `u2`. `u3` shows the
"A fellow" fallback name. `u2` reads "Member since —". `u1`, `u2`, `u3` show the default avatar.

**On a real build**, filter logcat for `FellowshipVisualization`:

| Log line | Means |
|---|---|
| `Showing N fellow(s) from Flutter.` | ✅ Working |
| `Flutter reports the player has no fellows yet` | App answered with an empty list — correct for a lone user |
| `No roster after 4s — asking Flutter again.` | Your first answer didn't arrive; retry going out |
| `Flutter sent no roster within 12s` | App never answered `REQUEST_FELLOWSHIP_PROFILES` |
| `Flutter roster had no 'fellows' array — ignoring.` | Malformed — usually `data` sent as a nested object instead of a JSON **string** |

---

## Message format reminder

This has not changed, but it is the single most common mistake:

```jsonc
{
  "command": "UPDATE_FELLOWSHIP_PROFILES",
  "data": "{\"fellows\":[ ... ]}"
}
```

`data` is a **string** containing JSON — `jsonEncode` twice. Unity's `JsonUtility` cannot read a
nested object. Your `_send()` already does this correctly; just don't "simplify" it.

---

## Done checklist

- [ ] `getReferralNetwork` returns an `upline` array
- [ ] Dart merges `upline` + `members` into one `fellows` list
- [ ] Each fellow carries `level`, `relation`, `isDirect`, `referralCount`
- [ ] Envelope carries `totalCount`, `directCount`, `truncated`
- [ ] `getReferralNetwork` called at most once per 2 minutes, not once per `UNITY_READY`
- [ ] A failed or empty fetch sends `{"fellows": []}` rather than nothing
- [ ] `photoUrl` confirmed in `users/{uid}` and fetchable without auth
- [ ] `createdAt` confirmed populated
- [ ] `REQUEST_EXIT_GAME` handled

---

## Also worth a ticket (not fellowship)

- **Gardens are never saved to Firebase.** Unity sends `SAVE_GARDEN_STATE` on every placement and
  asks `REQUEST_GARDEN_STATE` on cold start. Neither is handled — **a player who changes phone loses
  their entire garden.** Payload shape: `GardenStatePayload` in `FlutterDataModels.cs`.
- `REQUEST_REWARDED_AD` unhandled → "Watch Ad" buttons do nothing.
- `REQUEST_IAP_PURCHASE` unhandled → coin packs cannot be bought.
- `REQUEST_SHARE_PHOTO` / `REQUEST_SAVE_PHOTO` unhandled → photo mode writes a PNG and hands you the
  path, but nothing opens the share sheet.
- `UPDATE_AD_AVAILABILITY` never pushed → the game assumes no ad is ever ready.

Every command and payload is documented inline in
`Assets/Scripts/FlutterIntegration/FlutterDataModels.cs` on the Unity side.
