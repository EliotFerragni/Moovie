using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Parsing;
using Xunit;

namespace VideoMetadataFiller.Core.Tests;

/// <summary>
/// The parser's regression corpus. Real-world names go here rather than into ad-hoc asserts,
/// so a pattern tweak that fixes one name and breaks another shows up immediately.
/// </summary>
public class FilenameParserTests
{
    [Theory]
    // ---- Movies: plain, parenthesised and bare years ----
    [InlineData("The Matrix (1999).mp4", "The Matrix", 1999)]
    [InlineData("Inception.2010.1080p.BluRay.x264-SPARKS.mp4", "Inception", 2010)]
    [InlineData("Dune.Part.Two.2024.2160p.WEB-DL.DDP5.1.Atmos.HDR.HEVC-CMRG.mp4", "Dune Part Two", 2024)]
    [InlineData("Spider-Man.No.Way.Home.2021.1080p.WEBRip.mp4", "Spider-Man No Way Home", 2021)]
    [InlineData("the.godfather.1972.remastered.720p.mp4", "The Godfather", 1972)]
    [InlineData("Mad Max Fury Road 2015 EXTENDED 1080p.mp4", "Mad Max Fury Road", 2015)]
    [InlineData("Amelie (2001) [1080p] [YTS.AG].mp4", "Amelie", 2001)]
    [InlineData("Le.Fabuleux.Destin.d.Amelie.Poulain.2001.FRENCH.1080p.mp4",
        "Le Fabuleux Destin d Amelie Poulain", 2001)]
    // ---- Movies: numbers in the title must not be mistaken for the year ----
    [InlineData("Blade.Runner.2049.2017.1080p.BluRay.mp4", "Blade Runner 2049", 2017)]
    [InlineData("Blade.Runner.2049.1080p.BluRay.mp4", "Blade Runner 2049", null)]
    [InlineData("2012.2009.1080p.BluRay.mp4", "2012", 2009)]
    [InlineData("1917.2019.1080p.mp4", "1917", 2019)]
    [InlineData("2001.A.Space.Odyssey.1968.2160p.mp4", "2001 A Space Odyssey", 1968)]
    [InlineData("Apollo.13.1995.1080p.mp4", "Apollo 13", 1995)]
    [InlineData("Ocean's.8.2018.720p.mp4", "Ocean's 8", 2018)]
    // ---- Movies: titles whose words collide with release tokens ----
    [InlineData("Charlottes.Web.2006.720p.BRRip.mp4", "Charlottes Web", 2006)]
    [InlineData("The.French.Connection.1971.1080p.BluRay.mp4", "The French Connection", 1971)]
    [InlineData("The.Italian.Job.2003.1080p.mp4", "The Italian Job", 2003)]
    [InlineData("Uncut.Gems.2019.EXTENDED.1080p.WEB-DL.mp4", "Uncut Gems", 2019)]
    [InlineData("DC.League.of.Super-Pets.2022.1080p.mp4", "DC League of Super-Pets", 2022)]
    [InlineData("Mr.Hollands.Opus.1995.1080p.WEBRip.mp4", "Mr Hollands Opus", 1995)]
    [InlineData("Cam.2018.1080p.NF.WEB-DL.mp4", "Cam", 2018)]
    // ---- Movies that look like episodes ----
    [InlineData("Star.Wars.Episode.4.A.New.Hope.1977.1080p.mp4", "Star Wars Episode 4 A New Hope", 1977)]
    [InlineData("Se7en.1995.1080p.BluRay.mp4", "Se7en", 1995)]
    [InlineData("Toy.Story.2.1999.1080p.mp4", "Toy Story 2", 1999)]
    [InlineData("Alien.3.1992.1080p.mp4", "Alien 3", 1992)]
    public void ParsesMovies(string name, string expectedTitle, int? expectedYear)
    {
        var parsed = FilenameParser.Parse(name);

        Assert.Equal(MediaKind.Movie, parsed.Kind);
        Assert.Equal(expectedTitle, parsed.Title);
        Assert.Equal(expectedYear, parsed.Year);
    }

    [Theory]
    // ---- TV: the standard notations ----
    [InlineData("Breaking.Bad.S01E01.1080p.BluRay.x264.mp4", "Breaking Bad", 1, 1)]
    [InlineData("The.Wire.s02e05.720p.HDTV.mp4", "The Wire", 2, 5)]
    [InlineData("Severance S02E03 2160p ATVP WEB-DL.mp4", "Severance", 2, 3)]
    [InlineData("Friends - 3x14 - The One With Phoebes Ex Partner.mp4", "Friends", 3, 14)]
    [InlineData("Seinfeld.01x03.mp4", "Seinfeld", 1, 3)]
    [InlineData("Doctor Who Season 4 Episode 10 1080p.mp4", "Doctor Who", 4, 10)]
    [InlineData("Kaamelott.Saison.3.Episode.12.mp4", "Kaamelott", 3, 12)]
    [InlineData("Chernobyl.S01.E04.1080p.AMZN.WEB-DL.mp4", "Chernobyl", 1, 4)]
    [InlineData("The.Office.US.S03 - E12.mp4", "The Office US", 3, 12)]
    // ---- TV: show names carrying a year, and high season/episode numbers ----
    [InlineData("The.Office.2005.S02E01.1080p.mp4", "The Office", 2, 1)]
    [InlineData("Doctor.Who.2005.S12E10.720p.mp4", "Doctor Who", 12, 10)]
    [InlineData("One.Piece.S01E1071.1080p.mp4", "One Piece", 1, 1071)]
    [InlineData("Show.Name.S00E01.Special.mp4", "Show Name", 0, 1)]
    public void ParsesTvEpisodes(string name, string expectedShow, int expectedSeason, int expectedEpisode)
    {
        var parsed = FilenameParser.Parse(name);

        Assert.Equal(MediaKind.TvEpisode, parsed.Kind);
        Assert.Equal(expectedShow, parsed.Title);
        Assert.Equal(expectedSeason, parsed.Season);
        Assert.Equal([expectedEpisode], parsed.Episodes);
    }

