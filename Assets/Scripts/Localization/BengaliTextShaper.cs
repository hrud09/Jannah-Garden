using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Stock TextMeshPro draws Bengali text in strict logical (encoding) order. That's wrong for the
/// "pre-base" dependent vowel signs (ি, ে, ৈ, and the left half of the split ো/ৌ): Unicode stores them
/// immediately after the consonant they modify, but Bengali orthography renders them *before* that
/// consonant — and before the whole conjunct cluster if the consonant is itself part of one (e.g. ক্রি
/// must show ি first, not after র). This class performs that reordering before a string reaches a
/// TMP_Text, the same role <see cref="ArabicTextShaper"/> plays for Arabic.
///
/// Scope (deliberately kept lightweight rather than a full Unicode shaping engine):
///  - Fixes pre-base matra reordering, which is the visually glaring bug (reading order is simply wrong
///    without it). ো/ৌ are decomposed into their two-part form (ে+া, ে+ৗ) first so only the left half
///    needs to move.
///  - Does NOT synthesize conjunct ligatures (যুক্তাক্ষর) or reph/ra-phala — those need glyph tables this
///    project's fonts expose only via OpenType GSUB, which TMP doesn't evaluate. Consonant clusters still
///    render as their component letters joined by a visible hasant (্), which is a standard, legible
///    fallback presentation — just less compact than a true ligature.
///  - No script reordering: Bengali is left-to-right, so unlike Arabic there's no whole-line reversal.
/// Treat this as a solid MVP and give it a real visual pass in Unity with real Bengali content before
/// shipping.
/// </summary>
public static class BengaliTextShaper
{
    private static readonly Regex BengaliRangeRegex = new Regex("[ঀ-৿]");
    private static readonly Regex LeadingTagRegex = new Regex("^(?:<[^>]+>)+");
    private static readonly Regex TrailingTagRegex = new Regex("(?:<[^>]+>)+$");

    private const char Hasant = '্';       // U+09CD VIRAMA — joins consonants into a conjunct cluster.
    private const char VowelSignI = 'ি';   // U+09BF — pre-base.
    private const char VowelSignE = 'ে';   // U+09C7 — pre-base.
    private const char VowelSignAi = 'ৈ';  // U+09C8 — pre-base.
    private const char VowelSignO = 'ো';   // U+09CB — canonically decomposes to VowelSignE + VowelSignAa.
    private const char VowelSignAu = 'ৌ';  // U+09CC — canonically decomposes to VowelSignE + AuLengthMark.
    private const char VowelSignAa = 'া';  // U+09BE — post-base; right half of a decomposed O.
    private const char AuLengthMark = 'ৗ'; // U+09D7 — post-base; right half of a decomposed AU.
    private const char Zwj = '‍';
    private const char Zwnj = '‌';

    /// <summary>
    /// Reorders pre-base matras for TMP display. Safe to call on any string — text with no Bengali
    /// characters is returned unchanged, so callers don't need to check first.
    /// </summary>
    public static string Shape(string text)
    {
        if (string.IsNullOrEmpty(text) || !BengaliRangeRegex.IsMatch(text)) return text;

        // Process each line independently so "\n" boundaries stay in their original top-to-bottom order.
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = ShapeLineWithTags(lines[i]);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Strips rich-text tags that wrap a whole line (the same pattern <see cref="ArabicTextShaper"/>
    /// handles), reorders the inner text, then restores the tags unmoved.
    /// </summary>
    private static string ShapeLineWithTags(string line)
    {
        if (string.IsNullOrEmpty(line) || !BengaliRangeRegex.IsMatch(line)) return line;

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
        if (string.IsNullOrEmpty(line) || !BengaliRangeRegex.IsMatch(line)) return line;

        string decomposed = DecomposeSplitVowels(line);

        var output = new List<char>(decomposed.Length);
        int clusterStart = -1;   // index in `output` where the current consonant cluster began; -1 = none open.
        bool afterHasant = false; // true right after a hasant, so the next consonant continues the cluster.

        foreach (char c in decomposed)
        {
            if (c == Hasant)
            {
                output.Add(c);
                afterHasant = true;
                continue;
            }

            if (IsConsonant(c))
            {
                output.Add(c);
                if (!afterHasant) clusterStart = output.Count - 1; // a consonant not preceded by hasant starts a fresh cluster
                afterHasant = false;
                continue;
            }

            if (c == Zwj || c == Zwnj)
            {
                output.Add(c); // transparent — doesn't affect cluster/hasant tracking
                continue;
            }

            if (IsPreBaseMatra(c) && clusterStart != -1)
            {
                output.Insert(clusterStart, c); // jump before the whole consonant cluster, not just the last letter
                clusterStart = -1;
                afterHasant = false;
                continue;
            }

            output.Add(c);
            clusterStart = -1;
            afterHasant = false;
        }

        return new string(output.ToArray());
    }

    /// <summary>Splits the two-part vowel signs into pre-base + post-base halves so the reordering pass
    /// only ever has to move single-codepoint pre-base matras.</summary>
    private static string DecomposeSplitVowels(string line)
    {
        if (line.IndexOf(VowelSignO) < 0 && line.IndexOf(VowelSignAu) < 0) return line;

        var sb = new StringBuilder(line.Length + 4);
        foreach (char c in line)
        {
            if (c == VowelSignO) { sb.Append(VowelSignE); sb.Append(VowelSignAa); }
            else if (c == VowelSignAu) { sb.Append(VowelSignE); sb.Append(AuLengthMark); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsConsonant(char c)
    {
        if (c >= 'ক' && c <= 'ন') return true; // KA..NA
        if (c >= 'প' && c <= 'র') return true; // PA..RA
        if (c == 'ল') return true;                  // LA
        if (c >= 'শ' && c <= 'হ') return true; // SHA..HA
        if (c == 'ড়' || c == 'ঢ়' || c == 'য়') return true; // RRA, RHA, YYA (nukta forms)
        if (c == 'ৎ') return true;                  // KHANDA TA
        return false;
    }

    private static bool IsPreBaseMatra(char c) => c == VowelSignI || c == VowelSignE || c == VowelSignAi;
}
