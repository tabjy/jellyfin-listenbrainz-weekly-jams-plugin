using Jellyfin.Plugin.ListenBrainzWeeklyJams.Matching;
using Xunit;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Tests;

/// <summary>
/// Ported from scripts/test_dry_run.py — the C# normalizer must behave
/// identically, including the dakuten-preserving fold (design D-11).
/// </summary>
public sealed class NameNormalizerTests
{
    [Theory]
    [InlineData("Hatsune Miku", "hatsune miku")]
    [InlineData("hatsune  miku!", "hatsune miku")]
    [InlineData("HATSUNE MIKU (feat. GUMI)", "hatsune miku")]
    [InlineData("Café Blue", "cafe blue")]                 // Latin diacritics folded
    [InlineData("25時、ナイトコードで。", "25時 ナイトコードで")] // CJK punctuation → spaces
    [InlineData("みきとP", "みきとp")]                        // Latin letter in kana name casefolds
    [InlineData("Ｈａｔｓｕｎｅ　Ｍｉｋｕ", "hatsune miku")]     // full-width Latin + ideographic space
    [InlineData("曲 feat. 宵崎奏", "曲")]                    // feat-stripping with CJK around it
    [InlineData("少女レイ", "少女レイ")]                      // pure CJK passthrough
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_MatchesReferenceBehavior(string? input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_PreservesKanaVoicing()
    {
        // Regression (dry run, 2026-10-20): blanket combining-mark stripping
        // collapsed voiced kana (で -> て), causing false matches.
        Assert.NotEqual(NameNormalizer.Normalize("で"), NameNormalizer.Normalize("て"));
        Assert.NotEqual(NameNormalizer.Normalize("ド"), NameNormalizer.Normalize("ト"));
        Assert.NotEqual(NameNormalizer.Normalize("ばら"), NameNormalizer.Normalize("はら"));
    }

    [Theory]
    [InlineData("Song (Live)", "song")]
    [InlineData("Song [Demo]", "song")]
    [InlineData("Song", "song")]
    public void NormalizeWithoutBrackets_StripsQualifiers(string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.NormalizeWithoutBrackets(input));
    }
}
