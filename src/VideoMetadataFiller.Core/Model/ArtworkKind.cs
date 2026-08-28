using VideoMetadataFiller.Core.Localization;

namespace VideoMetadataFiller.Core.Model;

/// <summary>
/// A kind of image TMDB can offer for a title. Which one a file gets is a user preference, and
/// any of them can also be picked by hand in the preview pane.
/// </summary>
public enum ArtworkKind
{
    /// <summary>The 16:9 frame grabbed from the episode itself.</summary>
    EpisodeStill,

    /// <summary>2:3 poster for one season of a show.</summary>
    SeasonPoster,

    /// <summary>2:3 poster for the show as a whole.</summary>
    ShowPoster,

    /// <summary>16:9 wide art for the show as a whole.</summary>
    ShowBackdrop,

    MoviePoster,

    MovieBackdrop,
}

/// <summary>Which kinds make sense for which media, and how they read on screen.</summary>
public static class ArtworkKinds
{
    /// <summary>Offered for TV episodes, in the order they appear in Settings.</summary>
    public static readonly IReadOnlyList<ArtworkKind> ForTv =
        [ArtworkKind.EpisodeStill, ArtworkKind.SeasonPoster, ArtworkKind.ShowPoster, ArtworkKind.ShowBackdrop];

    /// <summary>Offered for movies.</summary>
    public static readonly IReadOnlyList<ArtworkKind> ForMovies =
        [ArtworkKind.MoviePoster, ArtworkKind.MovieBackdrop];

    /// <summary>
    /// How a kind reads on screen. Used by the Settings dropdowns, the picker's captions and the
    /// preview pane's caption, so it has to be translated like anything else.
    /// </summary>
    public static string Label(ArtworkKind kind) => kind switch
    {
        ArtworkKind.EpisodeStill => Strings.Get("artwork.episodeStill"),
        ArtworkKind.SeasonPoster => Strings.Get("artwork.seasonPoster"),
        ArtworkKind.ShowPoster => Strings.Get("artwork.showPoster"),
        ArtworkKind.ShowBackdrop => Strings.Get("artwork.showBackdrop"),
        ArtworkKind.MoviePoster => Strings.Get("artwork.poster"),
        ArtworkKind.MovieBackdrop => Strings.Get("artwork.backdrop"),
        _ => kind.ToString(),
    };

    /// <summary>Wide art is 16:9; posters are 2:3. Used to lay the preview out without cropping.</summary>
    public static bool IsWide(ArtworkKind kind) =>
        kind is ArtworkKind.EpisodeStill or ArtworkKind.ShowBackdrop or ArtworkKind.MovieBackdrop;
}
