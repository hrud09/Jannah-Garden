# OOM Crash Fix — What Changed (2026-09-04)

The game was getting killed by the OS a few minutes into a session, on **both real Android devices and
iOS** — not a Unity exception, no error screen, just the process disappearing. This doc covers only this
fix: what changed, why, and what (if anything) needs to happen on the Flutter side.

---

## Root cause

Every shop/treasure-box item's 3D prefab is a Remote Addressable — it downloads the first time a player
previews or places it. `AddressableItemLoader` cached each one **for the rest of the process's life** and
never freed it. That was fine when it was written; it stopped being safe once the item meshes got as heavy
as they are now — Meshy AI output runs roughly 30 MB per bundle even after compression. Browsing, buying,
or returning even a handful of different items in one sitting could pull several hundred MB of mesh/texture
data into memory that was never released.

That's on top of the scene itself, **and on top of the fact this isn't a standalone Unity app** —
`flutter_embed_unity` runs the Flutter/Impeller engine and the Unity engine in the same OS process, so
they're competing for the same memory ceiling. On a mid-range device that combination could exceed the
OS's per-app memory allowance a few minutes in, and the OS silently kills the process.

---

## What changed (Unity side — done, cross-platform, no Flutter-side code involved)

**[`Assets/Scripts/AddressableItemLoader.cs`](Assets/Scripts/AddressableItemLoader.cs)**
Now tracks each load's `AsyncOperationHandle` and last-used time, and exposes:
```csharp
public static void TrimCache(int maxEntries, ISet<string> keepKeys)
```
which releases the least-recently-used cached prefabs down to `maxEntries`, skipping anything in
`keepKeys`. Releasing a handle is safe even for prefabs already instantiated (items standing in the
garden, or pooled inactive clones) — Addressables only unloads the template, not objects already spawned
from it; the only cost is that instance's item type re-downloads/reloads on next use instead of hitting
the cache.

**[`Assets/Scripts/ItemPlacementManager.cs`](Assets/Scripts/ItemPlacementManager.cs)**
- New Inspector field `maxCachedUnusedItemPrefabs` (default **6**) — how many downloaded-but-unused item
  prefabs stay cached at once.
- `ComputeInUseAddressableKeys()` — protects every prefab actually standing in the garden, plus whatever
  the current placement is mid-download/mid-carry with, from eviction.
- `TrimAddressableCache()` is called after every point where an item's prefab could become unused:
  placing, cancelling, returning to store, rebuilding the garden on load.
- **New:** subscribes to `Application.lowMemory` — the one event Unity surfaces for both iOS's
  `didReceiveMemoryWarning` and Android's `onTrimMemory`. On that signal it immediately releases *every*
  unused item prefab (cap of 0, not 6) and calls `Resources.UnloadUnusedAssets()`, rather than waiting for
  the next placement/return. This is the part that matters most for iOS — see below.

None of this requires anything from the Flutter side. It ships automatically the next time
`android/unityLibrary` (and the iOS equivalent) gets re-exported from this project.

---

## What's needed on the Flutter side

### Android — one attribute, already applied

`android/app/src/main/AndroidManifest.xml`, on the `<application>` tag:

```xml
<application
    android:label="Amal"
    android:name="${applicationName}"
    android:icon="@mipmap/launcher_icon"
    android:extractNativeLibs="true"
    android:largeHeap="true">   <!-- ← added -->
```

This raises the memory ceiling Android grants the process — a mitigation, not the whole fix on its own,
since it only affects the Java/ART heap, not Unity's native mesh/texture memory (which is what the Unity
change above actually addresses). Already applied locally in the `amal` checkout on this machine
(`android/app/src/main/AndroidManifest.xml`); not pushed, since that's not this repo — hand this diff to
whoever owns that project.

### iOS — nothing needed

There is no iOS equivalent of `largeHeap`. iOS gives every app a hard, device-dependent memory ceiling
(roughly 1–3 GB depending on the model, less on older/cheaper phones) with no manifest flag, entitlement,
or Info.plist key that raises it. So on iOS, **the Unity-side fix above is the entire fix** — specifically
the new `Application.lowMemory` handler, since that's the only lever available once the OS is already
unhappy and there's no "ask for more headroom" option.

---

## How to verify

