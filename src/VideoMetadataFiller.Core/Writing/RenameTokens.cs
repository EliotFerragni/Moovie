using VideoMetadataFiller.Core.Localization;
using VideoMetadataFiller.Core.Model;

namespace VideoMetadataFiller.Core.Writing;

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
public sealed record RenameToken(
    string Name,
    TokenValueKind Kind,
    MediaKind AppliesTo = MediaKind.Unknown,
    IReadOnlyList<string>? ExtraFormats = null)
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
    public string Insertion => Kind switch
    {
        TokenValueKind.Number => $"{{{Name}:00}}",
        TokenValueKind.Date => $"{{{Name}:yyyy-MM-dd}}",
        _ => $"{{{Name}}}",
    };
}

/// <summary>The token vocabulary, shared by the renderer, the validator and the Settings palette.</summary>
public static class RenameTokens
{
    public static readonly IReadOnlyList<RenameToken> All =
    [
        new("title", TokenValueKind.Text),
        new("originalTitle", TokenValueKind.Text),
        new("year", TokenValueKind.Number),
        new("show", TokenValueKind.Text, MediaKind.TvEpisode),
        new("season", TokenValueKind.Number, MediaKind.TvEpisode),
        new("episode", TokenValueKind.Number, MediaKind.TvEpisode),
        new("episodeTitle", TokenValueKind.Text, MediaKind.TvEpisode),
        new("airDate", TokenValueKind.Date, MediaKind.TvEpisode),
        new("releaseDate", TokenValueKind.Date),
        new("genre", TokenValueKind.Text),
        new("studio", TokenValueKind.Text, MediaKind.Movie),
        new("network", TokenValueKind.Text, MediaKind.TvEpisode),
        new("resolution", TokenValueKind.Text, ExtraFormats: ["short"]),
        new("tmdbId", TokenValueKind.Number),
        new("imdbId", TokenValueKind.Text),
        new("ext", TokenValueKind.Text),
    ];

    private static readonly Dictionary<string, RenameToken> ByName =
        All.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks a token up by name, case-insensitively.</summary>
    public static RenameToken? Find(string name) =>
        ByName.TryGetValue(name, out var token) ? token : null;
}
