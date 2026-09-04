using System.Collections.Generic;
using System.Text;

/// <summary>One contiguous same-script piece of a line, e.g. the "ﷺ" in "নবী ﷺ প্রথম" splits that
/// string into a Bengali run, an Arabic run, and another Bengali run.</summary>
public struct ScriptRun
{
    public string Text;
    public AppLocale Locale;
    /// <summary>Index of this run's first character in the original (unsplit) line — lets a shaper
    /// offset cluster values back to that line's coordinate space.</summary>
    public int StartIndex;
}

/// <summary>
/// Splits a line into same-script runs so each can be shaped (and rendered) against the right font —
/// HarfBuzz shapes one script/font at a time, and a mixed Bengali+Arabic string shaped whole against a
/// single font just produces tofu boxes for whichever script that font doesn't cover (e.g. "ﷺ" shaped
/// against the Bengali face).
///
/// Deliberately simple: a "neutral" character (whitespace, punctuation) always extends the
/// current run rather than starting a new one, so a lone hyphen or space at a script boundary doesn't
/// spawn a needless extra run. This matches how a space-delimited embedded word/symbol (the actual
/// content pattern in this game, e.g. "নবী ﷺ প্রথম") behaves correctly; a neutral character directly
/// wedged between two different scripts with no separating whitespace (e.g. "ﷺ-এর") attaches to
/// whichever run reaches it first, which is not full Unicode bidi conformance but is a reasonable,
/// documented simplification for the actual content this project ships (mirroring how
/// <see cref="ArabicTextShaper"/> already simplifies bidi rather than implementing UAX #9 in full).
///
/// Bengali, Arabic, and Latin (ASCII letters/digits) are recognized as distinct scripts. Latin exists
/// so untranslated fallback content (e.g. a locale-suffixed data file that doesn't have a Bengali
/// translation yet, so the English source string leaks through) and plain numeral strings (e.g. a
/// dhikr counter, which is always formatted with plain ASCII digits, not localized numerals) still get
/// shaped against a Latin face instead of being folded into the surrounding Bengali/Arabic run and
/// mis-shaped against a font whose script-specific OpenType rules don't apply to them. ASCII digits are
/// treated as strong Latin (not neutral) specifically so a pure-digit string with no other strong
/// character anywhere in it — the dhikr counter case — resolves to Latin instead of falling back to
/// <paramref name="baseLocale"/>. This project's third locale (en) never reaches this path as a whole
/// line (see LocalizedRendering) — Latin only shows up here as an embedded run within Bengali content.
/// </summary>
public static class ScriptRunSplitter
{
    private static bool IsArabic(char c) =>
        (c >= '؀' && c <= 'ۿ') ||  // Arabic
        (c >= 'ݐ' && c <= 'ݿ') ||  // Arabic Supplement
        (c >= 'ࢠ' && c <= 'ࣿ') ||  // Arabic Extended-A
        (c >= 'ﭐ' && c <= '﷿') ||  // Arabic Presentation Forms-A (e.g. U+FDFA ﷺ)
        (c >= 'ﹰ' && c <= '﻿');    // Arabic Presentation Forms-B

    private static bool IsBengali(char c) => c >= 'ঀ' && c <= '৿';

    private static bool IsLatin(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

    /// <summary>Splits <paramref name="line"/> into script runs, defaulting to <paramref name="baseLocale"/>
    /// for neutral characters that appear before any strong-script character is seen.</summary>
    public static List<ScriptRun> Split(string line, AppLocale baseLocale)
    {
        var runs = new List<ScriptRun>();
        if (string.IsNullOrEmpty(line)) return runs;

        var sb = new StringBuilder();
        AppLocale currentLocale = baseLocale;
        int runStart = 0;
        bool haveContent = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            AppLocale? strong = IsArabic(c) ? AppLocale.ar : IsBengali(c) ? AppLocale.bn : IsLatin(c) ? AppLocale.en : (AppLocale?)null;

            if (strong.HasValue && haveContent && strong.Value != currentLocale)
            {
                runs.Add(new ScriptRun { Text = sb.ToString(), Locale = currentLocale, StartIndex = runStart });
                sb.Clear();
                runStart = i;
                currentLocale = strong.Value;
            }
            else if (!haveContent && strong.HasValue)
            {
                currentLocale = strong.Value;
            }

            sb.Append(c);
            haveContent = true;
        }

        if (sb.Length > 0) runs.Add(new ScriptRun { Text = sb.ToString(), Locale = currentLocale, StartIndex = runStart });
        return runs;
    }
}
