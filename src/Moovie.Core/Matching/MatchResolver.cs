using Moovie.Core.Model;
using Moovie.Core.Tmdb;
using Moovie.Core.Localization;

namespace Moovie.Core.Matching;

/// <summary>What the resolver concluded about one file.</summary>
/// <param name="Status">
/// <see cref="FileStatus.Matched"/>, <see cref="FileStatus.NeedsChoice"/> or
/// <see cref="FileStatus.NotFound"/>.
/// </param>
/// <param name="Metadata">
/// The chosen metadata. Also set for some <see cref="FileStatus.NeedsChoice"/> outcomes, where the
/// show is certain but the episode is not.
/// </param>
/// <param name="Candidates">Ranked alternatives to offer the user, best first.</param>
/// <param name="Message">Why a choice or a failure happened, in words fit for the UI.</param>
public sealed record MatchOutcome(
    FileStatus Status,
    MediaMetadata? Metadata,
    IReadOnlyList<Candidate> Candidates,
    string? Message = null)
{
    public static MatchOutcome NotFound(string message) => new(FileStatus.NotFound, null, [], message);

    public static MatchOutcome Matched(MediaMetadata metadata) => new(FileStatus.Matched, metadata, []);

    public static MatchOutcome NeedsChoice(
        IReadOnlyList<Candidate> candidates, string message, MediaMetadata? metadata = null) =>
        new(FileStatus.NeedsChoice, metadata, candidates, message);
}

/// <summary>How a set of candidates was ranked, and whether one of them wins outright.</summary>
public sealed record RankingDecision(bool IsAutomatic, IReadOnlyList<Candidate> Ranked)
{
    public Candidate? Best => Ranked.Count > 0 ? Ranked[0] : null;
}

/// <summary>
/// Turns a parsed filename into metadata, deciding along the way whether it is confident enough to
/// pick a title itself or has to ask.
/// </summary>
public sealed class MatchResolver(ITmdbService tmdb)
{
    /// <summary>A lone strong match needs this score to be taken without asking.</summary>
    private const double AutoSelectScore = 0.85;

    /// <summary>With rivals in play, this score plus a clear lead is enough.</summary>
    private const double AutoSelectWithLeadScore = 0.75;

    private const double RequiredLead = 0.15;

    /// <summary>A shaky parse has to clear a higher bar before we act on it unattended.</summary>
    private const double LowConfidenceScore = 0.90;

    private const double LowConfidenceLead = 0.20;

    /// <summary>Below this a candidate is not treated as a real rival.</summary>
    private const double PlausibleScore = 0.50;

    /// <summary>How many alternatives the chooser shows.</summary>
    private const int MaxCandidates = 8;

    /// <summary>Assumed season when a name carries an episode number but no season.</summary>
    private const int AssumedSeason = 1;

