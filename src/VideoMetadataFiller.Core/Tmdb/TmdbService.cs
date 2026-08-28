using TMDbLib.Client;
using TMDbLib.Objects.Exceptions;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;
using VideoMetadataFiller.Core.Model;

namespace VideoMetadataFiller.Core.Tmdb;

/// <summary>Raised when TMDB cannot be reached or refuses the request.</summary>
public sealed class TmdbException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// <see cref="ITmdbService"/> over TMDbLib.
/// </summary>
/// <remarks>
/// Two things here exist purely to make batches of hundreds of files behave: a cache keyed on
/// (id, language) so a 24-episode season costs one show lookup and one season lookup rather than
/// 24 searches, and a concurrency gate plus retry so TMDB's rate limiter is never tripped.
/// </remarks>
public sealed class TmdbService : ITmdbService, IDisposable
{
    /// <summary>TMDB tolerates far more, but there is nothing to gain from flooding it.</summary>
    private const int MaxConcurrentRequests = 4;

    private const int MaxRetries = 3;

    /// <summary>The language TMDB always has data in, used to fill gaps in a translation.</summary>
    private const string FallbackLanguage = "en-US";

    private const string DefaultImageBaseUrl = "https://image.tmdb.org/t/p/";

    private readonly TMDbClient _client;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(MaxConcurrentRequests, MaxConcurrentRequests);

    private readonly Dictionary<string, IReadOnlyList<Candidate>> _searchCache = [];
    private readonly Dictionary<string, Movie?> _movieCache = [];
    private readonly Dictionary<string, TvShow?> _showCache = [];
    private readonly Dictionary<string, TvSeason?> _seasonCache = [];
    private readonly Dictionary<string, byte[]?> _artworkCache = [];
    private readonly Dictionary<string, IReadOnlyList<ArtworkOption>> _artworkOptionsCache = [];
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private string? _imageBaseUrl;

