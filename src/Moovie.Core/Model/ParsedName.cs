namespace Moovie.Core.Model;

/// <summary>
/// Everything <see cref="Parsing.FilenameParser"/> could work out from a path,
/// before TMDB is consulted.
/// </summary>
public sealed record ParsedName
{
    /// <summary>Movie or TV episode, or Unknown when the name gives nothing away.</summary>
    public MediaKind Kind { get; init; } = MediaKind.Unknown;

    /// <summary>Cleaned-up movie title, or the show name for a TV episode.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Release year for a movie, when the name carried one.</summary>
    public int? Year { get; init; }

    public int? Season { get; init; }

    /// <summary>
    /// Episode numbers found in the name. Usually one entry; multi-episode files
    /// such as <c>S01E01E02</c> carry several, in the order they appeared.
    /// </summary>
    public IReadOnlyList<int> Episodes { get; init; } = [];

    /// <summary>Air date for date-based (daily) shows, e.g. <c>Show.2021-03-08</c>.</summary>
    public DateTime? AirDate { get; init; }

    /// <summary>Resolution token lifted from the name (<c>1080p</c>, <c>2160p</c>, …), if any.</summary>
    public string? Resolution { get; init; }

    /// <summary>
    /// True when the parse relied on a weak signal (bare episode number, no season,
    /// name recovered from the folder). Nudges the matcher towards asking the user.
    /// </summary>
    public bool IsLowConfidence { get; init; }

    /// <summary>How the kind/numbers were recognised — useful in the UI and in tests.</summary>
    public string? MatchedPattern { get; init; }

    public int? FirstEpisode => Episodes.Count > 0 ? Episodes[0] : null;

    public bool IsMultiEpisode => Episodes.Count > 1;
}
