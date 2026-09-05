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
