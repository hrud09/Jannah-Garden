# Jannah Garden — Onboarding Walkthrough

Welcome. This is the orientation doc for a new developer joining the **Jannah Garden** Unity project.
Read it top to bottom once; after that use it as a map. Every section points at real files, so follow
the links rather than trusting the summary.

---

## 1. What this project is

Jannah Garden is a mobile 3D garden-building game with an Islamic theme. The player walks around a
garden, buys items with **Noor Coins**, places them on a grid, earns **XP** to unlock new areas, opens
daily **Treasure Boxes**, and answers Dhikr / MCQ **question tasks** scattered around the world.

**The single most important thing to understand:** this game does **not** ship as a standalone app.
It is embedded as a Unity module inside the **Amal Flutter app** (`com.ibadah.amal`) — one APK, one
process. Flutter is the host: it owns the user account, the real coin balance, the cloud-saved garden,
the ads SDK, the language setting and the consent dialog. The game talks to it over a message bridge.
If you forget this, half the architecture will look strange.

| Fact | Value |
|---|---|
| Unity version | **6000.3.10f1** (Unity 6.3) — use exactly this, see `ProjectSettings/ProjectVersion.txt` |
| Render pipeline | URP 17.3 (`com.unity.render-pipelines.universal`) |
| Input | Input System 1.18 (`activeInputHandler: 1` — new system only) |
| Product / company | Jannah Garden / Amal |
| Android bundle id | `com.Amal.JannahGarden` (standalone test builds) |
| Android min SDK | 25 |
| Primary target | Android (iOS supported), mobile-first, 60 FPS cap |
| Host app | Amal Flutter app, `com.ibadah.amal` |

---

## 2. Day-one setup

1. **Install Unity 6000.3.10f1** via Unity Hub. Not a newer patch — the scene and prefab files are
   serialized by this version and an upgrade will dirty thousands of files.
2. **Clone the repo** and open the project folder. First import takes a long while (large Synty /
   ToonScapes art sets, terrain, video assets). Let it finish; do not interrupt it.
3. **Open `Assets/Scenes/Init Scene.unity`** and press Play. That is the real entry point.
4. Build-settings scene list is exactly three scenes, in this order:
   - `Assets/Scenes/Init Scene.unity`
   - `Assets/Scenes/Jannah Garden.unity`
   - `Assets/Scenes/Outer Garden.unity`
5. Skim the root `*.md` guides (section 10) — they are the project's accumulated institutional memory.

### Things that will trip you up
- `Library/`, `Temp/`, `Logs/`, `UserSettings/` are gitignored — never commit them.
- Many folder and file names contain **spaces** (`Assets/Scripts/Audio Manager/`, `Jannah Garden.unity`).
  Quote your paths in any script or shell command.
- `.csproj` / `.slnx` files at the root are Unity-generated. Ignore them; don't hand-edit.
- Running `Jannah Garden.unity` directly (not through Init Scene) works, but Flutter data will be
  missing. Most systems have an editor fallback for exactly this — see section 5.

---

## 3. Scene flow

```
Init Scene ──(LoadingScreenManager)──► Jannah Garden ◄──► Outer Garden
   │                                        │
   └─ bootstraps persistent singletons      └─ the main playable garden
```

- **Init Scene** — boots the game. Holds the `LoadingScreenManager` instance with `isInitScene = true`,
  which auto-loads `sceneToLoadOnInit` (default `"Jannah Garden"`).
- **Jannah Garden** — the player's own garden. Shop, placement grid, treasure boxes, XP, tutorial,
  photo mode, minimap. This is where ~90% of the work happens. The scene is large; expect slow saves.