    public async Task<MatchOutcome> ResolveAsync(
        ParsedName parsed, string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parsed.Title))
            return MatchOutcome.NotFound(Strings.Get("match.nothingToSearch"));

        return parsed.Kind == MediaKind.TvEpisode
            ? await ResolveEpisodeAsync(parsed, language, cancellationToken).ConfigureAwait(false)
            : await ResolveMovieAsync(parsed, language, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MatchOutcome> ResolveMovieAsync(
        ParsedName parsed, string language, CancellationToken cancellationToken)
    {
        var candidates = await tmdb.SearchMoviesAsync(parsed.Title, parsed.Year, language, cancellationToken)
            .ConfigureAwait(false);
        var decision = Rank(parsed, candidates);

        if (decision.Best is null)
            return MatchOutcome.NotFound(Strings.Format("match.noMovie", parsed.Title));

        if (!decision.IsAutomatic)
            return MatchOutcome.NeedsChoice(decision.Ranked, Strings.Get("match.severalMovies"));

        var metadata = await tmdb.GetMovieAsync(decision.Best.TmdbId, language, cancellationToken)
            .ConfigureAwait(false);
        if (metadata is null)
            return MatchOutcome.NeedsChoice(decision.Ranked, Strings.Get("match.noDetailsForBest"));

        metadata.Resolution = parsed.Resolution;
        return MatchOutcome.Matched(metadata);
    }

    private async Task<MatchOutcome> ResolveEpisodeAsync(
        ParsedName parsed, string language, CancellationToken cancellationToken)
    {
        var candidates = await tmdb.SearchShowsAsync(parsed.Title, parsed.Year, language, cancellationToken)
            .ConfigureAwait(false);
        var decision = Rank(parsed, candidates);

        if (decision.Best is null)
            return MatchOutcome.NotFound(Strings.Format("match.noShow", parsed.Title));

        if (!decision.IsAutomatic)
            return MatchOutcome.NeedsChoice(decision.Ranked, Strings.Get("match.severalShows"));

        var show = decision.Best;
        var coordinate = await LocateEpisodeAsync(show.TmdbId, parsed, language, cancellationToken)
            .ConfigureAwait(false);

        if (coordinate is null)
        {
            // The show is settled but the episode is not, so hand the user a half-filled form
            // rather than a bare list to choose from again.
            return MatchOutcome.NeedsChoice(
                decision.Ranked,
                Strings.Get("match.showButNoEpisode"),
                new MediaMetadata
                {
                    Kind = MediaKind.TvEpisode,
                    ShowName = show.Title,
                    TmdbId = show.TmdbId,
                    Season = parsed.Season,
                    Episodes = [.. parsed.Episodes],
                    ArtworkPath = show.PosterPath,
                    Resolution = parsed.Resolution,
                    Language = language,
                });
        }

        var episodes = parsed.Episodes.Count > 0 ? parsed.Episodes : [coordinate.Value.Episode];
        var metadata = await tmdb
            .GetEpisodeAsync(show.TmdbId, coordinate.Value.Season, episodes, language, cancellationToken)
            .ConfigureAwait(false);

        if (metadata is null)
        {
            return MatchOutcome.NeedsChoice(
                decision.Ranked,
                Strings.Format("match.noSuchEpisode", show.Title, coordinate.Value.Season, coordinate.Value.Episode));
        }

        metadata.Resolution = parsed.Resolution;
        return MatchOutcome.Matched(metadata);
    }

    /// <summary>
    /// Works out which episode a file holds: straight from the parsed numbers, or by air date for
    /// daily shows.
    /// </summary>
    private async Task<EpisodeCoordinate?> LocateEpisodeAsync(
        int showId, ParsedName parsed, string language, CancellationToken cancellationToken)
    {
        if (parsed.FirstEpisode is { } episode)
            return new EpisodeCoordinate(parsed.Season ?? AssumedSeason, episode);

        if (parsed.AirDate is { } airDate)
        {
            return await tmdb.FindEpisodeByAirDateAsync(showId, airDate, language, cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Scores and orders candidates, and decides whether the top one can be taken unattended.
    /// </summary>
    public static RankingDecision Rank(ParsedName parsed, IReadOnlyList<Candidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            candidate.Score = TitleScorer.Score(
                parsed.Title, candidate.Title, candidate.OriginalTitle, parsed.Year, candidate.Year);
        }

        var ranked = candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Popularity)
            .Take(MaxCandidates)
            .ToList();

        return new RankingDecision(IsAutomatic(parsed, ranked), ranked);
    }

    private static bool IsAutomatic(ParsedName parsed, List<Candidate> ranked)
    {
        if (ranked.Count == 0)
            return false;

        var best = ranked[0].Score;
        var lead = ranked.Count > 1 ? best - ranked[1].Score : 1d;
        var rivals = ranked.Count(c => c.Score >= PlausibleScore);

        if (parsed.IsLowConfidence)
            return best >= LowConfidenceScore && lead >= LowConfidenceLead;

        if (best >= AutoSelectScore && rivals <= 1)
            return true;

        return best >= AutoSelectWithLeadScore && lead >= RequiredLead;
    }
}
