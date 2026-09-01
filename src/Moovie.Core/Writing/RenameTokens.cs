using Moovie.Core.Localization;
using Moovie.Core.Model;

namespace Moovie.Core.Writing;

/// <summary>What kind of value a token produces, which decides the formats it accepts.</summary>
public enum TokenValueKind
{
    /// <summary>Accepts <c>upper</c>, <c>lower</c> and <c>title</c>.</summary>
    Text,

    /// <summary>Accepts digit-padding formats such as <c>00</c>.</summary>
    Number,

    /// <summary>Accepts .NET date format strings such as <c>yyyy-MM-dd</c>.</summary>
    Date,
}

/// <summary>One placeholder usable in a rename template.</summary>
/// <param name="OffersKindFormats">
/// Whether the formats implied by <paramref name="Kind"/> are worth offering for this token.
/// False where they cannot change the value: a year is always four digits, and a resolution or an
/// IMDb id is a lowercase code that case conversion only mangles. This decides what the palette
/// advertises; the validator still accepts them, so no existing template breaks.
/// </param>
public sealed record RenameToken(
    string Name,
    TokenValueKind Kind,
    MediaKind AppliesTo = MediaKind.Unknown,
    IReadOnlyList<string>? ExtraFormats = null,
    bool OffersKindFormats = true)
{
    /// <summary>
    /// Formats this token accepts beyond the ones its <see cref="Kind"/> allows. Kept here with
    /// the rest of the vocabulary so the validator stays generic rather than naming one token.
    /// </summary>
    public IReadOnlyList<string> ExtraFormats { get; } = ExtraFormats ?? [];

    /// <summary>
    /// What the token palette shows about this token. Keyed off the name rather than held here,
    /// so the vocabulary and its translations cannot drift apart.
    /// </summary>
    public string Description => Strings.Get($"token.{Name}");

    /// <summary>True when this token is meaningful for <paramref name="kind"/>.</summary>
    public bool IsRelevantTo(MediaKind kind) => AppliesTo == MediaKind.Unknown || AppliesTo == kind;

    /// <summary>What the token palette in Settings inserts when clicked.</summary>
    public string Insertion => OffersKindFormats
        ? Kind switch
        {
            TokenValueKind.Number => $"{{{Name}:00}}",
            TokenValueKind.Date => $"{{{Name}:yyyy-MM-dd}}",
            _ => $"{{{Name}}}",
        }
        : $"{{{Name}}}";

    /// <summary>
    /// Every form of this token worth showing in the palette, bare first: what the user may
    /// write after the colon. Null means the token with no format at all.
    /// </summary>
    /// <remarks>
    /// Exhaustive for numbers, text and a token's extra formats; dates are an open set, so those
    /// are a representative handful and the palette says as much. A format that spells out a
    /// default (<c>:0</c>, <c>:yyyy-MM-dd</c>) is left out, since it renders what the bare token
    /// already renders.
    /// </remarks>
    public IReadOnlyList<string?> OfferedFormats
    {
        get
        {
            string[] byKind = OffersKindFormats
                ? Kind switch
                {
                    TokenValueKind.Number => ["00", "000"],
                    TokenValueKind.Date => ["yyyy", "MMMM yyyy"],
                    _ => ["upper", "lower", "title"],
                }
                : [];

            return [null, .. byKind, .. ExtraFormats];
        }
    }

    /// <summary>Whether the palette should say the formats shown are only a sample.</summary>
    public bool HasOpenEndedFormats => Kind == TokenValueKind.Date && OffersKindFormats;

    /// <summary>This token written with one of its formats, e.g. <c>{season:00}</c>.</summary>
    public string Written(string? format) =>
        format is null ? $"{{{Name}}}" : $"{{{Name}:{format}}}";
}

/// <summary>The token vocabulary, shared by the renderer, the validator and the Settings palette.</summary>
public static class RenameTokens
{
    public static readonly IReadOnlyList<RenameToken> All =
    [
        new("title", TokenValueKind.Text),
        new("originalTitle", TokenValueKind.Text),
        new("year", TokenValueKind.Number, OffersKindFormats: false),
        new("show", TokenValueKind.Text, MediaKind.TvEpisode),
        new("season", TokenValueKind.Number, MediaKind.TvEpisode),
        new("episode", TokenValueKind.Number, MediaKind.TvEpisode),
        new("episodeTitle", TokenValueKind.Text, MediaKind.TvEpisode),
        new("airDate", TokenValueKind.Date, MediaKind.TvEpisode),
        new("releaseDate", TokenValueKind.Date),
        new("genre", TokenValueKind.Text),
        new("studio", TokenValueKind.Text, MediaKind.Movie),
        new("network", TokenValueKind.Text, MediaKind.TvEpisode),
        new("resolution", TokenValueKind.Text, ExtraFormats: ["short"], OffersKindFormats: false),
        new("tmdbId", TokenValueKind.Number, OffersKindFormats: false),
        new("imdbId", TokenValueKind.Text, OffersKindFormats: false),
        new("ext", TokenValueKind.Text),
    ];

    private static readonly Dictionary<string, RenameToken> ByName =
        All.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks a token up by name, case-insensitively.</summary>
    public static RenameToken? Find(string name) =>
        ByName.TryGetValue(name, out var token) ? token : null;
}