    [Theory]
    [InlineData("Firefly.S01E01E02.1080p.mp4", new[] { 1, 2 })]
    [InlineData("Firefly.S01E01-E02.1080p.mp4", new[] { 1, 2 })]
    [InlineData("Firefly.S01E01-02.1080p.mp4", new[] { 1, 2 })]
    [InlineData("Firefly.S01E01.1080p.mp4", new[] { 1 })]
    public void ParsesMultiEpisodeFiles(string name, int[] expected)
    {
        var parsed = FilenameParser.Parse(name);

        Assert.Equal(MediaKind.TvEpisode, parsed.Kind);
        Assert.Equal(expected, parsed.Episodes);
        Assert.Equal(expected.Length > 1, parsed.IsMultiEpisode);
    }

    [Fact]
    public void ResolutionInAnEpisodeNameIsNotReadAsAnExtraEpisode()
    {
        var parsed = FilenameParser.Parse("Show.S01E01-1080p.WEB-DL.mp4");

        Assert.Equal([1], parsed.Episodes);
        Assert.Equal("1080p", parsed.Resolution);
    }

    [Theory]
    [InlineData("Movie.2019.2160p.mp4", "2160p")]
    [InlineData("Movie.2019.4K.HDR.mp4", "2160p")]
    [InlineData("Movie.2019.720p.mp4", "720p")]
    [InlineData("Movie.2019.mp4", null)]
    public void ExtractsResolution(string name, string? expected)
    {
        Assert.Equal(expected, FilenameParser.Parse(name).Resolution);
    }

    [Fact]
    public void ParsesDailyShowsByAirDate()
    {
        var parsed = FilenameParser.Parse("The.Daily.Show.2021.03.08.1080p.WEB.mp4");

        Assert.Equal(MediaKind.TvEpisode, parsed.Kind);
        Assert.Equal("The Daily Show", parsed.Title);
        Assert.Equal(new DateTime(2021, 3, 8), parsed.AirDate);
        Assert.Null(parsed.Season);
    }

    [Fact]
    public void SeasonlessEpisodeNumberIsFlaggedForReview()
    {
        var parsed = FilenameParser.Parse("Some.Show.Ep05.mp4");

        Assert.Equal(MediaKind.TvEpisode, parsed.Kind);
        Assert.Equal("Some Show", parsed.Title);
        Assert.Null(parsed.Season);
        Assert.Equal([5], parsed.Episodes);
        Assert.True(parsed.IsLowConfidence);
    }

    [Fact]
    public void ParsesAnimeStyleDashedEpisodeNumbers()
    {
        var parsed = FilenameParser.Parse("[HorribleSubs] Steins Gate - 07 - Somewhere.mp4");

        Assert.Equal(MediaKind.TvEpisode, parsed.Kind);
        Assert.Equal("Steins Gate", parsed.Title);
        Assert.Equal([7], parsed.Episodes);
        Assert.True(parsed.IsLowConfidence);
    }

    [Theory]
    [InlineData("/media/Breaking Bad/Season 02/04 - Down.mp4", "Breaking Bad", 2, 4)]
    [InlineData("/media/Breaking Bad/Season 2/04.mp4", "Breaking Bad", 2, 4)]
    [InlineData("/media/The Bear (2022)/S03/07 - Legacy.mp4", "The Bear", 3, 7)]
    public void RecoversShowAndSeasonFromFolders(string path, string show, int season, int episode)
    {
        var parsed = FilenameParser.Parse(path);

        Assert.Equal(MediaKind.TvEpisode, parsed.Kind);
        Assert.Equal(show, parsed.Title);
        Assert.Equal(season, parsed.Season);
        Assert.Equal([episode], parsed.Episodes);
        Assert.True(parsed.IsLowConfidence);
    }

    [Fact]
    public void RecoversShowNameFromFolderWhenTheFilenameStartsAtTheSeasonMarker()
    {
        var parsed = FilenameParser.Parse("/media/Shows/Andor/S01E04.1080p.mp4");

        Assert.Equal(MediaKind.TvEpisode, parsed.Kind);
        Assert.Equal("Andor", parsed.Title);
        Assert.Equal(1, parsed.Season);
        Assert.Equal([4], parsed.Episodes);
    }

    [Fact]
    public void FallsBackToTheContainingFolderForAnUninformativeFilename()
    {
        var parsed = FilenameParser.Parse("/media/Movies/Arrival (2016)/video.mp4");

        Assert.Equal(MediaKind.Movie, parsed.Kind);
        Assert.Equal("Arrival", parsed.Title);
        Assert.Equal(2016, parsed.Year);
        Assert.True(parsed.IsLowConfidence);
    }

    [Fact]
    public void ReportsUnknownWhenThereIsNothingToGoOn()
    {
        var parsed = FilenameParser.Parse("1080p.mp4");

        Assert.Equal(MediaKind.Unknown, parsed.Kind);
    }
}
