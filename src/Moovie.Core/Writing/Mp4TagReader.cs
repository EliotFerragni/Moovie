using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TagLib;
using TagLib.Mpeg4;
using Moovie.Core.Localization;
using Moovie.Core.Model;
using File = TagLib.File;

namespace Moovie.Core.Writing;

/// <summary>Raised when a file's existing tags cannot be read. Carries a message fit to show in the pane.</summary>
public sealed class TagReadException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>What an MP4 already carries, read back out of its atoms.</summary>
/// <param name="Metadata">
/// The fields recovered from the container. Only fields the app actually writes are filled in:
/// there is no atom for a TMDB id or an original title, so those stay empty however the file was
/// tagged.
/// </param>
/// <param name="HasArtwork">Whether a cover image is embedded.</param>
/// <param name="ArtworkByteCount">Size of that image, for saying so in the pane.</param>
/// <param name="HdFlag">
/// The <c>hdvd</c> value as stored. Kept as the flag rather than mapped back to a resolution,
/// because the mapping loses information — 1080i, 1080p and 1440p all write a 2.
/// </param>
public sealed record ExistingTags(
    MediaMetadata Metadata,
    bool HasArtwork,
    int ArtworkByteCount,
    int HdFlag)
{
    /// <summary>An untagged file: nothing worth showing was found in it.</summary>
    public static ExistingTags None { get; } = new(new MediaMetadata(), false, 0, 0);

    /// <summary>
    /// True when the file carries no metadata of its own. A file that has only an HD flag counts
    /// as empty: players show nothing from it, and every tagger writes one.
    /// </summary>
    public bool IsEmpty =>
        !HasArtwork
        && Metadata.Kind == MediaKind.Unknown
        && string.IsNullOrWhiteSpace(Metadata.Title)
        && string.IsNullOrWhiteSpace(Metadata.ShowName)
        && string.IsNullOrWhiteSpace(Metadata.Overview)
        && string.IsNullOrWhiteSpace(Metadata.Studio)
        && string.IsNullOrWhiteSpace(Metadata.Network)
        && string.IsNullOrWhiteSpace(Metadata.ContentRating)
        && Metadata.Season is null
        && Metadata.Episodes.Count == 0
        && Metadata.ReleaseDate is null
        && Metadata.Year is null
        && Metadata.Genres.Count == 0
        && Metadata.Cast.Count == 0
        && Metadata.Directors.Count == 0
        && Metadata.Writers.Count == 0;
}

/// <summary>
/// Reads the iTunes-style atoms <see cref="Mp4TagWriter"/> writes back out of an MP4/M4V, so the
/// app can say what a file already holds instead of assuming it holds nothing.
/// </summary>
/// <remarks>
/// Only the atoms the writer produces are understood, and only as fields — the derived ones
/// (<c>©alb</c>, <c>©ART</c>, <c>soal</c>) are ignored, since they are restated elsewhere and
/// reading them back would invent differences that do not exist.
/// </remarks>
public sealed partial class Mp4TagReader
{
    /// <summary>
    /// Reads whatever <paramref name="path"/> already carries. A file with no metadata section is
    /// not an error: it comes back as <see cref="ExistingTags.None"/>.
    /// </summary>
    /// <param name="includeArtwork">
    /// Also return the embedded image bytes. Off by default — a batch only needs to know whether
    /// artwork is there, and holding a cover per file adds up quickly.
    /// </param>
    /// <exception cref="TagReadException">The file is missing, unsupported or damaged.</exception>
    public ExistingTags Read(string path, bool includeArtwork = false)
    {
        if (!System.IO.File.Exists(path))
            throw new TagReadException(Strings.Get("read.missing"));
        if (!Mp4TagWriter.IsSupported(path))
            throw new TagReadException(Strings.Get("read.notTaggable"));

        try
        {
            using var file = File.Create(path);
            if (file.GetTag(TagTypes.Apple) is not AppleTag tag)
                return ExistingTags.None;

            return FromTag(tag, includeArtwork);
        }
        catch (CorruptFileException e)
        {
            throw new TagReadException(Strings.Get("read.damaged"), e);
        }
        catch (UnsupportedFormatException e)
        {
            throw new TagReadException(Strings.Get("read.notTaggable"), e);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new TagReadException(Strings.Format("read.failed", e.Message), e);
        }
    }

