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

    private readonly Dictionary<string, Bitmap?> _decoded = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Loads artwork for a TMDB-relative path. Returns null when there is no artwork, no API
    /// access, or the download failed — callers show a placeholder instead.
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
                return cached;
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
            _decoded[key] = bitmap;
        }
        finally
        {
            _lock.Release();
        }

        return bitmap;
    }

    /// <summary>
    /// Decodes an image already in hand — a cover read straight out of a file, which has no TMDB
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
        foreach (var bitmap in _decoded.Values)
            bitmap?.Dispose();
        _decoded.Clear();
        _lock.Dispose();
    }
}
