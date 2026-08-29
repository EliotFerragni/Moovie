namespace Moovie.Core.Model;

/// <summary>One TMDB search hit offered to the user when the auto-match is not certain.</summary>
public sealed record Candidate
{
    public required int TmdbId { get; init; }
    public required MediaKind Kind { get; init; }
    public required string Title { get; init; }
    public string? OriginalTitle { get; init; }
    public int? Year { get; init; }
    public string? Overview { get; init; }

    /// <summary>TMDB-relative poster path, e.g. <c>/abc123.jpg</c>.</summary>
    public string? PosterPath { get; init; }

    public double Popularity { get; init; }

    /// <summary>0..1 similarity to the parsed name, from <see cref="Matching.TitleScorer"/>.</summary>
    public double Score { get; set; }

    public string Display => Year is null ? Title : $"{Title} ({Year})";
}
