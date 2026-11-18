using UnityEngine;

/// <summary>
/// Scene hub for the riding extras: the shared popup, the flying controls, the Orb Run HUD and the art
/// they use. One lives in the scene (built by <c>Jannah &gt; Animals &gt; Build Ride Features</c>); the
/// systems that need these find it through <see cref="Instance"/>, and every one of them carries on
/// without it (riding just skips the extras).
/// </summary>
public class RideFeatures : MonoBehaviour
{
    private static RideFeatures _instance;

    public static RideFeatures Instance
    {
        get
        {
            if (_instance == null) _instance = FindFirstObjectByType<RideFeatures>(FindObjectsInactive.Include);
            return _instance;
        }
    }

    [Header("UI")]
    public RidePromptPanel promptPanel;
    [Tooltip("Up/down control shown while riding something that flies.")]
    public AltitudeStick altitudeStick;

    [Header("Icons")]
    public Sprite camelIcon;
    public Sprite pegasusIcon;
    public Sprite orbIcon;
    public Sprite coinIcon;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;
    }

    /// <summary>The portrait for <paramref name="animal"/>: the pegasus for fliers, the camel otherwise.</summary>
    public Sprite IconFor(GrazingAnimal animal)
    {
        return animal != null && animal.canFly ? pegasusIcon : camelIcon;
    }

    /// <summary>The localized name of <paramref name="animal"/>'s kind ("camel" / "pegasus").</summary>
    public static string NameFor(GrazingAnimal animal)
    {
        return Loc(animal != null && animal.canFly ? "ride.pegasus" : "ride.camel");
    }

    /// <summary>Localized text, or the key itself when localization isn't up (tests, odd scenes).</summary>
    public static string Loc(string key, params object[] args)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (loc == null) return args != null && args.Length > 0 ? key + " " + string.Join(" ", args) : key;
        return args != null && args.Length > 0 ? loc.Get(key, args) : loc.Get(key);
    }

    /// <summary>Shows a toast, if the toast system is in the scene.</summary>
    public static void Toast(string message, ToastTone tone = ToastTone.Info)
    {
        if (ToastMessageManager.Instance != null) ToastMessageManager.Instance.ShowToast(message, tone);
    }
}
