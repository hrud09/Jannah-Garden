# Jannah Garden UI Motion: Handoff for Phase 7 and the remaining work

**Date:** 2026-11-17
**Read with:** `Docs/UI_Animation_Plan.md`, the original per-element plan. Its §2 and §3 also carry a status note for each phase that's done.

## Where things stand

Phases 0 to 6 of the plan are built and were tested in the Unity Editor's Play mode. None of it has been committed, and none of it has run on a phone.

This document covers three things:

1. **Phase 7:** on-device verification on the Pixel 6.
2. **Leftover items** from Phases 1 to 6, plus things nobody has watched run yet.
3. **The rules and APIs** the finished work relies on, so new code fits in.

Do the work in this order:

1. Read "Before you start".
2. Run the Phase 7 checklist and fix what it turns up.
3. Work through the leftover items in priority order.

---

## 1. Before you start

### 1.1 Commit hygiene

- Everything from Phases 0 to 6 is uncommitted on `main`. Commit it before changing anything, so your work can be reviewed on its own.
- **Leave out of every commit:** the two TMP font atlas assets that change on their own during Play mode, `NotoSansArabic-Regular SDF` and `NotoSansBengali-Regular Dynamic SDF`.
- The new files that do belong in a commit:
  - `Assets/Scripts/UI/**`
  - `Assets/Shaders/UI/UIShimmer.shader`
  - `Assets/Resources/UIShimmer.mat`
  - `Assets/Resources/UIMotionTokens.asset`
  - `Assets/2D Assets/UI/UI Elements/jg_noor_coin_fly.png`
  - `Assets/Scripts/XP System/XPBarAnimator.cs`
  - `Assets/Scripts/XP System/LevelUpCelebration.cs`
  - `Docs/`

### 1.2 Project facts that will bite you

