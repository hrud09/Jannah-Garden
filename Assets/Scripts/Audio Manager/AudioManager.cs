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
    Breathing
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

        // Initialize single SFX source
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
        }

        // Initialize Background Music
        if (backgroundMusics != null && backgroundMusics.Length > 0)
        {
            if (bgmSource == null)
            {
                bgmSource = gameObject.AddComponent<AudioSource>();
            }
            bgmSource.loop = false;
            bgmSource.volume = bgmVolume;
            PlayRandomBGM();
        }

        UpdateAudioSettings();
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
        if (bgmSource != null)
        {
            bgmSource.mute = IsMusicMuted;
        }
        if (sfxSource != null)
        {
            sfxSource.mute = IsSfxMuted;
        }
    }

    void Update()
    {
        if (bgmSource != null && !bgmSource.isPlaying && backgroundMusics != null && backgroundMusics.Length > 0)
        {
            PlayRandomBGM();
        }
    }

    private void PlayRandomBGM()
    {
        if (backgroundMusics == null || backgroundMusics.Length == 0) return;

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
        
        sfxSource.pitch = s.pitch;
        sfxSource.PlayOneShot(s.clip, s.volume);
    }
}
