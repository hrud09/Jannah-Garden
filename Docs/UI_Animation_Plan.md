# Jannah Garden — UI Motion Plan

Goal: every UI element has a deliberate **appear**, **disappear**, and **idle** state, so the game feels soft, alive and rewarding — calm garden mood, with "juicy" bursts saved for rewards.

## Where we are today

DOTween is installed and already used, but each script uses it differently, with its own durations and eases:

| Already animated (inconsistently) | Pops in/out instantly (`SetActive`) |
|---|---|
| Quiz (MCQManager), Dhikr, RewardFinaleDisplay, Toast, Settings, Noor Coin store (open only), Photo preview, Area unlock announcer, Shop panel (coroutine + curves), Quick-action drawer (coroutine), Minimap expand (coroutine), XP chart, XP slider, Treasure box opener (coroutine), Loading fades | Noor Coin store **close**, PlacedItemActionsUI, TreasureBoxConfirmationPanel, shop tabs/item panels, HUD buttons revealed by onboarding, walk labels (Return/Move), Inspector fly buttons, minimap sub-buttons, coin balance text, Treasure box slots, House ad countdown, tutorial replay button, language row |

Problems to fix along the way: no shared timing, close animations are missing or much weaker than opens, some tweens follow `Time.timeScale` and some don't, and nothing respects a "reduce motion" preference.

---

## 1. Foundation (build this first, everything else uses it)

All of it goes in `Assets/Scripts/UI/Motion/`.

> **Status (2026-11-16): built.** `UIMotionTokens` (asset at `Resources/UIMotionTokens.asset`), `UIMotion` (Punch / Flash / PopIn / ease helpers / Reduce Motion), `UIPanelMotion`, `UIButtonJuice` + `UIButtonJuiceBootstrap` + `NoButtonJuice`, `UICountTo`, and a Reduce Motion row in Settings. Nothing outside Settings is migrated to them yet — that's Phase 1 onward. `UIIdle`, `UIFlyToTarget` and the shimmer shader land with the phases that first need them.

### 1.1 `UIMotionTokens` (ScriptableObject in `Resources/`)
One place to tune the feel of the whole game:

| Token | Value | Use |
|---|---|---|
| `appear` | 0.32s, `OutBack(1.3)` | panels, popups |
| `appearSoft` | 0.25s, `OutCubic` | HUD, labels, fades |
| `disappear` | 0.18s, `InBack(1.2)` / `InQuad` | exits take about 60% as long as entries |
| `stagger` | 0.035s per child, capped at 0.3s total | lists, grids, tabs |
| `press` | 0.07s to scale 0.92, then release 0.22s `OutBack` to 1 | all buttons |
| `punch` | 0.35s, strength 0.12, vibrato 8 | value changes, confirms |
| `idleBreath` | 2.4s period, ±2.5% scale, `InOutSine` | attention items |
| `reduceMotion` | bool (in Settings) | swaps scale/slide for 0.15s fades and turns idle loops off |

Every tween uses `.SetUpdate(true)` (unscaled time) and `.SetLink(gameObject)`, so tweens die with their object. Each target gets `DOKill` before a new tween starts.

### 1.2 `UIPanelMotion` component
Drop it on any panel root. It exposes `Show()`, `Hide(Action onDone)` and `IsVisible`, and makes sure `SetActive(false)` only runs **after** the exit finishes.

- **Presets:** `Pop` (scale 0.85→1 + fade), `SlideUp`, `SlideFromEdge(L/R)`, `Fade`, `DropIn` (from above with a small bounce).
- **Optional backdrop:** a CanvasGroup that fades to 0.55 alpha while the window pops in.
- **Optional `staggerChildren`:** a list of children that cascade in (scale 0.6→1 + fade).
- **RTL aware:** when `LocalizationManager` is ar/ur, `SlideFromEdge` mirrors left and right.
- Blocks raycasts while a transition is running, which stops double-tap bugs.

### 1.3 `UIButtonJuice` (global)
Attach it automatically to every `Button` using the same bootstrap/LateUpdate scan that `ButtonClickSfx` already does. Behaviour:
- **Pointer down:** squash to 0.92, slightly darker. **Up:** OutBack spring to 1.
- **Disabled → enabled:** a small 1.08 pop plus a glint, so newly affordable things get noticed.
- Opt out per button with a `NoButtonJuice` marker component (the joystick, the slider handles).
- Animate a **child "visual" transform** rather than the button root wherever the button sits in a LayoutGroup, so the layout never rebuilds.

