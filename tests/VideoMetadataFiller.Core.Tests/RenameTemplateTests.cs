using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Settings;
using VideoMetadataFiller.Core.Writing;
using Xunit;

namespace VideoMetadataFiller.Core.Tests;

public class RenameTemplateTests
{
    private static MediaMetadata Movie() => new()
    {
        Kind = MediaKind.Movie,
        Title = "Blade Runner 2049",
        Year = 2017,
        ReleaseDate = new DateTime(2017, 10, 6),
        Genres = ["Science Fiction"],
        Resolution = "2160p",
        TmdbId = 335984,
    };

    private static MediaMetadata Episode() => new()
    {
        Kind = MediaKind.TvEpisode,
        ShowName = "Severance",
        Title = "Good News About Hell",
        Season = 1,
        Episodes = [1],
        ReleaseDate = new DateTime(2022, 2, 18),
    };

    private static string Render(string template, MediaMetadata metadata) =>
        RenameTemplate.Parse(template).Render(metadata);

    [Theory]
    [InlineData("{title} ({year})", "Blade Runner 2049 (2017)")]
    [InlineData("{title}", "Blade Runner 2049")]
    [InlineData("{title} {{tmdb-{tmdbId}}}", "Blade Runner 2049 {tmdb-335984}")]
    [InlineData("{title:upper}", "BLADE RUNNER 2049")]
    [InlineData("{title:lower}", "blade runner 2049")]
    [InlineData("{genre} - {title}", "Science Fiction - Blade Runner 2049")]
    [InlineData("{releaseDate:yyyy-MM-dd} {title}", "2017-10-06 Blade Runner 2049")]
    [InlineData("{title} ({releaseDate:yyyy})", "Blade Runner 2049 (2017)")]
    public void RendersMovieTemplates(string template, string expected)
    {
        Assert.Equal(expected, Render(template, Movie()));
    }

    [Theory]
    [InlineData("{show} - S{season:00}E{episode:00} - {episodeTitle}",
        "Severance - S01E01 - Good News About Hell")]
    [InlineData("{show} - S{season:00}E{episode:00}< - {episodeTitle}>",
        "Severance - S01E01 - Good News About Hell")]
    [InlineData("{show} - S{season:0}E{episode:0}", "Severance - S1E1")]
    [InlineData("{show} - S{season:000}E{episode:000}", "Severance - S001E001")]
    [InlineData("{show} {season}x{episode:00}", "Severance 1x01")]
    [InlineData("{show} - {airDate:yyyy-MM-dd}", "Severance - 2022-02-18")]
    public void RendersTvTemplates(string template, string expected)
    {
        Assert.Equal(expected, Render(template, Episode()));
    }

    [Theory]
    [InlineData("{season:0}", 1, "1")]
    [InlineData("{season:00}", 1, "01")]
    [InlineData("{season:000}", 1, "001")]
    [InlineData("{season:00}", 12, "12")]
    [InlineData("{season:0}", 12, "12")]
    public void DigitPaddingFollowsTheNumberOfZeros(string template, int season, string expected)
    {
        var episode = Episode();
        episode.Season = season;

        Assert.Equal(expected, Render(template, episode));
    }

    [Fact]
    public void OptionalSegmentIsDroppedWhenItsTokensAreEmpty()
    {
        var episode = Episode();
        episode.Title = null;

        Assert.Equal("Severance - S01E01",
            Render("{show} - S{season:00}E{episode:00}< - {episodeTitle}>", episode));
    }

    [Fact]
    public void OptionalSegmentIsKeptWhenItsTokensHaveValues()
    {
        Assert.Equal("Severance - S01E01 - Good News About Hell",
            Render("{show} - S{season:00}E{episode:00}< - {episodeTitle}>", Episode()));
    }

    [Fact]
    public void SquareBracketsAreLiteralSoTheCommonResolutionStyleJustWorks()
    {
        Assert.Equal("Blade Runner 2049 [2160p]", Render("{title} [{resolution}]", Movie()));
    }

    [Fact]
    public void EmptyTokenOutsideAnOptionalSegmentLeavesItsLiteralsBehind()
    {
        var movie = Movie();
        movie.Resolution = null;

        // Deliberate: only < … > collapses. That is what the optional syntax is for.
        Assert.Equal("Blade Runner 2049 []", Render("{title} [{resolution}]", movie));
    }

    [Fact]
    public void OptionalSegmentsNestAndCollapseTogether()
    {
        var movie = Movie();
        movie.Resolution = null;

        Assert.Equal("Blade Runner 2049", Render("{title}< [{resolution}]>", movie));
    }

    [Fact]
    public void MultiEpisodeFilesRepeatTheLetterPrefix()
    {
        var episode = Episode();
        episode.Episodes = [1, 2];

        Assert.Equal("Severance - S01E01-E02", Render("{show} - S{season:00}E{episode:00}", episode));
    }

    [Fact]
    public void MultiEpisodeFilesWithoutALetterPrefixJoinBare()
    {
        var episode = Episode();
        episode.Episodes = [1, 2];

        Assert.Equal("Severance 1x01-02", Render("{show} {season}x{episode:00}", episode));
    }

    [Fact]
    public void MissingValuesRenderAsNothing()
    {
        var movie = new MediaMetadata { Kind = MediaKind.Movie, Title = "Untitled" };

        Assert.Equal("Untitled ()", Render("{title} ({year})", movie));
    }

    [Theory]
    [InlineData("{title} ({year})")]
    [InlineData("{show} - S{season:00}E{episode:00}< - {episodeTitle}>")]
    [InlineData("{title:upper} {{literal}}")]
    [InlineData("{title} [{resolution}]")]
    [InlineData("{airDate:MMMM d, yyyy} {title}")]
    public void AcceptsValidTemplates(string template)
    {
        var validation = RenameTemplate.Validate(template);

        Assert.True(validation.IsValid, string.Join(" / ", validation.Errors));
    }

    [Theory]
    [InlineData("{titel}", "Unknown token")]
    [InlineData("{title", "Unclosed '{'")]
    [InlineData("{title} }", "Unmatched '}'")]
    [InlineData("{title} <{year}", "Unclosed '<'")]
    [InlineData("{title} {year}>", "Unmatched '>'")]
    [InlineData("{season:xx}", "digit padding")]
    [InlineData("{title:sentence}", "upper, lower or title")]
    [InlineData("", "empty")]
    [InlineData("Movies/{title}", "path separator")]
    public void RejectsBrokenTemplates(string template, string expectedFragment)
    {
        var validation = RenameTemplate.Validate(template);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DefaultTemplatesShippedInSettingsAreValid()
    {
        Assert.True(RenameTemplate.Validate(AppSettings.DefaultMovieTemplate).IsValid);
        Assert.True(RenameTemplate.Validate(AppSettings.DefaultTvTemplate).IsValid);
    }
}
