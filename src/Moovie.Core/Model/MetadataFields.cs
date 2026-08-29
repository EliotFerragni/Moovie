namespace Moovie.Core.Model;

/// <summary>
/// Copies one named field from one <see cref="MediaMetadata"/> onto another.
/// </summary>
/// <remarks>
/// This is what the preview diff's per-field revert is built on. Both sources it offers — the
/// tags read back out of the file and the snapshot the lookup returned — are plain
/// <see cref="MediaMetadata"/>, so pointing a field at either is the same operation with a
/// different source, and nothing has to be parsed back out of the strings the diff displays.
/// </remarks>
public static class MetadataFields
{
    /// <summary>
    /// Whether <see cref="Copy"/> knows this field. Keys are <c>nameof</c> of the property, which
    /// is what <c>MetadataDiff</c> puts on each row.
    /// </summary>
    public static bool CanCopy(string key) => key switch
    {
        nameof(MediaMetadata.Kind) => true,
        nameof(MediaMetadata.Title) => true,
        nameof(MediaMetadata.ShowName) => true,
        nameof(MediaMetadata.Season) => true,
        nameof(MediaMetadata.Episodes) => true,
        nameof(MediaMetadata.Network) => true,
        nameof(MediaMetadata.ReleaseDate) => true,
        nameof(MediaMetadata.Overview) => true,
        nameof(MediaMetadata.Genres) => true,
        nameof(MediaMetadata.Cast) => true,
        nameof(MediaMetadata.Directors) => true,
        nameof(MediaMetadata.Writers) => true,
        nameof(MediaMetadata.Studio) => true,
        nameof(MediaMetadata.ContentRating) => true,
        nameof(MediaMetadata.Resolution) => true,
        nameof(MediaMetadata.ArtworkPath) => true,
        _ => false,
    };

    /// <summary>
    /// Puts <paramref name="source"/>'s value for <paramref name="key"/> onto
    /// <paramref name="target"/>, and reports whether it knew the field. Lists are copied rather
    /// than shared, so the two sides stay independent.
    /// </summary>
    public static bool Copy(string key, MediaMetadata source, MediaMetadata target)
    {
        switch (key)
        {
            case nameof(MediaMetadata.Kind):
                target.Kind = source.Kind;
                return true;

            case nameof(MediaMetadata.Title):
                target.Title = source.Title;
                return true;

            case nameof(MediaMetadata.ShowName):
                target.ShowName = source.ShowName;
                return true;

            case nameof(MediaMetadata.Season):
                target.Season = source.Season;
                return true;

            case nameof(MediaMetadata.Episodes):
                target.Episodes = [.. source.Episodes];
                return true;

            case nameof(MediaMetadata.Network):
                target.Network = source.Network;
                return true;

            // The date and the year are one field as far as the file is concerned: the writer
            // falls back to the year when there is no date, so moving only one of them would
            // leave a value the diff still reports as different.
            case nameof(MediaMetadata.ReleaseDate):
                target.ReleaseDate = source.ReleaseDate;
                target.Year = source.Year;
                return true;

            case nameof(MediaMetadata.Overview):
                target.Overview = source.Overview;
                return true;

            case nameof(MediaMetadata.Genres):
                target.Genres = [.. source.Genres];
                return true;

            case nameof(MediaMetadata.Cast):
                target.Cast = [.. source.Cast];
                return true;

            case nameof(MediaMetadata.Directors):
                target.Directors = [.. source.Directors];
                return true;

            case nameof(MediaMetadata.Writers):
                target.Writers = [.. source.Writers];
                return true;

            case nameof(MediaMetadata.Studio):
                target.Studio = source.Studio;
                return true;

            case nameof(MediaMetadata.ContentRating):
                target.ContentRating = source.ContentRating;
                return true;

            case nameof(MediaMetadata.Resolution):
                target.Resolution = source.Resolution;
                return true;

            // The chosen image and any bytes already held for it move together. Tags read out of
            // a file carry the bytes and no path, which is exactly what "leave the cover alone"
            // needs: the writer only replaces a cover it was given a different image for.
            case nameof(MediaMetadata.ArtworkPath):
                target.ArtworkPath = source.ArtworkPath;
                target.ArtworkData = source.ArtworkData;
                return true;

            default:
                return false;
        }
    }
}
