using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Stock TextMeshPro draws Arabic text as isolated letterforms in logical (left-to-right) order — wrong
/// on both counts: Arabic letters must join into their initial/medial/final presentation forms, and the
/// whole run must be laid out right-to-left. This class fixes both before a string reaches a TMP_Text.
///
/// Scope (deliberately kept lightweight rather than a full Unicode BiDi/shaping engine):
///  - Covers the standard Arabic letters + Lam-Alef ligatures (Arabic Presentation Forms-B block).
///  - Leaves runs of digits/Latin characters inside an Arabic sentence in their own reading order, so
///    "٣ items" style mixed content doesn't come out backwards.
///  - Rich-text tags (&lt;color=...&gt;, &lt;b&gt;, …) are only handled when they wrap a whole *line*
///    (the pattern every string in this project actually uses, e.g. "&lt;color=..&gt;Line one&lt;/color&gt;\nLine two")
///    — tags interleaved mid-sentence are not specially reordered.
///  - No tashkeel (diacritics) reordering beyond passing them through untouched.
/// Treat this as a solid MVP and give it a real visual pass in Unity with real Arabic content before
/// shipping — hand-built presentation-form tables are easy to get subtly wrong.
/// </summary>
public static class ArabicTextShaper
{
    private static readonly Regex ArabicRangeRegex = new Regex("[؀-ۿ]");
    private static readonly Regex LeadingTagRegex = new Regex("^(?:<[^>]+>)+");
    private static readonly Regex TrailingTagRegex = new Regex("(?:<[^>]+>)+$");

    // Base Arabic letter -> presentation forms [isolated, initial, medial, final]. 0 = no such form.
    // Letters that only carry an isolated+final pair (no initial/medial) never join to the letter after
    // them — e.g. alef, dal, reh, waw — so the letter that follows one always starts a fresh join group.
    private static readonly Dictionary<char, ushort[]> Forms = new Dictionary<char, ushort[]>
    {
        { 'ء', new ushort[] { 0xFE80, 0, 0, 0 } },                     // HAMZA
        { 'آ', new ushort[] { 0xFE81, 0, 0, 0xFE82 } },                // ALEF MADDA
        { 'أ', new ushort[] { 0xFE83, 0, 0, 0xFE84 } },                // ALEF HAMZA ABOVE
        { 'ؤ', new ushort[] { 0xFE85, 0, 0, 0xFE86 } },                // WAW HAMZA
        { 'إ', new ushort[] { 0xFE87, 0, 0, 0xFE88 } },                // ALEF HAMZA BELOW
        { 'ئ', new ushort[] { 0xFE89, 0xFE8B, 0xFE8C, 0xFE8A } },      // YEH HAMZA
        { 'ا', new ushort[] { 0xFE8D, 0, 0, 0xFE8E } },                // ALEF
        { 'ب', new ushort[] { 0xFE8F, 0xFE91, 0xFE92, 0xFE90 } },      // BEH
        { 'ة', new ushort[] { 0xFE93, 0, 0, 0xFE94 } },                // TEH MARBUTA
        { 'ت', new ushort[] { 0xFE95, 0xFE97, 0xFE98, 0xFE96 } },      // TEH
        { 'ث', new ushort[] { 0xFE99, 0xFE9B, 0xFE9C, 0xFE9A } },      // THEH
        { 'ج', new ushort[] { 0xFE9D, 0xFE9F, 0xFEA0, 0xFE9E } },      // JEEM
        { 'ح', new ushort[] { 0xFEA1, 0xFEA3, 0xFEA4, 0xFEA2 } },      // HAH
        { 'خ', new ushort[] { 0xFEA5, 0xFEA7, 0xFEA8, 0xFEA6 } },      // KHAH
        { 'د', new ushort[] { 0xFEA9, 0, 0, 0xFEAA } },                // DAL
        { 'ذ', new ushort[] { 0xFEAB, 0, 0, 0xFEAC } },                // THAL
        { 'ر', new ushort[] { 0xFEAD, 0, 0, 0xFEAE } },                // REH
        { 'ز', new ushort[] { 0xFEAF, 0, 0, 0xFEB0 } },                // ZAIN
        { 'س', new ushort[] { 0xFEB1, 0xFEB3, 0xFEB4, 0xFEB2 } },      // SEEN
        { 'ش', new ushort[] { 0xFEB5, 0xFEB7, 0xFEB8, 0xFEB6 } },      // SHEEN
        { 'ص', new ushort[] { 0xFEB9, 0xFEBB, 0xFEBC, 0xFEBA } },      // SAD
        { 'ض', new ushort[] { 0xFEBD, 0xFEBF, 0xFEC0, 0xFEBE } },      // DAD
        { 'ط', new ushort[] { 0xFEC1, 0xFEC3, 0xFEC4, 0xFEC2 } },      // TAH
        { 'ظ', new ushort[] { 0xFEC5, 0xFEC7, 0xFEC8, 0xFEC6 } },      // ZAH
        { 'ع', new ushort[] { 0xFEC9, 0xFECB, 0xFECC, 0xFECA } },      // AIN
        { 'غ', new ushort[] { 0xFECD, 0xFECF, 0xFED0, 0xFECE } },      // GHAIN
        { 'ف', new ushort[] { 0xFED1, 0xFED3, 0xFED4, 0xFED2 } },      // FEH
        { 'ق', new ushort[] { 0xFED5, 0xFED7, 0xFED8, 0xFED6 } },      // QAF
        { 'ك', new ushort[] { 0xFED9, 0xFEDB, 0xFEDC, 0xFEDA } },      // KAF
        { 'ل', new ushort[] { 0xFEDD, 0xFEDF, 0xFEE0, 0xFEDE } },      // LAM
        { 'م', new ushort[] { 0xFEE1, 0xFEE3, 0xFEE4, 0xFEE2 } },      // MEEM
        { 'ن', new ushort[] { 0xFEE5, 0xFEE7, 0xFEE8, 0xFEE6 } },      // NOON
        { 'ه', new ushort[] { 0xFEE9, 0xFEEB, 0xFEEC, 0xFEEA } },      // HEH
        { 'و', new ushort[] { 0xFEED, 0, 0, 0xFEEE } },                // WAW
        { 'ى', new ushort[] { 0xFEEF, 0, 0, 0xFEF0 } },                // ALEF MAKSURA
        { 'ي', new ushort[] { 0xFEF1, 0xFEF3, 0xFEF4, 0xFEF2 } },      // YEH

        // Perso-Arabic extension letters needed for Urdu (not used by Arabic itself). Presentation forms
        // confirmed against the Unicode Character Database's compatibility decomposition for each codepoint
        // (e.g. U+FB56 decomposes as "<isolated> 067E") and cross-checked against Noto Sans Arabic's cmap.
        { 'پ', new ushort[] { 0xFB56, 0xFB58, 0xFB59, 0xFB57 } },      // PEH
        { 'ٹ', new ushort[] { 0xFB66, 0xFB68, 0xFB69, 0xFB67 } },      // TTEHEH
        { 'چ', new ushort[] { 0xFB7A, 0xFB7C, 0xFB7D, 0xFB7B } },      // TCHEH
        { 'ڈ', new ushort[] { 0xFB88, 0, 0, 0xFB89 } },                // DDAL
        { 'ڑ', new ushort[] { 0xFB8C, 0, 0, 0xFB8D } },                // RREH
        { 'ژ', new ushort[] { 0xFB8A, 0, 0, 0xFB8B } },                // JEH
        { 'گ', new ushort[] { 0xFB92, 0xFB94, 0xFB95, 0xFB93 } },      // GAF
        { 'ں', new ushort[] { 0xFB9E, 0, 0, 0xFB9F } },                // NOON GHUNNA
        { 'ھ', new ushort[] { 0xFBAA, 0xFBAC, 0xFBAD, 0xFBAB } },      // HEH DOACHASHMEE
        { 'ے', new ushort[] { 0xFBAE, 0, 0, 0xFBAF } },                // YEH BARREE
    };

