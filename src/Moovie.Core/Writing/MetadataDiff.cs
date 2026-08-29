using Moovie.Core.Localization;
using Moovie.Core.Model;

namespace Moovie.Core.Writing;

/// <summary>What applying will do to one field.</summary>
public enum ChangeKind
{
    /// <summary>The file has nothing here and a value will be written.</summary>
    Added,

    /// <summary>Both sides have a value and they differ.</summary>
    Changed,

    /// <summary>The file has a value and applying will empty it.</summary>
    Cleared,
}

/// <summary>One row of the preview diff: a field, what is in the file, and what will replace it.</summary>
public sealed record MetadataChange(string Key, string Label, string? Current, string? Pending)
{
    public ChangeKind Kind =>
        Current is null ? ChangeKind.Added
        : Pending is null ? ChangeKind.Cleared
        : ChangeKind.Changed;
}

/// <summary>
/// Compares what a file already holds against what applying would write into it.
/// </summary>
/// <remarks>
/// Two rules keep the result honest. Only fields <see cref="Mp4TagWriter"/> actually writes are
/// compared — a TMDB id has no atom, so listing it would promise a change that never happens —
/// and each side is compared in its <em>written</em> form via <see cref="TagFormat"/>, so a date
/// held as a year on one side and a full date on the other does not read as a difference when
/// both write the same string.
/// </remarks>
public static class MetadataDiff
{
    /// <summary>
    /// The rows that would change. Fields that are the same on both sides are left out: the
    /// point is what applying does, not an inventory of the file.
    /// </summary>
    public static IReadOnlyList<MetadataChange> Between(ExistingTags current, MediaMetadata pending)
    {
        var isTv = pending.Kind == MediaKind.TvEpisode;
        var changes = new List<MetadataChange>();

        void Compare(string key, string labelKey, string? before, string? after)
        {
            before = Blank(before);
            after = Blank(after);
            if (!string.Equals(before, after, StringComparison.Ordinal))
                changes.Add(new MetadataChange(key, Strings.Get(labelKey), before, after));
        }

        Compare(nameof(MediaMetadata.Kind), "diff.type",
            KindLabel(current.Metadata.Kind), KindLabel(pending.Kind));

        Compare(nameof(MediaMetadata.Title), "field.title",
            current.Metadata.Title, pending.Title);

        // The TV fields are compared for both kinds on purpose: switching a file to Movie clears
        // them, and that is a change worth seeing before it happens.
        Compare(nameof(MediaMetadata.ShowName), "field.show",
            current.Metadata.ShowName, isTv ? pending.ShowName : null);

        Compare(nameof(MediaMetadata.Season), "field.season",
            current.Metadata.Season?.ToString(), isTv ? pending.Season?.ToString() : null);

        Compare(nameof(MediaMetadata.Episodes), "field.episode",
            TagFormat.EpisodeLabel(current.Metadata), isTv ? TagFormat.EpisodeLabel(pending) : null);

        Compare(nameof(MediaMetadata.Network), "field.network",
            current.Metadata.Network, isTv ? pending.Network : null);

        Compare(nameof(MediaMetadata.ReleaseDate), "field.releaseDate",
            TagFormat.Date(current.Metadata), TagFormat.Date(pending));

        Compare(nameof(MediaMetadata.Overview), "field.overview",
            current.Metadata.Overview, pending.Overview);

        Compare(nameof(MediaMetadata.Genres), "field.genres",
            TagFormat.Join(current.Metadata.Genres), TagFormat.Join(pending.Genres));

        Compare(nameof(MediaMetadata.Cast), "field.cast",
            TagFormat.Join(current.Metadata.Cast), TagFormat.Join(pending.Cast));

        Compare(nameof(MediaMetadata.Directors), "field.directors",
            TagFormat.Join(current.Metadata.Directors), TagFormat.Join(pending.Directors));

        Compare(nameof(MediaMetadata.Writers), "field.writers",
            TagFormat.Join(current.Metadata.Writers), TagFormat.Join(pending.Writers));

        Compare(nameof(MediaMetadata.Studio), "field.studio",
            current.Metadata.Studio, pending.Studio);

        Compare(nameof(MediaMetadata.ContentRating), "field.contentRating",
            current.Metadata.ContentRating, pending.ContentRating);

        // Compared as the flag rather than as a resolution, because the flag is what is stored
        // and several resolutions share one.
        var currentHd = current.HdFlag;
        var pendingHd = TagFormat.HdFlag(pending.Resolution);
        if (currentHd != pendingHd)
        {
            changes.Add(new MetadataChange(
                nameof(MediaMetadata.Resolution), Strings.Get("diff.hdFlag"),
                TagFormat.HdFlagLabel(currentHd), TagFormat.HdFlagLabel(pendingHd)));
        }

        AddArtworkChange(changes, current, pending);
        return changes;
    }

    /// <summary>
    /// Artwork is the one field applying never empties: with no new image the writer leaves what
    /// is there alone, so an absent cover is not reported as a change waiting to happen.
    /// </summary>
    /// <remarks>
    /// When both sides have the image itself the bytes settle it, which is what makes a file
    /// read back straight after applying say there is nothing left to do. With only a TMDB path
    /// on the pending side there is nothing to compare, so the cover is reported as a write.
    /// </remarks>
    private static void AddArtworkChange(List<MetadataChange> changes, ExistingTags current, MediaMetadata pending)
    {
        var pendingHasArtwork = pending.ArtworkData is { Length: > 0 }
            || !string.IsNullOrWhiteSpace(pending.ArtworkPath);
        if (!pendingHasArtwork)
            return;

        if (pending.ArtworkData is { Length: > 0 } pendingImage
            && current.Metadata.ArtworkData is { Length: > 0 } currentImage
            && currentImage.AsSpan().SequenceEqual(pendingImage))
            return;

        changes.Add(new MetadataChange(
            nameof(MediaMetadata.ArtworkPath),
            Strings.Get("diff.artwork"),
            current.HasArtwork ? Strings.Format("diff.artworkPresent", Kilobytes(current.ArtworkByteCount)) : null,
            current.HasArtwork ? Strings.Get("diff.artworkReplaced") : Strings.Get("diff.artworkAdded")));
    }

    private static int Kilobytes(int bytes) => Math.Max(1, (int)Math.Round(bytes / 1024.0));

    private static string? KindLabel(MediaKind kind) => kind switch
    {
        MediaKind.Movie => Strings.Get("pane.movie"),
        MediaKind.TvEpisode => Strings.Get("pane.tvEpisode"),
        _ => null,
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
