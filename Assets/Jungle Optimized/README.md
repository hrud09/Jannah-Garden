# Jungle Optimized (Android)

A mobile-optimized copy of the jungle (`Environment/Jungle`): the same layout and look, built from new assets
in this folder. **The source packs (Stylized Nature Bundle, Synty, ToonScapes) are not modified.**

## Put it in the main scene (`Assets/Scenes/Jannah Garden.unity`)

1. Open `Jannah Garden.unity`.
2. **Tools > Jungle Optimized > Swap Into Open Scene**.
   This places `Jungle (Optimized).prefab` next to `Environment/Jungle` at the same transform and **disables**
   the old jungle (it is not deleted, so you can compare and roll back).
3. Press Play and check it. When you are happy, delete the old disabled `Jungle` object and save the scene.

To do it by hand instead: drag `Jungle (Optimized).prefab` under `Environment` at position/rotation 0 and
disable or delete `Environment/Jungle`. The prefab is laid out in world space on the shared terrain
(`Jannah Garden Terrain Data`).

## What's in the folder

| Path | What it is |
|---|---|
| `Jungle (Optimized).prefab` | The whole jungle, ready to drop in. Same group names as the original. |
| `Prefabs/Trees, Rocks, Mushrooms, Lanterns, Props, FX` | One optimized standalone prefab per source prefab the jungle used. Use these when hand-placing more jungle. |
| `Meshes/*Chunks.asset` | Undergrowth baked into 16 m chunks (one draw per chunk instead of one per plant). |
| `Materials/*_Mobile.mat` | Mobile copies of the SNB materials (`_Combined_Mobile` = for the chunks). |
| `Shaders/SNB_Nature_URP_Mobile.shader` | Mobile version of `JannahGarden/SNB_Nature_URP`, same look and wind. |
| `Scripts/MobileEnvironmentOptimizer.cs` | Runtime-only mobile layer (see below). |
| `Editor/JungleOptimizedBuilder.cs` | The tool that generates all of the above. |
| `Showcase/` | Before/after demo: split screen, live counters, benchmark and HTML report (see below). |

## What was optimized

- **Undergrowth** (`Undergrowth`, `Dense Forest Undergrowth`): 1,751 plant renderers baked into 138 chunk meshes.
  Each chunk culls as a whole at ~50 m and casts no shadows. The plants' own height is stored in UV2, so the
  shader's height gradient still works after merging.
- **Trees**: no LOD cross-fade, cull distance ~115 m on the Mobile tier (was effectively unlimited). Only the first
  two LOD levels cast shadows. The heavy Synty trees (`SM_Env_Tree_Large/Medium`, 7k–23k triangles) skip their
  LOD0, so LOD1 is the closest level.
- **Rocks / mushrooms / lanterns / props**: no shadows, cull at ~30–45 m.
- **Particles** (fireflies, falling leaves): pause when off-screen instead of always simulating; particle pool
  capped to what the emitter actually uses.
- **Materials / shader**: SNB materials use the mobile shader. It has fewer variants (no SSAO, additional-light
  shadows or screen-space shadows) and skips normal mapping where a material has no normal map. Synty and ToonScapes
  materials are unchanged.
- **Colliders**: unchanged (the packs already use simplified collision meshes).
- **Memory trade-off:** merged chunks can't share one mesh the way the separate plants did, so the undergrowth now
  uses ~6.8 MB of GPU mesh memory (the shared plant meshes were 0.2 MB). To keep that down, chunks use a compact
  40-byte vertex format (half-float normals/UVs, 8-bit colours) instead of Unity's default 72 bytes. They're also
  saved non-readable (no extra CPU copy in the player), which matters because the game shares its process with Flutter.

Cull distances assume the **Mobile** quality level (LOD bias 0.6). On PC (higher LOD bias) everything shows further away.

Measured in the editor on the Mobile tier, standing inside the jungle, compared with the original jungle:

| | Original | Optimized |
|---|---|---|
| Visible renderers | 914 | 319 |
| Main-view draw calls | 1,089 | 496 |
| Shadow draw calls | 635 | 147 |
| Realtime point lights on | 22 | 4 |

## Runtime: `Mobile Environment Optimizer` (child of the prefab)

Only active on mobile, or when the "Mobile" quality level is active. It changes nothing at edit time and undoes
everything if disabled. It:

- keeps only the 4 nearest lantern point lights on (of 22);
- pauses far-away ambient particles;
- distance-culls the jungle's small/medium props;
- pulls the camera far clip to 125 m and the linear fog to 35–120 m, so the tree cull edge fades into fog
  (turn off **Override Camera And Fog** if the scene should own its fog);
- optional: drag the scene's minimap camera and `MinimapBehaviour` into it to render the minimap every other
  frame, and add other decorative roots (e.g. `Environment/Scattered Foliage`) to **Cull Roots**.

Set **Apply Mode = Always** to preview the mobile result in the editor.

## Showing the result: Optimization Showcase (`Showcase/`)

A before/after demo for presenting the work. It needs a scene with **both** the original jungle and the optimized
one, so use a demo scene (e.g. `Assets/Scenes/Zaman/Jannah Garden shop asset Zaman v_2.unity`), not the shipped game scene.

1. Open the scene and run **Tools > Jungle Optimized > Set Up Showcase In Open Scene**. This:
   - adds the optimized prefab (disabled) next to the original;
   - adds an `Optimization Showcase (demo)` object with three camera viewpoints: inside the jungle, the lantern
     path, and the garden edge;
   - keeps any scene-level `Mobile Environment Optimizer` switched off, so the original is shown as it really was.
2. Press **Play** (click through the game's start screen). A panel appears in the Game view:
   - **Original / Optimized**: switch the whole jungle. The live counters (FPS, draw calls, triangles,
     shadow casters, objects rendered, lantern lights) update immediately.
   - **Split**: the original on the left of the line and the optimized jungle on the right, from the same camera
     in the same frame. Drag the line.
   - **View**: jump between the saved viewpoints and the free camera.
   - **Benchmark**: measures both versions at every viewpoint, in the order original, optimized, optimized,
     original, so changes over time cancel out.
   - **Full Report**: runs the benchmark, renders same-view screenshots of both versions (3D only, no UI) with a
     split image and a pixel-difference image, and writes `Report.html`. The report opens in the browser and
     is saved to `<project>/OptimizationShowcase/<date>/`, which is git-ignored.
   - Keys: `1` `2` `3` modes, `V` view, `B` benchmark, `R` report, `H` hide the panel.
3. Add more viewpoints: frame a shot in the Scene view, select the showcase object and click
   **Add viewpoint from Scene view camera**. With the object selected, the inspector also shows a content
   comparison without Play: renderers, triangles, shadow casters, particles and mesh memory.

The demo runs with the **Mobile** quality level, so both versions are judged with the phone settings. To show it on
a phone, add the demo scene to a **Development Build**. Draw-call counters only exist in development builds; FPS
works in any build. On the phone, reports are saved under `Application.persistentDataPath`.

## Rebuilding

If the original jungle changes, open a scene that contains the original `Environment/Jungle` and run
**Tools > Jungle Optimized > Build From Open Scene**. It regenerates every asset in this folder, keeping existing
GUIDs, so scenes that already use the prefab update automatically. Tuning values (cull distances, chunk size,
heavy-LOD threshold) are constants at the top of `Editor/JungleOptimizedBuilder.cs`.

Note: the builder only runs while the original jungle is in the open scene. Keep one copy of it (e.g. in
`Assets/Scenes/Zaman/Jannah Garden shop asset Zaman v_2.unity`) if you want to rebuild later.