- **Outer Garden** — a shared/social space, loaded through Addressables as a **remote** scene
  (so it isn't in the APK). Shows fellowship profile cards pushed from Flutter.

Scene loads go through [`LoadingScreenManager`](Assets/Scripts/LoadingScreenManager.cs) — never call
`SceneManager.LoadScene` directly:

```csharp
LoadingScreenManager.Instance.LoadScene("Outer Garden");
```

[`SceneLoader`](Assets/Scripts/SceneLoader.cs) is the inspector-friendly wrapper that wires buttons to
scene names and routes them through the loading screen.

---

## 4. Code layout

Everything lives under `Assets/Scripts/`. Runtime code is mostly in the **global namespace**
(no `JannahGarden.*`), with `FlutterIntegration` as the main exception.

| Folder | What's in it |
|---|---|
| `Ads/` | `AdsManager` — in-game ad request surface |
| `Audio Manager/` | `AudioManager`, `GameSfx`, `ButtonClickSfx` — SFX/music library and playback |
| `Economy/` | Noor Coins, inventory, IAP, daily offers, coin store popup |
| `Environment/` | `CloudBank` — procedural sky/cloud volume |
| `Fellowship Vissualization/` | Fellow profile cards spawned around the map *(note the typo in the folder name — it is load-bearing in paths)* |
| `Fencing/` | `FenceBarrier` — soft world boundaries |
| `FlutterIntegration/` | **The bridge.** `FlutterBridge`, `FlutterMessage`, `FlutterDataModels` |
| `Localization/` | 4-language support + HarfBuzz text shaping (Arabic/Bengali) |
| `Minimap/` | Minimap render + fade behaviour |
| `Native/` | Android/iOS native photo save & share |
| `Navigation/` | `GardenPathfinder`, `PlayerAutoWalker` — tap-to-walk |
| `Placement/` | `GardenGrid`, `ItemFootprint`, `PlacementGridView`, and `Areas/` (unlockable zones) |
| `Question Tasks/` | Dhikr counter, MCQ quiz, question-mark orbs, reward finale |
| `Services/` | `IAdService` abstraction, `LevelPlayAdService`, `AdServiceBootstrap`, **`MONETIZATION.md`** |
| `TreasureBox/` | Daily reward system: manager, tiers, save data, UI, mobile notifications |
| `Tutorial/` | `GameOnboardingManager` — the full first-run flow |
| `XP System/` | `PlayerXPManager` — levels, XP chart, area unlock gating |
| `Utility/` | `FPSCounter` |

### The managers you will meet first

| Script | Role |
|---|---|
| [`ItemPlacementManager`](Assets/Scripts/ItemPlacementManager.cs) (~1970 lines) | The heart of the game. Placing, moving, removing, saving and cloud-merging every placed item. |
| [`InGameShopManager`](Assets/Scripts/InGameShopManager.cs) (~1280 lines) | Shop UI, category tabs, tiers, buy / watch-ad flows. |
| [`TreasureBoxManager`](Assets/Scripts/TreasureBox/TreasureBoxManager.cs) (~1790 lines) | 4 tiers × 3 slots = 12 boxes per daily cycle, strict tier progression, ad-gated opens for free users. |
| [`GameOnboardingManager`](Assets/Scripts/Tutorial/GameOnboardingManager.cs) (~1530 lines) | Four sequential tutorial flows; every HUD button starts hidden and is revealed as earned. |
| [`LoadingScreenManager`](Assets/Scripts/LoadingScreenManager.cs) (~1230 lines) | `DontDestroyOnLoad` loading screen, UI built entirely at runtime. |
| [`JannahGardenManager`](Assets/Scripts/JannahGardenManager.cs) | Scene-level manager for the Jannah Garden scene; also sets the 60 FPS cap before the first scene loads. |
| [`GardenGrid`](Assets/Scripts/Placement/GardenGrid.cs) | The lattice items snap to, anchored to the Terrain transform so two devices agree on cell boundaries. |
| [`PlayerXPManager`](Assets/Scripts/XP%20System/PlayerXPManager.cs) | XP, levels, the XP gain chart. |
| [`NoorCoinManager`](Assets/Scripts/Economy/NoorCoinManager.cs) | Coin balance; Flutter's balance is authoritative when supplied. |
| [`LocalizationManager`](Assets/Scripts/Localization/LocalizationManager.cs) | Active locale + every UI string. |

### House patterns
- **Singletons everywhere**: `public static X Instance { get; private set; }`, duplicate-guarded in
  `Awake`. Some are scene-scoped (`JannahGardenManager`), some `DontDestroyOnLoad`.
- **Self-bootstrapping** via `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`
  — `FlutterBridge` and `LocalizationManager` create themselves, so you never place them per scene.
- **Static events** for cross-system comms (`NoorCoinManager.OnBalanceChanged`,
  `FlutterBridge.OnGardenStateReceived`, …). Static so listeners can subscribe before the producer
  exists and survive scene loads. **Always unsubscribe in `OnDestroy`.**
- **Last-payload caching**: Flutter data often arrives before the scene that needs it, so the bridge
  caches the latest of each payload. Consumers read the cache in `Start` *and* subscribe for updates.
  Copy this pattern for any new Flutter data.
- **Heavily commented**: the XML doc comments on these classes explain *why*, not just what. Read them
  before changing behaviour — most of them record a bug that was already paid for once.
- DOTween (`DG.Tweening`) is the animation library; `DOTWEEN` is a defined scripting symbol.

---

## 5. The Flutter bridge (read this twice)

[`Assets/Scripts/FlutterIntegration/FlutterBridge.cs`](Assets/Scripts/FlutterIntegration/FlutterBridge.cs)
is the single entry point for all Flutter ↔ Unity traffic.

Hard constraints:
- Flutter addresses Unity **by GameObject name**. The host object must be named exactly
  `"Flutter Bridge"` and the entry method must stay `ReceiveMessageFromFlutter`. Renaming either one
  breaks the Flutter side **silently**. Both are enforced in code — don't fight it.
- The bridge is scene-independent and self-bootstrapping. A copy left in a scene defers to the
  bootstrapped one.
- Messages are structured JSON commands (see `FlutterMessage.cs` / `FlutterDataModels.cs`).

What comes *in* from Flutter: user profile, authoritative coin balance, fellowship roster, cloud garden
state, locale (`SET_LOCALE`), rewarded-ad results and availability, ad-SDK init state, photo
save/share results.

What goes *out* to Flutter: exit-to-app, subscribe request, ad-SDK state request, photo share, garden
state saves.

**Editor fallbacks.** Running a scene in the editor means no Flutter, so systems fall back to local
data: `Resources/fellow_profiles.txt` for the fellowship roster, local `SaveSystem` files for coins and
garden state. That's by design — if something works in the editor but not in the merged build,
suspect the bridge first.

Full detail lives in [`flutter_bridge_guide.md`](flutter_bridge_guide.md).

---

## 6. Saving and the cloud

- [`SaveSystem`](Assets/Scripts/SaveSystem.cs) — a tiny static API:
  `Save<T>(key, data)`, `Load<T>(key)`, `Exists(key)`, delete. One `BinaryFormatter` `.dat` file per
  key under `Application.persistentDataPath`. Data classes must be `[System.Serializable]`.
- The garden itself is saved as `SaveStateCollection` (a list of `PlacedItemSaveData` + a close
  timestamp + a `revision` counter). Flutter also stores a copy in Firebase; on load,
  `ItemPlacementManager.IsCloudStateNewer` decides whether the cloud copy wins. The `revision`
  counter exists only to break ties when two devices report the same save second.
- Placed items are resolved back to their `ShopItemData` / `TreasureBoxRewardItemData` asset by
  `sourceItemId` + `sourceKind`, **not** by prefab name. Keep item IDs stable forever; changing one
  orphans every garden that contains it.
- To wipe local state while testing: **Tools ▸ Data ▸ Clear All Saved Data**, and
  **Tools ▸ Data ▸ Reset Onboarding Tutorial**.

---

## 7. Content & data authoring

Game content is ScriptableObject + `Resources` data, generated and maintained by editor tools rather
than hand-edited.

- `Assets/Resources/ShopItemsData.asset` — the shop catalogue (`ShopItemData` entries).
- `Assets/Resources/TreasureBoxRewardItemsData.asset`, `Resources/TreasureBoxData/` — reward tables.
- `Assets/Resources/Localization/ui_{en,ar,bn,ur}.json` — UI strings, as key/value arrays
  (`JsonUtility` can't deserialize a bare dictionary).
- `Assets/Resources/questions_{locale}.txt`, `dhikrs_{locale}.txt` — question-task content.
- `Assets/Prefabs/Shop Items/`, `Assets/Prefabs/Treasurebox Reward Items/` — the placeable prefabs.

### Editor tools — the `Tools` and `Jannah` menus
This project leans hard on custom editor tooling (`Assets/Editor/`). Learn the menus before writing a
one-off script; the job is probably already automated:

- **Tools ▸ Shop** — generate shop item data / prefabs / materials, assign IDs, assign unlock levels,
  rebuild category tabs, prefab pivot fixer.
- **Tools ▸ Localization** — Export CSV, Import CSV (translator round-trip), Validate Keys,
  Preview Locale ▸ English / Arabic / Bengali / Urdu.
- **Tools ▸ Jannah Garden ▸ Areas** — bake the area map, export it as PNG, merge areas.
- **Tools ▸ Jannah Garden ▸ Foliage** — scatter by region, clear, export region map.
- **Tools ▸ Performance** — Foliage Culler, Scene Asset Culler, Scene Texture & Material Optimizer.
- **Tools ▸ Addressables** — build content / build and deploy for the active platform.
- **Tools ▸ Build** — Android APK (ARM64-only device test, or all architectures).
- **Tools ▸ HarfBuzz** — glyph coverage scan, Arabic/Bengali smoke tests.
- **Tools ▸ Data** — clear saves, reset tutorial.
- **Jannah ▸ Audio** — populate / re-apply the sound library, report unmapped effects.
- **Jannah ▸ UI Kit** — restyle the active scene or UI prefabs (preview first, then apply).

---

## 8. Localization & text shaping

Four locales: **en, ar, bn, ur** (`AppLocale` enum). Two of them are non-Latin scripts that Unity's
text stack cannot shape correctly on its own, so the project ships a **HarfBuzz** integration
(`HarfBuzzNative`, `HarfBuzzShaper`, `ShapedTextGraphic`, `ArabicTextShaper`, `BengaliTextShaper`,
`ScriptRunSplitter`, `HarfBuzzFontRegistry`).

Rules of thumb:
- Never hardcode a user-facing string. Add a key to the CSV, import it, and use `LocalizedText` /
  `LocalizationManager`.
- Run **Tools ▸ Localization ▸ Validate Keys** before you push UI work.
- Arabic and Urdu are RTL — check layouts in all four locales with the Preview Locale menu.
- Adding glyphs? Run **Tools ▸ HarfBuzz ▸ Scan And Fill Glyph Coverage**.
- Locale changes arrive from Flutter via `SET_LOCALE` — see [`flutter_language_guide.md`](flutter_language_guide.md).

---

## 9. Monetization & ads

Read [`Assets/Scripts/Services/MONETIZATION.md`](Assets/Scripts/Services/MONETIZATION.md) in full
before touching anything ad-related. The short version:

- Mediation is **Unity LevelPlay** (`com.unity.services.levelplay` 9.5.1). It is the only ad SDK;
  Unity Ads and InMobi are LevelPlay *adapters*, never imported directly.
- **The Flutter host calls `LevelPlay.Init`, not the game.** LevelPlay is a per-process singleton and
  the host owns the consent dialog. The game never inits and never sets consent.
- The game shows ads on **its own ad unit** under the host's app key. Never put the host's ad unit ID
  in the game — two rewarded objects on one unit in one process fight over the same fill.
- Three rewarded surfaces (treasure boxes, watch-an-ad shop items, interaction prompt) all go through
  `IAdService` via `AdServiceBootstrap.Service`. Add new surfaces the same way; don't call LevelPlay
  directly.
- There's a 10-second handshake fallback: if the host doesn't answer `REQUEST_AD_SDK_STATE`, the game
  builds the rewarded ad anyway. If ads are missing in the merged app, check this handshake first and
  grep logcat for `[LevelPlayAdService]`.
- IAP / subscription prompts hand off to Flutter — see [`flutter_subscribe_guide.md`](flutter_subscribe_guide.md).

---

## 10. Addressables & remote content

The APK can't hold everything, so heavy content ships remotely.

- Groups: `Default Local Group`, **`Remote Item Prefabs`**, **`Remote Scenes`**
  (`Assets/AddressableAssetsData/AssetGroups/`).
- `Outer Garden` is a remote scene (**Tools ▸ Addressables ▸ Move Outer Garden Scene To Remote Group**).
- Remote bundles are built into `ServerData/<BuildTarget>/` and uploaded to **Cloudflare R2** by
  [`deploy-r2.ps1`](deploy-r2.ps1), called automatically by `AddressablesBatchBuild.Deploy()` before
  every Android/iOS player build. (`firebase.json` is the older Firebase Hosting path, kept around.)
- The upload uses `rclone copy` — additive, never deletes. Bundle names carry a content hash, so
  shipped app versions keep resolving the bundles they were built against. **Do not use `-Prune`**
  unless you are certain every app version referencing the old bundles has rolled off.
- `AddressablesPreBuildHook` / `AddressablesPreprocessBuildWithReport` wire the content build into the
  player build, so a normal build does the right thing.
- Runtime loads go through [`AddressableItemLoader`](Assets/Scripts/AddressableItemLoader.cs).

---

## 11. Performance — the constraints that shaped this codebase

It's a big 3D world on mid-range Android phones. Memory is the recurring enemy; the game was being
OS-killed minutes into a session on both Android and iOS until the fixes in
[`oom_crash_fix_guide.md`](oom_crash_fix_guide.md) landed. **Read that guide** — it explains the
texture, culling and streaming rules you're expected to keep.

In play:
- 60 FPS cap with vSync off (`JannahGardenManager.ConfigureFrameRate`).
- [`OcclusionCullingManager`](Assets/Scripts/OcclusionCullingManager.cs) + `OcclusionCullingZone`.
- [`MemoryPressureResponder`](Assets/Scripts/MemoryPressureResponder.cs) — reacts to OS memory warnings.
- [`Objectpool`](Assets/Scripts/Objectpool.cs) for churned objects.
- Editor optimizers: Foliage Culler, Scene Asset Culler, Scene Texture & Material Optimizer.
- `link.xml` preserves types from IL2CPP code stripping — if something works in the editor but
  null-refs in a build, check here.
- `FPSCounter` for quick on-device checks.

---

## 12. Building

**Editor path (easiest):** **Tools ▸ Build ▸ Android APK (ARM64 only, device test)** for a quick device
build, or **(all architectures)** for a release-shaped one. Both run through
[`Assets/Editor/AndroidApkBuilder.cs`](Assets/Editor/AndroidApkBuilder.cs) and trigger the Addressables
content build + R2 deploy first.

Notes:
- Android scripting backend is IL2CPP; builds are slow. Expect it.
- `google-services.json` at `Assets/` root is Firebase config for the standalone build.
- The real shipped artifact is built by the **Flutter** side, with this project embedded as a module.
  A standalone APK is a test harness, not the product. Always verify ads, coins, locale and cloud save
  in a **merged Flutter build** before calling a feature done.
- Scripting define symbols currently set for Android: `DOTWEEN`, `SENTIS_ANALYTICS_ENABLED`,
  `APP_UI_EDITOR_ONLY`, `LEVELPLAY_DEPENDENCIES_INSTALLED`, `LEVELPLAY_ENABLED`.

---

## 13. Reference guides in the repo root

These were written as problems got solved. They are the fastest way to understand a subsystem's history.

| File | What it covers |
|---|---|
| [`flutter_bridge_guide.md`](flutter_bridge_guide.md) | The bridge protocol, from the Flutter dev's side |
| [`flutter_integration_guide.md`](flutter_integration_guide.md) | Setting up the Flutter app for the JSON bridge |
| [`Flutter_Integration_Instructions.md`](Flutter_Integration_Instructions.md) | Noor Coins over `flutter_embed_unity` |
| [`flutter_language_guide.md`](flutter_language_guide.md) | `SET_LOCALE` language switching |
| [`flutter_subscribe_guide.md`](flutter_subscribe_guide.md) | `REQUEST_SUBSCRIBE` → subscription flow |
| [`flutter_ios_and_ads_guide.md`](flutter_ios_and_ads_guide.md) | iOS release + ads requirements on the Flutter side |
| [`fellowship_flutter_integration_guide.md`](fellowship_flutter_integration_guide.md) | Fellowship cards data contract |
| [`oom_crash_fix_guide.md`](oom_crash_fix_guide.md) | The out-of-memory crash fixes — **required reading** |
| [`Assets/Scripts/Services/MONETIZATION.md`](Assets/Scripts/Services/MONETIZATION.md) | Rewarded ads architecture |
| [`ShopSystemPrompt.md`](ShopSystemPrompt.md) | Spec of the custom shop system |

---

## 14. Working conventions

- Branch is `main`. Commit scene/prefab changes deliberately — Unity YAML files are huge and conflict
  badly. **Coordinate before editing `Jannah Garden.unity`**; two people in that scene at once is a
  guaranteed painful merge.
- Prefer editing **prefabs** over the scene where you can; it keeps diffs small and reviewable.
- Always commit the `.meta` file alongside any asset you add, and never regenerate GUIDs.
- Keep inspector fields `[SerializeField] private` with a `[Tooltip]`, grouped under `[Header]` — match
  the surrounding style. Use `[FormerlySerializedAs]` when renaming a serialized field so existing
  scene wiring survives.
- Match the comment density you find. These files explain *why*; keep that up.
- Don't add a new singleton or a new static event unless the existing pattern genuinely doesn't fit.
- There is no automated test suite for game code (`Tests.csproj` is effectively empty; the only tests
  are inside the `OpenFracture` plugin). Verification is manual: editor play-through, then a device
  build, then a merged Flutter build.

---

## 15. Suggested first week

1. **Day 1** — Open Init Scene, play through to Jannah Garden. Walk around, buy something, place it,
   open a treasure box, switch language with the Preview Locale menu. Just play it.
2. **Day 2** — Read `FlutterBridge.cs` end to end, then `flutter_bridge_guide.md`. Draw yourself the
   message flow. This unlocks everything else.
3. **Day 3** — Read `GardenGrid.cs`, then follow one item from `InGameShopManager` purchase →
   `ItemPlacementManager` placement → `SaveSystem` save → reload. That's the core loop.
4. **Day 4** — Read `MONETIZATION.md` and `oom_crash_fix_guide.md`. These are the two areas where a
   well-meant change does the most damage.
5. **Day 5** — Pick a small UI or localization ticket. Make the change, validate localization keys,
   test in all four locales, and get a device build onto a phone.

Ask about anything that looks arbitrary — it usually isn't, and the reason is usually worth knowing.