    private static ExistingTags FromTag(AppleTag tag, bool includeArtwork)
    {
        var metadata = new MediaMetadata
        {
            Title = Text(tag, Mp4Atoms.Title),
            ShowName = Text(tag, Mp4Atoms.ShowName),
            Network = Text(tag, Mp4Atoms.Network),
            Genres = TagFormat.Split(Text(tag, Mp4Atoms.Genre)),
            // ldes holds the full text; desc is the truncated copy, so it is only a fallback.
            Overview = Text(tag, Mp4Atoms.LongDescription)
                ?? Text(tag, Mp4Atoms.ShortDescription)
                ?? Text(tag, Mp4Atoms.Comment),
            Season = Integer(tag, Mp4Atoms.SeasonNumber),
            ContentRating = ParseContentRating(tag.GetDashBox(Mp4Atoms.DashBoxMean, Mp4Atoms.ContentRatingName)),
        };

        metadata.Kind = ParseKind(Integer(tag, Mp4Atoms.MediaType), metadata.ShowName);
        metadata.Episodes = ParseEpisodes(Text(tag, Mp4Atoms.EpisodeId), Integer(tag, Mp4Atoms.EpisodeNumber));
        ApplyDate(metadata, Text(tag, Mp4Atoms.Date));

        var people = MovieInfo.Parse(tag.GetDashBox(Mp4Atoms.DashBoxMean, Mp4Atoms.MovieInfoName));
        metadata.Cast = people.Cast;
        metadata.Directors = people.Directors;
        // ©wrt carries the same names as the property list; it is the fallback for files tagged
        // by something that did not write a plist.
        metadata.Writers = people.Writers.Count > 0 ? people.Writers : TagFormat.Split(Text(tag, Mp4Atoms.Writer));

        // aART is the studio for a movie but the show name for an episode, so it is only trusted
        // for the one kind it means something for.
        metadata.Studio = people.Studio
            ?? (metadata.Kind == MediaKind.TvEpisode ? null : Text(tag, Mp4Atoms.AlbumArtist));

        var picture = tag.Pictures.FirstOrDefault();
        var artwork = picture?.Data.Data;
        if (includeArtwork)
            metadata.ArtworkData = artwork;

        return new ExistingTags(
            metadata,
            HasArtwork: artwork is { Length: > 0 },
            ArtworkByteCount: artwork?.Length ?? 0,
            HdFlag: Integer(tag, Mp4Atoms.HdVideo) ?? 0);
    }

    private static MediaKind ParseKind(int? mediaType, string? showName) => mediaType switch
    {
        Mp4Atoms.MediaTypeTvShow => MediaKind.TvEpisode,
        Mp4Atoms.MediaTypeMovie => MediaKind.Movie,
        // No stik is the interesting case: a show name is the only other thing that tells us.
        _ => string.IsNullOrWhiteSpace(showName) ? MediaKind.Unknown : MediaKind.TvEpisode,
    };

    /// <summary>
    /// Recovers every episode a file covers. <c>tven</c> is preferred because it holds the whole
    /// range for a multi-episode file, where <c>tves</c> only holds the first.
    /// </summary>
    private static List<int> ParseEpisodes(string? episodeId, int? episodeNumber)
    {
        if (!string.IsNullOrWhiteSpace(episodeId))
        {
            var episodes = EpisodeMarker().Matches(episodeId)
                .Select(m => int.Parse(m.Groups[1].Value))
                .ToList();
            if (episodes.Count > 0)
                return episodes;
        }

        // Some taggers put the episode title in tven, which tells us nothing about numbering.
        return episodeNumber is { } number ? [number] : [];
    }

