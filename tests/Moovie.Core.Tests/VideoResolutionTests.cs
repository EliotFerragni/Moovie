using Moovie.Core.Writing;
using Xunit;

namespace Moovie.Core.Tests;

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
    // A DVD frame is 720 wide whatever it holds, so a cropped widescreen transfer is still
    // SD-class and not the 360p that scaling 720 by 9/16 would suggest.
    [InlineData(720, 406, "480p")]
    [InlineData(720, 302, "480p")]
    [InlineData(704, 396, "480p")]
    [InlineData(1024, 576, "576p")]
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

    [Theory]
    [InlineData("360p", "480p", true)]
    [InlineData("480p", "576p", true)]
    // "At or below" includes the threshold itself: naming 576p is how you stop seeing 576p.
    [InlineData("576p", "576p", true)]
    [InlineData("720p", "576p", false)]
    [InlineData("2160p", "576p", false)]
    [InlineData("1080p", "1080p", true)]
    [InlineData("1440p", "1080p", false)]
    // Interlaced 1080 is the same frame size as progressive 1080, so it hides with it.
    [InlineData("1080i", "1080p", true)]
    [InlineData("4k", "1080p", false)]
    public void Compares_one_resolution_against_a_threshold(string resolution, string threshold, bool expected)
    {
        Assert.Equal(expected, VideoResolution.IsAtOrBelow(resolution, threshold));
    }

    [Theory]
    // No threshold set is the default, and hides nothing.
    [InlineData("576p", null)]
    [InlineData("576p", "")]
    // A value this app does not recognise is left alone rather than hidden on a guess.
    [InlineData("SuperVision", "1080p")]
    [InlineData(null, "1080p")]
    [InlineData("1080p", "nonsense")]
    public void Hides_nothing_when_either_side_is_unknown(string? resolution, string? threshold)
    {
        Assert.False(VideoResolution.IsAtOrBelow(resolution, threshold));
    }

    [Theory]
    [InlineData("2160p", "4k")]
    [InlineData("4320p", "8k")]
    // Already how people write them, so shortening leaves them be.
    [InlineData("1080p", "1080p")]
    [InlineData("720p", "720p")]
    [InlineData("576p", "576p")]
    // "2K" properly means a 1080p-class frame, so 1440p must not borrow it.
    [InlineData("1440p", "1440p")]
    [InlineData("SuperVision", "SuperVision")]
    [InlineData(null, null)]
    public void Shortens_only_the_two_resolutions_that_have_a_short_name(string? label, string? expected)
    {
        Assert.Equal(expected, VideoResolution.Shorten(label));
    }

    [Fact]
    public void The_ladder_is_ordered_and_every_rung_is_ranked()
    {
        var ranks = VideoResolution.Ladder.Select(VideoResolution.Rank).ToList();

        Assert.DoesNotContain(0, ranks);
        Assert.Equal(ranks.Order(), ranks);
        Assert.Equal(ranks.Distinct(), ranks);
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
