using Moovie.Core.Model;

namespace Moovie.Core.Tmdb;

/// <summary>Where in a show a given episode sits.</summary>
public readonly record struct EpisodeCoordinate(int Season, int Episode);

/// <summary>
/// The app's view of TMDB. An interface so the matcher can be tested without a network or an
/// API key.
/// </summary>
public interface ITmdbService
{
    /// <summary>Searches movies. <paramref name="year"/> narrows the results when known.</summary>
    Task<IReadOnlyList<Candidate>> SearchMoviesAsync(
        string query, int? year, string language, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Candidate>> SearchShowsAsync(
        string query, int? year, string language, CancellationToken cancellationToken = default);

    /// <summary>Full metadata for a movie, including credits and certification.</summary>
    Task<MediaMetadata?> GetMovieAsync(
        int movieId, string language, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full metadata for one episode, merging show-level fields (network, genres, rating) with
    /// episode-level ones (title, overview, still).
    /// </summary>
    Task<MediaMetadata?> GetEpisodeAsync(
        int showId, int season, IReadOnlyList<int> episodes, string language,
        CancellationToken cancellationToken = default);

    /// <summary>Finds where a daily show's episode for <paramref name="airDate"/> sits.</summary>
    Task<EpisodeCoordinate?> FindEpisodeByAirDateAsync(
        int showId, DateTime airDate, string language, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every image TMDB holds for a title, across all the kinds that apply to it, so the user can
    /// pick one by hand. Unlike the paths carried on <see cref="MediaMetadata"/> this costs extra
    /// requests, so it is only called when the picker is actually opened.
    /// </summary>
    Task<IReadOnlyList<ArtworkOption>> GetArtworkOptionsAsync(
        MediaKind kind, int tmdbId, int? season, int? episode, string language,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads artwork for a TMDB-relative path, or null when there is none.</summary>
    Task<byte[]?> GetArtworkAsync(
        string? artworkPath, string size, CancellationToken cancellationToken = default);

    /// <summary>Absolute URL for a TMDB-relative image path, for on-screen previews.</summary>
    string? BuildImageUrl(string? artworkPath, string size);

    /// <summary>Checks the configured API key with a cheap call. Returns null on success, else why not.</summary>
    Task<string?> ValidateApiKeyAsync(CancellationToken cancellationToken = default);
}
