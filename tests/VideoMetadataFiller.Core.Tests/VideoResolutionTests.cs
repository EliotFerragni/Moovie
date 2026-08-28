using VideoMetadataFiller.Core.Writing;
using Xunit;

namespace VideoMetadataFiller.Core.Tests;

public class VideoResolutionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("vmf-res").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.mp4");

    [Theory]
    // The round numbers.
    [InlineData(7680, 4320, "4320p")]
    [InlineData(3840, 2160, "2160p")]
    [InlineData(2560, 1440, "1440p")]
    [InlineData(1920, 1080, "1080p")]
    [InlineData(1280, 720, "720p")]
    [InlineData(854, 480, "480p")]
    [InlineData(640, 360, "360p")]
    // Widescreen film is cropped in height, not letterboxed, so width has to promote it.
    [InlineData(1920, 800, "1080p")]
    [InlineData(1920, 804, "1080p")]
    [InlineData(3840, 1600, "2160p")]
    [InlineData(1280, 536, "720p")]
    // …but width must never demote, or 4:3 and anamorphic material is mislabelled.
    [InlineData(720, 576, "576p")]
    [InlineData(720, 480, "480p")]
    [InlineData(640, 480, "480p")]
    [InlineData(1440, 1080, "1080p")]
    [InlineData(1920, 1200, "1080p")]
    // Encodes rounded to a multiple of eight are the round number.
    [InlineData(1920, 1072, "1080p")]
    [InlineData(3840, 2144, "2160p")]
    public void Names_a_frame_size_the_way_a_release_would(int width, int height, string expected)
    {
        Assert.Equal(expected, VideoResolution.Label(width, height));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 1080)]
    [InlineData(1920, 0)]
    // Too small for any label to mean anything.
    [InlineData(32, 32)]
    [InlineData(160, 120)]
    public void Declines_to_name_a_size_it_cannot_stand_behind(int width, int height)
    {
        Assert.Null(VideoResolution.Label(width, height));
    }

    /// <summary>Proves the probe reaches the video track rather than quietly failing to nothing.</summary>
    [Fact]
    public void Reads_the_frame_size_out_of_the_container()
    {
        Assert.Equal((32, 32), VideoResolution.ReadSize(Fixture));
    }

    /// <summary>
    /// The bug this exists for: a name carrying no resolution token — which is what a rename
    /// leaves behind unless the template asks for one — used to mean no resolution at all, and
    /// an SD HD flag written over an HD file on the next pass. The container is asked instead,
    /// and it answers regardless of what the file is called.
    /// </summary>
    [Fact]
    public void Reports_a_full_hd_file_whose_name_says_nothing_about_it()
    {
        var path = DeclaringFrameSize(1920, 1080, "Blade Runner 2049.mp4");

        Assert.Equal((1920, 1080), VideoResolution.ReadSize(path));
        Assert.Equal("1080p", VideoResolution.Read(path));
    }

    [Fact]
    public void Reports_a_4k_file_the_same_way()
    {
        Assert.Equal("2160p", VideoResolution.Read(DeclaringFrameSize(3840, 2160)));
    }

    [Fact]
    public void A_file_that_cannot_be_read_falls_back_to_nothing_rather_than_throwing()
    {
        var damaged = Path.Combine(_directory, "broken.mp4");
        System.IO.File.WriteAllBytes(damaged, new byte[256]);

        Assert.Equal((0, 0), VideoResolution.ReadSize(damaged));
        Assert.Null(VideoResolution.Read(damaged));
        Assert.Null(VideoResolution.Read(Path.Combine(_directory, "nope.mp4")));
    }

    /// <summary>
    /// A copy of the fixture whose video sample entry declares <paramref name="width"/> by
    /// <paramref name="height"/>. Only the declared frame size is rewritten — that is the field
    /// the probe reads, and encoding a real 4K clip to commit would be absurd for two numbers.
    /// </summary>
    private string DeclaringFrameSize(int width, int height, string name = "clip.mp4")
    {
        var bytes = System.IO.File.ReadAllBytes(Fixture);

        // In an stsd box: 12 bytes of header and entry count, then the sample entry, whose
        // declared width and height are two big-endian shorts 32 bytes in.
        var stsd = IndexOf(bytes, "stsd"u8);
        var entry = stsd + 12;
        WriteBigEndian(bytes, entry + 32, width);
        WriteBigEndian(bytes, entry + 34, height);

        var path = Path.Combine(_directory, name);
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        var found = haystack.AsSpan().IndexOf(needle);
        Assert.True(found >= 0, "the fixture no longer contains an stsd box");
        return found;
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }
}