**Android** — install a release build, play past 5 minutes (actively browse/place/return several
different shop items — that's the pattern that triggered it, not sitting idle), and watch for a kill
signal:
```bash
adb logcat -c && adb logcat | grep -iE "lowmemorykiller|OutOfMemory|Unity.*Fatal"
```
A `lowmemorykiller` line naming this app's process confirms it was memory pressure; no output and the app
still running at 10+ minutes means the fix is holding.

**iOS** — same play pattern, then check for a memory kill rather than a real crash:
- Xcode → **Window → Organizer → Crashes** (for a TestFlight build), or
- on the device: **Settings → Privacy & Security → Analytics & Improvements → Analytics Data**, look for a
  report named after this app/`Runner`.

A **Jetsam** / `EXC_RESOURCE (RESOURCE_TYPE_MEMORY)` entry confirms memory pressure. Testing on an older or
lower-RAM iPhone (e.g. SE-class) is the most convincing check, since iOS's ceiling is strictest there and
has no override.

---

## Rollout

Both the Android manifest change and the Unity fix need to ship together — the manifest change alone barely
moves the needle without the Unity-side cache bound, and the Unity fix alone (already the whole story on
iOS) is still meaningfully better with `largeHeap` stacked on top on Android. Requires a fresh
`android/unityLibrary` (and iOS) export from this project before either side's build actually contains the
fix — editing the Flutter manifest alone does nothing until that export lands.

---

# Follow-up — Outer Garden crashes (2026-10-21)

Players were still getting killed, but now specifically after **spending a while in Outer Garden**. The
2026-09-04 fix above was correct; it was just scoped to the wrong place.

## Root cause

Everything that fix added lived on `ItemPlacementManager`, which exists **only in the Jannah Garden
scene** (verified by GUID: 1 occurrence in `Jannah Garden.unity`, 0 in `Outer Garden.unity` and
`Init Scene.unity`). Outer Garden loads `LoadSceneMode.Single`, so that manager is destroyed on the way
in and its `OnDestroy` unsubscribes the handler. For the whole of an Outer Garden visit:

* **Nothing was subscribed to `Application.lowMemory` anywhere in the project.** iOS's
  `didReceiveMemoryWarning` and Android's `onTrimMemory` fired into a void — in the one scene where the
  guide above calls that handler "the only lever available once the OS is already unhappy".
* **Up to ~180 MB of shop prefabs stayed pinned.** `AddressableItemLoader` is a `static` class, so its
  cache survives the scene change; the only code that could evict it (`TrimAddressableCache`) went with
  the manager. Nothing trimmed before leaving Jannah Garden either, so whatever the player browsed in
  the shop was carried in and held.
* **`Resources.UnloadUnusedAssets()` was never called** — its single call site was inside that same dead
  handler.

Meanwhile Outer Garden is the heaviest thing the process loads: a 26 MB scene file, a ~150 MB remote
bundle, ~18,700 foliage instances, and `OcclusionCullingManager` activates every mesh at load and never
deactivates any. Steady state was ~150 MB of scene + up to ~180 MB of pinned prefabs + the Flutter engine,
with no ability to respond to pressure. Hence the time dependency: the process sits just under the
ceiling, warnings accumulate, all are ignored, and the kernel eventually kills it.

## What changed (Unity side — no Flutter-side work)

**`Assets/Scripts/MemoryPressureResponder.cs`** (new)
`DontDestroyOnLoad` singleton bootstrapped with `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`, the
same pattern as `FlutterBridge`, so the handler is live in every scene regardless of which one the game
boots into. Owns `Application.lowMemory` now. Two entry points:
* `ReleaseUnusedItemPrefabs(maxEntries)` — trims to `maxEntries` beyond what is genuinely in use, clears
  `ItemFootprint`, then `Resources.UnloadUnusedAssets()`. This is what the OS warning calls, with 0.
* `ReleaseAllItemPrefabsForSceneExit()` — releases *everything*, ignoring the keep-set, for the moment
  before a Single-mode transition (the items the keep-set would protect are about to be destroyed by
  that very load). Skips the unload pass, since a Single-mode load runs its own.

It deliberately does not require `ItemPlacementManager`: with no garden loaded the keep-set is empty,
which is correct — nothing outside that scene holds a shop prefab.

**`Assets/Scripts/ItemPlacementManager.cs`** — gave up `Application.lowMemory` and `HandleLowMemory`;
`ComputeInUseAddressableKeys()` is now `public` so the responder can protect in-use prefabs. The
placement/cancel/return/rebuild trims are unchanged.

**`Assets/Scripts/JannahGardenManager.cs`** — `LoadOuterGarden()` now calls
`ReleaseAllItemPrefabsForSceneExit()` before starting the load, so the garden's cached item prefabs are
not carried into the heaviest scene in the game.

**`Assets/Scripts/LoadingScreenManager.cs`**
* Added `yield return Resources.UnloadUnusedAssets()` after every transition, behind the still-opaque
  panel. Unity's own unload pass runs at the scene swap — *before* the outgoing Addressable bundle is
  released — so a transition's worth of assets was otherwise staying resident until something else
  happened to trigger one. Yielded on, not called bare, so there is no synchronous stall.
* A **failed** addressable scene load now releases its handle instead of leaking the partial bundle and
  storing a dead handle as `_activeAddressableSceneHandle` (which would then have had
  `UnloadSceneAsync` called on an operation that never produced a scene).

**`Assets/Scripts/Objectpool.cs`** — this is `DontDestroyOnLoad` but its pooled clones are not, so its
two maps accumulated an entry per clone per scene change for the life of the process. Now prunes on
non-additive `sceneLoaded`. The reverse lookup is keyed by `GameObject` instead of `int` instance ID so
a destroyed clone can actually be recognised as destroyed — an `int` cannot be resolved back to an
object to null-check, and pruning by "not currently in a pool queue" would have wrongly evicted live
clones parented under a `DontDestroyOnLoad` object.

## How to verify

Same play pattern as above, but **stay in Outer Garden for 10+ minutes** rather than browsing the shop.
The signal to look for is the handler actually firing now:
```bash
adb logcat -c && adb logcat | grep -iE "lowmemorykiller|onTrimMemory|MemoryPressureResponder"
```
Before the fix: OS trim warnings with no `[MemoryPressureResponder]` line in response. After: each
warning is answered by `[MemoryPressureResponder] OS low-memory warning — releasing every unused item
prefab.` and the process survives. A `lowmemorykiller` line naming the app still means it was killed.

As before, this needs a fresh `android/unityLibrary` (and iOS) export before either side's build
contains it.