    // Lam-Alef ligatures: LAM followed by one of the four Alef forms collapses into a single glyph.
    // Keyed by the Alef variant; value is [isolated, final].
    private static readonly Dictionary<char, ushort[]> LamAlefLigatures = new Dictionary<char, ushort[]>
    {
        { 'آ', new ushort[] { 0xFEF5, 0xFEF6 } }, // LAM + ALEF MADDA
        { 'أ', new ushort[] { 0xFEF7, 0xFEF8 } }, // LAM + ALEF HAMZA ABOVE
        { 'إ', new ushort[] { 0xFEF9, 0xFEFA } }, // LAM + ALEF HAMZA BELOW
        { 'ا', new ushort[] { 0xFEFB, 0xFEFC } }, // LAM + ALEF
    };

    private const char Lam = 'ل';

    /// <summary>
    /// Reshapes and visually reorders Arabic content for TMP display. Safe to call on any string —
    /// text with no Arabic characters is returned unchanged, so callers don't need to check first.
    /// </summary>
    public static string Shape(string text)
    {
        if (string.IsNullOrEmpty(text) || !ArabicRangeRegex.IsMatch(text)) return text;

        // Process each line independently so "\n" boundaries (used throughout the toast/tutorial/result
        // strings) stay in their original top-to-bottom order.
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = ShapeLineWithTags(lines[i]);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Strips rich-text tags that wrap a single line (e.g. "&lt;color=#FFD700&gt;...&lt;/color&gt;" as
    /// its own line of a multi-line message — the common pattern in this project's content, where only
    /// the first line of a result message is colored), shapes the inner text, then restores the tags
    /// unmoved. Tags interleaved mid-word are not specially handled — see the class-level doc comment.
    /// </summary>
    private static string ShapeLineWithTags(string line)
    {
        if (string.IsNullOrEmpty(line) || !ArabicRangeRegex.IsMatch(line)) return line;

        Match leading = LeadingTagRegex.Match(line);
        string prefix = leading.Success ? leading.Value : string.Empty;

        string remainder = line.Substring(prefix.Length);
        Match trailing = TrailingTagRegex.Match(remainder);
        string suffix = trailing.Success ? trailing.Value : string.Empty;

        string inner = remainder.Substring(0, remainder.Length - suffix.Length);

        return prefix + ShapeLine(inner) + suffix;
    }

    private static string ShapeLine(string line)
    {
        if (string.IsNullOrEmpty(line) || !ArabicRangeRegex.IsMatch(line)) return line;

        List<char> shaped = new List<char>(line.Length);

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (!Forms.TryGetValue(c, out ushort[] forms))
            {
                shaped.Add(c); // Not an Arabic letter (digit, space, Latin, punctuation) — pass through.
                continue;
            }

            char? prevJoined = PreviousJoiningLetter(line, i);
            bool joinsPrev = prevJoined.HasValue && Forms.TryGetValue(prevJoined.Value, out ushort[] prevForms) && ConnectsForward(prevForms);

            // Lam directly followed by an Alef variant becomes a single ligature glyph instead of two.
            if (c == Lam && i + 1 < line.Length && LamAlefLigatures.TryGetValue(line[i + 1], out ushort[] ligature))
            {
                ushort glyph = joinsPrev ? ligature[1] : ligature[0]; // [isolated, final]
                shaped.Add((char)glyph);
                i++; // consume the Alef too
                continue;
            }

            bool connectsNext = ConnectsForward(forms) && NextIsJoinable(line, i);

            ushort chosen;
            if (joinsPrev && connectsNext) chosen = forms[2] != 0 ? forms[2] : forms[0];       // medial
            else if (joinsPrev) chosen = forms[3] != 0 ? forms[3] : forms[0];                  // final
            else if (connectsNext) chosen = forms[1] != 0 ? forms[1] : forms[0];               // initial
            else chosen = forms[0];                                                            // isolated

            shaped.Add((char)chosen);
        }

        return ReorderForDisplay(shaped);
    }

    /// <summary>Walks backward over combining marks/diacritics to find the nearest actual letter.</summary>
    private static char? PreviousJoiningLetter(string line, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            char c = line[i];
            if (Forms.ContainsKey(c)) return c;
            if (!IsArabicCombiningMark(c)) return null; // hit a space/Latin/digit — no join across it
        }
        return null;
    }

