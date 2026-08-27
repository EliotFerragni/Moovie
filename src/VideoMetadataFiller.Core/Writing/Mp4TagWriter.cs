using System.Text;
using TagLib;
using TagLib.Mpeg4;
using VideoMetadataFiller.Core.Model;
using File = TagLib.File;

namespace VideoMetadataFiller.Core.Writing;

/// <summary>Raised when a file cannot be tagged. Carries a message fit to show in the file list.</summary>
public sealed class TagWriteException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Writes <see cref="MediaMetadata"/> into an MP4/M4V container as iTunes-style atoms, then reads
/// the file back to confirm the write landed.
/// </summary>
public sealed class Mp4TagWriter
{
    /// <summary>Extensions this writer handles.</summary>
    public static readonly string[] SupportedExtensions = [".mp4", ".m4v"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Writes <paramref name="metadata"/> into <paramref name="path"/> in place.
    /// </summary>
    /// <param name="createBackup">Copy the original to <c>&lt;name&gt;.bak</c> first.</param>
    /// <exception cref="TagWriteException">The file could not be read, tagged or verified.</exception>
    public void Write(string path, MediaMetadata metadata, bool createBackup = false)
    {
        if (!System.IO.File.Exists(path))
            throw new TagWriteException("The file no longer exists.");
        if (!IsSupported(path))
            throw new TagWriteException($"{Path.GetExtension(path)} files are not supported — only MP4 and M4V.");

        if (createBackup)
            CreateBackup(path);

        try
        {
            using var file = File.Create(path);
            if (file.GetTag(TagTypes.Apple, create: true) is not AppleTag tag)
                throw new TagWriteException("The file has no MP4 metadata section and one could not be created.");

            Apply(tag, metadata);
            file.Save();
        }
        catch (TagWriteException)
        {
            throw;
        }
        catch (CorruptFileException e)
        {
            throw new TagWriteException("The file's structure is damaged and cannot be tagged.", e);
        }
        catch (UnsupportedFormatException e)
        {
            throw new TagWriteException("The file is not a container this app can tag.", e);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new TagWriteException($"The file could not be written: {e.Message}", e);
        }

        Verify(path, metadata);
    }

    /// <summary>Reads back the atoms that matter most, so a silent no-op write is not reported as success.</summary>
    private static void Verify(string path, MediaMetadata metadata)
    {
        try
        {
            using var file = File.Create(path);
            if (file.GetTag(TagTypes.Apple) is not AppleTag tag)
                throw new TagWriteException("The tags did not survive being written.");

            var expectedTitle = metadata.Title;
            if (!string.IsNullOrWhiteSpace(expectedTitle) && tag.Title != expectedTitle)
                throw new TagWriteException("The title did not survive being written.");

            if (metadata.Kind == MediaKind.TvEpisode
                && !string.IsNullOrWhiteSpace(metadata.ShowName)
                && ReadFirst(tag, Mp4Atoms.ShowName) != metadata.ShowName)
                throw new TagWriteException("The show name did not survive being written.");
        }
        catch (TagWriteException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new TagWriteException($"The file could not be re-read after tagging: {e.Message}", e);
        }
    }

    private static void CreateBackup(string path)
    {
        var backup = path + ".bak";
        try
        {
            if (!System.IO.File.Exists(backup))
                System.IO.File.Copy(path, backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new TagWriteException($"The backup copy could not be created: {e.Message}", e);
        }
    }

    private static void Apply(AppleTag tag, MediaMetadata metadata)
    {
        var isTv = metadata.Kind == MediaKind.TvEpisode;

        SetText(tag, Mp4Atoms.Title, metadata.Title);
        SetText(tag, Mp4Atoms.Date, FormatDate(metadata));
        SetText(tag, Mp4Atoms.Genre, metadata.Genres.Count > 0 ? string.Join(", ", metadata.Genres) : null);
        SetText(tag, Mp4Atoms.ShortDescription, Truncate(metadata.Overview, 250));
        SetText(tag, Mp4Atoms.LongDescription, metadata.Overview);
        SetText(tag, Mp4Atoms.Comment, metadata.Overview);

        SetInteger(tag, Mp4Atoms.MediaType, isTv ? Mp4Atoms.MediaTypeTvShow : Mp4Atoms.MediaTypeMovie);
        SetInteger(tag, Mp4Atoms.HdVideo, HdFlagFor(metadata.Resolution));

        if (isTv)
        {
            SetText(tag, Mp4Atoms.ShowName, metadata.ShowName);
            SetText(tag, Mp4Atoms.Network, metadata.Network);
            SetText(tag, Mp4Atoms.EpisodeId, EpisodeLabel(metadata));

            // iTunes and the Apple TV app group episodes by album, so mirror their convention.
            var album = metadata.Season is null
                ? metadata.ShowName
                : $"{metadata.ShowName}, Season {metadata.Season}";
            SetText(tag, Mp4Atoms.Album, album);
            SetText(tag, Mp4Atoms.SortAlbum, album);

            // The artist of an episode is its show, which is what players expect to sort by.
            SetText(tag, Mp4Atoms.Artist, metadata.ShowName);
            SetText(tag, Mp4Atoms.AlbumArtist, metadata.ShowName);

            SetIntegerOrClear(tag, Mp4Atoms.SeasonNumber, metadata.Season);
            SetIntegerOrClear(tag, Mp4Atoms.EpisodeNumber, metadata.FirstEpisode);
        }
        else
        {
            ClearAll(tag,
                Mp4Atoms.ShowName, Mp4Atoms.Network, Mp4Atoms.EpisodeId,
                Mp4Atoms.SeasonNumber, Mp4Atoms.EpisodeNumber, Mp4Atoms.SortAlbum);

            SetText(tag, Mp4Atoms.Album, metadata.Title);
            var credited = metadata.Directors.Count > 0 ? metadata.Directors : metadata.Cast;
            SetText(tag, Mp4Atoms.Artist, credited.Count > 0 ? string.Join(", ", credited) : null);
            SetText(tag, Mp4Atoms.AlbumArtist, metadata.Studio);
        }

        SetText(tag, Mp4Atoms.Writer, metadata.Writers.Count > 0 ? string.Join(", ", metadata.Writers) : null);

        WriteMovieInfo(tag, metadata);
        WriteContentRating(tag, metadata);
        WriteArtwork(tag, metadata);
    }

    /// <summary>
    /// Writes the <c>iTunMOVI</c> property list. This is the only place cast, directors,
    /// producers and screenwriters can live in an MP4 where Infuse and iTunes will find them.
    /// </summary>
    private static void WriteMovieInfo(AppleTag tag, MediaMetadata metadata)
    {
        if (metadata.Cast.Count == 0 && metadata.Directors.Count == 0 && metadata.Writers.Count == 0)
        {
            tag.SetDashBox(Mp4Atoms.DashBoxMean, Mp4Atoms.MovieInfoName, null);
            return;
        }

        var plist = new StringBuilder();
        plist.Append("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
            """);
        plist.AppendLine();
        AppendPeople(plist, "cast", metadata.Cast);
        AppendPeople(plist, "directors", metadata.Directors);
        AppendPeople(plist, "screenwriters", metadata.Writers);
        if (!string.IsNullOrWhiteSpace(metadata.Studio))
            AppendPeople(plist, "studio", [metadata.Studio]);
        plist.Append("</dict>\n</plist>\n");

        tag.SetDashBox(Mp4Atoms.DashBoxMean, Mp4Atoms.MovieInfoName, plist.ToString());
    }

    private static void AppendPeople(StringBuilder plist, string key, IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            return;

        plist.Append("  <key>").Append(key).Append("</key>\n  <array>\n");
        foreach (var name in names)
        {
            plist.Append("    <dict><key>name</key><string>")
                .Append(EscapeXml(name))
                .Append("</string></dict>\n");
        }

        plist.Append("  </array>\n");
    }

    private static string EscapeXml(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    /// <summary>
    /// Writes <c>iTunEXTC</c>, the certification string Apple devices display. The rating system
    /// is inferred from the shape of the value, which is as good as it gets without a country.
    /// </summary>
    private static void WriteContentRating(AppleTag tag, MediaMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.ContentRating))
        {
            tag.SetDashBox(Mp4Atoms.DashBoxMean, Mp4Atoms.ContentRatingName, null);
            SetInteger(tag, Mp4Atoms.Advisory, 0);
            return;
        }

        var rating = metadata.ContentRating.Trim();
        var system = rating.StartsWith("TV-", StringComparison.OrdinalIgnoreCase) ? "us-tv" : "mpaa";
        tag.SetDashBox(Mp4Atoms.DashBoxMean, Mp4Atoms.ContentRatingName, $"{system}|{rating}|500|");
    }

    private static void WriteArtwork(AppleTag tag, MediaMetadata metadata)
    {
        if (metadata.ArtworkData is not { Length: > 0 } bytes)
            return;

        tag.Pictures =
        [
            new Picture(new ByteVector(bytes))
            {
                Type = PictureType.FrontCover,
                MimeType = DetectImageMimeType(bytes),
                Description = "Cover",
            },
        ];
    }

    /// <summary>
    /// Sniffs the image type from its magic bytes. TagLib picks the <c>covr</c> data flag from the
    /// MIME type, and a wrong flag makes players ignore the artwork.
    /// </summary>
    private static string DetectImageMimeType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
            return "image/png";
        return "image/jpeg";
    }

    /// <summary>Apple's HD flag, derived from the resolution we lifted off the filename.</summary>
    private static int HdFlagFor(string? resolution) => resolution switch
    {
        "4320p" or "2160p" => 3,
        "1440p" or "1080p" or "1080i" => 2,
        "720p" => 1,
        _ => 0,
    };

    private static string? EpisodeLabel(MediaMetadata metadata)
    {
        if (metadata.Episodes.Count == 0)
            return null;
        var episodes = string.Join("-E", metadata.Episodes.Select(e => e.ToString("00")));
        return metadata.Season is null ? $"E{episodes}" : $"S{metadata.Season:00}E{episodes}";
    }

    private static string? FormatDate(MediaMetadata metadata)
    {
        if (metadata.ReleaseDate is { } date)
            return date.ToString("yyyy-MM-dd");
        return metadata.Year?.ToString();
    }

    private static string? Truncate(string? text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= limit)
            return text;
        return string.Concat(text.AsSpan(0, limit - 1).TrimEnd(), "…");
    }

    private static void SetText(AppleTag tag, ReadOnlyByteVector atom, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            tag.ClearData(atom);
        else
            tag.SetText(atom, value);
    }

    private static void SetInteger(AppleTag tag, ReadOnlyByteVector atom, int value) =>
        tag.SetData(atom, ByteVector.FromUInt((uint)value), Mp4Atoms.IntegerFlag);

    private static void SetIntegerOrClear(AppleTag tag, ReadOnlyByteVector atom, int? value)
    {
        if (value is null)
            tag.ClearData(atom);
        else
            SetInteger(tag, atom, value.Value);
    }

    private static void ClearAll(AppleTag tag, params ReadOnlyByteVector[] atoms)
    {
        foreach (var atom in atoms)
            tag.ClearData(atom);
    }

    private static string? ReadFirst(AppleTag tag, ReadOnlyByteVector atom) =>
        tag.GetText(atom).FirstOrDefault();
}
