using TagLib;
using TagLib.Mpeg4;
using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Writing;
using Xunit;
using File = TagLib.File;

namespace VideoMetadataFiller.Core.Tests;

/// <summary>
/// Round-trips real tags through a tiny committed MP4. The fixture is a 32×32 tenth-of-a-second
/// clip, which is enough for TagLib to parse and rewrite the container.
/// </summary>
public class Mp4TagWriterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("vmf-tags").FullName;

    private string CopyFixture(string name = "video.mp4")
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.mp4");
        var target = Path.Combine(_directory, name);
        System.IO.File.Copy(source, target);
        return target;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static AppleTag ReadTag(string path)
    {
        var file = File.Create(path);
        return (AppleTag)file.GetTag(TagTypes.Apple)!;
    }

    private static string? Atom(AppleTag tag, string fourcc)
    {
        var name = fourcc.StartsWith('©')
            ? new ReadOnlyByteVector(0xA9, (byte)fourcc[1], (byte)fourcc[2], (byte)fourcc[3])
            : new ReadOnlyByteVector((byte)fourcc[0], (byte)fourcc[1], (byte)fourcc[2], (byte)fourcc[3]);
        return tag.GetText(name).FirstOrDefault();
    }

    private static uint? IntAtom(AppleTag tag, string fourcc)
    {
        var name = new ReadOnlyByteVector((byte)fourcc[0], (byte)fourcc[1], (byte)fourcc[2], (byte)fourcc[3]);
        var data = tag.DataBoxes(name).FirstOrDefault();
        return data?.Data.ToUInt();
    }

    [Fact]
    public void WritesMovieTagsThatCanBeReadBack()
    {
        var path = CopyFixture();
        var metadata = new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Blade Runner 2049",
            OriginalTitle = "Blade Runner 2049",
            ReleaseDate = new DateTime(2017, 10, 6),
            Year = 2017,
            Overview = "A young blade runner uncovers a long-buried secret.",
            Genres = ["Science Fiction", "Drama"],
            Directors = ["Denis Villeneuve"],
            Writers = ["Hampton Fancher"],
            Cast = ["Ryan Gosling", "Harrison Ford"],
            Studio = "Alcon Entertainment",
            ContentRating = "R",
            Resolution = "2160p",
            TmdbId = 335984,
        };

        new Mp4TagWriter().Write(path, metadata);

        var tag = ReadTag(path);
        Assert.Equal("Blade Runner 2049", tag.Title);
        Assert.Equal("2017-10-06", Atom(tag, "©day"));
        Assert.Equal("Science Fiction, Drama", Atom(tag, "©gen"));
        Assert.Equal("Denis Villeneuve", Atom(tag, "©ART"));
        Assert.Equal("Hampton Fancher", Atom(tag, "©wrt"));
        Assert.Equal(metadata.Overview, Atom(tag, "ldes"));
        // 9 marks the file as a movie; without it players ignore the rest.
        Assert.Equal(9u, IntAtom(tag, "stik"));
        // 3 is Apple's flag for 4K.
        Assert.Equal(3u, IntAtom(tag, "hdvd"));
    }

    [Fact]
    public void WritesTvTagsThatCanBeReadBack()
    {
        var path = CopyFixture();
        var metadata = new MediaMetadata
        {
            Kind = MediaKind.TvEpisode,
            ShowName = "Severance",
            Title = "Good News About Hell",
            Season = 1,
            Episodes = [1],
            ReleaseDate = new DateTime(2022, 2, 18),
            Overview = "Mark is promoted.",
            Genres = ["Drama"],
            Network = "Apple TV+",
            ContentRating = "TV-MA",
            Resolution = "1080p",
        };

        new Mp4TagWriter().Write(path, metadata);

        var tag = ReadTag(path);
        Assert.Equal("Good News About Hell", tag.Title);
        Assert.Equal("Severance", Atom(tag, "tvsh"));
        Assert.Equal("Apple TV+", Atom(tag, "tvnn"));
        Assert.Equal("S01E01", Atom(tag, "tven"));
        Assert.Equal("Severance, Season 1", Atom(tag, "©alb"));
        Assert.Equal("Severance", Atom(tag, "©ART"));
        Assert.Equal(1u, IntAtom(tag, "tvsn"));
        Assert.Equal(1u, IntAtom(tag, "tves"));
        // 10 marks the file as a TV episode.
        Assert.Equal(10u, IntAtom(tag, "stik"));
        Assert.Equal(2u, IntAtom(tag, "hdvd"));
    }

    [Fact]
    public void MultiEpisodeFilesGetARangeInTheEpisodeId()
    {
        var path = CopyFixture();
        var metadata = new MediaMetadata
        {
            Kind = MediaKind.TvEpisode,
            ShowName = "Firefly",
            Title = "Serenity",
            Season = 1,
            Episodes = [1, 2],
        };

        new Mp4TagWriter().Write(path, metadata);

        Assert.Equal("S01E01-E02", Atom(ReadTag(path), "tven"));
    }

    [Fact]
    public void EmbedsArtwork()
    {
        var path = CopyFixture();
        // A one-pixel JPEG is enough to prove the covr atom and its data flag are written.
        var jpeg = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwc" +
            "JC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPDs0NDT/wAALCAABAAEBAREA/8QAFAABAQAAAAAAAAAAAAAA" +
            "AAAAAAn/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFAEBAAAAAAAAAAAAAAAAAAAAAP/EABQRAQAAAAAA" +
            "AAAAAAAAAAAAAAD/2gAMAwEAAhEDEQA/AJgA/9k=");

        new Mp4TagWriter().Write(path, new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Arrival",
            ArtworkData = jpeg,
        });

        var pictures = ReadTag(path).Pictures;
        Assert.Single(pictures);
        Assert.NotEmpty(pictures[0].Data.Data);
    }

    [Fact]
    public void WritesCastAndCrewIntoTheMovieInfoPropertyList()
    {
        var path = CopyFixture();

        new Mp4TagWriter().Write(path, new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Arrival",
            Cast = ["Amy Adams", "Jeremy Renner"],
            Directors = ["Denis Villeneuve"],
        });

        var plist = ReadTag(path).GetDashBox("com.apple.iTunes", "iTunMOVI");
        Assert.Contains("<key>cast</key>", plist);
        Assert.Contains("Amy Adams", plist);
        Assert.Contains("<key>directors</key>", plist);
        Assert.Contains("Denis Villeneuve", plist);
    }

    [Fact]
    public void EscapesXmlInNames()
    {
        var path = CopyFixture();

        new Mp4TagWriter().Write(path, new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Test",
            Cast = ["Bell & Ross <actor>"],
        });

        var plist = ReadTag(path).GetDashBox("com.apple.iTunes", "iTunMOVI");
        Assert.Contains("Bell &amp; Ross &lt;actor&gt;", plist);
    }

    [Fact]
    public void SwitchingAMovieToTvClearsTheOtherKindsAtoms()
    {
        var path = CopyFixture();
        var writer = new Mp4TagWriter();

        writer.Write(path, new MediaMetadata
        {
            Kind = MediaKind.TvEpisode,
            ShowName = "Severance",
            Title = "Episode",
            Season = 1,
            Episodes = [1],
        });
        writer.Write(path, new MediaMetadata { Kind = MediaKind.Movie, Title = "Severance" });

        var tag = ReadTag(path);
        Assert.Null(Atom(tag, "tvsh"));
        Assert.Null(IntAtom(tag, "tvsn"));
        Assert.Equal(9u, IntAtom(tag, "stik"));
    }

    [Fact]
    public void KeepsABackupWhenAsked()
    {
        var path = CopyFixture();
        var originalLength = new FileInfo(path).Length;

        new Mp4TagWriter().Write(path, new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Arrival",
        }, createBackup: true);

        Assert.True(System.IO.File.Exists(path + ".bak"));
        Assert.Equal(originalLength, new FileInfo(path + ".bak").Length);
    }

    [Fact]
    public void RejectsAMissingFile()
    {
        var exception = Assert.Throws<TagWriteException>(() =>
            new Mp4TagWriter().Write(Path.Combine(_directory, "nope.mp4"), new MediaMetadata()));

        Assert.Contains("no longer exists", exception.Message);
    }

    [Fact]
    public void RejectsAnUnsupportedExtension()
    {
        var path = Path.Combine(_directory, "clip.mkv");
        System.IO.File.WriteAllText(path, "not really a matroska file");

        var exception = Assert.Throws<TagWriteException>(() =>
            new Mp4TagWriter().Write(path, new MediaMetadata()));

        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public void ReportsADamagedFileWithoutThrowingRawTagLibErrors()
    {
        var path = Path.Combine(_directory, "broken.mp4");
        System.IO.File.WriteAllBytes(path, new byte[256]);

        Assert.Throws<TagWriteException>(() =>
            new Mp4TagWriter().Write(path, new MediaMetadata { Title = "x" }));
    }

    [Theory]
    [InlineData("movie.mp4", true)]
    [InlineData("movie.M4V", true)]
    [InlineData("movie.mkv", false)]
    [InlineData("movie.avi", false)]
    public void RecognisesSupportedExtensions(string name, bool expected)
    {
        Assert.Equal(expected, Mp4TagWriter.IsSupported(name));
    }
}