### 1.4 Reusable helpers
- `UICountTo` rolls a TMP number from old to new value, with a punch on the label and a short colour flash (gold for gains, soft red for spends).
- `UIIdle` adds idle loops: `Breathe`, `Bob` (±6px), `Sway` (±3°), `AttentionWiggle` (a shake every N seconds while a condition is true), `Shimmer` (a sweep of light across the sprite).
- `UIFlyToTarget` sends icons flying from a world or screen point to a HUD target along a curved path, then punches the target when they land. This is the core of reward juice.
- `UIShimmer.shader` is a UI shader with a scrolling highlight band. **Add it to Always Included Shaders** (lesson from the stripped `UI/ShapedTextSDF`).

---

## 2. HUD (always on screen)

> **Phase 1 status (2026-11-16): built.**
> - **Coin counter:** rolls to its new value, the coin pill punches, and a +N / −N drifts below it (`NoorCoinDisplay` + `UICountTo` + `UIFloatingText`).
> - **XP bar:** a ghost fill leads, a sparkle rides the fill's edge, and the bar runs a full level-up sequence (`XPBarAnimator`).
> - **Level-up moment:** a medallion, a ribbon, rays and sparkles (`LevelUpCelebration`, key `hud.level_up`).
> - **Startup HUD entrance:** each piece slides in from its nearest screen edge as the loading screen fades out (`UIHudEntrance` on *All Canvas*, triggered by `LoadingScreenManager.Revealing`).
> - **Rest of this table:** still to do (button reveals, walk labels, drawer, minimap sub-buttons, joystick, treasure slots). These overlap Phases 2 and 5.

| Element | Appear | Disappear | Idle / state change |
|---|---|---|---|
| **Noor Coin display** (`NoorCoinDisplay`) | Slides down from the top edge after the loading screen ends | — | `UICountTo` on every balance change: punch the coin icon, add +N floating text, gold flash. When coins arrive from a reward, they fly in first (`UIFlyToTarget`) and the counter ticks once per coin landing. The loading text gets three pulsing dots. |
| **XP bar + level** (`PlayerXPManager`) | Slides down, staggered 0.06s after the coins | — | The slider already uses `DOValue`; add a bright "ghost fill" that leads the real fill, plus a sparkle at the fill tip. **Level-up (a new moment):** bar flashes full, the level number scales 1→1.5→1 with a ray burst, and a "Level N" ribbon drops in (`DropIn`), holds 1.5s, then leaves. Today it only plays the SFX. |
| **XP gain chart** (`ToggleXPGainChart`) | Replace the coroutine with `UIPanelMotion SlideFromEdge`; rows stagger in | Slides out at the faster exit speed | — |
| **Shop open button** | When onboarding reveals it: scale 0→1 `OutBack` plus a ring pulse (see §6) | Scale to 0 | While a daily offer is unclaimed: `AttentionWiggle` every 6s. The open/close arrow swap becomes a 180° rotate instead of two GameObjects toggling. |
| **Photo / Settings / Inspector / Return-to-base / Move-to-area / Outer garden / Treasure buttons** | Onboarding unlock = the same "reveal" pop + ring. Normal scene start = staggered pop, left column top-to-bottom | Pop out | Settings gear rotates 90° when pressed. Treasure button `Breathe`s and shimmers when any box is ready to open. |
| **Walk labels** (`activeWalkLabel` on Return/Move) | Fade + slide 12px from the button | Fade | While walking: three animated dots or a footstep icon bobbing. |
| **Inspector fly up/down buttons** | Slide out from behind the inspector button (up and down respectively) | Slide back in | Active-mode label pulses softly. |
| **Quick-action drawer** | Swap the coroutine for `UIPanelMotion`; its items stagger out from the toggle like a fan | Items collapse back into the toggle, in reverse order | Toggle icon rotates 45° (+ becomes ×). |
| **Minimap** | Expand/collapse already animated; add a 1.03 overshoot on the frame at the end | — | The sub-buttons (collapse, recenter, treasure tiers) currently `SetActive`. Make them stagger-pop after the expand finishes and vanish *before* the collapse starts. Player marker gets a slow radar ping. |
| **Joystick** | Fades in on first touch at the touch point (scale 0.8→1) | Fades to 35% after 1.5s idle | The knob springs back to centre (`OutBack`) on release instead of snapping. |
| **Tutorial replay button** | Pop | Pop | — |
| **Treasure box status UI** (slots) | Slots stagger in | — | Locked slot: lock icon shakes when tapped. When a slot unlocks: the lock pops off (scale + rotate + fade) and the box bounces. A ready box bobs and shimmers. |

