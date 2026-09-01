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

/// <summary>
/// One row of the preview diff: a field, the value the file already carries, the value the lookup
/// produced, and the value that will actually be written. Three values rather than two because
/// the pane lets each field be pointed at either source, and a field typed into by hand matches
/// neither.
/// </summary>
public sealed record MetadataChange(string Key, string Label, string? Current, string? Pending)
{
    /// <summary>What the lookup produced for this field, in written form.</summary>
    public string? Fetched { get; init; }

    /// <summary>False for a file nothing has been looked up for, where there is no TMDB side.</summary>
    public bool HasFetched { get; init; }

    /// <summary>
    /// Whether the file's own value can be taken over. False only for the HD flag: the stored
    /// flag maps back to several resolutions, so there is no honest value to adopt.
    /// </summary>
    public bool CanAdoptCurrent { get; init; } = true;

    /// <summary>
    /// Whether applying changes this field at all. A row can exist without being a change: one
    /// pointed at the file's own value is listed so it can be pointed back at TMDB.
    /// </summary>
    public bool IsChange { get; init; } = true;

    public ChangeKind Kind =>
        Current is null ? ChangeKind.Added
        : Pending is null ? ChangeKind.Cleared
        : ChangeKind.Changed;

    /// <summary>The file's value is the one that will be written.</summary>
    public bool PendingMatchesCurrent => !IsChange;

    /// <summary>
    /// The lookup's value is the one that will be written. Stored rather than worked out from
    /// the two strings, because the artwork row displays images and its text is only a caption.
    /// </summary>
    public bool PendingMatchesFetched { get; init; }
}

/// <summary>
/// Compares what a file already holds, what the lookup produced for it, and what applying would
/// write into it.
/// </summary>
/// <remarks>
/// Three rules keep the result honest: only fields <see cref="Mp4TagWriter"/> actually writes are
/// compared, every column is compared in its <em>written</em> form via <see cref="TagFormat"/>,
/// and all three columns are gated by the <em>pending</em> media kind. Otherwise a row would offer
/// a value that applying could not produce.
/// </remarks>
public static class MetadataDiff
{
    /// <summary>
    /// The rows worth showing. A field is listed when applying would change it, or when what
    /// will be written is no longer what the lookup produced: that second case is how a field
    /// taken over from the file, or typed by hand, stays on screen to be put back.
    /// </summary>
    /// <param name="fetched">
    /// The metadata as the lookup last returned it, or null for a file that has not been looked
    /// up, which leaves every row with only the file's side to offer.
    /// </param>
    public static IReadOnlyList<MetadataChange> Between(
        ExistingTags current, MediaMetadata pending, MediaMetadata? fetched = null)
    {
        var isTv = pending.Kind == MediaKind.TvEpisode;
        var changes = new List<MetadataChange>();

        void Compare(string key, string labelKey, string? before, string? after, string? tmdb)
        {
            before = Blank(before);
            after = Blank(after);
            tmdb = Blank(tmdb);

            var isChange = !string.Equals(before, after, StringComparison.Ordinal);
            var strayed = fetched is not null && !string.Equals(after, tmdb, StringComparison.Ordinal);
            if (!isChange && !strayed)
                return;

            changes.Add(new MetadataChange(key, Strings.Get(labelKey), before, after)
            {
                Fetched = tmdb,
                HasFetched = fetched is not null,
                PendingMatchesFetched = !strayed && fetched is not null,
                IsChange = isChange,
            });
        }

        Compare(nameof(MediaMetadata.Kind), "diff.type",
            KindLabel(current.Metadata.Kind), KindLabel(pending.Kind), KindLabel(fetched?.Kind ?? MediaKind.Unknown));

        Compare(nameof(MediaMetadata.Title), "field.title",
            current.Metadata.Title, pending.Title, fetched?.Title);

        // The TV fields are compared for both kinds on purpose: switching a file to Movie clears
        // them, and that is a change worth seeing before it happens.
        Compare(nameof(MediaMetadata.ShowName), "field.show",
            current.Metadata.ShowName, isTv ? pending.ShowName : null, isTv ? fetched?.ShowName : null);

        Compare(nameof(MediaMetadata.Season), "field.season",
            current.Metadata.Season?.ToString(), isTv ? pending.Season?.ToString() : null,
            isTv ? fetched?.Season?.ToString() : null);

        Compare(nameof(MediaMetadata.Episodes), "field.episode",
            TagFormat.EpisodeLabel(current.Metadata), isTv ? TagFormat.EpisodeLabel(pending) : null,
            isTv && fetched is not null ? TagFormat.EpisodeLabel(fetched) : null);

        Compare(nameof(MediaMetadata.Network), "field.network",
            current.Metadata.Network, isTv ? pending.Network : null, isTv ? fetched?.Network : null);

        Compare(nameof(MediaMetadata.ReleaseDate), "field.releaseDate",
            TagFormat.Date(current.Metadata), TagFormat.Date(pending),
            fetched is null ? null : TagFormat.Date(fetched));

        Compare(nameof(MediaMetadata.Overview), "field.overview",
            current.Metadata.Overview, pending.Overview, fetched?.Overview);

        Compare(nameof(MediaMetadata.Genres), "field.genres",
            TagFormat.Join(current.Metadata.Genres), TagFormat.Join(pending.Genres),
            fetched is null ? null : TagFormat.Join(fetched.Genres));

        Compare(nameof(MediaMetadata.Cast), "field.cast",
            TagFormat.Join(current.Metadata.Cast), TagFormat.Join(pending.Cast),
            fetched is null ? null : TagFormat.Join(fetched.Cast));

        Compare(nameof(MediaMetadata.Directors), "field.directors",
            TagFormat.Join(current.Metadata.Directors), TagFormat.Join(pending.Directors),
            fetched is null ? null : TagFormat.Join(fetched.Directors));

        Compare(nameof(MediaMetadata.Writers), "field.writers",
            TagFormat.Join(current.Metadata.Writers), TagFormat.Join(pending.Writers),
            fetched is null ? null : TagFormat.Join(fetched.Writers));

        Compare(nameof(MediaMetadata.Studio), "field.studio",
            current.Metadata.Studio, pending.Studio, fetched?.Studio);

        Compare(nameof(MediaMetadata.ContentRating), "field.contentRating",
            current.Metadata.ContentRating, pending.ContentRating, fetched?.ContentRating);

        AddHdFlagChange(changes, current, pending, fetched);
        AddArtworkChange(changes, current, pending, fetched);
        return changes;
    }

