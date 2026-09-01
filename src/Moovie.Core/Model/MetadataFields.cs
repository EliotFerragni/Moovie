namespace Moovie.Core.Model;

/// <summary>
/// Copies one named field from one <see cref="MediaMetadata"/> onto another, which is what the
/// preview diff's per-field revert is built on. Both sources it offers are plain
/// <see cref="MediaMetadata"/>, so nothing has to be parsed back out of the strings on screen.
/// </summary>
public static class MetadataFields
{
    /// <summary>
    /// How each field moves from one metadata object to another. Keys are <c>nameof</c> of the
    /// property, which is what <c>MetadataDiff</c> puts on each row: anything the diff can list
    /// has to appear here or clicking that row would do nothing. Lists are copied rather than
    /// shared, so the two sides stay independent.
    /// </summary>
    private static readonly Dictionary<string, Action<MediaMetadata, MediaMetadata>> Copiers =
        new(StringComparer.Ordinal)
        {
            [nameof(MediaMetadata.Kind)] = (from, to) => to.Kind = from.Kind,
            [nameof(MediaMetadata.Title)] = (from, to) => to.Title = from.Title,
            [nameof(MediaMetadata.ShowName)] = (from, to) => to.ShowName = from.ShowName,
            [nameof(MediaMetadata.Season)] = (from, to) => to.Season = from.Season,
            [nameof(MediaMetadata.Episodes)] = (from, to) => to.Episodes = [.. from.Episodes],
            [nameof(MediaMetadata.Network)] = (from, to) => to.Network = from.Network,
            [nameof(MediaMetadata.Overview)] = (from, to) => to.Overview = from.Overview,
            [nameof(MediaMetadata.Genres)] = (from, to) => to.Genres = [.. from.Genres],
            [nameof(MediaMetadata.Cast)] = (from, to) => to.Cast = [.. from.Cast],
            [nameof(MediaMetadata.Directors)] = (from, to) => to.Directors = [.. from.Directors],
            [nameof(MediaMetadata.Writers)] = (from, to) => to.Writers = [.. from.Writers],
            [nameof(MediaMetadata.Studio)] = (from, to) => to.Studio = from.Studio,
            [nameof(MediaMetadata.ContentRating)] = (from, to) => to.ContentRating = from.ContentRating,
            [nameof(MediaMetadata.Resolution)] = (from, to) => to.Resolution = from.Resolution,

            // The date and the year are one field as far as the file is concerned: the writer
            // falls back to the year when there is no date, so moving only one would leave the
            // diff still reporting a difference.
            [nameof(MediaMetadata.ReleaseDate)] = (from, to) =>
            {
                to.ReleaseDate = from.ReleaseDate;
                to.Year = from.Year;
            },

            // The chosen image and any bytes held for it move together. Tags read out of a file
            // carry bytes and no path, which is exactly what "leave the cover alone" needs.
            [nameof(MediaMetadata.ArtworkPath)] = (from, to) =>
            {
                to.ArtworkPath = from.ArtworkPath;
                to.ArtworkData = from.ArtworkData;
            },
        };

    /// <summary>Whether <see cref="Copy"/> knows this field.</summary>
    public static bool CanCopy(string key) => Copiers.ContainsKey(key);

    /// <summary>
    /// Puts <paramref name="source"/>'s value for <paramref name="key"/> onto
    /// <paramref name="target"/>, and reports whether it knew the field.
    /// </summary>
    public static bool Copy(string key, MediaMetadata source, MediaMetadata target)
    {
        if (!Copiers.TryGetValue(key, out var copy))
            return false;

        copy(source, target);
        return true;
    }
}