    public TmdbService(string apiKey, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new TmdbException("No TMDB API key is configured. Add one in Settings.");

        _client = new TMDbClient(apiKey);
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<IReadOnlyList<Candidate>> SearchMoviesAsync(
        string query, int? year, string language, CancellationToken cancellationToken = default)
    {
        var key = CacheKey("movie-search", query, year?.ToString(), language);
        if (TryGetCached(_searchCache, key, out var cached))
            return cached!;

        var results = await SendAsync(
            () => _client.SearchMovieAsync(query, language, year: year ?? 0, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var candidates = SafeResults(results.Results).Select(ToCandidate).ToList();

        // A year filter that finds nothing is more likely a bad year than a missing film.
        if (candidates.Count == 0 && year is not null)
        {
            var unfiltered = await SendAsync(
                () => _client.SearchMovieAsync(query, language, cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false);
            candidates = SafeResults(unfiltered.Results).Select(ToCandidate).ToList();
        }

        await StoreAsync(_searchCache, key, candidates).ConfigureAwait(false);
        return candidates;
    }

    public async Task<IReadOnlyList<Candidate>> SearchShowsAsync(
        string query, int? year, string language, CancellationToken cancellationToken = default)
    {
        var key = CacheKey("tv-search", query, year?.ToString(), language);
        if (TryGetCached(_searchCache, key, out var cached))
            return cached!;

        var results = await SendAsync(
            () => _client.SearchTvShowAsync(query, language, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var candidates = SafeResults(results.Results).Select(ToCandidate).ToList();
        await StoreAsync(_searchCache, key, candidates).ConfigureAwait(false);
        return candidates;
    }

    public async Task<MediaMetadata?> GetMovieAsync(
        int movieId, string language, CancellationToken cancellationToken = default)
    {
        var movie = await GetMovieRecordAsync(movieId, language, cancellationToken).ConfigureAwait(false);
        if (movie is null)
            return null;

        var metadata = MapMovie(movie, language);

        if (NeedsFallback(metadata, language))
        {
            var fallback = await GetMovieRecordAsync(movieId, FallbackLanguage, cancellationToken)
                .ConfigureAwait(false);
            if (fallback is not null)
                FillGaps(metadata, MapMovie(fallback, FallbackLanguage));
        }

        return metadata;
    }

    public async Task<MediaMetadata?> GetEpisodeAsync(
        int showId, int season, IReadOnlyList<int> episodes, string language,
        CancellationToken cancellationToken = default)
    {
        var show = await GetShowRecordAsync(showId, language, cancellationToken).ConfigureAwait(false);
        if (show is null)
            return null;

        var seasonRecord = await GetSeasonRecordAsync(showId, season, language, cancellationToken)
            .ConfigureAwait(false);
        var wanted = episodes.Count > 0 ? episodes[0] : 1;
        var episode = seasonRecord?.Episodes?.FirstOrDefault(e => e.EpisodeNumber == wanted);
        if (episode is null)
            return null;

        var metadata = MapEpisode(show, seasonRecord, episode, season, episodes, language);

        if (NeedsFallback(metadata, language))
        {
            var fallbackShow = await GetShowRecordAsync(showId, FallbackLanguage, cancellationToken)
                .ConfigureAwait(false);
            var fallbackSeason = await GetSeasonRecordAsync(showId, season, FallbackLanguage, cancellationToken)
                .ConfigureAwait(false);
            var fallbackEpisode = fallbackSeason?.Episodes?.FirstOrDefault(e => e.EpisodeNumber == wanted);
            if (fallbackShow is not null && fallbackEpisode is not null)
                FillGaps(metadata, MapEpisode(
                    fallbackShow, fallbackSeason, fallbackEpisode, season, episodes, FallbackLanguage));
        }

        return metadata;
    }

    public async Task<EpisodeCoordinate?> FindEpisodeByAirDateAsync(
        int showId, DateTime airDate, string language, CancellationToken cancellationToken = default)
    {
        var show = await GetShowRecordAsync(showId, language, cancellationToken).ConfigureAwait(false);
        if (show?.Seasons is null)
            return null;

        // Daily shows are usually organised by year, so try the seasons whose start date sits
        // closest to the one we are looking for before widening the search.
        var ordered = show.Seasons
            .Where(s => s.SeasonNumber > 0)
            .OrderBy(s => s.AirDate is null ? 1 : 0)
            .ThenBy(s => Math.Abs((airDate - (s.AirDate ?? airDate)).TotalDays))
            .ToList();

        foreach (var candidateSeason in ordered)
        {
            var seasonRecord = await GetSeasonRecordAsync(
                showId, candidateSeason.SeasonNumber, language, cancellationToken).ConfigureAwait(false);
            var episode = seasonRecord?.Episodes?
                .FirstOrDefault(e => e.AirDate is { } date && date.Date == airDate.Date);
            if (episode is not null)
                return new EpisodeCoordinate(candidateSeason.SeasonNumber, (int)episode.EpisodeNumber);
        }

        return null;
    }

    /// <summary>Caps how many images of one kind the picker offers: a popular show has hundreds.</summary>
    private const int MaxOptionsPerKind = 12;

    public async Task<IReadOnlyList<ArtworkOption>> GetArtworkOptionsAsync(
        MediaKind kind, int tmdbId, int? season, int? episode, string language,
        CancellationToken cancellationToken = default)
    {
        var key = CacheKey("art-options", $"{kind}-{tmdbId}-{season}-{episode}", language, null);
        if (TryGetCached(_artworkOptionsCache, key, out var cached))
            return cached!;

        var options = new List<ArtworkOption>();

        if (kind == MediaKind.Movie)
        {
            var images = await TryFetchAsync(
                () => _client.GetMovieImagesAsync(tmdbId, cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false);
            Collect(options, ArtworkKind.MoviePoster, images?.Posters);
            Collect(options, ArtworkKind.MovieBackdrop, images?.Backdrops);
        }
        else
        {
            if (season is { } seasonNumber && episode is { } episodeNumber)
            {
                var stills = await TryFetchAsync(
                    () => _client.GetTvEpisodeImagesAsync(
                        tmdbId, seasonNumber, episodeNumber, cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                Collect(options, ArtworkKind.EpisodeStill, stills?.Stills);
            }

            if (season is { } posterSeason)
            {
                var seasonImages = await TryFetchAsync(
                    () => _client.GetTvSeasonImagesAsync(
                        tmdbId, posterSeason, cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                Collect(options, ArtworkKind.SeasonPoster, seasonImages?.Posters);
            }

            var showImages = await TryFetchAsync(
                () => _client.GetTvShowImagesAsync(tmdbId, cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false);
            Collect(options, ArtworkKind.ShowPoster, showImages?.Posters);
            Collect(options, ArtworkKind.ShowBackdrop, showImages?.Backdrops);
        }

        var ordered = OrderArtwork(options, language);
        await StoreAsync(_artworkOptionsCache, key, ordered).ConfigureAwait(false);
        return ordered;
    }

    /// <summary>
    /// A title with no images of one kind answers with a 404. That is an answer, not a failure:
    /// the picker should still show whatever other kinds did come back.
    /// </summary>
    private async Task<T?> TryFetchAsync<T>(Func<Task<T?>> call, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await SendAsync(call, cancellationToken).ConfigureAwait(false);
        }
        catch (TmdbException)
        {
            return null;
        }
    }

    private static void Collect(
        List<ArtworkOption> into, ArtworkKind kind,
        IEnumerable<TMDbLib.Objects.General.ImageData>? images)
    {
        foreach (var image in images ?? [])
        {
            if (string.IsNullOrWhiteSpace(image.FilePath))
                continue;

            into.Add(new ArtworkOption
            {
                Kind = kind,
                Path = image.FilePath,
                Width = image.Width,
                Height = image.Height,
                Language = string.IsNullOrWhiteSpace(image.Iso_639_1) ? null : image.Iso_639_1,
                VoteAverage = image.VoteAverage,
            });
        }
    }

    /// <summary>
    /// Groups by kind so the picker reads in a predictable order, and within a kind puts artwork
    /// in the requested language first, then textless art, then whatever TMDB rates highest.
    /// </summary>
    private static IReadOnlyList<ArtworkOption> OrderArtwork(List<ArtworkOption> options, string language)
    {
        var wanted = language.Split('-')[0];

        return options
            .GroupBy(o => o.Kind)
            .OrderBy(g => (int)g.Key)
            .SelectMany(g => g
                .OrderBy(o => o.Language == wanted ? 0 : o.Language is null ? 1 : 2)
                .ThenByDescending(o => o.VoteAverage)
                .Take(MaxOptionsPerKind))
            .ToList();
    }

    public async Task<byte[]?> GetArtworkAsync(
        string? artworkPath, string size, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artworkPath))
            return null;

        var key = CacheKey("art", artworkPath, size, null);
        if (TryGetCached(_artworkCache, key, out var cached))
            return cached;

        var url = BuildImageUrl(artworkPath, size);
        if (url is null)
            return null;

        byte[]? bytes = null;
        try
        {
            bytes = await _http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Artwork is a nice-to-have: a failed download must not fail the whole file.
        }

        await StoreAsync(_artworkCache, key, bytes).ConfigureAwait(false);
        return bytes;
    }

    public string? BuildImageUrl(string? artworkPath, string size)
    {
        if (string.IsNullOrWhiteSpace(artworkPath))
            return null;
        var baseUrl = _imageBaseUrl ?? DefaultImageBaseUrl;
        return $"{baseUrl}{size}{artworkPath}";
    }

    public async Task<string?> ValidateApiKeyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await SendAsync<TMDbLib.Objects.General.TMDbConfig>(
                async () => await _client.GetConfigAsync().ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
            _imageBaseUrl = config.Images?.SecureBaseUrl ?? DefaultImageBaseUrl;
            return null;
        }
        catch (TmdbException e)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// Runs a TMDB call behind the concurrency gate, retrying when the rate limiter or a transient
    /// network error gets in the way, and translating failures into <see cref="TmdbException"/>.
    /// </summary>
    private async Task<T> SendAsync<T>(Func<Task<T?>> call, CancellationToken cancellationToken)
        where T : class
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await call().ConfigureAwait(false)
                           ?? throw new TmdbException("TMDB returned an empty response.");
                }
                catch (RequestLimitExceededException) when (attempt < MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (attempt < MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                }
                catch (RequestLimitExceededException e)
                {
                    throw new TmdbException("TMDB is rate-limiting these requests. Try again shortly.", e);
                }
                catch (UnauthorizedAccessException e)
                {
                    throw new TmdbException("TMDB rejected the API key. Check it in Settings.", e);
                }
                catch (TMDbException e)
                {
                    throw new TmdbException($"TMDB refused the request: {e.Message}", e);
                }
                catch (HttpRequestException e)
                {
                    throw new TmdbException($"TMDB could not be reached: {e.Message}", e);
                }
                catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TmdbException("The request to TMDB timed out.", e);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Movie?> GetMovieRecordAsync(int movieId, string language, CancellationToken cancellationToken)
    {
        var key = CacheKey("movie", movieId.ToString(), language, null);
        if (TryGetCached(_movieCache, key, out var cached))
            return cached;

        var movie = await SendAsync(
            () => _client.GetMovieAsync(
                movieId, language,
                extraMethods: MovieMethods.Credits | MovieMethods.ReleaseDates | MovieMethods.ExternalIds,
                cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);

        await StoreAsync(_movieCache, key, movie).ConfigureAwait(false);
        return movie;
    }

    private async Task<TvShow?> GetShowRecordAsync(int showId, string language, CancellationToken cancellationToken)
    {
        var key = CacheKey("show", showId.ToString(), language, null);
        if (TryGetCached(_showCache, key, out var cached))
            return cached;

        var show = await SendAsync(
            () => _client.GetTvShowAsync(
                showId,
                TvShowMethods.ContentRatings | TvShowMethods.ExternalIds | TvShowMethods.Credits,
                language,
                cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);

        await StoreAsync(_showCache, key, show).ConfigureAwait(false);
        return show;
    }

    private async Task<TvSeason?> GetSeasonRecordAsync(
        int showId, int season, string language, CancellationToken cancellationToken)
    {
        var key = CacheKey("season", $"{showId}-{season}", language, null);
        if (TryGetCached(_seasonCache, key, out var cached))
            return cached;

        TvSeason? record;
        try
        {
            record = await SendAsync(
                () => _client.GetTvSeasonAsync(
                    showId, season, language: language, cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (TmdbException)
        {
            // A season that does not exist is an answer, not an error: cache the miss.
            record = null;
        }

        await StoreAsync(_seasonCache, key, record).ConfigureAwait(false);
        return record;
    }

    private static Candidate ToCandidate(SearchMovie movie) => new()
    {
        TmdbId = movie.Id,
        Kind = MediaKind.Movie,
        Title = movie.Title ?? string.Empty,
        OriginalTitle = movie.OriginalTitle,
        Year = movie.ReleaseDate?.Year,
        Overview = movie.Overview,
        PosterPath = movie.PosterPath,
        Popularity = movie.Popularity,
    };

    private static Candidate ToCandidate(SearchTv show) => new()
    {
        TmdbId = show.Id,
        Kind = MediaKind.TvEpisode,
        Title = show.Name ?? string.Empty,
        OriginalTitle = show.OriginalName,
        Year = show.FirstAirDate?.Year,
        Overview = show.Overview,
        PosterPath = show.PosterPath,
        Popularity = show.Popularity,
    };

    private static MediaMetadata MapMovie(Movie movie, string language) => new()
    {
        Kind = MediaKind.Movie,
        Title = movie.Title,
        OriginalTitle = movie.OriginalTitle,
        ReleaseDate = movie.ReleaseDate,
        Year = movie.ReleaseDate?.Year,
        Overview = movie.Overview,
        Genres = GenreNames(movie.Genres),
        Cast = TopCast(movie.Credits?.Cast?.Select(c => c.Name)),
        Directors = CrewNamed(movie.Credits?.Crew, "Director"),
        Writers = CrewNamed(movie.Credits?.Crew, "Writer", "Screenplay", "Story"),
        Studio = movie.ProductionCompanies?.FirstOrDefault()?.Name,
        ContentRating = MovieCertification(movie),
        TmdbId = movie.Id,
        ImdbId = movie.ImdbId,
        ArtworkPath = movie.PosterPath,
        ArtworkByKind = MovieArtwork(movie),
        Language = language,
    };

    private static MediaMetadata MapEpisode(
        TvShow show, TvSeason? seasonRecord, TvSeasonEpisode episode, int season,
        IReadOnlyList<int> episodes, string language) => new()
    {
        Kind = MediaKind.TvEpisode,
        ShowName = show.Name,
        OriginalTitle = show.OriginalName,
        Title = episode.Name,
        Season = season,
        Episodes = episodes.Count > 0 ? [.. episodes] : [(int)episode.EpisodeNumber],
        ReleaseDate = episode.AirDate,
        Year = episode.AirDate?.Year ?? show.FirstAirDate?.Year,
        Overview = string.IsNullOrWhiteSpace(episode.Overview) ? show.Overview : episode.Overview,
        Genres = GenreNames(show.Genres),
        Cast = TopCast(show.Credits?.Cast?.Select(c => c.Name)),
        Directors = CrewNamed(episode.Crew, "Director"),
        Writers = CrewNamed(episode.Crew, "Writer", "Screenplay", "Story"),
        Network = show.Networks?.FirstOrDefault()?.Name,
        Studio = show.ProductionCompanies?.FirstOrDefault()?.Name,
        ContentRating = ShowCertification(show),
        TmdbId = show.Id,
        ImdbId = show.ExternalIds?.ImdbId,
        // An episode still is more useful than the show poster, but not every episode has one.
        // The real choice is made later against the user's preferred kind; this is the fallback.
        ArtworkPath = episode.StillPath ?? show.PosterPath,
        ArtworkByKind = EpisodeArtwork(show, seasonRecord, episode),
        Language = language,
    };

    private static Dictionary<ArtworkKind, string> EpisodeArtwork(
        TvShow show, TvSeason? seasonRecord, TvSeasonEpisode episode)
    {
        var map = new Dictionary<ArtworkKind, string>();
        AddArtwork(map, ArtworkKind.EpisodeStill, episode.StillPath);
        AddArtwork(map, ArtworkKind.SeasonPoster, seasonRecord?.PosterPath);
        AddArtwork(map, ArtworkKind.ShowPoster, show.PosterPath);
        AddArtwork(map, ArtworkKind.ShowBackdrop, show.BackdropPath);
        return map;
    }

    private static Dictionary<ArtworkKind, string> MovieArtwork(Movie movie)
    {
        var map = new Dictionary<ArtworkKind, string>();
        AddArtwork(map, ArtworkKind.MoviePoster, movie.PosterPath);
        AddArtwork(map, ArtworkKind.MovieBackdrop, movie.BackdropPath);
        return map;
    }

    private static void AddArtwork(Dictionary<ArtworkKind, string> map, ArtworkKind kind, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            map[kind] = path;
    }

    /// <summary>TMDB happily returns a null results array; treat it as no results.</summary>
    private static IEnumerable<T> SafeResults<T>(IEnumerable<T>? results) => results ?? [];

    private static List<string> GenreNames(IEnumerable<TMDbLib.Objects.General.Genre>? genres) =>
        genres?.Where(g => !string.IsNullOrWhiteSpace(g.Name)).Select(g => g.Name!).ToList() ?? [];

    /// <summary>Only the billed cast: an iTunMOVI list with fifty names in it helps nobody.</summary>
    private static List<string> TopCast(IEnumerable<string?>? names) =>
        names?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Take(15).ToList() ?? [];

    private static List<string> CrewNamed(IEnumerable<TMDbLib.Objects.General.Crew>? crew, params string[] jobs) =>
        crew?.Where(c => jobs.Contains(c.Job, StringComparer.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => c.Name!)
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList() ?? [];

    /// <summary>
    /// Picks a certification, preferring the US one because that is the vocabulary Apple devices
    /// understand, then falling back to whatever TMDB has.
    /// </summary>
    private static string? MovieCertification(Movie movie)
    {
        var releases = movie.ReleaseDates?.Results;
        if (releases is null)
            return null;

        var preferred = releases.FirstOrDefault(r => r.Iso_3166_1 == "US") ?? releases.FirstOrDefault();
        return preferred?.ReleaseDates?
            .Select(r => r.Certification)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
    }

    private static string? ShowCertification(TvShow show)
    {
        var ratings = show.ContentRatings?.Results;
        if (ratings is null)
            return null;

        var preferred = ratings.FirstOrDefault(r => r.Iso_3166_1 == "US") ?? ratings.FirstOrDefault();
        return string.IsNullOrWhiteSpace(preferred?.Rating) ? null : preferred.Rating;
    }

    /// <summary>True when the requested language left the text fields that matter empty.</summary>
    /// <summary>
    /// Whether the original-language record is worth fetching as well. Two independent reasons:
    /// text TMDB has no translation for, and artwork that exists only under the original
    /// language. The second is easy to overlook — a fully translated season can still have no
    /// poster of its own — and without it the preferred artwork kind silently downgrades.
    /// The extra fetch is cached per show and season, so a whole season costs it once.
    /// </summary>
    private static bool NeedsFallback(MediaMetadata metadata, string language) =>
        !language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(metadata.Title)
            || string.IsNullOrWhiteSpace(metadata.Overview)
            || !ArtworkSelector.HasEveryKind(metadata));

    /// <summary>
    /// Fills fields the requested language had nothing for, recording which ones fell back so the
    /// preview pane can say so.
    /// </summary>
    private static void FillGaps(MediaMetadata target, MediaMetadata source)
    {
        if (string.IsNullOrWhiteSpace(target.Title) && !string.IsNullOrWhiteSpace(source.Title))
        {
            target.Title = source.Title;
            target.FallbackFields.Add(nameof(target.Title));
        }

        if (string.IsNullOrWhiteSpace(target.Overview) && !string.IsNullOrWhiteSpace(source.Overview))
        {
            target.Overview = source.Overview;
            target.FallbackFields.Add(nameof(target.Overview));
        }

        if (target.Genres.Count == 0 && source.Genres.Count > 0)
        {
            target.Genres = [.. source.Genres];
            target.FallbackFields.Add(nameof(target.Genres));
        }

        if (string.IsNullOrWhiteSpace(target.ShowName) && !string.IsNullOrWhiteSpace(source.ShowName))
        {
            target.ShowName = source.ShowName;
            target.FallbackFields.Add(nameof(target.ShowName));
        }

        // Artwork is deliberately not recorded as a fallback field: the pane uses those to
        // explain untranslated text, and a poster with no words on it is not a translation gap.
        ArtworkSelector.FillMissingKinds(target, source);
    }

    private static string CacheKey(string kind, string? a, string? b, string? c) =>
        string.Join('|', kind, a?.ToLowerInvariant(), b, c);

    private bool TryGetCached<T>(Dictionary<string, T> cache, string key, out T? value)
    {
        _cacheLock.Wait();
        try
        {
            return cache.TryGetValue(key, out value!);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task StoreAsync<T>(Dictionary<string, T> cache, string key, T value)
    {
        await _cacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            cache[key] = value;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        _cacheLock.Dispose();
        _http.Dispose();
    }
}
