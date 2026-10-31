using UnityEngine;

/// <summary>
/// Null-safe shorthand for firing sound effects.
///
/// The manager is a DontDestroyOnLoad singleton that may not exist yet during boot, in the very
/// first frames of a scene load, or in editor test scenes. Every gameplay call site used to carry
/// its own `if (AudioManager.Instance != null)` guard; this collapses that to one place so adding
/// a sound to a system is a single readable line.
/// </summary>
public static class GameSfx
{
    /// <summary>Fires a flat 2D effect. Silently does nothing when no manager is alive.</summary>
    public static void Play(SoundEffect effect)
    {
        AudioManager manager = AudioManager.Instance;
        if (manager != null) manager.PlaySound(effect);
    }

    /// <summary>
    /// Fires an effect positioned in the world. Effects not marked spatial on the manager fall
    /// back to the flat 2D path, so callers can always pass a position without checking.
    /// </summary>
    public static void PlayAt(SoundEffect effect, Vector3 worldPosition)
    {
        AudioManager manager = AudioManager.Instance;
        if (manager != null) manager.PlaySoundAt(effect, worldPosition);
    }

    /// <summary>
    /// Fires at a transform when it is still alive, otherwise flat. Convenience for callbacks that
    /// land after the object they describe has already been despawned.
    /// </summary>
    public static void PlayAt(SoundEffect effect, Transform at)
    {
        if (at == null) Play(effect);
        else PlayAt(effect, at.position);
    }

    /// <summary>
    /// Picks the message chime that matches the severity of a toast, so a failure never sounds
    /// like a confirmation. See <see cref="ToastTone"/>.
    /// </summary>
    public static void PlayToast(ToastTone tone)
    {
        switch (tone)
        {
            case ToastTone.Silent:  break;
            case ToastTone.Success: Play(SoundEffect.SuccessShow); break;
            case ToastTone.Warning: Play(SoundEffect.WarningShow); break;
            case ToastTone.Error:   Play(SoundEffect.ErrorShow);   break;
            default:                Play(SoundEffect.ToastShow);   break;
        }
    }
}

/// <summary>Severity of a toast message, which selects its chime.</summary>
public enum ToastTone
{
    Info,
    Success,
    Warning,
    Error,

    /// <summary>
    /// Shows the message without a chime. For callers that already sound the event themselves
    /// (coins moving, an item placed) and would otherwise double up.
    /// </summary>
    Silent
}
