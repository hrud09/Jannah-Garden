# Rewarded ads — setup reference

All three rewarded surfaces — treasure boxes, the shop's watch-an-ad items, and the interaction
prompt — go through `IAdService`, reached via `AdServiceBootstrap.Service` (or the injectable
`TreasureBoxManager.AdService`, which is assigned from it).

Mediation is **Unity LevelPlay** (`com.unity.services.levelplay`, Ads Mediation 9.5.1). LevelPlay is the
only ad SDK in the project. Unity Ads, Meta, and InMobi reach the game as LevelPlay *adapters* — their
SDKs are never imported directly, and their IDs are never compiled into the app.

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

**Meta Audience Network** — App ID `959094003385668`. Placements:
Android `959094003385668_959094200052315`, iOS `959094003385668_959094196718982`.
Blocked on Meta's app review; also needs domain/ownership verification.

**InMobi** — Account `7335d9e5bb7a431496e61b34daf2c8d6`. App keys Android `10000235181`,
iOS `10000235182`. Placements Android `10000824692`, iOS `10000824693`.

**Mintegral** — no IDs yet, awaiting their review (watch admin@amal-app.com). Post-launch. When it lands,
install its adapter from the Integration Manager and link it in the dashboard; no code change needed.

## Adapters installed

`Assets/LevelPlay/Editor/*.xml` is the source of truth for which adapters are in the build:

- `IronSourceSDKDependencies.xml` — LevelPlay SDK 9.6.0
- `ISUnityAdsAdapterDependencies.xml` — Unity Ads 5.14.0.0
- `ISFacebookAdapterDependencies.xml` — Meta 5.7.0.0
- `ISInMobiAdapterDependencies.xml` — InMobi 5.14.0.0

**Never install the AdMob adapter.** It pulls in google_mobile_ads, which the Flutter host also ships;
two copies in one process is what crashed iOS before (see the notes on `AdsManager`).

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
