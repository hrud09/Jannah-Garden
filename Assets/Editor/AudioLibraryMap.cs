using System.Collections.Generic;

/// <summary>
/// The curated effect-to-clip table for the garden.
///
/// Each entry names clips by filename stem (no extension, case-insensitive). The first name that
/// resolves becomes the primary clip; the rest become variations, so a repeated tap never plays the
/// same file twice in a row. Names that do not resolve are skipped silently, which is what lets a
/// row list a ZapSplat drop-in first and a library fallback after it: drop the file into
/// Assets/Audio/SFX Packs/ and re-run the populate, and the row upgrades itself with no code change.
///
/// Why these clips: the garden is calm and devotional, so the palette is built from the tonal
/// families in the 400-pack (music box, vibraphone, xylophone, sitar, grand piano) rather than the
/// 8-bit or brass ones. Related moments share an instrument so they read as a family -- every
/// treasure-box beat is music box, every area beat is sitar, every reward is vibraphone -- while
/// refusals and errors stay deliberately dry and non-musical so they never feel like a reward.
/// </summary>
public static class AudioLibraryMap
{
    /// <summary>Volume and pitch shaping for an effect, applied when the row is first created.</summary>
    public struct Shaping
    {
        public float volume;
        public float pitch;
        public float pitchVariance;
        public float cooldown;
        public SoundCategory category;
        public bool spatial;
    }

    public struct Entry
    {
        public string[] clips;
        public Shaping shaping;
    }

    private static Entry E(SoundCategory category, float volume, float pitchVariance, float cooldown,
                           bool spatial, params string[] clips)
    {
        return new Entry
        {
            clips = clips,
            shaping = new Shaping
            {
                volume = volume,
                pitch = 1f,
                pitchVariance = pitchVariance,
                cooldown = cooldown,
                category = category,
                spatial = spatial
            }
        };
    }

