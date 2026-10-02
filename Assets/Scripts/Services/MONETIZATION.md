# Rewarded ads — setup reference

All three rewarded surfaces — treasure boxes, the shop's watch-an-ad items, and the interaction
prompt — go through `IAdService`, reached via `AdServiceBootstrap.Service` (or the injectable
`TreasureBoxManager.AdService`, which is assigned from it).

Mediation is **Unity LevelPlay** (`com.unity.services.levelplay`, Ads Mediation 9.5.1). LevelPlay is the
only ad SDK in the project. Unity Ads and InMobi reach the game as LevelPlay *adapters* — their
SDKs are never imported directly, and their IDs are never compiled into the app.

## Who initialises LevelPlay — the Flutter host, not the game

The game ships inside the Amal Flutter app (`com.ibadah.amal`): **one APK, one process, one LevelPlay app
key.** LevelPlay is a per-process singleton and may only be initialised once, so the host owns that call.
The game never calls `LevelPlay.Init`, and never sets consent.

Why the host and not the game:

- It runs first — Flutter is up long before the Unity scene loads.
- It owns the GDPR/UMP consent dialog, and the answer has to reach the SDK *before* init.
- Only one side can own it, and the side that asks the player the question owns the answer. The game
  previously hardcoded `consentGranted = true`, which could overwrite a player's "no" from Flutter's
  dialog, since both write to the same SDK singleton and the last writer wins.

The game still reports its own revenue, because app key and ad unit are different things: it shows ads on
its own ad unit under the host's shared app key.

**Do not give the game the host's ad unit ID.** Two `LevelPlayRewardedAd` objects on one ad unit in one
process compete for the same loaded fill, and which side's callbacks fire becomes undefined.

### The handshake (what the Flutter side must implement)

Three messages over `FlutterBridge`. Unity's side is done; these are the host's half.

| Direction | Command | Payload | Meaning |
|---|---|---|---|
| Unity → Flutter | `REQUEST_AD_SDK_STATE` | `{}` | "Is LevelPlay up yet?" |
| Flutter → Unity | `UPDATE_AD_SDK_STATE` | `{ "initialized": bool, "message": string }` | The answer, **and** pushed unprompted the moment init succeeds or fails |
| Flutter → Unity | `UPDATE_AD_AVAILABILITY` | `{ "rewardedReady": bool }` | Optional; drives "Watch Ad" button state |

Both directions matter. The host normally finishes init before the Unity scene exists, so a game that
only listened for the unprompted push would wait forever for an event that already fired — hence the
request. And a game that only asked once would miss a later init — hence the push.

If the host does not answer within 10 seconds, the game builds the rewarded ad anyway, assuming Flutter
has LevelPlay up but has not been taught to announce it. That fallback exists because the first merged QA
build showed no ads at all: the Flutter side predated this handshake, so the game waited for an
announcement nothing was sending and every treasure box fell through to the no-ad bypass.

The fallback is a safety net, not the design — it guesses. Implement the two messages and the game stops
guessing. If ads never appear in the merged app, this handshake is still the first thing to check; look
for these in logcat:

```
[LevelPlayAdService] No rewarded ad object — LevelPlay never initialised.
[LevelPlayAdService] The host did not answer REQUEST_AD_SDK_STATE within 10s.
```

## What lives in code

Only the LevelPlay app key and rewarded ad unit ID, in `LevelPlayAdService.cs`:

| | Android | iOS |
|---|---|---|
| App key | `285c611cd` | `285c64b55` |
| Rewarded ad unit | `cdsr8xxvqptsq70h` | `jyzi8bfs2bbt15co` |

## What lives in the LevelPlay dashboard

These IDs are entered when linking each network as a mediated network. Nothing here goes in the project.

**Unity Ads** — Game ID Android `6196476`, iOS `6196477`. Placements `Rewarded_Android` / `Rewarded_iOS`.
Linking also needs a reporting **API key** generated in the Unity Ads dashboard.

**InMobi** — Account `7335d9e5bb7a431496e61b34daf2c8d6`. App keys Android `10000235181`,
iOS `10000235182`. Placements Android `10000824692`, iOS `10000824693`.

**Mintegral** — no IDs yet, awaiting their review (watch admin@amal-app.com). Post-launch. Adding it
needs no code change, but the adapter may only be installed *after* Mintegral is added to the Flutter
host's privacy policy, `app-ads.txt`, and Play Data safety declaration — see "Adapters installed" below.

## Adapters installed

`Assets/LevelPlay/Editor/*.xml` is the source of truth for which adapters are in the build:

- `IronSourceSDKDependencies.xml` — LevelPlay SDK 9.6.0
- `ISUnityAdsAdapterDependencies.xml` — Unity Ads 5.13.0, unity-ads 4.20.1
- `ISInMobiAdapterDependencies.xml` — InMobi 5.9.0, inmobi-ads-kotlin 11.4.1

**Only these three adapters may be installed.** The game ships inside the Amal Flutter app
(`com.ibadah.amal`), and the Flutter host's privacy policy, `app-ads.txt`, and Play Store Data
safety declaration list only Unity Ads, InMobi, and ironSource. Any other network SDK inside the
APK is an undeclared data collector — a policy violation and account risk.

**Never install the AdMob adapter.** It pulls in `ads-mobile-sdk`, whose `com.google.android.gms.ads.*`
classes collide with the older `play-services-ads-lite` that the Flutter host's `google_mobile_ads`
ships. On Android this fails the Release build outright at `:app:checkReleaseDuplicateClasses`; on iOS,
two copies in one process is what crashed the app before (see the notes on `AdsManager`).

Do **not** try to fix a collision with a Gradle `exclude`. The adapter calls into the classes you would
be excluding, so the build passes and the app crashes at runtime instead. The fix is always to leave the
adapter uninstalled.

*2026-10-01: AdMob, AppLovin, and Meta adapters were found installed and were removed — they broke the
Flutter host's Release build. After changing anything in `Assets/LevelPlay/Editor/`, run
Assets → External Dependency Manager → Android Resolver → **Force Resolve**, then confirm
`Assets/Plugins/Android/mainTemplate.gradle` contains no `ads-mobile`, `admob`, `applovin`, `facebook`,
or `audience-network` line. That template is committed, so a stale resolve gets pushed to the repo.*

iOS SKAdNetwork IDs are injected into Info.plist automatically at build time by the package's post-build
step, for whichever adapters are installed. No manual plist editing.

## Still open

- **Consent.** `AdServiceBootstrap` passes `consentGranted = false` as a placeholder. The Flutter host owns
  the UMP/GDPR flow and `FlutterCommands` has no command to carry the answer across. Fix before any EU
  release — either add an `UPDATE_CONSENT` bridge command or move consent into Unity.
- **`AdsManager` is now dead as an ad path.** Nothing calls its `ShowRewardedAd` any more, so its fake-ad
  panel and its `REQUEST_REWARDED_AD` bridge call are unreachable. It still sits on the `AdManager`
  GameObject and still subscribes to `FlutterBridge.OnRewardedAdResult`, which is harmless (no ad is ever
  in flight, so a late result just logs). Worth deleting once the Flutter host drops its ad code too.
- **Test suite.** Run LevelPlay's Ad Inspector per network before going live.
- **app-ads.txt.** Each network supplies a line to publish at the developer domain.
