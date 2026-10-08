using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// An in-game language picker: one button per shipped <see cref="AppLocale"/>, wired to
/// <see cref="LocalizationManager.SetLocale(string, bool)"/>. Drop it on a settings row and assign one
/// <see cref="Option"/> per button.
///
/// Until now the active locale could only be driven by the Flutter host (FlutterCommands.SetLocale) or
/// by whatever was last cached in PlayerPrefs, so a player running the Unity build on its own had no way
/// to change language. SetLocale already persists to PlayerPrefs and raises OnLocaleChanged, so this
/// component only has to call it — every subscribed label re-renders itself.
///
/// Each button's caption is the language's own endonym ("বাংলা", "العربية"), which is deliberately NOT
/// read from the localization tables: a language's name in its own script is the same string whatever
/// the active locale is, and showing all four at once is what makes the row readable to someone who
/// can't read the current language — which is the whole reason they're looking for this control.
/// </summary>
public class LanguageSelector : MonoBehaviour
{
    [Serializable]
    public class Option
    {
        public AppLocale locale;
        [Tooltip("Button the player taps to switch to this language.")]
        public Button button;
        [Tooltip("Caption inside the button. Filled with the language's endonym, shaped for that "
               + "language's own script — leave the authored text alone, it gets overwritten.")]
        public TMP_Text label;
        [Tooltip("Optional graphic tinted to show which language is active. Falls back to the button's "
               + "own targetGraphic when left empty.")]
        public Graphic highlight;
    }

    /// <summary>
    /// Each language's name written in its own script. Kept here rather than in ui_{locale}.json for the
    /// reason given in the class doc — these are fixed labels, not translations of anything.
    /// </summary>
    private static readonly Dictionary<AppLocale, string> Endonyms = new Dictionary<AppLocale, string>
    {
        { AppLocale.en, "English" },
        { AppLocale.ar, "العربية" },
        { AppLocale.bn, "বাংলা" },
        { AppLocale.ur, "اردو" },
    };

    [Tooltip("One entry per language button. Order here is the order the player sees.")]
    public List<Option> options = new List<Option>();

    [Header("Selection Tint")]
    [Tooltip("Highlight colour for the language currently in use.")]
    public Color selectedColor = new Color(0.62f, 0.91f, 0.60f, 1f);
    [Tooltip("Highlight colour for every other language.")]
    public Color unselectedColor = new Color(1f, 1f, 1f, 0.35f);

    private void Awake()
    {
        foreach (Option option in options)
        {
            if (option == null || option.button == null) continue;

            AppLocale target = option.locale; // captured per-iteration, not by reference to the loop var
            option.button.onClick.AddListener(() => SelectLocale(target));
        }
    }

    private void OnEnable()
    {
        LocalizationManager.OnLocaleChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        LocalizationManager.OnLocaleChanged -= Refresh;
    }

    private void SelectLocale(AppLocale locale)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        if (loc == null) return;
        if (loc.CurrentLocale == locale) return;

        if (AudioManager.Instance != null) AudioManager.Instance.PlaySound(SoundEffect.ButtonClick);

        // Raises OnLocaleChanged, which calls Refresh() below and re-renders every other localized
        // label in the game. It also writes the choice to PlayerPrefs, so it survives a restart.
        loc.SetLocale(locale.ToString());
    }

    /// <summary>
    /// Repaints every button's caption and highlight. Called on enable and on every locale change —
    /// including ones this component didn't cause (e.g. the Flutter host sending SET_LOCALE), so the
    /// row always agrees with the language actually in use.
    /// </summary>
    private void Refresh()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        AppLocale active = loc != null ? loc.CurrentLocale : AppLocale.en;

        foreach (Option option in options)
        {
            if (option == null) continue;

            if (option.label != null)
            {
                string endonym = Endonyms.TryGetValue(option.locale, out string value)
                    ? value
                    : option.locale.ToString();

                // Shaped with the button's OWN locale, not the active one: "বাংলা" has to go through
                // the HarfBuzz path and "العربية" through the Arabic joiner no matter what language the
                // game is currently in, or the row renders as broken letterforms for three of four
                // buttons. One label throwing must not cost the other three their caption or tint.
                try
                {
                    LocalizedRendering.SetText(option.label, endonym, option.locale);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LanguageSelector] Failed to render the '{option.locale}' caption: {e}");
                }
            }

            Graphic highlight = option.highlight != null
                ? option.highlight
                : (option.button != null ? option.button.targetGraphic : null);

            if (highlight != null) highlight.color = option.locale == active ? selectedColor : unselectedColor;
        }
    }
}