    private static bool NextIsJoinable(string line, int index)
    {
        for (int i = index + 1; i < line.Length; i++)
        {
            char c = line[i];
            if (Forms.ContainsKey(c)) return true;
            if (!IsArabicCombiningMark(c)) return false;
        }
        return false;
    }

    private static bool IsArabicCombiningMark(char c) => c >= 'ً' && c <= 'ْ';

    private static bool ConnectsForward(ushort[] forms) => forms[1] != 0 || forms[2] != 0;

    /// <summary>
    /// Reverses the shaped characters into right-to-left visual order, except for runs of digits/Latin
    /// letters, which are reversed back in place so numbers and English fragments still read left-to-right
    /// within the Arabic sentence.
    /// </summary>
    private static string ReorderForDisplay(List<char> shaped)
    {
        shaped.Reverse();

        int count = shaped.Count;
        bool[] isStrongLtr = new bool[count];
        bool[] isNeutral = new bool[count];
        for (int i = 0; i < count; i++)
        {
            char c = shaped[i];
            isStrongLtr[i] = char.IsDigit(c) || (c < 128 && char.IsLetter(c));
            isNeutral[i] = !isStrongLtr[i] && IsJoiningNeutral(c);
        }

        // Extend each LTR run through neutral characters (space, hyphen, apostrophe, …) that sit between
        // two LTR characters, so a transliterated phrase like "Ar-Rahman" or "Jannah Garden" keeps its own
        // internal left-to-right order instead of being torn into separately-flipped fragments.
        bool[] isLtrRun = (bool[])isStrongLtr.Clone();
        int neutralStart = -1;
        for (int i = 0; i <= count; i++)
        {
            if (i < count && isNeutral[i])
            {
                if (neutralStart == -1) neutralStart = i;
            }
            else if (neutralStart != -1)
            {
                bool leftIsLtr = neutralStart > 0 && isLtrRun[neutralStart - 1];
                bool rightIsLtr = i < count && isStrongLtr[i];
                if (leftIsLtr && rightIsLtr)
                {
                    for (int j = neutralStart; j < i; j++) isLtrRun[j] = true;
                }
                neutralStart = -1;
            }
        }

        int runStart = -1;
        for (int i = 0; i <= count; i++)
        {
            bool inRun = i < count && isLtrRun[i];
            if (inRun && runStart == -1)
            {
                runStart = i;
            }
            else if (!inRun && runStart != -1)
            {
                shaped.Reverse(runStart, i - runStart);
                runStart = -1;
            }
        }

        var sb = new StringBuilder(count);
        foreach (char c in shaped) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Punctuation/space that should stay attached to a flanking LTR run rather than floating free.</summary>
    private static bool IsJoiningNeutral(char c)
    {
        return c == ' ' || c == '-' || c == '\'' || c == '.' || c == ',' || c == ':' || c == '/';
    }
}
