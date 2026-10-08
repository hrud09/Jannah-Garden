# iOS Release + Ads — What the Flutter Side Needs (2026-10-03)

Everything you (the Flutter developer) need to change in the Amal app for two things:

1. **Ads** — the game now owns the ad SDK. This changed, it affects **both platforms**, and it is
   currently not implemented on your side. This is why rewarded ads don't work.
2. **iOS** — the game has never shipped on iOS. Four things can only be done in your Xcode project,
   not in the Unity export.

Nothing here needs a new `unityLibrary` export to get started on — the Unity side of the ad handshake
is already built and shipping.

> ### ⚠️ This supersedes §"Handling `REQUEST_REWARDED_AD`" in `flutter_bridge_guide.md`
>
> That section says *"The game has no ad SDK… ads are yours entirely."* **That is out of date.** It was
> written when the Unity-side Google Mobile Ads plugin had just been removed and before LevelPlay was
> added. `REQUEST_REWARDED_AD` is now dead code in the game — nothing sends it, so if you implemented a
> handler for it, it has never fired. Read this document instead for anything ad-related. The rest of
> `flutter_bridge_guide.md` (garden state, fellowship roster, photos, coins, OOM) is still accurate.

---

# Part 1 — Ads (both platforms, currently broken)

## What changed

The game used to have no ad SDK and asked you to show ads for it. It now has **Unity LevelPlay**
mediation (Unity Ads + InMobi as mediated networks) and shows its own rewarded ads, on its own ad unit.

What you own now is **initialisation and consent only**:

| Who | Owns |
|---|---|
| **Flutter (you)** | Calling `LevelPlay.Init` — once, per process. The GDPR/UMP consent dialog. On iOS, the ATT prompt. Telling the game when init is done. |
| **Unity (the game)** | Creating its own rewarded ad object, loading it, showing it, granting the reward. |

### Why it has to be this way

LevelPlay is a **per-process singleton** and may only be initialised once. The game and the Flutter app
are one binary, one process, one LevelPlay app key. Only one side can make that call, and it has to be
you because:

- You run first — Flutter is up long before the Unity scene loads.
- You own the GDPR/UMP consent dialog, and the answer must reach the SDK **before** init.
- The game previously hardcoded `consentGranted = true`, which could silently overwrite a player's
  "no" from your dialog. The game no longer touches consent at all.

## What to implement — three messages

Over the existing `FlutterBridge`, same envelope format as every other command.

| Direction | Command | Payload | Meaning |
|---|---|---|---|
| Unity → Flutter | `REQUEST_AD_SDK_STATE` | `{}` | "Is LevelPlay up yet?" |
| Flutter → Unity | `UPDATE_AD_SDK_STATE` | `{ "initialized": bool, "message": string }` | The answer — **and** pushed unprompted the moment init succeeds or fails |
| Flutter → Unity | `UPDATE_AD_AVAILABILITY` | `{ "rewardedReady": bool }` | Optional. Drives "Watch Ad" button enabled state |

`message` is only used for logging on the game's side when `initialized` is `false` — put the SDK's
error text in it and it'll show up in logcat / Console, which will save you a debugging session.

**Both directions are required.** This is not belt-and-braces:

- You normally finish init *before* the Unity scene exists, so an unprompted push alone would fire into
  the void and the game would wait forever for an announcement that already happened. Hence the request.
- A game that only asked once would miss a later init (or a retry after a failure). Hence the push.

### Sketch

```dart
// 1. Answer the request.
case 'REQUEST_AD_SDK_STATE':
  UnityBridge.send('UPDATE_AD_SDK_STATE', {
    'initialized': _levelPlayReady,
    'message': _levelPlayError ?? '',
  });
  break;

// 2. And push unprompted, the moment init resolves — success or failure.
void _onLevelPlayInitComplete(bool ok, String? error) {
  _levelPlayReady = ok;
  _levelPlayError = error;
  UnityBridge.send('UPDATE_AD_SDK_STATE', {
    'initialized': ok,
    'message': error ?? '',
  });
}
```

## Two things that will silently break it

**Do not give the game your ad unit ID.** The game has its own rewarded ad unit under the same shared
app key — app key and ad unit are different things. Two `LevelPlayRewardedAd` objects on *one* ad unit
in *one* process compete for the same loaded fill, and which side's callbacks fire becomes undefined.
It will mostly work in testing and fail unpredictably in production.

**Use the correct app key per platform.** These are the shared app keys the game is built against:

| | Android | iOS |
|---|---|---|
| LevelPlay app key | `285c611cd` | `285c64b55` |

If your init hardcodes the Android key, iOS gets **zero fill** and it will look like a mediation or
dashboard problem rather than a one-line bug. Worth checking before anything else on iOS.

## What happens today, without this

The game waits 10 seconds for `UPDATE_AD_SDK_STATE`, then builds the rewarded ad anyway on the
assumption that you have LevelPlay up but haven't been taught to announce it.

**That fallback is a guess, not a design.** If LevelPlay genuinely isn't initialised, every treasure box
and every "watch an ad" shop item falls through to a no-ad bypass that grants the reward for free —
**capped at 3 per day**, after which players are simply blocked with no explanation.

If ads don't appear in a merged build, check this handshake first. Look for these in logcat / Xcode
Console:

```
[LevelPlayAdService] No rewarded ad object — LevelPlay never initialised.
[LevelPlayAdService] The host did not answer REQUEST_AD_SDK_STATE within 10s.
```

## Networks — don't add any

The game ships exactly three adapters: **ironSource (LevelPlay SDK), Unity Ads, and InMobi.** That list
matches what the Amal privacy policy, `app-ads.txt`, and the Play Data safety declaration disclose. Any
other network SDK in the binary is an undeclared data collector — a policy violation and an account risk.

