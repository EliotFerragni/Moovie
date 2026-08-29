using Moovie.Core.Model;
using Moovie.Core.Settings;
using Moovie.Core.Writing;
using Xunit;

namespace Moovie.Core.Tests;

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

    [Fact]
    public void Short_is_accepted_on_the_resolution_and_nowhere_else()
    {
        Assert.True(RenameTemplate.Validate("{resolution:short}").IsValid);

        var wrongToken = RenameTemplate.Validate("{title:short}");
        Assert.False(wrongToken.IsValid);
        Assert.Contains("upper, lower or title", wrongToken.Errors[0]);
    }

    [Fact]
    public void The_resolutions_error_names_short_among_its_formats()
    {
        var errors = RenameTemplate.Validate("{resolution:sideways}").Errors;

        Assert.Contains("upper, lower, title or short", errors[0]);
    }

    [Fact]
    public void The_ordinary_text_formats_still_work_on_the_resolution()
    {
        Assert.True(RenameTemplate.Validate("{resolution:upper}").IsValid);
    }

    /// <summary>
    /// The palette shows every form of a token with a worked example, so the two lists have to
    /// agree: anything offered there and refused here would be advertised and then rejected.
    /// </summary>
    [Fact]
    public void Every_format_the_palette_offers_is_one_the_validator_accepts()
    {
        foreach (var token in RenameTokens.All)
        {
            foreach (var written in token.OfferedFormats.Select(token.Written))
                Assert.True(RenameTemplate.Validate(written).IsValid, written);
        }
    }

    /// <summary>
    /// A worked example only teaches when there is something in the field, so the samples have
    /// to carry a value for every token their template kind offers.
    /// </summary>
    [Theory]
    [InlineData(MediaKind.Movie)]
    [InlineData(MediaKind.TvEpisode)]
    public void The_palette_sample_has_a_value_for_every_token_it_offers(MediaKind kind)
    {
        var sample = kind == MediaKind.Movie ? RenameEngine.SampleMovie : RenameEngine.SampleEpisode;

        // {ext} is the exception: it comes from the file, not the metadata.
        foreach (var token in RenameTokens.All.Where(t => t.IsRelevantTo(kind) && t.Name != "ext"))
        {
            var rendered = RenameTemplate.Parse(token.Written(null)).Render(sample);
            Assert.False(string.IsNullOrEmpty(rendered), $"{{{token.Name}}} renders nothing for the {kind} sample");
        }
    }

    [Fact]
    public void A_token_is_written_with_and_without_a_format()
    {
        var season = RenameTokens.Find("season")!;

        Assert.Equal("{season}", season.Written(null));
        Assert.Equal("{season:00}", season.Written("00"));
    }

    /// <summary>
    /// The complaint this exists for: {tmdbId:00} rendered the id unchanged, three times, and a
    /// reference that lists three ways of writing the same number teaches the opposite of what it
    /// is for. Case conversion is exempt — whether :title changes anything depends on the value,
    /// not on the token, so it stays offered even where a well-cased sample makes it look inert.
    /// </summary>
    [Theory]
    [InlineData(MediaKind.Movie)]
    [InlineData(MediaKind.TvEpisode)]
    public void No_offered_format_leaves_the_value_exactly_as_it_was(MediaKind kind)
    {
        string[] caseConversions = ["upper", "lower", "title"];
        var sample = kind == MediaKind.Movie ? RenameEngine.SampleMovie : RenameEngine.SampleEpisode;

        foreach (var token in RenameTokens.All.Where(t => t.IsRelevantTo(kind) && t.Name != "ext"))
        {
            var bare = RenameTemplate.Parse(token.Written(null)).Render(sample);

            foreach (var format in token.OfferedFormats
                         .Where(f => f is not null && !caseConversions.Contains(f)))
            {
                var rendered = RenameTemplate.Parse(token.Written(format)).Render(sample);
                Assert.False(rendered == bare,
                    $"{token.Written(format)} renders '{rendered}', the same as {token.Written(null)}");
            }
        }
    }

    [Theory]
    // A year is always four digits and an id is not a counted quantity, so padding is inert.
    [InlineData("year")]
    [InlineData("tmdbId")]
    // Codes, not prose: upper mangles them and title is nonsense on a value with no words.
    [InlineData("imdbId")]
    public void A_token_its_formats_cannot_help_offers_only_itself(string name)
    {
        Assert.Equal([null], RenameTokens.Find(name)!.OfferedFormats);
    }

    [Fact]
    public void The_resolution_offers_its_own_format_and_none_of_the_text_ones()
    {
        Assert.Equal([null, "short"], RenameTokens.Find("resolution")!.OfferedFormats);
    }

    /// <summary>Clicking a chip must not insert the very format that does nothing.</summary>
    [Fact]
    public void A_chip_inserts_a_format_only_where_one_helps()
    {
        Assert.Equal("{season:00}", RenameTokens.Find("season")!.Insertion);
        Assert.Equal("{year}", RenameTokens.Find("year")!.Insertion);
        Assert.Equal("{tmdbId}", RenameTokens.Find("tmdbId")!.Insertion);
    }

    /// <summary>The validator is unchanged: nothing that used to parse stops parsing.</summary>
    [Theory]
    [InlineData("{year:0000}")]
    [InlineData("{tmdbId:00}")]
    [InlineData("{resolution:upper}")]
    [InlineData("{imdbId:lower}")]
    public void A_format_the_palette_stopped_offering_is_still_accepted(string template)
    {
        Assert.True(RenameTemplate.Validate(template).IsValid);
    }
}