- **Engine:** Unity 6000.3.10f1 with URP, a mobile game. The main scene is `Assets/Scenes/Jannah Garden.unity`. DOTween lives in `Plugins/Demigiant`.
- **Line endings:** many scripts use **CRLF line endings and a UTF-8 BOM**, and some have trailing spaces. Match their line endings exactly when you search and replace, or keep them when you rewrite a file.
- **Editing scripts during Play mode** makes Unity recompile mid-session, which wipes every static, so every `Instance` singleton reads null afterwards. **Stop Play mode before editing scripts.**
- **Scene edits and saves** only stick outside Play mode.
- **The "Capture UI Panels" tool** (`Jannah/UI Kit/Capture UI Panels`, which writes to `Temp/ui_shots`) has three blind spots. It renders only the UI layer. It doesn't show the screen-space overlay that the flying icons and sparkle bursts are drawn on. And it hides the panels themselves, so the HUD shot can't show an open dialog. To check motion, use a Play-mode screen recording or the phone.
- **Shaders found by name get stripped from Android builds.** `Shader.Find` returns null for shaders the build stripped; this already broke `UI/ShapedTextSDF` on Android once. Any new custom UI shader must ship through a material in `Resources/` (how the shimmer ships) or through Always Included Shaders.
- **Localized text:** Bengali, Arabic and Urdu go through `LocalizedRendering.SetText`. Bengali text is drawn by a child `ShapedTextGraphic`, so **fade text through a CanvasGroup**, never through `TMP_Text.alpha` or its colour (the child won't follow).
- **Two scripts don't run in this scene during direct Play:**
  - `GardenAreaManager.Instance` is null.
  - `LoadingScreenManager` only exists when the game starts from the Init scene.

---

## 2. The motion system: what exists and how to use it

All of it lives in `Assets/Scripts/UI/Motion/`.

| File | Use it for |
|---|---|
| `UIMotionTokens.cs` plus `Resources/UIMotionTokens.asset` | Every duration, ease and amount in one asset. **Never hard-code timings**; read `UIMotion.Tokens.appear` and friends. Main tokens: `appear` (0.32s OutBack), `appearSoft`, `disappear` (0.18s InBack), `disappearSoft`, `staggerChild`, `popFromScale` 0.85, `punch`, `pressScale` 0.92, `gainFlash`/`spendFlash`, `reducedFade`. |
| `UIMotion.cs` | Static helpers listed below. All run on unscaled time and are linked to their GameObject. |
| `UIPanelMotion.cs` | A component for panels. Presets: Pop, SlideUp, DropIn, SlideFromStart/End (mirrored for RTL), Fade. Optional backdrop and staggered children. API: `Show()`, `Hide(onDone)`. |
| `UIButtonJuice.cs` | Added to every Button and Toggle automatically by `UIButtonJuiceBootstrap`. Gives the press squash, the release spring, a pop when a button becomes interactable, and the toggle checkmark pop. Opt out with the `NoButtonJuice` marker. |
| `UIToggleMotion.cs` | `UIToggleMotion.SetActive(go, bool, delay, slideFrom)`: an animated `SetActive` for small pieces. |
| `UICountTo.cs` | Rolls a number to a new value, with a punch and a colour flash. |
| `UIFloatingText.cs` | "+25"-style text that rises and fades. |
| `UIFlyToTarget.cs` plus `UIRewardHold.cs` | Reward icons that fly to the HUD. **Start the hold before granting the reward**, so the counter waits for the icons. Helpers: `UIFlyToTarget.Coins(amount, fromScreen, delay)`, `.XP(...)`, `.Burst(screenPos, color)` (sparkles) and `.ScreenPoint(rect or world)`. |
| `UIIdle.cs` | Idle effects: Breathe, Bob, Sway, Wiggle, Shimmer. Switch them on and off with `Playing`. It layers on top of other tweens without drifting. **Set its fields before calling `SetEffects`** (the shimmer reads them once). |
| `UIFxSprites.cs` | Built-in procedural sprites: `Glow`, `Sparkle`, `Rays`, `Ring`. |
| `UIHudEntrance.cs` | Slides the HUD in when the loading screen reveals the scene. |

### `UIMotion` helpers

- `Punch(t, mult)`: routes through `UIButtonJuice` when the target is a button. **Always use this rather than `DOPunchScale` on buttons.**
- `PopIn(t, delay, rest)` and `Stamp(t, delay, rest)`.
- `Reveal(t, delay, rest)`: grow, overshoot and ripple, for "something new is here".
- `Ripple(rect, color)`.
- `Flash(graphic, color)`.
- `PanelIn(panel, scale)` and `PanelOut(panel, onDone)`, for dialogs that animate from their own code.
- `SetBackdrop(go, show)`: fades a dim or blur in and out.
- `StaggerDelay(i)`.
- `ReduceMotion`: the player's setting. Every new motion must respect it: swap movement and scaling for a short fade, and turn idle loops off.
- `IsRightToLeft`.

### Rules every new piece of motion follows

1. Use `.SetUpdate(true)` (unscaled time) and `.SetLink(gameObject)`. `Apply(token, link)` does both.
2. **Don't `DOKill` a transform another script also animates.** Buttons are shared with `UIButtonJuice`, so go through `UIMotion.Punch`.
3. Exits take about 60% as long as entrances. Deactivate an object only after its exit finishes.
4. Record the authored rest pose and always return to it, including when a tween is killed. Restore the pose in `OnKill`.
5. Don't move an element's position inside a LayoutGroup. Scale, rotation and CanvasGroup alpha are safe.
6. Fade with CanvasGroup rather than `Graphic.color` (cheaper, and it works with Bengali text).

---

## 3. Phase 7: on-device verification (Pixel 6)

How to build and install is in the project memory notes. In short: the project exports a Gradle project, so use the `Tools/Build` menu, then install over adb. Addressables are hosted on Cloudflare R2; that's needed only if shop prefabs must download during the test.

### 3.1 Performance (Unity Profiler attached to a development build)

- [ ] No frame over 16 ms while the shop opens: the tab drop-in plus the card cascade (`InGameShopManager.CascadeTabs` and `CascadeCards`).
- [ ] No frame over 16 ms while a reward flies (at most 12 icons, see `UIFlyToTarget.MaxIcons`), including a level-up celebration running at the same time.
- [ ] Idle effects cost little with everything visible: shop wiggle, treasure button breathe and shimmer, premium cards shimmering. Watch draw calls: each shimmering Image gets its own material, which breaks batching. If the premium cards cost too much, shimmer only the cards visible in the scroll view.
- [ ] No GC allocation every frame from `UIIdle.LateUpdate`, the joystick, or `LoadingScreenManager.Update` (fill sparkle, tip rotation).

### 3.2 Look and correctness on the phone

- [ ] **Shimmer shader** (`UI/Jannah Shimmer`) renders on device and isn't pink (stripped). Check it on the premium shop cards, the best-value Noor Coin offer, the treasure "watch ad" button and the loading logo.
- [ ] **Flying coins and XP** land on the coin pill and the XP badge at the right spot on the real aspect ratio. The coin target is "Coin Fly Target", a child of the Noor Coin Holder's BG Image at anchor x 0.845.
- [ ] **Overlay canvas:** the fly layer is a screen-space overlay canvas at sorting order 100. Confirm it draws above the game canvases and below nothing that matters (notch and safe area).
- [ ] **Quiz flow:** answer correctly. The panel pops in; on close, coins and XP fly out of the reward chips; the HUD counter ticks once per coin landing; the XP bar fills after its icons land; a level-up shows `LevelUpCelebration`.
- [ ] **Dhikr flow:** same as the quiz, and each tap ripples from the counter.
- [ ] **Treasure box:** open a box through the confirmation panel. The 3D box rattles for 0.35s, opens, and its coins stream from the box to the counter.
- [ ] **Shop:**
  - Coin pack: coins burst from the card.
  - Purchase: the card punches and sparkles.
  - An unaffordable or level-locked tap: the card wobbles and the price flashes.
  - Level up while the shop is closed, then reopen it: the padlock pops off cards that just unlocked.
- [ ] **Noor Coin store popup:** opens and closes with animation, reopening mid-close is safe, and the "you can afford it now" auto-close waits for the coins to land.
- [ ] **Settings:** opens with the dim fading up and the window popping in; closes cleanly; toggle checkmarks pop. This panel was broken before Phase 6, so check it on device.
- [ ] **Toast:** rises and settles; a second toast while one is showing punches in place; toasts hide while the game is paused.
- [ ] **Photo:** the preview "prints" (drops from oversized with a tilt that straightens), closes with an animation, and the save button punches on success.
- [ ] **Onboarding:** start a fresh profile and play the whole tutorial.
  - The dim fades in and out.
  - Instruction text cross-fades.
  - The pointer hand pops in, presses, and sends a ring out from its target.
  - Buttons handed out by the tutorial arrive with the reveal (grow, settle, ring), staggered when several arrive together.
  - The pulse keeps each button at its authored scale; the minimap buttons are authored at 2.77.
  - **None of this has been seen running yet.**
- [ ] **Area unlock:** the card shows rays, a stamped title, a body that fades in, and sparkles. Use the component's **Preview Celebration** context-menu entry, or unlock an area for real.
- [ ] **Area banner:** walk across an area boundary. The card drops in, the name tightens from wide letter spacing, and the accent bar punches. **Not yet seen running.**
- [ ] **Loading screen** (start from the Init scene): the logo eases in, then breathes and shimmers; a sparkle rides the progress fill; tips cross-fade every 4.5s when there's more than one. **Not yet seen running.**
- [ ] **HUD entrance:** after loading, each HUD piece slides in from its nearest screen edge.
- [ ] **Treasure HUD button:** breathes and shimmers while today's boxes are out. It sits inside the treasure stats panel, which was hidden in every test so far. **Not yet seen running.**
- [ ] **Joystick:** fades and grows in under the finger. On release, input stops at once while the knob springs back and the stick fades.
- [ ] **World orbs:** the question mark floats and pulses, and squashes when tapped.

### 3.3 Settings and edge cases

- [ ] **Reduce Motion on** (Settings, "Reduce Motion" row): everything still works with simple fades. Nothing slides, bounces or idles; the shimmer may stay. Run the quiz, shop, rewards, onboarding and a level-up once each.
- [ ] **Right-to-left languages** (switch to ar, then ur):
  - Shop tab switches swipe the mirrored way.
  - `UIPanelMotion` SlideFromStart/End presets mirror.
  - The photo tilt and the padlock spin mirror.
  - Text fades work with Bengali (bn) text (CanvasGroup path).
- [ ] **Pause (`Time.timeScale = 0`):** UI motion keeps running and toasts still hide. Gameplay-world motion (orbs, the treasure box rattle) is allowed to freeze.
- [ ] **Interruptions:**
  - Close a panel mid-entrance; reopen it mid-exit.
  - Switch shop tabs quickly several times.
  - Mash a button.
  - Background the app mid-flight; the reward hold times out after 12s at most, so the HUD must never stay frozen.

---

## 4. Leftover work from Phases 1 to 6

**P1** means do it with Phase 7. **P2** is worth doing. **P3** is nice to have.

### 4.1 Scene wiring (P1, no new code needed)

The code for these is already written; it does nothing until the fields are assigned in `Jannah Garden.unity`.

- `ReturnToBaseUI.activeWalkLabel` and `MoveToUnlockedAreaUI.activeWalkLabel`: the walk labels. They use `UIToggleMotion` with a 14-unit slide.
- `InspectorModeUI`: the fly up and fly down buttons and the mode label (staggered slides from ±40).
- `GameSettingsManager.settingsOptions` is **empty**. Assign the settings rows in order so they cascade in on open. They're animated from scale 0, so rows must be authored at scale 1.

### 4.2 HUD (plan §2)

- **P2. XP gain chart** (`PlayerXPManager.ToggleXPGainChart`, a coroutine): replace it with `UIPanelMotion` using `SlideFromEnd`, and stagger the rows in. Exit at exit speed.
- **P2. Shop open button arrows:** `InGameShopManager.SetShopOpen` swaps `openArrow`/`closeArrow` GameObjects. Use one arrow that rotates 180° (OutBack, token durations).
- **P2. Settings gear:** rotate the gear icon 90° on press. Rotate the icon child, not the button frame.
- **P2. Treasure box slots** (`TreasureBox/TreasureBoxStatusUI.cs`):
  - Slots stagger in when the panel opens.
  - A locked slot's lock shakes when tapped.
  - When a slot unlocks, the lock pops off (scale, rotate, fade, as `ShopItemUI.PlayUnlock` does) and the box bounces.
  - A ready box bobs (`UIIdle` Bob) and shimmers.
- **P3. Minimap** (`Minimap/MinimapBehaviour.cs`):
  - A 1.03 overshoot on the frame at the end of expanding.
  - A slow radar ping on the player marker. The minimap renders through an orthographic camera with layer-15 proxies, so use a flat proxy rather than a depth-based effect (see project notes).
  - Flash a zone on the minimap when it unlocks.
- **P3. Tutorial replay button** (`Tutorial/TutorialReplayButton.cs`): pop in and out (`UIToggleMotion` or `UIMotion.Reveal`).
- **P3. Walk labels:** animated "..." dots or a bobbing footstep while walking, once 4.1 is wired.

### 4.3 Panels (plan §3)

- **P2. Quiz** (`Question Tasks/MCQManager.cs`):
  - On a correct answer, the option fills green from left to right, a ✓ pops, and sparkles burst (`UIFlyToTarget.Burst`).
  - On a wrong answer, add a red fill. The shake and the correct-answer pulse already exist.
  - Check that the submit button stays non-interactable until an option is picked; `UIButtonJuice` already pops it when it becomes interactable.
- **P3. Dhikr** (`Question Tasks/DhikrManager.cs`):
  - A circular progress ring around the count (needs a radial-fill sprite; the jade/gold UI kit is generated by `Tools/ui_kit/generate.py`).
  - On completion, soft rays behind "Mash'Allah" (`UIFxSprites.Rays`).
- **P2. House ad** (`Ads/HouseAdPresenter.cs`): pop in and out; the countdown number punches every second; the close × fades in with a pop when the countdown ends.
- **P2. Placement controls** (`ItemPlacementManager.cs`: the place button and the rotation slider shown while placing):
  - They slide up from the bottom when placement starts and slide down when it ends.
  - The place button breathes (`UIIdle`) while the spot is valid, and turns grey with a small shake on an invalid tap.
  - Placing punches the button.
  - A dust puff in the world when the item lands (world VFX, optional).
- **P3. Placed item actions** (`PlacedItemActionsUI.cs`): the rotate button spins 90° on tap; the delete or store button shakes on its confirm step.
- **P3. Shop card extras** (`ShopItemUI.cs`):
  - Download progress becomes a radial fill with a spinning glow that pops into a ✓ and fades. Today it's a plain fill (`downloadProgressFillImg`).
  - Daily offer: a "SALE" ribbon sways (`UIIdle` Sway), and the countdown pulses red under 1 hour.
- **P3. Inventory card** (`InventoryItemUI.cs`): the count label punches when it changes; the selected card scales to 1.05 with a glow outline.
- **P3. Shop tab underline:** if the tab bar gets an underline or highlight, slide it to the new tab. The width and colour animation already exists in `CategoryTransitionRoutine`.
- **P3. Settings language row** (`Localization/LanguageSelector.cs`): the selected language's flag punches.
- **P3. Toast:**
  - A tone icon (success or warning) that pops 0.1s after the toast lands. The toast prefab has no icon yet.
  - Queued toasts push the previous one up rather than replacing it. Today there's a single holder that punches in place.
- **P3. Photo mode** (`PhotoModeManager.cs`):
  - Shutter-blink bars from the top and bottom on capture.
  - The save button morphs into a ✓ after saving.
  - A viewfinder frame is optional, since photo mode is a single tap.
- **P3. Treasure confirmation** (`TreasureBoxConfirmationPanel.cs`): the plan's "rarity background slowly rotates its rays" needs radial ray art. The current rarity background isn't radial, so rotating it would look wrong. Either add a `UIFxSprites.Rays` image behind the reward icon, or skip this.
- **P3. Area banner** (`Placement/Areas/GardenAreaBanner.cs`): when the card hides (only when `staysVisible` is false), fade it and slide it up.
- **Optional. Onboarding spotlight iris:** there's no cutout today; the highlighted target is raised above a small dim by sort order. An iris would need a UI mask shader with a circular hole (shipped through `Resources/`, per 1.2), animated from full screen down onto the target. It's a design decision whether it's worth it.

### 4.4 World UI (plan §4)

- **P3. Fellow name tags** (`Fellowship Vissualization/FellowProfileObject.cs`): fade by distance instead of a hard cut-off; turn smoothly to face the camera.
- **P3. "+N coins / +XP" text above placed items in the world:** rises about 60px with a little randomness, and fades.

### 4.5 Known gaps and decisions

- `TreasureBox/UI/TreasureBoxOpenAnimator.cs` has **no callers** (dead code). The real open animation is `TreasureBox.PlayOpenAnimation`, the 3D box. Delete the dead script or leave it.
- The shop card prefab has **no tier badge or frame image**, so Tier 3 and 4 cards shimmer across the whole card background. If badge art is added, `ShopItemUI` already prefers the badge.
- `UIIdle` puts a material on each shimmering Image (see 3.1 on batching).
- The Joystick Pack's `FloatingJoystick.cs` was edited (a third-party package), so re-apply the change if the package is ever updated.

---

## 5. What done looks like

- Every box in §3 is ticked, on the Pixel 6, with Reduce Motion both off and on, and with ar or ur.
- Every P1 item is done, and every P2 item is done or deliberately dropped, with the reason noted in `Docs/UI_Animation_Plan.md`.
- No console errors in a full session: onboarding, quiz, dhikr, shop purchase, treasure box, level-up, photo, settings.
- Add a "Phase 7 status" note to `Docs/UI_Animation_Plan.md`, in the same style as the existing phase notes.
