using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Every distinct audible moment in the garden.
///
/// IMPORTANT: append only, never insert or reorder. <see cref="Sound.effect"/> serializes as the
/// underlying int, so inserting anywhere above silently reassigns every clip already wired up in
/// the scene. New entries go at the bottom, under the matching section comment.
/// </summary>
public enum SoundEffect
{
    ButtonClick,
    Walk,
    TreasureBoxOpen,
    QuestionMarkOrbOpen,
    QuizSubmit,
    QuizClose,
    ItemPlace,
    ShopOpenClose,
    TabSwitch,
    ItemPurchase,
    MinimapExpand,
    MinimapCollapse,
    DhikrIncrement,
    DhikrDecrement,
    DhikrSubmit,
    DhikrClose,
    TreasureBoxShow,
    XPGainChartToggle,
    ItemInteract,
    Run,
    Breathing,

    // Appended last on purpose: this enum is serialized by index on the AudioManager's `sounds` array,
    // so inserting anywhere above would silently reassign every clip already set up in the scene.
    CameraShutter,

    // -- Buttons and panels ---------------------------------------------------
    ButtonBack,
    ButtonDisabled,
    ToggleOn,
    ToggleOff,
    PanelOpen,
    PanelClose,
    SliderTick,

    // -- Messaging ------------------------------------------------------------
    ToastShow,
    WarningShow,
    ErrorShow,
    SuccessShow,

    // -- Garden areas ---------------------------------------------------------
    AreaUnlockFanfare,
    AreaUnlockReveal,
    AreaEnterUnlocked,
    AreaEnterLocked, // Unused: locked-zone crossings are silent. Kept so later ids keep their serialized values.
    AreaEnterPath,
    AreaFull,

    // -- Item handling --------------------------------------------------------
    ItemSelect,
    ItemDeselect,
    ItemGhostStep,
    ItemPlaceBlocked,
    ItemRotate,
    ItemPickUp,
    ItemStore,
    ItemGrowComplete,

    // -- Asset streaming and reveal -------------------------------------------
    AssetDownloadStart,
    AssetDownloadComplete,
    AssetDownloadFailed,
    AssetReveal,

    // -- Question-mark orbs ---------------------------------------------------
    OrbAppear,
    OrbFocus,
    OrbCollect,
    QuizOptionSelect,
    QuizCorrect,
    QuizWrong,

    // -- Treasure boxes -------------------------------------------------------
    TreasureBoxAppear,
    TreasureBoxTap,
    TreasureBoxLocked,
    TreasureBoxReady,
    TreasureBoxLidOpen,
    TreasureBoxRewardReveal,
    TreasureBoxSetComplete,
    RewardFinale,

    // -- Economy and progression ----------------------------------------------
    CoinGain,
    CoinSpend,
    CoinInsufficient,
    PurchaseFailed,
    XPGain,
    LevelUp,

    // -- Misc -----------------------------------------------------------------
    NotificationChime,
    TutorialAdvance,
    Whoosh
}

/// <summary>Mixing bus a sound belongs to, so whole families can be balanced at once.</summary>
public enum SoundCategory
{
    UI,
    World,
    Reward,
    Voice
}

[System.Serializable]
public class Sound
{
    public SoundEffect effect;

    /// <summary>Primary clip. Kept as the first field for backwards compatibility with scenes
    /// authored before <see cref="variations"/> existed.</summary>
    public AudioClip clip;

    [Tooltip("Optional alternates. When present, each play picks one of clip+variations at random " +
             "without repeating the previous pick, so repeated taps never sound mechanical.")]
    public AudioClip[] variations;

    [Range(0f, 1f)]
    public float volume = 1f;

    [Range(.1f, 3f)]
    public float pitch = 1f;

    [Tooltip("Random +/- offset applied to pitch on every play. 0 means dead-exact repeats.")]
    [Range(0f, 0.5f)]
    public float pitchVariance = 0f;

    [Tooltip("Minimum seconds between two plays of this effect. Stops rapid-fire callers " +
             "(toasts, grid steps, coin ticks) from stacking into a buzz.")]
    [Range(0f, 2f)]
    public float cooldown = 0f;

    public SoundCategory category = SoundCategory.UI;

    [Tooltip("Play positionally in the world instead of flat 2D. Only honoured by PlaySoundAt.")]
    public bool spatial = false;

    /// <summary>Index into the clip set chosen last time, so the next pick can avoid it.</summary>
    [System.NonSerialized] public int lastVariationIndex = -1;

    /// <summary>Unscaled time of the last play, for <see cref="cooldown"/>.</summary>
    [System.NonSerialized] public float lastPlayTime = -999f;

    /// <summary>Count of non-null clips: <see cref="clip"/> followed by <see cref="variations"/>.</summary>
    public int ClipCount
    {
        get
        {
            int n = clip != null ? 1 : 0;
            if (variations != null)
            {
                for (int i = 0; i < variations.Length; i++)
                    if (variations[i] != null) n++;
            }
            return n;
        }
    }

