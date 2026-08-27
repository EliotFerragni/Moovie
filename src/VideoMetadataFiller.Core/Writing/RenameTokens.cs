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
    string Description,
    MediaKind AppliesTo = MediaKind.Unknown)
{
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
        new("title", TokenValueKind.Text, "Movie title, or the episode title for a TV file"),
        new("originalTitle", TokenValueKind.Text, "Title in the original language"),
        new("year", TokenValueKind.Number, "Release year"),
        new("show", TokenValueKind.Text, "Show name", MediaKind.TvEpisode),
        new("season", TokenValueKind.Number, "Season number", MediaKind.TvEpisode),
        new("episode", TokenValueKind.Number, "Episode number (a range for multi-episode files)", MediaKind.TvEpisode),
        new("episodeTitle", TokenValueKind.Text, "Episode title", MediaKind.TvEpisode),
        new("airDate", TokenValueKind.Date, "Episode air date", MediaKind.TvEpisode),
        new("releaseDate", TokenValueKind.Date, "Release date"),
        new("genre", TokenValueKind.Text, "First genre"),
        new("studio", TokenValueKind.Text, "Production company", MediaKind.Movie),
        new("network", TokenValueKind.Text, "Broadcast network", MediaKind.TvEpisode),
        new("resolution", TokenValueKind.Text, "Resolution taken from the original filename"),
        new("tmdbId", TokenValueKind.Number, "TMDB id"),
        new("imdbId", TokenValueKind.Text, "IMDb id"),
        new("ext", TokenValueKind.Text, "File extension (added automatically if you leave it out)"),
    ];

    private static readonly Dictionary<string, RenameToken> ByName =
        All.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks a token up by name, case-insensitively.</summary>
    public static RenameToken? Find(string name) =>
        ByName.TryGetValue(name, out var token) ? token : null;
}