    public static readonly Dictionary<SoundEffect, Entry> Table = new Dictionary<SoundEffect, Entry>
    {
        // ── Buttons, panels, navigation ──────────────────────────────────────
        // Light and short. Variations on the click keep a menu from sounding like a metronome.
        { SoundEffect.ButtonClick,   E(SoundCategory.UI, 0.55f, 0.04f, 0.04f, false,
            "JDSherbert - Ultimate UI SFX Pack - Select - 1", "JDSherbert - Ultimate UI SFX Pack - Select - 2", "select_1", "select_2") },
        { SoundEffect.ButtonBack,    E(SoundCategory.UI, 0.5f, 0.03f, 0.04f, false,
            "JDSherbert - Ultimate UI SFX Pack - Cancel - 1", "cancel") },
        { SoundEffect.ButtonDisabled,E(SoundCategory.UI, 0.5f, 0f, 0.25f, false,
            "sci_fi_disallow", "synth_error") },
        { SoundEffect.ToggleOn,      E(SoundCategory.UI, 0.55f, 0f, 0f, false, "toggle_on", "click_double_on") },
        { SoundEffect.ToggleOff,     E(SoundCategory.UI, 0.55f, 0f, 0f, false, "toggle_off", "click_double_off") },
        { SoundEffect.PanelOpen,     E(SoundCategory.UI, 0.6f, 0f, 0.05f, false,
            "JDSherbert - Ultimate UI SFX Pack - Popup Open - 1", "pop_1") },
        { SoundEffect.PanelClose,    E(SoundCategory.UI, 0.55f, 0f, 0.05f, false,
            "JDSherbert - Ultimate UI SFX Pack - Popup Close - 1", "pop_3") },
        { SoundEffect.TabSwitch,     E(SoundCategory.UI, 0.5f, 0.05f, 0.03f, false,
            "JDSherbert - Ultimate UI SFX Pack - Swipe - 1", "JDSherbert - Ultimate UI SFX Pack - Swipe - 2", "page_turn") },
        // A drag sweeps through many values, so this one is quiet, varied and hard-throttled.
        { SoundEffect.SliderTick,    E(SoundCategory.UI, 0.3f, 0.08f, 0.05f, false, "menu_blip", "sci_fi_hover") },
        { SoundEffect.Whoosh,        E(SoundCategory.UI, 0.45f, 0.06f, 0.05f, false, "whoosh_1", "whoosh_2") },

        // ── Messaging ────────────────────────────────────────────────────────
        // Toasts fire often, so Info is the quietest thing in the game. Warning and Error are dry
        // and unmusical on purpose; Success borrows the reward family's vibraphone.
        { SoundEffect.ToastShow,     E(SoundCategory.UI, 0.35f, 0.03f, 0.35f, false, "pop_2", "menu_blip") },
        { SoundEffect.WarningShow,   E(SoundCategory.UI, 0.5f, 0f, 0.35f, false, "synth_warning", "sci_fi_disallow") },
        { SoundEffect.ErrorShow,     E(SoundCategory.UI, 0.55f, 0f, 0.35f, false,
            "JDSherbert - Ultimate UI SFX Pack - Error - 1", "sci_fi_error", "synth_error") },
        { SoundEffect.SuccessShow,   E(SoundCategory.UI, 0.5f, 0f, 0.3f, false,
            "vibraphone_chime_quick", "music_box_chime_quick") },

        // ── Garden areas: the sitar family ───────────────────────────────────
        { SoundEffect.AreaUnlockFanfare, E(SoundCategory.Reward, 0.85f, 0f, 0f, false,
            "sitar_level_complete", "brass_level_complete") },
        { SoundEffect.AreaUnlockReveal,  E(SoundCategory.Reward, 0.6f, 0f, 0f, false,
            "sitar_level_start", "grand_piano_level_start") },
        { SoundEffect.AreaEnterUnlocked, E(SoundCategory.World, 0.45f, 0.03f, 0.5f, false,
            "sitar_chime_quick", "music_box_chime_quick") },
        { SoundEffect.AreaEnterPath,     E(SoundCategory.World, 0.3f, 0.05f, 0.5f, false,
            "paper_move", "sci_fi_hover") },
        { SoundEffect.AreaFull,          E(SoundCategory.World, 0.45f, 0f, 0.5f, false,
            "sitar_negative", "synth_warning") },

        // ── Item handling: wood and stone, handled things ────────────────────
        { SoundEffect.ItemSelect,     E(SoundCategory.UI, 0.55f, 0.04f, 0.04f, false,
            "wood_small_pickup", "sci_fi_select") },
        { SoundEffect.ItemDeselect,   E(SoundCategory.UI, 0.4f, 0.04f, 0.04f, false, "sci_fi_deselect", "cancel") },
        // Fires on every grid cell crossed, so it is the quietest and most throttled cue here.
        { SoundEffect.ItemGhostStep,  E(SoundCategory.World, 0.22f, 0.1f, 0.06f, false,
            "subtle_knock", "metal_blunt_tap") },
        { SoundEffect.ItemPlace,      E(SoundCategory.World, 0.7f, 0.05f, 0f, true,
            "wood_small_drop", "stone_push_short") },
        { SoundEffect.ItemPlaceBlocked, E(SoundCategory.World, 0.55f, 0f, 0.2f, false,
            "cardboard_hit", "sci_fi_disallow") },
        { SoundEffect.ItemRotate,     E(SoundCategory.World, 0.35f, 0.06f, 0.05f, false, "slide_and_click", "snap") },
        { SoundEffect.ItemPickUp,     E(SoundCategory.World, 0.6f, 0.05f, 0f, true,
            "wood_small_pickup", "cardboard_pick_up") },
        { SoundEffect.ItemStore,      E(SoundCategory.World, 0.6f, 0.04f, 0f, false,
            "cardboard_box_close", "wood_small_gather") },
        { SoundEffect.ItemInteract,   E(SoundCategory.World, 0.55f, 0.04f, 0.05f, true,
            "item_equip", "sci_fi_select") },
        { SoundEffect.ItemGrowComplete, E(SoundCategory.Reward, 0.6f, 0f, 0f, true,
            "xylophone_chime_positive", "grow_big") },

        // ── Asset streaming and reveal ───────────────────────────────────────
        { SoundEffect.AssetDownloadStart,    E(SoundCategory.UI, 0.4f, 0f, 0f, false, "air_pump", "sci_fi_hover_high") },
        { SoundEffect.AssetDownloadComplete, E(SoundCategory.UI, 0.55f, 0f, 0f, false,
            "synth_process_complete", "8_bit_chime_quick") },
        { SoundEffect.AssetDownloadFailed,   E(SoundCategory.UI, 0.6f, 0f, 0f, false, "sci_fi_error", "power_down") },
        // The moment a downloaded model actually appears in the garden.
        { SoundEffect.AssetReveal,           E(SoundCategory.Reward, 0.65f, 0f, 0f, true,
            "vibraphone_chime_positive", "grand_piano_chime_positive") },

        // ── Question-mark orbs: the mystery family ───────────────────────────
        { SoundEffect.OrbAppear,            E(SoundCategory.World, 0.5f, 0.05f, 0.1f, true,
            "music_box_mystery", "8_bit_mystery") },
        { SoundEffect.OrbFocus,             E(SoundCategory.World, 0.35f, 0.05f, 0.15f, false,
            "sci_fi_hover_high", "glass_ping_small") },
        { SoundEffect.OrbCollect,           E(SoundCategory.Reward, 0.65f, 0.03f, 0f, true,
            "gem_collect", "music_box_chime_positive") },
        { SoundEffect.QuestionMarkOrbOpen,  E(SoundCategory.UI, 0.6f, 0f, 0f, false,
            "grand_piano_mystery", "book_open") },
        { SoundEffect.QuizOptionSelect,     E(SoundCategory.UI, 0.5f, 0.05f, 0.03f, false,
            "sci_fi_select", "select_3", "select_4") },
        { SoundEffect.QuizSubmit,           E(SoundCategory.UI, 0.6f, 0f, 0f, false, "sci_fi_confirm", "synth_confirmation") },
        { SoundEffect.QuizCorrect,          E(SoundCategory.Reward, 0.8f, 0f, 0f, false,
            "grand_piano_positive_long", "vibraphone_positive_long") },
        // Wrong answers stay gentle: this is a devotional quiz, not a game show buzzer.
        { SoundEffect.QuizWrong,            E(SoundCategory.UI, 0.5f, 0f, 0f, false,
            "grand_piano_negative_quick", "music_box_negative_quick") },
        { SoundEffect.QuizClose,            E(SoundCategory.UI, 0.5f, 0f, 0f, false,
            "JDSherbert - Ultimate UI SFX Pack - Popup Close - 1", "book_close") },

        // ── Dhikr counter ────────────────────────────────────────────────────
        // The increment is the most-repeated sound in the game: a bead, quiet, with real pitch
        // variance so a long count never turns into a drone.
        //
        // One material, four recordings of it. The first pass mixed a wooden knock with three
        // xylophone notes, so consecutive taps sounded like different objects and the count read as a
        // melody being played at the player rather than beads moving under a thumb. Wood only now, and
        // quieter, because the count runs to 33 and anything bright gets tiring well before that.
        { SoundEffect.DhikrIncrement, E(SoundCategory.UI, 0.3f, 0.09f, 0.03f, false,
            "wood_small_hollow", "wood_small_drop", "wood_small_pickup", "wood_small_gather") },
        // Same wooden material, drop-led so it lands heavier than the increment: stepping back reads
        // as part of the same object rather than a UI pop from another family.
        { SoundEffect.DhikrDecrement, E(SoundCategory.UI, 0.3f, 0.05f, 0.03f, false,
            "wood_small_drop", "wood_small_hollow") },
        { SoundEffect.DhikrSubmit,    E(SoundCategory.Reward, 0.7f, 0f, 0f, false,
            "music_box_level_complete", "vibraphone_chime_positive") },
        { SoundEffect.DhikrClose,     E(SoundCategory.UI, 0.5f, 0f, 0f, false,
            "JDSherbert - Ultimate UI SFX Pack - Popup Close - 1", "book_close") },

        // ── Treasure boxes: the music-box family, escalating ─────────────────
        { SoundEffect.TreasureBoxAppear,  E(SoundCategory.World, 0.55f, 0.04f, 0.2f, true,
            "music_box_mystery", "jingle_bells_1") },
        { SoundEffect.TreasureBoxShow,    E(SoundCategory.World, 0.6f, 0f, 0.2f, false, "jingle_bells_2", "8_bit_mystery") },
        { SoundEffect.TreasureBoxTap,     E(SoundCategory.UI, 0.6f, 0f, 0.1f, false, "ceramic_jar_open", "keys_jingling") },
        { SoundEffect.TreasureBoxLocked,  E(SoundCategory.UI, 0.55f, 0f, 0.2f, false, "lock_lock", "music_box_negative") },
        { SoundEffect.TreasureBoxReady,   E(SoundCategory.UI, 0.6f, 0f, 0.2f, false, "lock_unlock", "music_box_chime_quick") },
        { SoundEffect.TreasureBoxLidOpen, E(SoundCategory.Reward, 0.8f, 0f, 0f, true,
            "creaky_door_short", "ceramic_jar_open") },
        { SoundEffect.TreasureBoxOpen,    E(SoundCategory.Reward, 0.8f, 0f, 0f, false,
            "music_box_level_complete", "8_bit_level_complete") },
        { SoundEffect.TreasureBoxRewardReveal, E(SoundCategory.Reward, 0.85f, 0f, 0f, false,
            "vibraphone_positive_long", "music_box_positive_long") },
        { SoundEffect.TreasureBoxSetComplete,  E(SoundCategory.Reward, 0.9f, 0f, 0f, false,
            "brass_level_complete", "music_box_level_complete") },
        { SoundEffect.RewardFinale,            E(SoundCategory.Reward, 0.85f, 0f, 0f, false,
            "grand_piano_level_complete", "vibraphone_level_complete") },

        // ── Economy and progression ──────────────────────────────────────────
        { SoundEffect.CoinGain,         E(SoundCategory.Reward, 0.6f, 0.04f, 0.08f, false,
            "coin_collect", "coins_gather_quick") },
        { SoundEffect.ItemPurchase,     E(SoundCategory.Reward, 0.7f, 0f, 0f, false,
            "coins_gather_medium", "chips_gather") },
        { SoundEffect.CoinSpend,        E(SoundCategory.UI, 0.55f, 0.03f, 0.08f, false,
            "coins_gather_small", "chips_place_1") },
        { SoundEffect.CoinInsufficient, E(SoundCategory.UI, 0.55f, 0f, 0.3f, false, "synth_warning", "8_bit_negative_quick") },
        { SoundEffect.PurchaseFailed,   E(SoundCategory.UI, 0.6f, 0f, 0.3f, false, "synth_error", "power_down_2") },
        { SoundEffect.XPGain,           E(SoundCategory.Reward, 0.45f, 0.05f, 0.1f, false,
            "xylophone_chime_quick", "match_xylophone_4", "match_xylophone_5") },
        { SoundEffect.LevelUp,          E(SoundCategory.Reward, 0.9f, 0f, 0f, false,
            "xylophone_level_complete", "power_up") },
        { SoundEffect.XPGainChartToggle, E(SoundCategory.UI, 0.5f, 0f, 0.05f, false, "map_open", "page_turn") },

        // ── Shop, minimap, camera, movement ──────────────────────────────────
        { SoundEffect.ShopOpenClose,   E(SoundCategory.UI, 0.55f, 0f, 0.1f, false, "map_open", "cardboard_push") },
        { SoundEffect.MinimapExpand,   E(SoundCategory.UI, 0.5f, 0f, 0.05f, false, "map_open", "paper_sort") },
        { SoundEffect.MinimapCollapse, E(SoundCategory.UI, 0.5f, 0f, 0.05f, false, "map_close", "paper_move") },
        { SoundEffect.CameraShutter,   E(SoundCategory.UI, 0.7f, 0f, 0.1f, false, "finger_click", "snap") },
        // Footsteps are driven per-step by the controller, so the variance here matters more than
        // the volume: four grass steps with jitter is the difference between walking and ticking.
        //
        // Foley, not the `digital_*` set: the digital footsteps are synthesised and read as a UI tick
        // rather than a person walking, which is what playtesting rejected. The concrete foley is the
        // recording the garden shipped with before the library existed and the one that was liked.
        { SoundEffect.Walk,            E(SoundCategory.World, 0.3f, 0.12f, 0f, false,
            "foley_footstep_concrete_4", "foley_footstep_concrete_1", "foley_footstep_concrete_2", "foley_footstep_concrete_3") },
        { SoundEffect.Run,             E(SoundCategory.World, 0.4f, 0.12f, 0f, false,
            "foley_footstep_gravel_1", "foley_footstep_gravel_2", "foley_footstep_gravel_3", "foley_footstep_gravel_4") },
        // Deliberately clipless. The idle loop in PlayerMovement plays this on a looping
        // AudioSource whenever the player stands still, and the only "human" vocals in the
        // 400-pack (man_0 .. man_10) are a man counting numbers out loud, not breathing -- on
        // a loop that read as the game counting at you. A null clip is handled: the idle branch
        // skips the loop entirely, so standing still is silent. Name a real breath/exhale clip
        // here once one is in the project and re-run the populate.
        { SoundEffect.Breathing,       E(SoundCategory.Voice, 0.35f, 0.06f, 0f, false) },

        // ── Misc ─────────────────────────────────────────────────────────────
        { SoundEffect.NotificationChime, E(SoundCategory.UI, 0.6f, 0f, 0.2f, false,
            "music_box_chime_quick", "jingle_bells_1") },
        { SoundEffect.TutorialAdvance,   E(SoundCategory.UI, 0.5f, 0.03f, 0.05f, false,
            "page_turn", "JDSherbert - Ultimate UI SFX Pack - Select - 1") },
    };
}
