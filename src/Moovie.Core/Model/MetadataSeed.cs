using Moovie.Core.Parsing;
using Moovie.Core.Writing;

namespace Moovie.Core.Model;

/// <summary>
/// Builds the metadata a file starts with, before anything is looked up: the tags it already
/// carries, with the filename filling in whatever it does not.
/// </summary>
/// <remarks>
/// The order is the whole point. Tags a previous pass wrote are real data and outrank a guess
/// pulled out of a filename, so a file that has been tagged before opens showing its own content
/// rather than a blank sheet. The filename is there to supply what is missing, which for an
/// untagged file is everything.
/// </remarks>
public static class MetadataSeed
{
    public static MediaMetadata From(ExistingTags? tags, ParsedName? parsed)
    {
        var metadata = (tags ?? ExistingTags.None).Metadata.Clone();

        if (parsed is null)
            return metadata;

        if (metadata.Kind == MediaKind.Unknown)
        {
            // A file with no type of its own is a movie unless the name says otherwise, which is
            // what the rest of the app assumes and what the form has to lay itself out for.
            metadata.Kind = parsed.Kind == MediaKind.Unknown ? MediaKind.Movie : parsed.Kind;
        }

        metadata.Season ??= parsed.Season;
        metadata.Year ??= parsed.Year;
        metadata.ReleaseDate ??= parsed.AirDate;

        if (metadata.Episodes.Count == 0)
            metadata.Episodes = [.. parsed.Episodes];

        if (metadata.Kind == MediaKind.TvEpisode)
            metadata.ShowName ??= parsed.Title;
        else if (string.IsNullOrWhiteSpace(metadata.Title))
            metadata.Title = parsed.Title;

        // The exception to the order: the container stores a coarse HD flag, not a frame size, so
        // there is nothing in it to prefer over what the video track itself reported.
        metadata.Resolution = parsed.Resolution;

        return metadata;
    }
}