---

## 3. Panels and popups

> **Phase 2 status (2026-11-16): built.**
> - **Placed item actions:** the panel pops in and its three buttons cascade in after it. This replaces the radial arc in the table below, because the real panel is a centred dialog.
> - **Treasure box confirmation:** the window pops in over the dim, the reward icon pops in, and the rarity badge stamps on.
> - **Noor Coin store:** has a real close animation, the pack cards cascade in, and reopening it mid-close is safe.
> - **Minimap buttons:** the expanded-only buttons stagger in after the panel grows, and shrink away on collapse.
> - **Return / Move buttons:** they punch when they flip between "go" and "stop".
> - **New helper:** `UIToggleMotion.SetActive` is an animated `SetActive` for small pieces.
> - **Dormant code:** the walk labels and the inspector fly buttons and mode label are coded to animate, but they're not assigned in the scene.
>
> **Phase 3 status (2026-11-17): built.**
> - **Core:** `UIFlyToTarget` flies pooled icons along curved paths on its own overlay canvas (sort order 100) and re-aims at the target every frame. `UIRewardHold` keeps the coin counter and XP bar on their old values until the icons land, then each landing moves them a step. Every hold has a timeout.
> - **Coin target:** coins fly to "Coin Fly Target", an invisible rect over the coin drawn into the HUD pill art. The flying copies use `jg_noor_coin_fly.png`, a circular crop of that art.
> - **Quiz and dhikr finale:** the HUD holds before the reward is granted. When the panel closes, coins and XP fly from their chips to the HUD.
> - **Treasure box:** the 3D box rattles for 0.35s before it bursts. Its per-box coins then stream from the box to the counter.
> - **Shop:** coin packs burst coins from the card. A purchase punches the card and sprays sparkles. Tapping an item you can't afford, or one that's level-locked, wobbles the card and flashes the price. A card that unlocked since you last saw it pops off its padlock. This also fixes padlocks that used to stay on screen until the shop was rebuilt.
> - **Coin store:** the auto-close waits for the coins to land.
> - **Phase 4 status (2026-11-17): built.**
>   - **Shop:** after the panel opens, the tabs drop in one by one and the cards in view cascade in (off-screen cards skip it). Switching tabs swipes the content in the direction of travel (mirrored for RTL) while it cross-fades, and the new tab's cards cascade. An interrupted switch hides abandoned panels at once. The panel transitions run on unscaled time.
>   - **Onboarding:** the dim overlay fades in and out. Instruction text cross-fades. The pointer hand pops in, presses at the bottom of each bob and sends a ring out from its target. Buttons handed out mid-session use the §6 reveal (`UIMotion.Reveal` + `UIMotion.Ripple`, staggered). The pulse now keeps each button's own scale instead of forcing 1. **No iris:** there is no spotlight cutout to animate (the highlight raises the target's sort order), and adding one needs a mask shader.
>   - **Area unlock announcer:** rays bloom and turn slowly behind the card, the title stamps down, the body fades in after it, and gold and green sparkles burst. The card settles back as it fades. There's a `Preview Celebration` context-menu entry for checking it.
>   - **Area banner:** a real crossing drops the card in from 14 units above, the name settles from wide letter spacing, and the accent bar punches. Not yet seen in Play mode (it needs the player to walk across an area boundary).
>   - **Not done:** the minimap flash for a newly unlocked zone.
> - **Phase 5 status (2026-11-17): built.**
>   - **`UIIdle`:** combinable Breathe / Bob / Sway / Wiggle / Shimmer, switched on and off with `Playing` (eases in and out). Each effect is applied as an offset on top of the element's current pose, so presses, punches and reveals still play. Rests recorded mid-breath snap back to the authored rest, so presses never make a button drift. It stops under Reduce Motion (except the shimmer) and while disabled.
>   - **Shimmer shader:** `UI/Jannah Shimmer` (`Assets/Shaders/UI/UIShimmer.shader`) sweeps a diagonal band of light that follows the sprite's alpha, measured across the sprite's own atlas rect. It ships through `Resources/UIShimmer.mat`, not Shader.Find or Always Included Shaders, so the build can't strip it.
>   - **Where idle effects are used:**
>     - Treasure HUD button: breathes and shimmers while today's boxes are out.
>     - Shop button: wiggles every 6s while a daily offer is waiting and the shop is closed.
>     - Best-value Noor Coin offer: breathes and shimmers.
>     - Tier 3/4 shop cards: shimmer across the card, because the prefab has no tier badge.
>     - Treasure box "watch ad" button: shimmers.
>     - Loading logo: breathes and shimmers.
>   - **Quick-action drawer:** its tools pop out one after another when it opens.
>   - **World orbs:** the question mark floats and pulses, and squashes and pops when tapped.
>   - **Joystick:** fades and grows in under the finger. On release, input stops at once while the knob springs back and the stick fades out.
>   - **Loading screen:** the logo eases in, a sparkle rides the fill's leading edge, and tips rotate every 4.5s with a cross-fade (when there's more than one).
>   - **Not done:** the minimap player radar ping, the settings gear rotation, the locked treasure slot shake, the walk-label dots (labels unwired), the "SALE" ribbon sway, and the confirmation panel ray rotation (its rarity background isn't radial art).
> - **Phase 6 status (2026-11-17): built.** The hand-tuned tweens now take their timings from `UIMotionTokens`, run on unscaled time, and are linked to their objects.
>   - **Shared helpers:** `UIMotion.PanelIn` / `PanelOut` (pop from 0.85 with a fade, then a quick InBack exit) and `UIMotion.SetBackdrop` (backdrop fades in and out, deactivating only once clear).
>   - **Quiz and Dhikr:** the panels use the shared entrance and exit, and the blurred backdrop now fades with the panel instead of blinking. The question pops in, the options cascade on the token stagger, and the select / correct / reveal / Mash'Allah punches go through `UIMotion.Punch`, so they no longer fight the button squash. Each dhikr tap sends a ring out from the counter.
>   - **Settings:** fixed a bug where the open animation never played. It lived in `OnEnable` on the always-active holder, so opening the panel left the dim at alpha 0. The dim now fades up while the window pops in (rows would cascade too, but `settingsOptions` is empty in the scene). Closing shrinks the window and clears the dim at exit speed. Every toggle in the game now pops its checkmark when switched on (in `UIButtonJuice`).
>   - **Toast:** rises on the shared OutBack instead of the elastic bounce, with a fade. A second message while one is showing punches in place instead of re-rising. It hides on unscaled time (it used `Invoke`, which stalls while paused).
>   - **Photo:** the preview "prints" (drops from 1.25× with a 4° tilt that straightens) and closes with an exit instead of vanishing. The save button punches when a save succeeds. Not built: the HUD slide-away (photo mode is a single tap, so there's no framing mode to slide away from), the viewfinder brackets and the shutter bars.
> - **Not used:** `TreasureBoxOpenAnimator` has no callers, so the anticipation went on the real 3D box (`TreasureBox.PlayOpenAnimation`) instead. A purchased item goes straight to placement, so there's no "fly to inventory tab".

| Panel | Appear | Disappear | Inside the panel |
|---|---|---|---|
| **Shop / Inventory** (`InGameShopManager`) | Keep the curve-driven slide, but move the timings into the tokens. After the panel lands: tabs drop in staggered, then the **visible** item cards cascade (scale 0.7→1 + fade, row by row). Off-screen cards skip the animation. | Cards don't animate; the panel slides out fast | **Tab switch:** the underline/highlight slides to the new tab and the content cross-fades with a 20px slide in the swipe direction. **Category scroll** already uses `scrollCurve`. |
| **Shop item card** (`ShopItemUI`) | (cascade above) | — | **Locked → unlocked** (level up): the padlock breaks (two halves fall and fade), the card flashes white, then shimmers once. **Unaffordable tap:** horizontal shake + the price flashes red. **Purchase:** card punch + the item icon flies to the inventory tab, which punches. **Tier badge:** gentle shimmer on rare and epic tiers. **Download progress:** a radial fill with a spinning glow, which pops into a ✓ and fades when done. **Daily offer:** a "SALE" ribbon sways, and the countdown pulses red under 1 hour. |
| **Inventory card** (`InventoryItemUI`) | Same cascade | — | The count label punches when it changes. Selecting a card scales it to 1.05 with a glow outline. |
| **Noor Coin store** (`NoorCoinStorePopup`) | Already pops in; add a backdrop fade and stagger the pack cards. The best-value card `Breathe`s with a shimmer | **Add a real close:** scale to 0.9 + fade in 0.18s, then `SetActive(false)` | On purchase: coins burst from the pack and fly to the HUD counter (`UIFlyToTarget`). The auto-close on "affordable" waits until the coins have landed. |
| **Settings** (`GameSettingsManager`) | Already staggers its options; keep that but move it to the tokens | Already reverses | Toggles get a knob slide plus a colour tween (no hard swap). The language row's selected language flag punches. Add the **Reduce Motion** toggle here. |
| **Photo mode** (`PhotoModeManager`) | Flash is already done. Add: the HUD slides away to the edges when you enter photo mode, and a viewfinder frame fades in with its corner brackets animating inward | HUD slides back in | **Capture:** shutter-blink bars from top and bottom, then the photo "prints" by scaling down from full screen into the preview window with a slight tilt (−3° → 0). The save button gets a ✓ morph after saving. |
| **Placed item actions** (`PlacedItemActionsUI`) | **Currently instant.** Make it a radial or arc pop of the action buttons around the item's screen position, staggered 0.04s | Buttons collapse back into the centre | The rotate button spins 90° on tap. The delete/store button shakes on the confirm step. The panel follows the item with damping (lerp), not a hard snap. |
| **Placement controls** (place button, rotation slider, during placement) | Slide up from the bottom | Slide down | The place button `Breathe`s when placement is valid and turns grey with a small shake on an invalid tap. Placing triggers a dust puff (world VFX) + a button punch. |
| **Treasure box confirmation** (`TreasureBoxConfirmationPanel`) | **Currently instant.** Backdrop fade + `Pop`. The reward icon drops in after 0.15s and the rarity badge stamps in (scale 1.6→1, `OutBack`) | Pop out | The rarity background slowly rotates its rays. The Open button shimmers. |
| **Treasure box open** (`TreasureBoxOpenAnimator`) | Already sequenced; add anticipation: the box shakes with rising intensity for 0.6s, a light burst comes out of the gap, then the lid opens | Rewards fly to their HUD targets, then the overlay fades | Reward chips pop out one by one in an arc. Higher tiers add more particles and a short screen-edge glow in the tier's colour. |
| **MCQ quiz** (`MCQManager`) | Already good (pop + staggered options). Add a backdrop fade | Already punch + InBack | Correct: the option fills green from left to right, a ✓ pops, sparkles. Wrong: shake (already there) + red fill, and the correct answer pulses (already there). The submit button stays disabled and greyed until a choice is made, then pops when enabled (via `UIButtonJuice`). |
| **Dhikr** (`DhikrManager`) | Already pops | Already punch + InBack | Each tap sends a ripple ring out from the counter, and the number rolls (`UICountTo`). Add a circular progress ring around the count. Completion triggers the existing MashaAllah punch plus soft light rays. |
| **Reward finale** (`RewardFinaleDisplay`) | Already staggered chips | — | Coin and XP numbers count up instead of appearing (`UICountTo`). On dismiss, the chips fly to the coin and XP HUD (`UIFlyToTarget`), so the reward visibly *lands*. |
| **Toast** (`ToastMessageManager`) | Already elastic rise. Tone it down to `OutBack`, which suits the calm mood better | Already InBack | Tone icon (success/warn) pops 0.1s after the toast lands. Queued toasts push the previous one up instead of overlapping. |
| **Area banner** (`GardenAreaBanner`) | Already fades. Add: the card slides 20px down + fades, the text reveals by letter spacing tightening (wide → normal) | Fade + slide up | — |
| **Area unlock announcer** | Already a punch. Add: rays rotating behind it, the title stamps in, and a confetti/petal burst (UIParticle is already in the GUI kits) | Fade | The minimap's newly unlocked zone flashes. |
| **Onboarding** (`GameOnboardingManager`) | Dim overlay fades in. The instruction panel slides up from the bottom. The spotlight cutout shrinks onto its target (iris) | Iris opens back out, panel slides down | The pointer hand bobs and taps (scale 0.9) at the target. Each step's text cross-fades instead of being replaced. Revealed HUD buttons use the reveal pop + a ring pulse, so the player sees where each new feature landed. |
| **Loading screen** (`LoadingScreenManager`) | Logo: fade + scale 0.95→1, plus one shimmer pass | Fade | Progress bar: smoothed fill (never jumps backward) + a sparkle on its tip. Tips cross-fade every N seconds with a slight slide. A small leaf or orb icon spins gently. The handoff into the game is the HUD stagger described in §2. |
| **House ad** (`HouseAdPresenter`) | Pop | Pop | Countdown number punches every second. The close × fades in when the countdown ends, with a small pop. |

---

## 4. World-space UI

- **Question-mark orbs:** already scale in and out. Add an idle bob + slow spin + a periodic glow pulse. On tap, the orb squashes, then bursts.
- **Fellow profile name tags / world text:** fade in by distance (not a hard cull) and billboard smoothly.
- **Floating "+N coins / +XP" text** above placed items: rises 60px and fades, slightly randomized.

---

## 5. Rules that keep it fast on mobile

1. **Separate canvases:** put the always-animated HUD bits (counters, idle loops) on their own sub-Canvas, so a coin tick doesn't rebuild the shop canvas.
2. **Never tween anything under a LayoutGroup directly.** Tween a child "visual" holder, or the layout fights the tween.
3. **Prefer CanvasGroup alpha and transforms over `Image.color`.** Changing a colour dirties the mesh; alpha on a CanvasGroup and transform changes are cheaper.
4. **Idle loops pause when hidden:** `OnDisable` kills them, and loops on cards outside the scroll viewport stop.
5. **Cap simultaneous fly-to-target icons at about 12,** and pool them through `Objectpool`.
6. **Test on the Pixel 6** at the 0.8 render scale; target no frame over 16ms during the shop cascade.
7. **Shimmer and other custom UI shaders** go in Always Included Shaders, and get checked on device for bn/ar/ur fonts.

---

## 6. The "reveal" moment (shared by onboarding and unlocks)

When something becomes available for the first time:
scale 0 → 1.2 → 1 (`OutBack`) → a ring ripples out from it (scale 1→2, alpha 0.6→0) → `AttentionWiggle` until the first tap. This one pattern used everywhere teaches the player that "a wiggle means something new".

---

## 7. Implementation order

| Phase | Work | Why first |
|---|---|---|
| **0** | `UIMotionTokens`, `UIPanelMotion`, `UIButtonJuice` (global), `UICountTo`, Reduce Motion toggle | Everything else depends on these; global button squash alone changes the feel the most |
| **1** | HUD: coin counter roll, XP ghost fill, **level-up moment**, startup HUD stagger | Seen every second of play |
| **2** | The instant panels: PlacedItemActions, TreasureBoxConfirmation, Noor Coin store close, walk labels, minimap sub-buttons, inspector buttons | Biggest gaps today: things that blink in and out |
| **3** | Reward loop: `UIFlyToTarget`, reward finale fly-out, treasure box anticipation, coin-store purchase burst, card unlock/purchase | Makes earning feel good |
| **4** | Shop cascade + tab transitions, onboarding iris/pointer, area banner and announcer upgrades | Polish on longer flows |
| **5** | Idle life (`UIIdle`, shimmer shader), world orbs, joystick, loading screen | Ambient, lowest risk |
| **6** | Migrate the existing ad-hoc tweens (Quiz, Dhikr, Settings, Toast, Photo) onto the tokens | Consistency; the feel is tuned from one asset |
| **7** | Verify on device: Pixel 6 profiler pass, Reduce Motion, RTL slide mirroring, timeScale=0 paths (pause) | — |

Each phase can be checked with the existing **Jannah/UI Kit/Capture UI Panels** tool for still states. Motion itself needs a Play-mode screen recording or a test on device.
