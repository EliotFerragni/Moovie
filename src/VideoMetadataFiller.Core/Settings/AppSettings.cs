using System.Text.Json.Serialization;

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

    public string MovieRenameTemplate { get; set; } = DefaultMovieTemplate;

    public string TvRenameTemplate { get; set; } = DefaultTvTemplate;

    /// <summary>Whether Apply also renames files. Off by default — writing tags is reversible enough, renaming is noisier.</summary>
    public bool RenameEnabled { get; set; }

    public SeparatorStyle Separator { get; set; } = SeparatorStyle.Space;

    /// <summary>TMDB image size for embedded artwork.</summary>
    public string ArtworkSize { get; set; } = "w780";

    /// <summary>Keep a <c>.bak</c> copy of each file before its tags are rewritten.</summary>
    public bool CreateBackup { get; set; }

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 820;

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(TmdbApiKey);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
