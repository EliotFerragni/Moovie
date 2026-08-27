using VideoMetadataFiller.Core.Matching;
using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Parsing;
using VideoMetadataFiller.Core.Tmdb;
using Xunit;

namespace VideoMetadataFiller.Core.Tests;

/// <summary>
/// Covers the decision the app cannot get wrong: when to pick a title itself, and when to stop and
/// ask. Every case runs against <see cref="FakeTmdbService"/>, so no network or API key is involved.
/// </summary>
public class MatchResolverTests
{
    private const string Language = "en-US";

    private static Candidate Movie(int id, string title, int? year = null, double popularity = 1) => new()
    {
        TmdbId = id,
        Kind = MediaKind.Movie,
        Title = title,
        Year = year,
        Popularity = popularity,
    };

    private static Candidate Show(int id, string title, int? year = null, double popularity = 1) => new()
    {
        TmdbId = id,
        Kind = MediaKind.TvEpisode,
        Title = title,
        Year = year,
        Popularity = popularity,
    };

    [Fact]
    public async Task AnExactTitleAndYearIsTakenWithoutAsking()
    {
        var tmdb = new FakeTmdbService { MovieResults = [Movie(1, "Inception", 2010)] };
        var parsed = FilenameParser.Parse("Inception.2010.1080p.BluRay.mp4");

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.Matched, outcome.Status);
        Assert.Equal("Inception", outcome.Metadata!.Title);
        Assert.Equal(1, outcome.Metadata.TmdbId);
    }

    [Fact]
    public async Task TwoEquallyGoodTitlesBecomeAChoice()
    {
        // The classic ambiguous case: a remake with the same title and no year in the filename.
        var tmdb = new FakeTmdbService
        {
            MovieResults = [Movie(1, "The Italian Job", 1969), Movie(2, "The Italian Job", 2003)],
        };
        var parsed = FilenameParser.Parse("The.Italian.Job.1080p.BluRay.mp4");

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.NeedsChoice, outcome.Status);
        Assert.Equal(2, outcome.Candidates.Count);
        Assert.Contains("pick one", outcome.Message);
    }

    [Fact]
    public async Task TheYearInTheFilenameResolvesAnOtherwiseAmbiguousTitle()
    {
        var tmdb = new FakeTmdbService
        {
            MovieResults = [Movie(1, "The Italian Job", 1969), Movie(2, "The Italian Job", 2003)],
        };
        var parsed = FilenameParser.Parse("The.Italian.Job.2003.1080p.mp4");

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.Matched, outcome.Status);
        Assert.Equal(2, outcome.Metadata!.TmdbId);
    }

    [Fact]
    public async Task AClearFavouriteIsTakenEvenWithWeakerRivalsAround()
    {
        var tmdb = new FakeTmdbService
        {
            MovieResults =
            [
                Movie(1, "Arrival", 2016),
                Movie(2, "Arrival of a Train at La Ciotat", 1896),
                Movie(3, "The Arrival", 1996),
            ],
        };
        var parsed = FilenameParser.Parse("Arrival.2016.1080p.mp4");

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.Matched, outcome.Status);
        Assert.Equal(1, outcome.Metadata!.TmdbId);
    }

    [Fact]
    public async Task NoResultsIsReportedAsNotFound()
    {
        var tmdb = new FakeTmdbService();
        var parsed = FilenameParser.Parse("Some.Obscure.Thing.2019.mp4");

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.NotFound, outcome.Status);
        Assert.Empty(outcome.Candidates);
    }

    [Fact]
    public async Task AnUnparseableNameIsNotSearchedForAtAll()
    {
        var tmdb = new FakeTmdbService();

        var outcome = await new MatchResolver(tmdb).ResolveAsync(FilenameParser.Parse("1080p.mp4"), Language);

        Assert.Equal(FileStatus.NotFound, outcome.Status);
        Assert.Equal(0, tmdb.MovieSearchCalls);
        Assert.Equal(0, tmdb.ShowSearchCalls);
    }

    [Fact]
    public async Task AnEpisodeIsMatchedThroughItsShow()
    {
        var tmdb = new FakeTmdbService
        {
            ShowResults = [Show(10, "Severance", 2022)],
            Episodes = { [(2, 3)] = "Who Is Alive?" },
        };
        var parsed = FilenameParser.Parse("Severance.S02E03.2160p.ATVP.WEB-DL.mp4");

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.Matched, outcome.Status);
        Assert.Equal("Severance", outcome.Metadata!.ShowName);
        Assert.Equal("Who Is Alive?", outcome.Metadata.Title);
        Assert.Equal(2, outcome.Metadata.Season);
        Assert.Equal([3], outcome.Metadata.Episodes);
    }

    [Fact]
    public async Task AMultiEpisodeFileKeepsBothNumbers()
    {
        var tmdb = new FakeTmdbService
        {
            ShowResults = [Show(10, "Firefly", 2002)],
            Episodes = { [(1, 1)] = "Serenity" },
        };
        var parsed = FilenameParser.Parse("Firefly.S01E01-E02.1080p.mp4");

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.Matched, outcome.Status);
        Assert.Equal([1, 2], outcome.Metadata!.Episodes);
    }

    [Fact]
    public async Task AKnownShowWithAnUnknownEpisodeAsksButKeepsTheShow()
    {
        // "Some Show Ep05" has no season, so the parser guesses season 1 and flags itself.
        var tmdb = new FakeTmdbService
        {
            ShowResults = [Show(10, "Some Show")],
            // Deliberately no season 1 episode 5.
        };
        var parsed = new ParsedName
        {
            Kind = MediaKind.TvEpisode,
            Title = "Some Show",
            Season = 1,
            Episodes = [5],
        };

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.NeedsChoice, outcome.Status);
        Assert.Contains("no season 1 episode 5", outcome.Message);
    }

    [Fact]
    public async Task AnEpisodeWithNoNumberAtAllHandsBackAHalfFilledForm()
    {
        var tmdb = new FakeTmdbService { ShowResults = [Show(10, "Some Show")] };
        var parsed = new ParsedName { Kind = MediaKind.TvEpisode, Title = "Some Show" };

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.NeedsChoice, outcome.Status);
        // The show is settled, so the form is pre-filled and only the numbers are missing.
        Assert.Equal("Some Show", outcome.Metadata!.ShowName);
        Assert.Equal(10, outcome.Metadata.TmdbId);
        Assert.Contains("set the season and episode", outcome.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ADailyShowIsLocatedByItsAirDate()
    {
        var tmdb = new FakeTmdbService
        {
            ShowResults = [Show(10, "The Daily Show", 1996)],
            EpisodesByAirDate = { [new DateTime(2021, 3, 8)] = new EpisodeCoordinate(26, 71) },
            Episodes = { [(26, 71)] = "Guest of the night" },
        };
        var parsed = FilenameParser.Parse("The.Daily.Show.2021.03.08.1080p.WEB.mp4");

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.Matched, outcome.Status);
        Assert.Equal(26, outcome.Metadata!.Season);
        Assert.Equal([71], outcome.Metadata.Episodes);
    }

    [Fact]
    public async Task AShakyParseIsHeldToAHigherBar()
    {
        // A season-folder parse of "Show Name/Season 02/04.mp4" is flagged low-confidence. A single
        // exact hit still passes; a near-miss that would otherwise squeak through does not.
        var tmdb = new FakeTmdbService { ShowResults = [Show(10, "Breaking Bad Rebooted", 2024)] };
        var parsed = new ParsedName
        {
            Kind = MediaKind.TvEpisode,
            Title = "Breaking Bad",
            Season = 2,
            Episodes = [4],
            IsLowConfidence = true,
        };

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.NeedsChoice, outcome.Status);
    }

    [Fact]
    public async Task AShakyParseWithAnExactHitStillMatches()
    {
        var tmdb = new FakeTmdbService
        {
            ShowResults = [Show(10, "Breaking Bad", 2008)],
            Episodes = { [(2, 4)] = "Down" },
        };
        var parsed = FilenameParser.Parse("/media/Breaking Bad/Season 02/04 - Down.mp4");

        Assert.True(parsed.IsLowConfidence);

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.Matched, outcome.Status);
        Assert.Equal("Down", outcome.Metadata!.Title);
    }

    [Fact]
    public async Task TheResolutionFromTheFilenameSurvivesTheLookup()
    {
        var tmdb = new FakeTmdbService { MovieResults = [Movie(1, "Dune Part Two", 2024)] };
        var parsed = FilenameParser.Parse("Dune.Part.Two.2024.2160p.WEB-DL.mp4");

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal("2160p", outcome.Metadata!.Resolution);
    }

    [Fact]
    public async Task ATitleFoundButWithoutDetailsFallsBackToAChoice()
    {
        var tmdb = new FakeTmdbService
        {
            MovieResults = [Movie(1, "Inception", 2010)],
            MoviesWithoutDetails = { 1 },
        };
        var parsed = FilenameParser.Parse("Inception.2010.1080p.mp4");

        var outcome = await new MatchResolver(tmdb).ResolveAsync(parsed, Language);

        Assert.Equal(FileStatus.NeedsChoice, outcome.Status);
        Assert.Single(outcome.Candidates);
    }

    [Fact]
    public async Task TheRequestedLanguageIsPassedThrough()
    {
        var tmdb = new FakeTmdbService { MovieResults = [Movie(1, "Amelie", 2001)] };
        var parsed = FilenameParser.Parse("Amelie.2001.1080p.mp4");

        await new MatchResolver(tmdb).ResolveAsync(parsed, "fr-FR");

        Assert.All(tmdb.RequestedLanguages, language => Assert.Equal("fr-FR", language));
    }

    [Fact]
    public void CandidatesAreRankedBestFirstAndCappedForTheChooser()
    {
        var candidates = Enumerable.Range(1, 20)
            .Select(i => Movie(i, i == 7 ? "Heat" : $"Heat Wave {i}", 1995))
            .ToList();
        var parsed = new ParsedName { Kind = MediaKind.Movie, Title = "Heat", Year = 1995 };

        var decision = MatchResolver.Rank(parsed, candidates);

        Assert.Equal("Heat", decision.Best!.Title);
        Assert.True(decision.Ranked.Count <= 8, "the chooser should not be handed twenty rows");
        Assert.True(decision.Ranked[0].Score >= decision.Ranked[^1].Score);
    }

    [Fact]
    public void PopularityOnlyBreaksTiesBetweenEquallyGoodTitles()
    {
        var candidates = new List<Candidate>
        {
            Movie(1, "Heat", 1995, popularity: 3),
            Movie(2, "Heat", 1995, popularity: 90),
        };
        var parsed = new ParsedName { Kind = MediaKind.Movie, Title = "Heat", Year = 1995 };

        var decision = MatchResolver.Rank(parsed, candidates);

        Assert.Equal(2, decision.Best!.TmdbId);
        // Two identically-titled films from the same year are exactly what a human should settle.
        Assert.False(decision.IsAutomatic);
    }
}