    /// <summary>
    /// The HD flag is compared as the flag rather than as a resolution, because the flag is what
    /// is stored and several resolutions share one. That is also why the file's side cannot be
    /// taken over: a stored 2 gives no way back to 1080i, 1080p or 1440p.
    /// </summary>
    private static void AddHdFlagChange(
        List<MetadataChange> changes, ExistingTags current, MediaMetadata pending, MediaMetadata? fetched)
    {
        var currentHd = current.HdFlag;
        var pendingHd = TagFormat.HdFlag(pending.Resolution);
        var fetchedHd = fetched is null ? pendingHd : TagFormat.HdFlag(fetched.Resolution);

        var isChange = currentHd != pendingHd;
        if (!isChange && pendingHd == fetchedHd)
            return;

        changes.Add(new MetadataChange(
            nameof(MediaMetadata.Resolution), Strings.Get("diff.hdFlag"),
            TagFormat.HdFlagLabel(currentHd), TagFormat.HdFlagLabel(pendingHd))
        {
            Fetched = TagFormat.HdFlagLabel(fetchedHd),
            HasFetched = fetched is not null,
            PendingMatchesFetched = fetched is not null && pendingHd == fetchedHd,
            CanAdoptCurrent = false,
            IsChange = isChange,
        });
    }

    /// <summary>
    /// Artwork is the one field applying never empties: with no new image the writer leaves what
    /// is there alone. When both sides hold the image itself the bytes settle it, which is what
    /// lets a file read back straight after applying report nothing left to do; with only a TMDB
    /// path on the pending side there is nothing to compare, so it counts as a write.
    /// </summary>
    private static void AddArtworkChange(
        List<MetadataChange> changes, ExistingTags current, MediaMetadata pending, MediaMetadata? fetched)
    {
        var willWrite = WillWriteArtwork(current, pending);
        var strayed = fetched is not null
            && !string.Equals(pending.ArtworkPath, fetched.ArtworkPath, StringComparison.Ordinal);
        if (!willWrite && !strayed)
            return;

        var present = current.HasArtwork
            ? Strings.Format("diff.artworkPresent", Kilobytes(current.ArtworkByteCount))
            : null;

        changes.Add(new MetadataChange(
            nameof(MediaMetadata.ArtworkPath),
            Strings.Get("diff.artwork"),
            present,
            willWrite
                ? current.HasArtwork ? Strings.Get("diff.artworkReplaced") : Strings.Get("diff.artworkAdded")
                : present)
        {
            Fetched = fetched is null || string.IsNullOrWhiteSpace(fetched.ArtworkPath)
                ? null
                : Strings.Get("diff.artworkFromTmdb"),
            HasFetched = fetched is not null,
            PendingMatchesFetched = fetched is not null && !strayed,
            IsChange = willWrite,
        });
    }

    /// <summary>Whether applying would put a different cover into the file.</summary>
    private static bool WillWriteArtwork(ExistingTags current, MediaMetadata pending)
    {
        var pendingHasArtwork = pending.ArtworkData is { Length: > 0 }
            || !string.IsNullOrWhiteSpace(pending.ArtworkPath);
        if (!pendingHasArtwork)
            return false;

        return pending.ArtworkData is not { Length: > 0 } pendingImage
            || current.Metadata.ArtworkData is not { Length: > 0 } currentImage
            || !currentImage.AsSpan().SequenceEqual(pendingImage);
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