    /// <summary>
    /// Reads <c>©day</c>, which may be a full date, a bare year, or the ISO timestamp iTunes
    /// writes. A year on its own must not become the first of January, or every such file would
    /// look like it needed its date corrected.
    /// </summary>
    private static void ApplyDate(MediaMetadata metadata, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var text = value.Trim();
        if (text.Length == 4 && int.TryParse(text, out var year))
        {
            metadata.Year = year;
            return;
        }

        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
        {
            metadata.ReleaseDate = date;
            metadata.Year = date.Year;
        }
    }

    /// <summary>Pulls the rating out of an <c>iTunEXTC</c> string such as <c>us-tv|TV-MA|500|</c>.</summary>
    private static string? ParseContentRating(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split('|');
        var rating = parts.Length >= 2 ? parts[1].Trim() : value.Trim();
        return string.IsNullOrWhiteSpace(rating) ? null : rating;
    }

    private static string? Text(AppleTag tag, ReadOnlyByteVector atom)
    {
        var value = tag.GetText(atom).FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? Integer(AppleTag tag, ReadOnlyByteVector atom)
    {
        var data = tag.DataBoxes(atom).FirstOrDefault()?.Data;
        if (data is null || data.Count == 0)
            return null;
        return (int)data.ToUInt();
    }

    [GeneratedRegex(@"[eE](\d{1,3})")]
    private static partial Regex EpisodeMarker();

    /// <summary>
    /// The <c>iTunMOVI</c> property list, which is where cast and crew live in an MP4.
    /// </summary>
    /// <remarks>
    /// Written by this app as arrays of <c>{name: …}</c> dictionaries, which is Apple's shape and
    /// what Subler and iTunes produce too. The studio is accepted as either that or a plain
    /// string, since both are found in the wild.
    /// </remarks>
    private sealed record MovieInfo(
        List<string> Cast, List<string> Directors, List<string> Writers, string? Studio)
    {
        private static readonly MovieInfo Empty = new([], [], [], null);

        internal static MovieInfo Parse(string? plist)
        {
            if (string.IsNullOrWhiteSpace(plist))
                return Empty;

            XElement? root;
            try
            {
                // The plist declares Apple's DTD by URL. Ignoring it keeps the parse offline and
                // away from the external-entity footgun.
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using var reader = XmlReader.Create(new StringReader(plist), settings);
                root = XDocument.Load(reader).Root?.Element("dict");
            }
            catch (XmlException)
            {
                // A plist we cannot parse is reported as no cast rather than as a broken file:
                // it costs the user a redundant diff row, not their batch.
                return Empty;
            }

            if (root is null)
                return Empty;

            var values = ReadDictionary(root);
            return new MovieInfo(
                Names(values.GetValueOrDefault("cast")),
                Names(values.GetValueOrDefault("directors")),
                Names(values.GetValueOrDefault("screenwriters")),
                Names(values.GetValueOrDefault("studio")).FirstOrDefault()
                    ?? StringValue(values.GetValueOrDefault("studio")));
        }

        /// <summary>A plist dictionary is a flat run of key/value pairs, not nested elements.</summary>
        private static Dictionary<string, XElement> ReadDictionary(XElement dict)
        {
            var values = new Dictionary<string, XElement>(StringComparer.Ordinal);
            XElement? key = null;
            foreach (var element in dict.Elements())
            {
                if (element.Name.LocalName == "key")
                {
                    key = element;
                    continue;
                }

                if (key is not null)
                    values[key.Value.Trim()] = element;
                key = null;
            }

            return values;
        }

        private static List<string> Names(XElement? array)
        {
            if (array is null || array.Name.LocalName != "array")
                return [];

            return [.. array.Elements("dict")
                .Select(d => ReadDictionary(d).GetValueOrDefault("name")?.Value.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)];
        }

        private static string? StringValue(XElement? element) =>
            element?.Name.LocalName == "string" && !string.IsNullOrWhiteSpace(element.Value)
                ? element.Value.Trim()
                : null;
    }
}
