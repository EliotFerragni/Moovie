using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Tmdb;

namespace VideoMetadataFiller.Core.Tests;

/// <summary>
/// A scripted <see cref="ITmdbService"/>. Lets the matcher's decisions be tested exactly, with no
/// network and no API key.
/// </summary>
public sealed class FakeTmdbService : ITmdbService
{
    public List<Candidate> MovieResults { get; init; } = [];

    public List<Candidate> ShowResults { get; init; } = [];

    /// <summary>Episodes the fake show has, keyed by (season, episode).</summary>
    public Dictionary<(int Season, int Episode), string> Episodes { get; init; } = [];

    public Dictionary<DateTime, EpisodeCoordinate> EpisodesByAirDate { get; init; } = [];

    /// <summary>Movie ids that have no details, to exercise the "found it, then lost it" path.</summary>
    public HashSet<int> MoviesWithoutDetails { get; init; } = [];

    public List<string> RequestedLanguages { get; } = [];

    public int MovieSearchCalls { get; private set; }

    public int ShowSearchCalls { get; private set; }

    public Task<IReadOnlyList<Candidate>> SearchMoviesAsync(
        string query, int? year, string language, CancellationToken cancellationToken = default)
    {
        MovieSearchCalls++;
        RequestedLanguages.Add(language);
        return Task.FromResult<IReadOnlyList<Candidate>>(Clone(MovieResults));
    }

    public Task<IReadOnlyList<Candidate>> SearchShowsAsync(
        string query, int? year, string language, CancellationToken cancellationToken = default)
    {
        ShowSearchCalls++;
        RequestedLanguages.Add(language);
        return Task.FromResult<IReadOnlyList<Candidate>>(Clone(ShowResults));
    }

    /// <summary>
    /// Candidates carry a mutable score that the matcher writes, so each call hands out fresh
    /// copies rather than letting one test's scoring leak into the next assertion.
    /// </summary>
    private static List<Candidate> Clone(List<Candidate> source) =>
        source.Select(c => c with { }).ToList();

    public Task<MediaMetadata?> GetMovieAsync(
        int movieId, string language, CancellationToken cancellationToken = default)
    {
        RequestedLanguages.Add(language);
        if (MoviesWithoutDetails.Contains(movieId))
            return Task.FromResult<MediaMetadata?>(null);

        var candidate = MovieResults.FirstOrDefault(c => c.TmdbId == movieId);
        return Task.FromResult<MediaMetadata?>(new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = candidate?.Title ?? $"Movie {movieId}",
            Year = candidate?.Year,
            TmdbId = movieId,
            Language = language,
        });
    }

    public Task<MediaMetadata?> GetEpisodeAsync(
        int showId, int season, IReadOnlyList<int> episodes, string language,
        CancellationToken cancellationToken = default)
    {
        RequestedLanguages.Add(language);
        var wanted = episodes.Count > 0 ? episodes[0] : 1;
        if (!Episodes.TryGetValue((season, wanted), out var title))
            return Task.FromResult<MediaMetadata?>(null);

        var show = ShowResults.FirstOrDefault(c => c.TmdbId == showId);
        return Task.FromResult<MediaMetadata?>(new MediaMetadata
        {
            Kind = MediaKind.TvEpisode,
            ShowName = show?.Title ?? $"Show {showId}",
            Title = title,
            Season = season,
            Episodes = [.. episodes],
            TmdbId = showId,
            Language = language,
        });
    }

    public Task<EpisodeCoordinate?> FindEpisodeByAirDateAsync(
        int showId, DateTime airDate, string language, CancellationToken cancellationToken = default) =>
        Task.FromResult(EpisodesByAirDate.TryGetValue(airDate.Date, out var coordinate)
            ? coordinate
            : (EpisodeCoordinate?)null);

    public Task<byte[]?> GetArtworkAsync(
        string? artworkPath, string size, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public string? BuildImageUrl(string? artworkPath, string size) => artworkPath is null ? null : "https://example.invalid" + artworkPath;

    public Task<string?> ValidateApiKeyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
