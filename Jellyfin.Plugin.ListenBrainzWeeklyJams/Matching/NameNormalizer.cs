using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;

/// <summary>
/// Name normalization for fallback matching (FR-13). A faithful port of the
/// dry-run reference implementation (scripts/dry_run.py), including the
/// compatibility-folding rule discovered there: NFKD decomposition is applied
/// per character, combining marks are stripped only over ASCII-letter bases
/// (é → e), and kana stay precomposed so voicing marks survive (で must not
/// collapse into て). See docs/design.md D-11.
/// </summary>
public static class NameNormalizer
{
    private static readonly Regex FeatRegex = new(
        @"\b(feat|ft|featuring)\b\.?\s.*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex PunctuationRegex = new(
        @"[^\w\s]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex BracketRegex = new(
        @"[(\[].*?[)\]]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    /// <summary>
    /// Normalizes a name: folds compatibility forms and Latin diacritics,
    /// lowercases, strips feat-credits, maps punctuation to spaces and
    /// collapses whitespace.
    /// </summary>
    /// <param name="value">The raw name.</param>
    /// <returns>The normalized name; empty for null/blank input.</returns>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var folded = FoldCompatibility(value);
        var lowered = folded.ToLowerInvariant();
        var withoutFeat = FeatRegex.Replace(lowered, " ");
        var withoutPunctuation = PunctuationRegex.Replace(withoutFeat, " ");
        return WhitespaceRegex.Replace(withoutPunctuation, " ").Trim();
    }

    /// <summary>
    /// Normalizes a name with bracketed qualifiers removed first, e.g.
    /// "Song (Remastered 2011)" behaves like "Song".
    /// </summary>
    /// <param name="value">The raw name.</param>
    /// <returns>The normalized, bracket-stripped name.</returns>
    public static string NormalizeWithoutBrackets(string? value)
        => Normalize(BracketRegex.Replace(value ?? string.Empty, " "));

    private static string FoldCompatibility(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            string decomposition;
            try
            {
                decomposition = ch.ToString().Normalize(NormalizationForm.FormKD);
            }
            catch (ArgumentException)
            {
                // Unpaired surrogates and similar cannot be normalized; pass through.
                builder.Append(ch);
                continue;
            }

            if (decomposition.Length == 1)
            {
                // e.g. Ａ -> A, ideographic space -> space
                builder.Append(decomposition);
            }
            else if (TailIsAllCombiningMarks(decomposition))
            {
                var baseChar = decomposition[0];
                if (char.IsAsciiLetter(baseChar))
                {
                    // Latin diacritic: é -> e
                    builder.Append(baseChar);
                }
                else
                {
                    // Kana with dakuten/handakuten: keep the precomposed
                    // character so voiced/unvoiced kana never collapse.
                    builder.Append(ch);
                }
            }
            else
            {
                // e.g. ligatures ﬁ -> fi
                builder.Append(decomposition);
            }
        }

        return builder.ToString();
    }

    private static bool TailIsAllCombiningMarks(string decomposition)
    {
        for (var i = 1; i < decomposition.Length; i++)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(decomposition[i]) != UnicodeCategory.NonSpacingMark)
            {
                return false;
            }
        }

        return true;
    }
}
