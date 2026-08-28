using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Writing;
using Xunit;

namespace VideoMetadataFiller.Core.Tests;

public class MetadataDiffTests
{
    private static ExistingTags Existing(MediaMetadata metadata, bool hasArtwork = false, int hdFlag = 0) =>
        new(metadata, hasArtwork, hasArtwork ? 40960 : 0, hdFlag);

    private static MetadataChange? Row(IReadOnlyList<MetadataChange> changes, string key) =>
        changes.FirstOrDefault(c => c.Key == key);

    [Fact]
    public void A_file_that_already_holds_the_metadata_shows_no_changes()
    {
        var metadata = new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Arrival",
            Year = 2016,
            Genres = ["Drama", "Science Fiction"],
            Overview = "Louise learns a language.",
            Cast = ["Amy Adams"],
            Studio = "FilmNation",
        };

        var changes = MetadataDiff.Between(Existing(metadata.Clone()), metadata);

        Assert.Empty(changes);
    }

    [Fact]
    public void Everything_is_an_addition_for_an_untagged_file()
    {
        var pending = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival", Year = 2016 };

        var changes = MetadataDiff.Between(ExistingTags.None, pending);

        Assert.All(changes, c => Assert.Equal(ChangeKind.Added, c.Kind));
        Assert.Equal("Arrival", Row(changes, nameof(MediaMetadata.Title))!.Pending);
        Assert.Null(Row(changes, nameof(MediaMetadata.Title))!.Current);
    }

    [Fact]
    public void A_field_the_new_metadata_lacks_is_reported_as_cleared()
    {
        var current = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival", Genres = ["Drama"] };
        var pending = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival" };

        var changes = MetadataDiff.Between(Existing(current), pending);

        var genres = Row(changes, nameof(MediaMetadata.Genres));
        Assert.NotNull(genres);
        Assert.Equal(ChangeKind.Cleared, genres!.Kind);
        Assert.Equal("Drama", genres.Current);
    }

    /// <summary>
    /// Switching a file to Movie wipes the TV atoms, so the show and season have to appear in the
    /// diff even though the form hides them for a movie.
    /// </summary>
    [Fact]
    public void Turning_an_episode_into_a_movie_shows_the_tv_fields_being_cleared()
    {
        var current = new MediaMetadata
        {
            Kind = MediaKind.TvEpisode,
            ShowName = "Severance",
            Title = "Good News About Hell",
            Season = 1,
            Episodes = [1],
            Network = "Apple TV+",
        };
        var pending = new MediaMetadata { Kind = MediaKind.Movie, Title = "Severance" };

        var changes = MetadataDiff.Between(Existing(current), pending);

        Assert.Equal(ChangeKind.Cleared, Row(changes, nameof(MediaMetadata.ShowName))!.Kind);
        Assert.Equal(ChangeKind.Cleared, Row(changes, nameof(MediaMetadata.Season))!.Kind);
        Assert.Equal(ChangeKind.Cleared, Row(changes, nameof(MediaMetadata.Episodes))!.Kind);
        Assert.Equal(ChangeKind.Cleared, Row(changes, nameof(MediaMetadata.Network))!.Kind);
        Assert.Equal(ChangeKind.Changed, Row(changes, nameof(MediaMetadata.Kind))!.Kind);
    }

    /// <summary>
    /// Only one date atom is written, so a year and the matching full date are the same write and
    /// must not be reported as a difference.
    /// </summary>
    [Fact]
    public void A_year_and_a_full_date_that_write_the_same_string_are_not_a_change()
    {
        var current = new MediaMetadata { Kind = MediaKind.Movie, ReleaseDate = new DateTime(2016, 11, 11), Year = 2016 };
        var pending = new MediaMetadata { Kind = MediaKind.Movie, ReleaseDate = new DateTime(2016, 11, 11) };

        var changes = MetadataDiff.Between(Existing(current), pending);

        Assert.Null(Row(changes, nameof(MediaMetadata.ReleaseDate)));
    }

    [Fact]
    public void A_different_date_is_a_change()
    {
        var current = new MediaMetadata { Kind = MediaKind.Movie, Year = 2016 };
        var pending = new MediaMetadata { Kind = MediaKind.Movie, ReleaseDate = new DateTime(2016, 11, 11) };

        var date = Row(MetadataDiff.Between(Existing(current), pending), nameof(MediaMetadata.ReleaseDate));

        Assert.Equal("2016", date!.Current);
        Assert.Equal("2016-11-11", date.Pending);
    }

    /// <summary>Several resolutions share one flag, so only a change of flag is a change.</summary>
    [Fact]
    public void Resolutions_sharing_an_hd_flag_are_not_a_change()
    {
        var current = new MediaMetadata { Kind = MediaKind.Movie };
        var pending = new MediaMetadata { Kind = MediaKind.Movie, Resolution = "1080i" };

        var unchanged = MetadataDiff.Between(Existing(current, hdFlag: 2), pending);
        var changed = MetadataDiff.Between(Existing(current, hdFlag: 3), pending);

        Assert.Null(Row(unchanged, nameof(MediaMetadata.Resolution)));
        Assert.Equal("1080p", Row(changed, nameof(MediaMetadata.Resolution))!.Pending);
    }

    /// <summary>Applying with no new image leaves the embedded one alone, so there is nothing to report.</summary>
    [Fact]
    public void Artwork_already_in_the_file_is_not_reported_when_nothing_will_replace_it()
    {
        var metadata = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival" };

        var changes = MetadataDiff.Between(Existing(metadata.Clone(), hasArtwork: true), metadata);

        Assert.Null(Row(changes, nameof(MediaMetadata.ArtworkPath)));
    }

    [Fact]
    public void Artwork_that_will_be_written_is_reported_as_added_or_replaced()
    {
        var pending = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival", ArtworkPath = "/poster.jpg" };
        var current = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival" };

        var onBare = Row(MetadataDiff.Between(Existing(current), pending), nameof(MediaMetadata.ArtworkPath));
        var onCovered = Row(
            MetadataDiff.Between(Existing(current, hasArtwork: true), pending), nameof(MediaMetadata.ArtworkPath));

        Assert.Equal("will be added", onBare!.Pending);
        Assert.Equal("will be replaced", onCovered!.Pending);
        Assert.Equal("present (40 kB)", onCovered.Current);
    }

    [Fact]
    public void The_very_image_already_embedded_is_not_reported_as_a_write()
    {
        byte[] image = [1, 2, 3, 4];
        var current = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival", ArtworkData = image };
        var pending = new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Arrival",
            ArtworkPath = "/poster.jpg",
            ArtworkData = [.. image],
        };

        var changes = MetadataDiff.Between(Existing(current, hasArtwork: true), pending);

        Assert.Null(Row(changes, nameof(MediaMetadata.ArtworkPath)));
    }

    [Fact]
    public void A_different_image_is_still_reported_as_a_replacement()
    {
        var current = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival", ArtworkData = [1, 2, 3, 4] };
        var pending = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival", ArtworkData = [9, 9, 9, 9] };

        var changes = MetadataDiff.Between(Existing(current, hasArtwork: true), pending);

        Assert.Equal("will be replaced", Row(changes, nameof(MediaMetadata.ArtworkPath))!.Pending);
    }

    /// <summary>
    /// The diff has to stay limited to atoms the writer touches: promising a change that applying
    /// never makes would be worse than saying nothing.
    /// </summary>
    [Fact]
    public void Fields_with_no_atom_of_their_own_are_left_out()
    {
        var current = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival" };
        var pending = new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Arrival",
            OriginalTitle = "Story of Your Life",
            TmdbId = 329865,
            ImdbId = "tt2543164",
            Language = "fr-FR",
        };

        Assert.Empty(MetadataDiff.Between(Existing(current), pending));
    }
}