    /// <summary>Nth non-null clip, counting <see cref="clip"/> as index 0.</summary>
    public AudioClip ClipAt(int index)
    {
        if (clip != null)
        {
            if (index == 0) return clip;
            index--;
        }

        if (variations == null) return null;

        for (int i = 0; i < variations.Length; i++)
        {
            if (variations[i] == null) continue;
            if (index == 0) return variations[i];
            index--;
        }

        return null;
    }
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Sound Effects")]
    public Sound[] sounds;
    public AudioSource sfxSource;

    [Header("Background Music")]
    public AudioClip[] backgroundMusics;
    public AudioSource bgmSource;
    [Range(0f, 1f)]
    public float bgmVolume = 1f;

    [Header("Mixing")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float uiVolume = 1f;
    [Range(0f, 1f)] public float worldVolume = 1f;
    [Range(0f, 1f)] public float rewardVolume = 1f;
    [Range(0f, 1f)] public float voiceVolume = 1f;

    [Header("Voices")]
    [Tooltip("Pooled 2D one-shot sources. Each play claims a free voice so overlapping sounds keep " +
             "their own pitch; a single shared source retunes everything already in flight.")]
    [Range(2, 24)]
    public int voiceCount = 10;

    /// <summary>Fast lookup built once from <see cref="sounds"/>.</summary>
    private Dictionary<SoundEffect, Sound> _lookup;

    private AudioSource[] _voices;
    private int _nextVoice;

    /// <summary>Effects already reported as missing, so a per-frame caller logs once, not forever.</summary>
    private readonly HashSet<SoundEffect> _warned = new HashSet<SoundEffect>();

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);

        EnsureSources();
        BuildLookup();

        if (backgroundMusics != null && backgroundMusics.Length > 0)
        {
            PlayRandomBGM();
        }

        UpdateAudioSettings();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void BuildLookup()
    {
        _lookup = new Dictionary<SoundEffect, Sound>();
        if (sounds == null) return;

        foreach (Sound s in sounds)
        {
            if (s == null) continue;
            // First entry wins: a duplicated effect in the inspector should not shadow the one the
            // designer filled in first.
            if (!_lookup.ContainsKey(s.effect)) _lookup[s.effect] = s;
        }
    }

    /// <summary>
    /// Rebuilds the effect lookup. Call after mutating <see cref="sounds"/> at runtime or from
    /// editor tooling; <see cref="Awake"/> already does it for normal play.
    /// </summary>
    public void RefreshLookup() => BuildLookup();

    /// <summary>
    /// Guarantees the audio sources live on this GameObject. Sources assigned from the scene
    /// (e.g. on the Controller) are destroyed on scene load while this manager survives via
    /// DontDestroyOnLoad, leaving dangling references that throw on every PlaySound call.
    /// </summary>
    private void EnsureSources()
    {
        if (!IsOwnSource(sfxSource))
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }

