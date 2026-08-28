using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Settings;
using Xunit;

namespace VideoMetadataFiller.Core.Tests;

public class ArtworkSelectorTests
{
    private static MediaMetadata Episode(params (ArtworkKind Kind, string Path)[] artwork)
    {
        var metadata = new MediaMetadata { Kind = MediaKind.TvEpisode };
        foreach (var (kind, path) in artwork)
            metadata.ArtworkByKind[kind] = path;
        return metadata;
    }

    [Fact]
    public void Picks_the_preferred_kind_when_it_exists()
    {
        var episode = Episode(
            (ArtworkKind.EpisodeStill, "/still.jpg"),
            (ArtworkKind.SeasonPoster, "/season.jpg"),
            (ArtworkKind.ShowPoster, "/show.jpg"));

        Assert.Equal("/season.jpg", ArtworkSelector.Resolve(episode, ArtworkKind.SeasonPoster));
        Assert.Equal("/still.jpg", ArtworkSelector.Resolve(episode, ArtworkKind.EpisodeStill));
        Assert.Equal("/show.jpg", ArtworkSelector.Resolve(episode, ArtworkKind.ShowPoster));
    }

    [Fact]
    public void Falls_through_to_the_next_tv_kind_when_the_preferred_one_is_missing()
    {
        // The common case: an old episode with no still of its own.
        var episode = Episode(
            (ArtworkKind.ShowPoster, "/show.jpg"),
            (ArtworkKind.ShowBackdrop, "/backdrop.jpg"));

        Assert.Equal("/show.jpg", ArtworkSelector.Resolve(episode, ArtworkKind.EpisodeStill));
        Assert.Equal("/show.jpg", ArtworkSelector.Resolve(episode, ArtworkKind.SeasonPoster));
    }

    [Fact]
    public void Falls_back_to_backdrop_when_that_is_all_there_is()
    {
        var episode = Episode((ArtworkKind.ShowBackdrop, "/backdrop.jpg"));

        Assert.Equal("/backdrop.jpg", ArtworkSelector.Resolve(episode, ArtworkKind.EpisodeStill));
    }

    [Fact]
    public void Returns_null_when_the_title_has_no_artwork_at_all()
    {
        Assert.Null(ArtworkSelector.Resolve(Episode(), ArtworkKind.EpisodeStill));
    }

    [Fact]
    public void Movies_choose_between_poster_and_backdrop()
    {
        var movie = new MediaMetadata { Kind = MediaKind.Movie };
        movie.ArtworkByKind[ArtworkKind.MoviePoster] = "/poster.jpg";
        movie.ArtworkByKind[ArtworkKind.MovieBackdrop] = "/backdrop.jpg";

        Assert.Equal("/poster.jpg", ArtworkSelector.Resolve(movie, ArtworkKind.MoviePoster));
        Assert.Equal("/backdrop.jpg", ArtworkSelector.Resolve(movie, ArtworkKind.MovieBackdrop));
    }

    [Fact]
    public void A_movie_wanting_a_backdrop_it_lacks_still_gets_its_poster()
    {
        var movie = new MediaMetadata { Kind = MediaKind.Movie };
        movie.ArtworkByKind[ArtworkKind.MoviePoster] = "/poster.jpg";

        Assert.Equal("/poster.jpg", ArtworkSelector.Resolve(movie, ArtworkKind.MovieBackdrop));
    }

    [Fact]
    public void Settings_map_each_medium_to_its_own_preference()
    {
        var settings = new AppSettings
        {
            TvArtwork = ArtworkKind.SeasonPoster,
            MovieArtwork = ArtworkKind.MovieBackdrop,
        };

        Assert.Equal(ArtworkKind.SeasonPoster, settings.PreferredArtwork(MediaKind.TvEpisode));
        Assert.Equal(ArtworkKind.MovieBackdrop, settings.PreferredArtwork(MediaKind.Movie));
    }

    [Fact]
    public void Cloning_metadata_copies_the_artwork_map_rather_than_sharing_it()
    {
        var original = Episode((ArtworkKind.EpisodeStill, "/still.jpg"));
        var clone = original.Clone();

        clone.ArtworkByKind[ArtworkKind.ShowPoster] = "/show.jpg";

        Assert.Single(original.ArtworkByKind);
        Assert.Equal(2, clone.ArtworkByKind.Count);
    }
}