**AdMob in particular must never be added to the game's mediation list.** On 2026-10-01 the AdMob adapter
was found installed on the Unity side and broke your Release build at
`:app:checkReleaseDuplicateClasses` — its `ads-mobile-sdk:1.5.0` and your `google_mobile_ads`'
`play-services-ads-lite:23.6.0` declare the same `com.google.android.gms.ads.*` classes. It's been
removed and there's now a build-time guard on the Unity side that fails the export if it comes back.

**Your own `google_mobile_ads` is fine and should stay.** The conflict was only ever two copies of the
*same* SDK in one process. LevelPlay with no AdMob adapter doesn't pull in Google's ad SDK at all, so
there's nothing to collide with.

---

# Part 2 — iOS

The game has never shipped on iOS. Four of the required changes live in your Xcode project and cannot be
done from the Unity side, because the Unity export's `Info.plist` and `Podfile` belong to the
`Unity-iPhone` target — a target your app never builds. Only `UnityFramework` ships.

That's the rule of thumb for everything below: **anything Unity writes into
`ios/unityLibrary/Info.plist` or `ios/unityLibrary/Podfile` is a reference copy for you to transcribe,
not something that reaches the app.**

## 2.1 — CocoaPods

Unity's dependency manager writes LevelPlay's pods into `ios/unityLibrary/Podfile` and expects to run
`pod install` against `Unity-iPhone.xcworkspace`. That never happens in a Flutter build. Add these to
your `ios/Podfile` by hand:

```ruby
pod 'IronSourceSDK',             '9.6.1.0'
pod 'IronSourceUnityAdsAdapter', '5.12.0.0'
pod 'IronSourceInMobiAdapter',   '5.10.0.0'
```

**Re-check this on every `unityLibrary` export.** There is no automation bridging the gap, so if an
adapter is added, removed, or version-bumped on the Unity side, these lines go stale silently. After each
export, diff the generated `ios/unityLibrary/Podfile` against the three lines above — that file is the
source of truth for what the Unity code expects to link against.

## 2.2 — `ios/Runner/Info.plist`

Three keys. Unity writes all three into the wrong plist, so all three are yours:

**`NSUserTrackingUsageDescription`** — required by Apple whenever AppTrackingTransparency is linked,
which it is. **If this key is missing, iOS terminates the app at the prompt** rather than refusing the
request. The string Unity generates for its own copy is:

> This identifier will be used to deliver personalized ads to you.

**`NSPhotoLibraryAddUsageDescription`** — same failure mode (termination, not refusal) when the player
taps **Save** on a garden photo. Already noted in `flutter_bridge_guide.md` §"Photos", so you may have
this one already:

> Jannah Garden saves the photos you take of your garden to your gallery.

**`SKAdNetworkItems`** — the attribution IDs for every mediated network. Without them, iOS attribution
doesn't work, networks can't measure installs, and they bid lower or not at all. **This is a silent
revenue loss, not a crash** — nothing will look broken.

LevelPlay generates this array at export time from whichever adapters are installed, into
`ios/unityLibrary/Info.plist`. After the next iOS export I'll send you that array to paste in. It'll be a
long list of `<dict><key>SKAdNetworkIdentifier</key><string>…</string></dict>` entries — copy it verbatim.
It needs re-checking whenever the adapter list changes, same as the pods.

## 2.3 — Who calls `ATTrackingManager.requestTrackingAuthorization`?

Only one side should, and the answer has to land **before** your `LevelPlay.Init` for the consent signal
to be correct. Since you already own init and the UMP/GDPR flow, it should be you — but let's be explicit
rather than both assuming the other does it, because "both assume the other does it" produces an app that
never asks and quietly loses IDFA.

Tell me which way you want it and I'll make the Unity side match.

## 2.4 — Export location

Same shape as Android: the Unity export goes to `ios/unityLibrary` in your Flutter project, in the layout
the `flutter_embed_unity_2022_3_ios` plugin expects. The export already disables bitcode and moves the
`Data` folder into the `UnityFramework` target, so you shouldn't need to touch the generated Xcode
project.

---

# Checklist

**Ads — both platforms, do first:**

- [ ] Handle `REQUEST_AD_SDK_STATE`, reply with `UPDATE_AD_SDK_STATE`
- [ ] Also push `UPDATE_AD_SDK_STATE` unprompted when init resolves (success *and* failure)
- [ ] Confirm init uses `285c611cd` on Android and `285c64b55` on iOS
- [ ] Confirm the game is **not** given your rewarded ad unit ID
- [ ] Confirm consent reaches the SDK before init
- [ ] *(Optional)* Push `UPDATE_AD_AVAILABILITY` so "Watch Ad" buttons reflect real fill

**iOS:**

- [ ] Three `pod` lines in `ios/Podfile`
- [ ] `NSUserTrackingUsageDescription` in `ios/Runner/Info.plist`
- [ ] `NSPhotoLibraryAddUsageDescription` in `ios/Runner/Info.plist`
- [ ] `SKAdNetworkItems` in `ios/Runner/Info.plist` (I'll send the array)
- [ ] Decide who calls ATT, and when relative to init

**Still on my side — not yours:**

- Removing leftover Google Mobile Ads native binaries from the Unity project (they'd re-introduce the
  old `dyld` launch crash on iOS if exported as-is)
- Building and uploading the iOS Addressable content to the CDN — the iOS prefix is currently empty, so
  remote 3D items would 404 on device today
- Sending you the generated `SKAdNetworkItems` array after the next export

Anything unclear or that doesn't match what you're seeing in the app, ask — several of the failure modes
above are silent, so "it builds and runs" isn't evidence any of them are done.
