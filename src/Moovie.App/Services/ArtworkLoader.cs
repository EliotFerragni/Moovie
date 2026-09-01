using Avalonia.Media.Imaging;
using Moovie.Core.Tmdb;

namespace Moovie.App.Services;

/// <summary>
/// Turns TMDB image paths into bitmaps for on-screen previews, keeping decoded images around so
/// scrolling a long candidate list does not re-download or re-decode anything.
/// </summary>
public sealed class ArtworkLoader : IDisposable
{
    /// <summary>Small enough to stay cheap, large enough for the preview pane's poster.</summary>
    public const string PreviewSize = "w342";

    /// <summary>Thumbnail size for the candidate chooser.</summary>
    public const string ThumbnailSize = "w154";

    /// <summary>
    /// Height to decode an embedded cover to for the file list. A cover in a container is
    /// full-size, and a scrolled list of three hundred of them at full size is a lot of memory
    /// spent on 48-pixel tiles.
    /// </summary>
    public const int ThumbnailHeight = 96;

    /// <summary>
    /// How much decoded artwork to keep. A decoded bitmap costs four bytes a pixel whatever the
    /// JPEG behind it weighed, so this holds a few hundred w154 thumbnails: more than one scroll
    /// through a candidate list needs, and short of holding every image a week-old session showed.
    /// </summary>
    private const long Budget = 48L * 1024 * 1024;

    /// <summary>
    /// And a ceiling on entries, because a lookup that found no artwork is cached too, as the
    /// cheapest way not to ask again, and those weigh nothing against <see cref="Budget"/>.
    /// </summary>
    private const int MaxEntries = 512;

    private sealed class Entry
    {
        public Bitmap? Bitmap { get; init; }

        /// <summary>Four bytes a pixel, or zero for a lookup that found nothing.</summary>
        public long Bytes { get; init; }

        public long LastUsed { get; set; }
    }

    private readonly Dictionary<string, Entry> _decoded = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    private long _uses;
    private long _held;

    /// <summary>
    /// Loads artwork for a TMDB-relative path. Returns null when there is no artwork, no API
    /// access, or the download failed: callers show a placeholder instead.
    /// </summary>
    public async Task<Bitmap?> LoadAsync(
        ITmdbService? tmdb, string? artworkPath, string size, CancellationToken cancellationToken = default)
    {
        if (tmdb is null || string.IsNullOrWhiteSpace(artworkPath))
            return null;

        var key = $"{size}|{artworkPath}";

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_decoded.TryGetValue(key, out var cached))
            {
                cached.LastUsed = ++_uses;
                return cached.Bitmap;
            }
        }
        finally
        {
            _lock.Release();
        }

        Bitmap? bitmap = null;
        var bytes = await tmdb.GetArtworkAsync(artworkPath, size, cancellationToken).ConfigureAwait(false);
        if (bytes is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                bitmap = new Bitmap(stream);
            }
            catch (Exception)
            {
                // A corrupt or unexpected image format is not worth failing the file over.
                bitmap = null;
            }
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Keep(key, bitmap);
        }
        finally
        {
            _lock.Release();
        }

        return bitmap;
    }

    /// <summary>
    /// Files one decoded image and drops the least recently used ones until the cache is back
    /// inside its limits. Call with <see cref="_lock"/> held.
    /// </summary>
    /// <remarks>
    /// What is dropped is the reference, never the bitmap: whatever asked for an image usually
    /// still holds it, and disposing one Avalonia is about to draw would take the window down.
    /// Letting go is enough, since the collector reclaims a surface no view is showing.
    /// </remarks>
    private void Keep(string key, Bitmap? bitmap)
    {
        var bytes = bitmap is null
            ? 0
            : (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;

        if (_decoded.Remove(key, out var replaced))
            _held -= replaced.Bytes;

        _decoded[key] = new Entry { Bitmap = bitmap, Bytes = bytes, LastUsed = ++_uses };
        _held += bytes;

        while (_decoded.Count > MaxEntries || (_held > Budget && _decoded.Count > 1))
        {
            var oldest = key;
            var oldestUse = long.MaxValue;
            foreach (var (candidate, entry) in _decoded)
            {
                if (entry.LastUsed >= oldestUse)
                    continue;

                oldest = candidate;
                oldestUse = entry.LastUsed;
            }

            if (!_decoded.Remove(oldest, out var evicted))
                break;

            _held -= evicted.Bytes;
        }
    }

    /// <summary>
    /// Decodes an image already in hand: a cover read straight out of a file, which has no TMDB
    /// path to cache it under. The caller owns the bitmap and disposes it when done.
    /// </summary>
    /// <param name="decodeToHeight">
    /// Decode down to this height rather than at full size, for a list tile that would otherwise
    /// hold a 1500-pixel cover to draw 48 of them.
    /// </param>
    public static Bitmap? Decode(byte[]? bytes, int? decodeToHeight = null)
    {
        if (bytes is not { Length: > 0 })
            return null;

        try
        {
            using var stream = new MemoryStream(bytes);
            return decodeToHeight is { } height
                ? Bitmap.DecodeToHeight(stream, height)
                : new Bitmap(stream);
        }
        catch (Exception)
        {
            // A cover in a format we cannot decode is a blank tile, not a broken pane.
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var entry in _decoded.Values)
            entry.Bitmap?.Dispose();

        _decoded.Clear();
        _held = 0;
        _lock.Dispose();
    }
}
