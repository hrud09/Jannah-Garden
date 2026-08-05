using UnityEngine;
using System.Collections.Generic;

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
    CameraShutter
}

[System.Serializable]
public class Sound
{
    public SoundEffect effect;
    public AudioClip clip;
    [Range(0f, 1f)]
    public float volume = 1f;
    [Range(.1f, 3f)]
    public float pitch = 1f;
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

    public void PlaySound(SoundEffect effect)
    {
        Sound s = System.Array.Find(sounds, item => item.effect == effect);
        if (s == null)
        {
            Debug.LogWarning("Sound: " + effect + " not found!");
            return;
        }

        if (s.clip == null) return;

        EnsureSources();

        sfxSource.pitch = s.pitch;
        sfxSource.PlayOneShot(s.clip, s.volume);
    }
}
