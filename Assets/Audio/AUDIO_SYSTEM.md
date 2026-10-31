# Jannah Garden — audio system

Every interaction in the garden maps to one entry in the `SoundEffect` enum
(`Assets/Scripts/Audio Manager/AudioManager.cs`). 74 effects, all currently audible.

## How to play a sound

```csharp
GameSfx.Play(SoundEffect.ItemPlace);                 // flat 2D
GameSfx.PlayAt(SoundEffect.OrbCollect, transform);   // positional, if the effect is marked spatial
GameSfx.PlayToast(ToastTone.Warning);                // the right chime for a message severity
```

`GameSfx` is null-safe: it does nothing when no `AudioManager` exists yet (boot, scene load,
editor test scenes), so no call site needs a guard.

Toasts chime on their own. `ToastMessageManager.ShowToast(msg)` picks the chime from the text
colour (red → error, amber → warning, green → success, otherwise neutral), or pass a tone
explicitly: `ShowToast(msg, ToastTone.Warning)`. Use `ToastTone.Silent` when the caller already
plays its own cue — the coin and XP paths do this so they do not double up.

## Adding a new sound to a system

1. Append a value to `SoundEffect` — **at the bottom, never in the middle**. The enum serialises
   by integer, so inserting silently reassigns every clip already wired up in the scene.
2. Add a row to `AudioLibraryMap.Table` (`Assets/Editor/AudioLibraryMap.cs`).
3. Run **Jannah → Audio → Populate Sound Library**, then save the scene.

## The editor tools

| Menu | What it does |
|---|---|
| `Jannah → Audio → Populate Sound Library` | Fills empty rows from the map. Hand-tuned clips survive. |
| `Jannah → Audio → Re-apply Sound Library (overwrite)` | Forces every mapped row back to the map's clips and shaping. |
| `Jannah → Audio → Report Unmapped Effects` | Lists silent effects and effects that will sound identical on every repeat. |

The same three buttons are on the AudioManager inspector.

## Why it sounds the way it does

The garden is calm and devotional, so the palette comes from the **tonal** families in the
400-pack rather than the 8-bit or brass ones. Related moments share an instrument so they read
as a family:

- **Treasure boxes** — music box, escalating: mystery on appear → jar/lock on tap →
  lid → vibraphone payoff → brass on a completed set.
- **Garden areas** — sitar: a quick chime entering ground you own, a sitar negative when that
  ground is full, paper rustle on the road between. Locked ground is silent in both directions —
  entering it, walking through it and leaving it all play nothing, because the zone card already
  says it is locked and a cue on every boundary of an unusable zone turns into nagging.
- **Rewards and XP** — vibraphone and xylophone.
- **Items** — wood and stone: real handled-object sounds, not UI beeps.
- **Refusals and errors** — deliberately dry and unmusical, so a blocked placement never feels
  like a reward.

Repetition is handled per-sound rather than per-call: `variations` holds alternates that are
picked without an immediate repeat, and `pitchVariance` jitters each play. The two most-repeated
sounds in the game — footsteps and the dhikr bead — lean hardest on both.

`cooldown` throttles the rapid-fire callers (grid steps, toasts, coin ticks) so they cannot stack
into a buzz.

## Dropping in ZapSplat clips

ZapSplat downloads require a free account, so this has to be done by hand. The system is built so
that costs no code changes:

1. Download the clip (WAV preferred — the index prefers `.wav` over `.ogg` over `.mp3`).
2. Drop it anywhere under `Assets/Audio/SFX Packs/`.
3. Add its **filename without the extension** as the *first* name in that effect's row in
   `AudioLibraryMap.cs`. Names that do not resolve are skipped silently, so leaving the current
   library clip after it keeps the row working until the file lands.
4. **Jannah → Audio → Re-apply Sound Library (overwrite)**, then save the scene.

Example — swapping in a ZapSplat chest-open over the current one:

```csharp
{ SoundEffect.TreasureBoxLidOpen, E(SoundCategory.Reward, 0.8f, 0f, 0f, true,
    "zapsplat_treasure_chest_open_creak",   // new: resolves once the file is in the project
    "creaky_door_short", "ceramic_jar_open") },
```

### A note on the cartoon category

The page suggested for sourcing (`zapsplat.com/sound-effect-category/cartoon/`) is slapstick —
boings, slurps, lip smacks, comedy whistles. That register fights the garden's tone almost
everywhere, and the current mapping deliberately avoids it.

Where the cartoon category *does* fit, and is worth auditioning:

| Effect | What to search |
|---|---|
| `OrbAppear` / `OrbFocus` | cartoon magic sparkle, twinkle appear |
| `TreasureBoxAppear` | cartoon pop appear, magic reveal |
| `ItemGhostStep` | cartoon light tick, blip |
| `AreaUnlockFanfare` | cartoon fanfare, success jingle |
| `LevelUp` | cartoon level up, ascending chime |
| `CoinGain` | cartoon coin, pickup |
| `Whoosh` | cartoon whoosh, swipe |

For the devotional moments (dhikr, quiz results, area unlocks) the **Musical Effects** families
already in the project are the stronger choice.

### Licensing

ZapSplat's free tier requires attribution in the credits. If any ZapSplat clip ships in the build,
add their standard attribution line to the in-game credits before release.
