using TagLib;
using TagLib.Mpeg4;
using Moovie.Core.Model;
using Moovie.Core.Writing;
using Xunit;
using File = TagLib.File;

namespace Moovie.Core.Tests;

/// <summary>
/// The reader's job is to recover what the writer put in, so most of this is a round trip through
/// the same fixture <see cref="Mp4TagWriterTests"/> uses. The rest covers files tagged by
/// something else, which is the case the app has no control over.
/// </summary>
public class Mp4TagReaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("vmf-read").FullName;

    private string CopyFixture(string name = "video.mp4")
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.mp4");
        var target = Path.Combine(_directory, name);
        System.IO.File.Copy(source, target);
        return target;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static ExistingTags WriteThenRead(string path, MediaMetadata metadata)
    {
        new Mp4TagWriter().Write(path, metadata);
        return new Mp4TagReader().Read(path);
    }

    [Fact]
    public void Reads_back_every_movie_field_the_writer_wrote()
    {
        var path = CopyFixture();
        var written = new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Blade Runner 2049",
            ReleaseDate = new DateTime(2017, 10, 6),
            Year = 2017,
            Overview = "A young blade runner uncovers a long-buried secret.",
            Genres = ["Science Fiction", "Drama"],
            Directors = ["Denis Villeneuve"],
            Writers = ["Hampton Fancher", "Michael Green"],
            Cast = ["Ryan Gosling", "Harrison Ford"],
            Studio = "Alcon Entertainment",
            ContentRating = "R",
            Resolution = "2160p",
        };

        var read = WriteThenRead(path, written).Metadata;

        Assert.Equal(MediaKind.Movie, read.Kind);
        Assert.Equal("Blade Runner 2049", read.Title);
        Assert.Equal(new DateTime(2017, 10, 6), read.ReleaseDate);
        Assert.Equal(2017, read.Year);
        Assert.Equal(written.Overview, read.Overview);
        Assert.Equal(["Science Fiction", "Drama"], read.Genres);
        Assert.Equal(["Denis Villeneuve"], read.Directors);
        Assert.Equal(["Hampton Fancher", "Michael Green"], read.Writers);
        Assert.Equal(["Ryan Gosling", "Harrison Ford"], read.Cast);
        Assert.Equal("Alcon Entertainment", read.Studio);
        Assert.Equal("R", read.ContentRating);
    }

    [Fact]
    public void Reads_back_every_tv_field_the_writer_wrote()
    {
        var path = CopyFixture();

        var read = WriteThenRead(path, new MediaMetadata
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
        }).Metadata;

        Assert.Equal(MediaKind.TvEpisode, read.Kind);
        Assert.Equal("Severance", read.ShowName);
        Assert.Equal("Good News About Hell", read.Title);
        Assert.Equal(1, read.Season);
        Assert.Equal([1], read.Episodes);
        Assert.Equal("Apple TV+", read.Network);
        Assert.Equal("TV-MA", read.ContentRating);
    }

    /// <summary>
    /// aART holds the show name for an episode and the studio for a movie, so trusting it for
    /// both kinds would report every episode's studio as its own show.
    /// </summary>
    [Fact]
    public void Does_not_mistake_an_episodes_album_artist_for_its_studio()
    {
        var path = CopyFixture();

        var read = WriteThenRead(path, new MediaMetadata
        {
            Kind = MediaKind.TvEpisode,
            ShowName = "Severance",
            Title = "Half Loop",
            Season = 1,
            Episodes = [2],
        }).Metadata;

        Assert.Null(read.Studio);
    }

    [Fact]
    public void Recovers_both_episodes_of_a_multi_episode_file()
    {
        var path = CopyFixture();

        var read = WriteThenRead(path, new MediaMetadata
        {
            Kind = MediaKind.TvEpisode,
            ShowName = "Firefly",
            Title = "Serenity",
            Season = 1,
            Episodes = [1, 2],
        }).Metadata;

        Assert.Equal([1, 2], read.Episodes);
    }

    /// <summary>A year with no full date must not come back as the first of January.</summary>
    [Fact]
    public void Keeps_a_bare_year_a_bare_year()
    {
        var path = CopyFixture();

        var read = WriteThenRead(path, new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Amelie",
            Year = 2001,
        }).Metadata;

        Assert.Equal(2001, read.Year);
        Assert.Null(read.ReleaseDate);
    }

    [Fact]
    public void Reports_artwork_without_loading_it_unless_asked()
    {
        var path = CopyFixture();
        new Mp4TagWriter().Write(path, new MediaMetadata
        {
            Kind = MediaKind.Movie,
            Title = "Arrival",
            ArtworkData = OnePixelJpeg,
        });

        var lean = new Mp4TagReader().Read(path);
        Assert.True(lean.HasArtwork);
        Assert.True(lean.ArtworkByteCount > 0);
        Assert.Null(lean.Metadata.ArtworkData);

        var full = new Mp4TagReader().Read(path, includeArtwork: true);
        Assert.Equal(OnePixelJpeg, full.Metadata.ArtworkData);
    }

    [Fact]
    public void An_untagged_file_reads_as_empty()
    {
        var tags = new Mp4TagReader().Read(CopyFixture());

        Assert.True(tags.IsEmpty);
        Assert.False(tags.HasArtwork);
        Assert.Equal(MediaKind.Unknown, tags.Metadata.Kind);
    }

    [Fact]
    public void A_tagged_file_does_not_read_as_empty()
    {
        var path = CopyFixture();

        var tags = WriteThenRead(path, new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival" });

        Assert.False(tags.IsEmpty);
    }

    /// <summary>
    /// Every tagger writes an HD flag, so a file carrying nothing else is still an untagged file
    /// as far as the user is concerned.
    /// </summary>
    [Fact]
    public void An_hd_flag_on_its_own_does_not_count_as_metadata()
    {
        var path = CopyFixture();
        using (var file = File.Create(path))
        {
            var tag = (AppleTag)file.GetTag(TagTypes.Apple, create: true)!;
            tag.SetData(
                new ReadOnlyByteVector((byte)'h', (byte)'d', (byte)'v', (byte)'d'),
                ByteVector.FromUInt(2),
                (uint)AppleDataBox.FlagType.ForTempo);
            file.Save();
        }

        var tags = new Mp4TagReader().Read(path);

        Assert.Equal(2, tags.HdFlag);
        Assert.True(tags.IsEmpty);
    }

    /// <summary>Files tagged elsewhere carry no stik; a show name is then the only signal.</summary>
    [Fact]
    public void Infers_an_episode_from_a_show_name_when_the_media_type_is_missing()
    {
        var path = CopyFixture();
        using (var file = File.Create(path))
        {
            var tag = (AppleTag)file.GetTag(TagTypes.Apple, create: true)!;
            tag.SetText(new ReadOnlyByteVector((byte)'t', (byte)'v', (byte)'s', (byte)'h'), "Andor");
            file.Save();
        }

        var tags = new Mp4TagReader().Read(path);

        Assert.Equal(MediaKind.TvEpisode, tags.Metadata.Kind);
        Assert.Equal("Andor", tags.Metadata.ShowName);
    }

    [Fact]
    public void Reads_a_studio_written_as_a_plain_plist_string()
    {
        var path = CopyFixture();
        using (var file = File.Create(path))
        {
            var tag = (AppleTag)file.GetTag(TagTypes.Apple, create: true)!;
            tag.SetDashBox("com.apple.iTunes", "iTunMOVI", """
                <?xml version="1.0" encoding="UTF-8"?>
                <plist version="1.0">
                <dict>
                  <key>studio</key><string>A24</string>
                  <key>cast</key>
                  <array><dict><key>name</key><string>Mia Goth</string></dict></array>
                </dict>
                </plist>
                """);
            file.Save();
        }

        var read = new Mp4TagReader().Read(path).Metadata;

        Assert.Equal("A24", read.Studio);
        Assert.Equal(["Mia Goth"], read.Cast);
    }

    /// <summary>A property list we cannot parse costs a redundant diff row, not the whole read.</summary>
    [Fact]
    public void Survives_an_unparseable_property_list()
    {
        var path = CopyFixture();
        using (var file = File.Create(path))
        {
            var tag = (AppleTag)file.GetTag(TagTypes.Apple, create: true)!;
            tag.SetText(new ReadOnlyByteVector(0xA9, (byte)'n', (byte)'a', (byte)'m'), "Pearl");
            tag.SetDashBox("com.apple.iTunes", "iTunMOVI", "<plist><dict><key>cast</key");
            file.Save();
        }

        var read = new Mp4TagReader().Read(path).Metadata;

        Assert.Equal("Pearl", read.Title);
        Assert.Empty(read.Cast);
    }

    /// <summary>
    /// The guarantee the preview diff rests on: everything the writer put in comes back out
    /// comparing equal, so a file looked at again straight after applying reports nothing left
    /// to do. This is also what catches the writer and the reader drifting apart.
    /// </summary>
    [Fact]
    public void A_file_reads_back_with_nothing_left_to_apply()
    {
        var path = CopyFixture();
        var metadata = new MediaMetadata
        {
            Kind = MediaKind.TvEpisode,
            ShowName = "Severance",
            Title = "Good News About Hell",
            Season = 1,
            Episodes = [1, 2],
            ReleaseDate = new DateTime(2022, 2, 18),
            Overview = "Mark Scout leads a team at Lumon Industries.",
            Genres = ["Drama", "Mystery"],
            Cast = ["Adam Scott", "Britt Lower"],
            Directors = ["Ben Stiller"],
            Writers = ["Dan Erickson"],
            Network = "Apple TV+",
            ContentRating = "TV-MA",
            Resolution = "1080p",
            ArtworkData = OnePixelJpeg,
        };

        new Mp4TagWriter().Write(path, metadata);
        var existing = new Mp4TagReader().Read(path, includeArtwork: true);

        Assert.Empty(MetadataDiff.Between(existing, metadata));
    }

    [Fact]
    public void Rejects_a_missing_file()
    {
        var exception = Assert.Throws<TagReadException>(() =>
            new Mp4TagReader().Read(Path.Combine(_directory, "nope.mp4")));

        Assert.Contains("no longer exists", exception.Message);
    }

    [Fact]
    public void Rejects_an_unsupported_extension()
    {
        var path = Path.Combine(_directory, "clip.mkv");
        System.IO.File.WriteAllText(path, "not really a matroska file");

        Assert.Throws<TagReadException>(() => new Mp4TagReader().Read(path));
    }

    [Fact]
    public void Reports_a_damaged_file_without_throwing_raw_taglib_errors()
    {
        var path = Path.Combine(_directory, "broken.mp4");
        System.IO.File.WriteAllBytes(path, new byte[256]);

        Assert.Throws<TagReadException>(() => new Mp4TagReader().Read(path));
    }

    private static readonly byte[] OnePixelJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwc" +
        "JC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPDs0NDT/wAALCAABAAEBAREA/8QAFAABAQAAAAAAAAAAAAAA" +
        "AAAAAAn/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFAEBAAAAAAAAAAAAAAAAAAAAAP/EABQRAQAAAAAA" +
        "AAAAAAAAAAAAAAD/2gAMAwEAAhEDEQA/AJgA/9k=");
}
