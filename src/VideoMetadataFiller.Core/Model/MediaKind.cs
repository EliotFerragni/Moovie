namespace VideoMetadataFiller.Core.Model;

/// <summary>What kind of title a file holds, as detected from its name or chosen by the user.</summary>
public enum MediaKind
{
    Unknown = 0,
    Movie = 1,
    TvEpisode = 2,
}
