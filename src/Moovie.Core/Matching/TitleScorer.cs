using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Moovie.Core.Matching;

/// <summary>
/// Scores how well a TMDB result matches the title recovered from a filename, on a 0..1 scale.
/// </summary>
/// <remarks>
/// The scale is what <see cref="MatchResolver"/>'s auto-select thresholds are calibrated against,
/// so the anchor points matter: an exact match after normalisation is 1.0, a containment match
/// lands in the 0.6-0.9 band depending on how much extra text there is, and unrelated titles
/// sharing a word or two stay below 0.5.
/// </remarks>
public static class TitleScorer
{
    private static readonly Regex NonAlphanumeric = new(@"[^\p{L}\p{N}\s]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Words too common to count towards a match on their own.</summary>
    private static readonly HashSet<string> Noise =
        new(StringComparer.Ordinal) { "the", "a", "an", "of", "and", "le", "la", "les", "der", "die", "das" };

    /// <summary>
    /// Compares <paramref name="query"/> against a candidate's titles and years.
    /// </summary>
    public static double Score(
        string query,
        string? candidateTitle,
        string? candidateOriginalTitle,
        int? queryYear,
        int? candidateYear)
    {
        var textScore = Math.Max(
            TextScore(query, candidateTitle),
            TextScore(query, candidateOriginalTitle));

        return Math.Clamp(textScore + YearAdjustment(queryYear, candidateYear), 0d, 1d);
    }

    /// <summary>
    /// A year we parsed off the filename is strong evidence: matching it lifts a candidate over
    /// the auto-select line, and contradicting it pushes one under.
    /// </summary>
    private static double YearAdjustment(int? queryYear, int? candidateYear)
    {
        if (queryYear is null || candidateYear is null)
            return 0d;

        var difference = Math.Abs(queryYear.Value - candidateYear.Value);
        return difference switch
        {
            0 => 0.15,
            1 => 0.05, // Release years legitimately differ by one between territories.
            _ => -0.30,
        };
    }

    private static double TextScore(string query, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return 0d;

        var left = Normalize(query);
        var right = Normalize(candidate);
        if (left.Length == 0 || right.Length == 0)
            return 0d;

        if (left == right)
            return 1d;

        // One title contained in the other: usually a subtitle or an edition suffix. How much
        // was left over decides how confident we are.
        if (right.StartsWith(left, StringComparison.Ordinal) || left.StartsWith(right, StringComparison.Ordinal))
        {
            var ratio = (double)Math.Min(left.Length, right.Length) / Math.Max(left.Length, right.Length);
            return 0.6 + (0.3 * ratio);
        }

        return 0.8 * TokenOverlap(left, right);
    }

    /// <summary>Jaccard overlap of the meaningful words, so word order and noise words do not matter.</summary>
    private static double TokenOverlap(string left, string right)
    {
        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return 0d;

        var shared = leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.Ordinal).Count();
        return (double)shared / union;
    }

    private static HashSet<string> Tokenize(string normalized)
    {
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !Noise.Contains(t))
            .ToHashSet(StringComparer.Ordinal);

        // A title made entirely of noise words ("The The") still needs something to compare.
        return tokens.Count > 0
            ? tokens
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Lowercases, strips accents and drops punctuation, so "Amélie" matches "Amelie" and
    /// "Spider-Man: No Way Home" matches "Spider Man No Way Home".
    /// </summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        var stripped = builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        stripped = stripped.Replace('&', ' ').Replace('-', ' ').Replace('\'', ' ');
        stripped = NonAlphanumeric.Replace(stripped, " ");
        return Whitespace.Replace(stripped, " ").Trim();
    }
}
