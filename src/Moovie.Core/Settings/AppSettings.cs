using System.Text.Json.Serialization;
using Moovie.Core.Localization;
using Moovie.Core.Model;

namespace Moovie.Core.Settings;

/// <summary>How spaces are treated in a rendered filename.</summary>
public enum SeparatorStyle
{
    Space,
    Dot,
    Underscore,
    Dash,
}

/// <summary>Which colour scheme the interface uses.</summary>
public enum AppTheme
{
    /// <summary>Follow the operating system, and follow it as it changes.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Everything the app remembers between runs.</summary>
public sealed class AppSettings
{
    public const string DefaultMovieTemplate = "{title} ({year})";
    public const string DefaultTvTemplate = "{show} - S{season:00}E{episode:00}< - {episodeTitle}>";

    /// <summary>The <see cref="ScanDepth"/> that goes all the way down, however deep that is.</summary>
    public const int UnlimitedScanDepth = -1;

    /// <summary>The deepest <see cref="ScanDepth"/> that can be picked before "all the way down".</summary>
    public const int MaxNamedScanDepth = 5;

    /// <summary>The user's own TMDB API key (the v3 "API Key", not the v4 read-access token).</summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>Default TMDB language tag, e.g. <c>en-US</c>. Overridable per file.</summary>
    public string Language { get; set; } = "en-US";

    /// <summary>
    /// Language of the app's own interface: a two-letter tag, or <c>system</c> to follow the
    /// operating system. Deliberately separate from <see cref="Language"/>: wanting a French
    /// interface and English metadata is a perfectly reasonable combination.
    /// </summary>
    public string AppLanguage { get; set; } = Strings.SystemTag;

    /// <summary>
    /// Light, dark, or whatever the operating system is set to. Separate from
    /// <see cref="AppLanguage"/> in one important way: this one takes effect immediately, because
    /// the theme is a live property rather than something the views read once as they load.
    /// </summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    public string MovieRenameTemplate { get; set; } = DefaultMovieTemplate;

    public string TvRenameTemplate { get; set; } = DefaultTvTemplate;

    /// <summary>Whether Apply also renames files. Off by default: writing tags is reversible enough, renaming is noisier.</summary>
    public bool RenameEnabled { get; set; }

    public SeparatorStyle Separator { get; set; } = SeparatorStyle.Space;

    /// <summary>
    /// A resolution at or below which <c>{resolution}</c> is left out of a rendered filename, for
    /// the ones a library treats as ordinary and does not bother naming. Empty writes them all.
    /// Only the filename is affected: the resolution is still read, shown, and written as the HD
    /// flag.
    /// </summary>
    public string OmitResolutionAtOrBelow { get; set; } = string.Empty;

    /// <summary>
    /// What stands in for a character no Windows filename may hold. Empty drops them. A colon is
    /// the exception and always becomes a dash, because it separates a title from its subtitle
    /// often enough that anything else reads badly.
    /// </summary>
    public string IllegalCharacterReplacement { get; set; } = string.Empty;

    /// <summary>The three settings that shape a rendered filename, as one value to hand around.</summary>
    [JsonIgnore]
    public NamingRules Naming => new(Separator, OmitResolutionAtOrBelow, IllegalCharacterReplacement);

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

    /// <summary>
    /// How far below a chosen folder the scan goes. 0 takes only what is directly inside it, 1 also
    /// opens each subfolder, and <see cref="UnlimitedScanDepth"/> goes all the way down. The
    /// shallow default keeps a first run pointed at the root of a NAS share from pulling in
    /// everything on it.
    /// </summary>
    public int ScanDepth { get; set; }

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 820;

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(TmdbApiKey);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