        if (!IsOwnSource(bgmSource))
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            bgmSource.loop = false;
            bgmSource.volume = bgmVolume;
        }

        EnsureVoices();
    }

    private void EnsureVoices()
    {
        int want = Mathf.Max(2, voiceCount);
        if (_voices != null && _voices.Length == want && IsOwnSource(_voices[0])) return;

        _voices = new AudioSource[want];
        for (int i = 0; i < want; i++)
        {
            AudioSource voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0f;
            _voices[i] = voice;
        }
    }

    private bool IsOwnSource(AudioSource source)
    {
        // The null check must come first: reading .gameObject on a destroyed object throws.
        return source != null && source.gameObject == gameObject;
    }

    // Music mute state
    public bool IsMusicMuted
    {
        get { return PlayerPrefs.GetInt("MusicMuted", 0) == 1; }
        set
        {
            PlayerPrefs.SetInt("MusicMuted", value ? 1 : 0);
            PlayerPrefs.Save();
            UpdateAudioSettings();
        }
    }

    // SFX mute state
    public bool IsSfxMuted
    {
        get { return PlayerPrefs.GetInt("SFXMuted", 0) == 1; }
        set
        {
            PlayerPrefs.SetInt("SFXMuted", value ? 1 : 0);
            PlayerPrefs.Save();
            UpdateAudioSettings();
        }
    }

    public void UpdateAudioSettings()
    {
        EnsureSources();
        bgmSource.mute = IsMusicMuted;
        sfxSource.mute = IsSfxMuted;

        bool sfxMuted = IsSfxMuted;
        if (_voices != null)
        {
            for (int i = 0; i < _voices.Length; i++)
                if (_voices[i] != null) _voices[i].mute = sfxMuted;
        }
    }

    void Update()
    {
        if (backgroundMusics == null || backgroundMusics.Length == 0) return;

        if (!IsOwnSource(bgmSource))
        {
            EnsureSources();
            UpdateAudioSettings();
        }

        if (!bgmSource.isPlaying)
        {
            PlayRandomBGM();
        }
    }

    private void PlayRandomBGM()
    {
        if (backgroundMusics == null || backgroundMusics.Length == 0) return;

        EnsureSources();

        int randomIndex = Random.Range(0, backgroundMusics.Length);
        bgmSource.clip = backgroundMusics[randomIndex];
        bgmSource.Play();
    }

    private float VolumeFor(SoundCategory category)
    {
        switch (category)
        {
            case SoundCategory.World:  return worldVolume;
            case SoundCategory.Reward: return rewardVolume;
            case SoundCategory.Voice:  return voiceVolume;
            default:                   return uiVolume;
        }
    }

    /// <summary>
    /// Resolves an effect to its configured <see cref="Sound"/>, honouring the cooldown.
    /// Returns null when the effect is unmapped, has no clip, or fired too recently.
    /// </summary>
    private Sound Resolve(SoundEffect effect)
    {
        if (_lookup == null) BuildLookup();

        Sound s;
        if (!_lookup.TryGetValue(effect, out s))
        {
            if (_warned.Add(effect)) Debug.LogWarning("Sound: " + effect + " not found!");
            return null;
        }

        if (s.ClipCount == 0) return null;

        if (s.cooldown > 0f && Time.unscaledTime - s.lastPlayTime < s.cooldown) return null;
        s.lastPlayTime = Time.unscaledTime;

        return s;
    }

    /// <summary>Picks a clip, avoiding an immediate repeat when alternates exist.</summary>
    private AudioClip PickClip(Sound s)
    {
        int count = s.ClipCount;
        if (count <= 1) return s.ClipAt(0);

        int index = Random.Range(0, count);
        if (index == s.lastVariationIndex) index = (index + 1) % count;
        s.lastVariationIndex = index;

        return s.ClipAt(index);
    }

    private AudioSource NextVoice()
    {
        EnsureVoices();

        // Prefer a genuinely idle voice; fall back to round-robin stealing when all are busy.
        for (int i = 0; i < _voices.Length; i++)
        {
            AudioSource candidate = _voices[(_nextVoice + i) % _voices.Length];
            if (candidate != null && !candidate.isPlaying)
            {
                _nextVoice = (_nextVoice + i + 1) % _voices.Length;
                return candidate;
            }
        }

        AudioSource stolen = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _voices.Length;
        return stolen;
    }

    /// <summary>
    /// Next clip for an effect, rotating through its variations exactly as a normal play would, for
    /// the few callers that must drive their own <see cref="AudioSource"/> rather than a pooled voice
    /// (footsteps, which retune per step). Returns null when the effect has no clips.
    ///
    /// Deliberately skips the cooldown: these callers already rate-limit themselves, and a cooldown
    /// miss here would return null and drop the step rather than merely skipping a stacked play.
    /// </summary>
    public AudioClip NextClip(SoundEffect effect)
    {
        if (_lookup == null) BuildLookup();

        Sound s;
        if (!_lookup.TryGetValue(effect, out s)) return null;
        if (s.ClipCount == 0) return null;

        return PickClip(s);
    }

    public void PlaySound(SoundEffect effect)
    {
        Sound s = Resolve(effect);
        if (s == null) return;

        PlayResolved(s, Vector3.zero, false);
    }

    /// <summary>
    /// Plays an effect at a world position. Sounds flagged <see cref="Sound.spatial"/> get a
    /// temporary positional source; everything else falls back to the flat 2D path, so a caller
    /// never has to know which is which.
    /// </summary>
    public void PlaySoundAt(SoundEffect effect, Vector3 worldPosition)
    {
        Sound s = Resolve(effect);
        if (s == null) return;

        PlayResolved(s, worldPosition, s.spatial);
    }

    private void PlayResolved(Sound s, Vector3 worldPosition, bool spatial)
    {
        AudioClip chosen = PickClip(s);
        if (chosen == null) return;

        float gain = s.volume * VolumeFor(s.category) * masterVolume;
        float finalPitch = s.pitchVariance > 0f
            ? Mathf.Clamp(s.pitch + Random.Range(-s.pitchVariance, s.pitchVariance), 0.1f, 3f)
            : s.pitch;

        if (!spatial)
        {
            AudioSource voice = NextVoice();
            voice.mute = IsSfxMuted;
            voice.spatialBlend = 0f;
            voice.pitch = finalPitch;
            voice.PlayOneShot(chosen, gain);
            return;
        }

        if (IsSfxMuted) return;

        // A detached one-shot: PlayClipAtPoint cannot carry pitch, so build the throwaway source here.
        GameObject go = new GameObject("SFX_" + s.effect);
        go.transform.position = worldPosition;

        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = chosen;
        src.volume = gain;
        src.pitch = finalPitch;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 4f;
        src.maxDistance = 45f;
        src.Play();

        Destroy(go, chosen.length / Mathf.Max(0.1f, finalPitch) + 0.1f);
    }
}
