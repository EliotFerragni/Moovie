using Moovie.Core.Model;
using Moovie.Core.Writing;
using Xunit;

namespace Moovie.Core.Tests;

public class MetadataFieldsTests
{
    /// <summary>
    /// Every key the diff can put on a row has to be one the copier knows, or clicking that row
    /// would do nothing. The HD flag is the deliberate exception and is checked separately.
    /// </summary>
    [Fact]
    public void Every_field_the_diff_reports_can_be_copied()
    {
        var current = new MediaMetadata
        {
            Kind = MediaKind.TvEpisode,
            Title = "Good News About Hell",
            ShowName = "Severance",
            Season = 1,
            Episodes = [1],
            Network = "Apple TV+",
            ReleaseDate = new DateTime(2022, 2, 18),
            Overview = "Mark leads a team.",
            Genres = ["Drama"],
            Cast = ["Adam Scott"],
            Directors = ["Ben Stiller"],
            Writers = ["Dan Erickson"],
            Studio = "Red Hour",
            ContentRating = "TV-MA",
            ArtworkData = [1, 2, 3],
        };

        var pending = new MediaMetadata { Kind = MediaKind.Movie, Title = "Something else" };

        var changes = MetadataDiff.Between(new ExistingTags(current, true, 3, 0), pending);

        Assert.NotEmpty(changes);
        foreach (var change in changes)
            Assert.True(MetadataFields.CanCopy(change.Key), $"no copier for {change.Key}");
    }

    [Fact]
    public void Copying_a_field_leaves_every_other_field_alone()
    {
        var source = new MediaMetadata { Title = "Arrival", Overview = "Louise learns a language." };
        var target = new MediaMetadata { Title = "arrival 2016", Overview = "Kept." };

        Assert.True(MetadataFields.Copy(nameof(MediaMetadata.Title), source, target));

        Assert.Equal("Arrival", target.Title);
        Assert.Equal("Kept.", target.Overview);
    }

    /// <summary>
    /// Lists are the case where sharing rather than copying would bite: the two metadata objects
    /// outlive the click, and editing one afterwards would silently change the other.
    /// </summary>
    [Fact]
    public void Copied_lists_are_independent_of_the_source()
    {
        var source = new MediaMetadata { Genres = ["Drama", "Science Fiction"] };
        var target = new MediaMetadata();

        MetadataFields.Copy(nameof(MediaMetadata.Genres), source, target);
        target.Genres.Add("Thriller");

        Assert.Equal(2, source.Genres.Count);
        Assert.Equal(3, target.Genres.Count);
    }

    /// <summary>
    /// The writer falls back to the year when there is no date, so the two move together — moving
    /// only the date would leave a value the diff still reports as different.
    /// </summary>
    [Fact]
    public void The_release_date_and_the_year_move_together()
    {
        var source = new MediaMetadata { ReleaseDate = null, Year = null };
        var target = new MediaMetadata { ReleaseDate = new DateTime(2016, 11, 11), Year = 2016 };

        MetadataFields.Copy(nameof(MediaMetadata.ReleaseDate), source, target);

        Assert.Null(target.ReleaseDate);
        Assert.Null(target.Year);
    }

    /// <summary>
    /// Tags read out of a file carry the cover's bytes and no TMDB path. Copying both is what
    /// turns "keep what is in the file" into something the writer honours: it is handed the same
    /// image it already has, so nothing about the cover changes.
    /// </summary>
    [Fact]
    public void Taking_the_files_own_cover_moves_the_bytes_and_drops_the_path()
    {
        var fromFile = new MediaMetadata { ArtworkData = [1, 2, 3, 4] };
        var pending = new MediaMetadata { ArtworkPath = "/tmdb.jpg" };

        MetadataFields.Copy(nameof(MediaMetadata.ArtworkPath), fromFile, pending);

        Assert.Null(pending.ArtworkPath);
        Assert.Equal([1, 2, 3, 4], pending.ArtworkData);
        Assert.DoesNotContain(
            MetadataDiff.Between(new ExistingTags(fromFile, true, 4, 0), pending),
            c => c.Key == nameof(MediaMetadata.ArtworkPath));
    }

    [Fact]
    public void Going_back_to_the_lookups_cover_restores_the_path_and_forgets_the_bytes()
    {
        var fetched = new MediaMetadata { ArtworkPath = "/tmdb.jpg" };
        var pending = new MediaMetadata { ArtworkData = [1, 2, 3, 4] };

        MetadataFields.Copy(nameof(MediaMetadata.ArtworkPath), fetched, pending);

        Assert.Equal("/tmdb.jpg", pending.ArtworkPath);
        Assert.Null(pending.ArtworkData);
    }

    [Fact]
    public void An_unknown_field_is_reported_rather_than_silently_ignored()
    {
        Assert.False(MetadataFields.CanCopy(nameof(MediaMetadata.TmdbId)));
        Assert.False(MetadataFields.Copy(nameof(MediaMetadata.TmdbId), new MediaMetadata(), new MediaMetadata()));
    }
}
