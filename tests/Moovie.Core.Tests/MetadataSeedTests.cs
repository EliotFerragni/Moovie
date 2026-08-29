using Moovie.Core.Model;
using Moovie.Core.Parsing;
using Moovie.Core.Writing;
using Xunit;

namespace Moovie.Core.Tests;

public class MetadataSeedTests
{
    private static ExistingTags Tagged(MediaMetadata metadata) => new(metadata, false, 0, 0);

    /// <summary>
    /// The reported gap: a file that had been tagged before opened with a blank form, because the
    /// seed came from the filename alone and the tags were only ever read for the diff.
    /// </summary>
    [Fact]
    public void The_files_own_tags_come_first()
    {
        var tags = Tagged(new MediaMetadata
        {
            Kind = MediaKind.TvEpisode,
            ShowName = "Severance",
            Title = "Good News About Hell",
            Season = 1,
            Episodes = [1],
            Overview = "Mark takes a promotion.",
            Genres = ["Drama"],
            ContentRating = "TV-14",
        });

        var parsed = new ParsedName
        {
            Kind = MediaKind.TvEpisode,
            Title = "severance",
            Season = 9,
            Episodes = [9],
        };

        var seeded = MetadataSeed.From(tags, parsed);

        Assert.Equal("Severance", seeded.ShowName);
        Assert.Equal("Good News About Hell", seeded.Title);
        Assert.Equal(1, seeded.Season);
        Assert.Equal([1], seeded.Episodes);
        Assert.Equal("Mark takes a promotion.", seeded.Overview);
        Assert.Equal(["Drama"], seeded.Genres);
        Assert.Equal("TV-14", seeded.ContentRating);
    }

    [Fact]
    public void The_filename_fills_what_the_file_does_not_carry()
    {
        // Tagged with a show name and nothing else, which is what a half-done pass leaves.
        var tags = Tagged(new MediaMetadata { Kind = MediaKind.TvEpisode, ShowName = "Severance" });
        var parsed = new ParsedName
        {
            Kind = MediaKind.TvEpisode,
            Title = "severance",
            Season = 1,
            Episodes = [3],
            Year = 2022,
        };

        var seeded = MetadataSeed.From(tags, parsed);

        Assert.Equal("Severance", seeded.ShowName);
        Assert.Equal(1, seeded.Season);
        Assert.Equal([3], seeded.Episodes);
        Assert.Equal(2022, seeded.Year);
    }

    [Fact]
    public void An_untagged_file_falls_back_to_its_name_entirely()
    {
        var parsed = new ParsedName { Kind = MediaKind.Movie, Title = "Blade Runner 2049", Year = 2017 };

        var seeded = MetadataSeed.From(ExistingTags.None, parsed);

        Assert.Equal(MediaKind.Movie, seeded.Kind);
        Assert.Equal("Blade Runner 2049", seeded.Title);
        Assert.Equal(2017, seeded.Year);
    }

    /// <summary>
    /// The container stores a coarse HD flag rather than a frame size, so there is nothing in it
    /// worth preferring over what the video track itself reported.
    /// </summary>
    [Fact]
    public void The_resolution_always_comes_from_the_probe()
    {
        var tags = Tagged(new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival", Resolution = "720p" });
        var parsed = new ParsedName { Kind = MediaKind.Movie, Title = "Arrival", Resolution = "2160p" };

        Assert.Equal("2160p", MetadataSeed.From(tags, parsed).Resolution);

        // Including when the probe found nothing: a stale flag is not a frame size either.
        Assert.Null(MetadataSeed.From(tags, new ParsedName { Title = "Arrival" }).Resolution);
    }

    [Fact]
    public void A_file_with_no_type_of_its_own_takes_the_names_word_for_it()
    {
        var tags = Tagged(new MediaMetadata { Title = "Good News About Hell" });
        var parsed = new ParsedName { Kind = MediaKind.TvEpisode, Title = "Severance", Season = 1, Episodes = [1] };

        var seeded = MetadataSeed.From(tags, parsed);

        Assert.Equal(MediaKind.TvEpisode, seeded.Kind);
        Assert.Equal("Severance", seeded.ShowName);
        Assert.Equal("Good News About Hell", seeded.Title);
    }

    [Fact]
    public void A_name_that_gives_nothing_away_is_treated_as_a_movie()
    {
        var seeded = MetadataSeed.From(ExistingTags.None, new ParsedName { Title = "Some File" });

        Assert.Equal(MediaKind.Movie, seeded.Kind);
        Assert.Equal("Some File", seeded.Title);
    }

    /// <summary>
    /// The tags are cloned rather than handed over: the form is edited in place, and the copy the
    /// diff compares against has to keep saying what is really in the file.
    /// </summary>
    [Fact]
    public void Seeding_does_not_hand_out_the_tags_it_read()
    {
        var tags = Tagged(new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival", Genres = ["Drama"] });

        var seeded = MetadataSeed.From(tags, null);
        seeded.Title = "Edited";
        seeded.Genres.Add("Thriller");

        Assert.Equal("Arrival", tags.Metadata.Title);
        Assert.Single(tags.Metadata.Genres);
    }

    [Fact]
    public void Nothing_read_and_nothing_parsed_is_an_empty_form_rather_than_a_crash()
    {
        var seeded = MetadataSeed.From(null, null);

        Assert.Equal(MediaKind.Unknown, seeded.Kind);
        Assert.Null(seeded.Title);
    }
}
