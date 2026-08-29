using System.Text.Json.Serialization;
using VideoMetadataFiller.Core.Localization;
using VideoMetadataFiller.Core.Model;

namespace VideoMetadataFiller.Core.Settings;

/// <summary>How spaces are treated in a rendered filename.</summary>
public enum SeparatorStyle
{
    Space,
    Dot,
    Underscore,
    Dash,
}

/// <summary>Everything the app remembers between runs.</summary>
public sealed class AppSettings
{
    public const string DefaultMovieTemplate = "{title} ({year})";
    public const string DefaultTvTemplate = "{show} - S{season:00}E{episode:00}< - {episodeTitle}>";

    /// <summary>
    /// The user's own TMDB API key (the v3 "API Key", not the v4 read-access token).
    /// </summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>Default TMDB language tag, e.g. <c>en-US</c>. Overridable per file.</summary>
    public string Language { get; set; } = "en-US";

    /// <summary>
    /// Language of the app's own interface: a two-letter tag, or <c>system</c> to follow the
    /// operating system. Deliberately separate from <see cref="Language"/> — wanting a French
    /// interface and English metadata is a perfectly reasonable combination.
    /// </summary>
    public string AppLanguage { get; set; } = Strings.SystemTag;

    public string MovieRenameTemplate { get; set; } = DefaultMovieTemplate;

    public string TvRenameTemplate { get; set; } = DefaultTvTemplate;

    /// <summary>Whether Apply also renames files. Off by default — writing tags is reversible enough, renaming is noisier.</summary>
    public bool RenameEnabled { get; set; }

    public SeparatorStyle Separator { get; set; } = SeparatorStyle.Space;

    /// <summary>
    /// A resolution at or below which <c>{resolution}</c> is left out of a rendered filename, for
    /// the ones a library treats as ordinary and does not bother naming. Empty writes them all.
    /// </summary>
    /// <remarks>
    /// Only the filename is affected. The resolution is still read, still shown in the pane, and
    /// still decides the HD flag written into the file — hiding it from a name is a naming
    /// preference, not a claim that the app does not know it.
    /// </remarks>
    public string OmitResolutionAtOrBelow { get; set; } = string.Empty;

    /// <summary>
    /// TMDB image size for embedded artwork. The preview pane downloads this same size, so what
    /// is on screen is what gets written.
    /// </summary>
    public string ArtworkSize { get; set; } = "w780";

    /// <summary>Which artwork a TV episode gets by default. Falls back when the kind is missing.</summary>
    public ArtworkKind TvArtwork { get; set; } = ArtworkKind.EpisodeStill;

    /// <summary>Which artwork a movie gets by default.</summary>
    public ArtworkKind MovieArtwork { get; set; } = ArtworkKind.MoviePoster;

    /// <summary>The preferred kind for a given medium.</summary>
    public ArtworkKind PreferredArtwork(MediaKind kind) =>
        kind == MediaKind.Movie ? MovieArtwork : TvArtwork;

    /// <summary>Keep a <c>.bak</c> copy of each file before its tags are rewritten.</summary>
    public bool CreateBackup { get; set; }

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 820;

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(TmdbApiKey);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
