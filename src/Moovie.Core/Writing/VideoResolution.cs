using TagLib;
using File = TagLib.File;

namespace Moovie.Core.Writing;

/// <summary>
/// The frame size of the video itself, named the way a release would name it.
/// </summary>
/// <remarks>
/// Read from the container rather than the filename: a rename with a template that drops
/// <c>{resolution}</c> would leave the next pass with nothing, and an SD <c>hdvd</c> flag
/// written over a 4K film.
/// </remarks>
public static class VideoResolution
{
    /// <summary>
    /// Names the frame size, or null when the file has no video track or is too small to label.
    /// </summary>
    public static string? Read(string path)
    {
        var (width, height) = ReadSize(path);
        return Label(width, height);
    }

    /// <summary>
    /// The video's frame size in pixels, or <c>(0, 0)</c> when it cannot be read. A file that
    /// will not open is not an error here: the caller falls back to the filename.
    /// </summary>
    public static (int Width, int Height) ReadSize(string path)
    {
        try
        {
            using var file = File.Create(path);
            var properties = file.Properties;
            return properties is null ? (0, 0) : (properties.VideoWidth, properties.VideoHeight);
        }
        catch (Exception e) when (e is CorruptFileException or UnsupportedFormatException
                                      or IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// The vocabulary, smallest frame first. What the "leave it out at or below" setting offers.
    /// <c>1080i</c> is understood but not offered: same frame size as <c>1080p</c>, so it would
    /// be a threshold without a difference.
    /// </summary>
    public static IReadOnlyList<string> Ladder { get; } =
        ["360p", "480p", "576p", "720p", "1080p", "1440p", "2160p", "4320p"];

    /// <summary>
    /// Orders the vocabulary so one resolution can be compared against another. Frame size is
    /// what is ranked, so <c>1080i</c> and <c>1080p</c> tie. Anything unrecognised ranks 0 and
    /// compares as unknown, not as small.
    /// </summary>
    public static int Rank(string? label) =>
        label is not null && Ranks.TryGetValue(label.Trim(), out var rank) ? rank : 0;

    private static readonly Dictionary<string, int> Ranks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["360p"] = 1,
        ["480p"] = 2,
        ["576p"] = 3,
        ["720p"] = 4,
        ["1080i"] = 5,
        ["1080p"] = 5,
        ["1440p"] = 6,
        ["2160p"] = 7,
        ["4k"] = 7,
        ["4320p"] = 8,
        ["8k"] = 8,
    };

    /// <summary>
    /// The short name a filename would use, for <c>{resolution:short}</c>. Only 2160p and 4320p
    /// have one; 1440p is left alone because "2K" properly means a 1080p-class frame. Anything
    /// unrecognised comes back untouched.
    /// </summary>
    public static string? Shorten(string? label) =>
        label is not null && ShortNames.TryGetValue(label.Trim(), out var name) ? name : label;

    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2160p"] = "4k",
        ["4k"] = "4k",
        ["4320p"] = "8k",
        ["8k"] = "8k",
    };

    /// <summary>
    /// Whether <paramref name="resolution"/> is no larger than <paramref name="threshold"/>:
    /// the test behind leaving ordinary resolutions out of a filename. Either one unknown means
    /// "no", so nothing is hidden on a guess.
    /// </summary>
    public static bool IsAtOrBelow(string? resolution, string? threshold)
    {
        var floor = Rank(threshold);
        if (floor == 0)
            return false;

        var rank = Rank(resolution);
        return rank > 0 && rank <= floor;
    }

    /// <summary>
    /// Names a frame size: <c>1080p</c>, <c>2160p</c> and so on, matching the vocabulary the
    /// filename parser recognises so both sources can feed the same field.
    /// </summary>
    /// <remarks>
    /// Height and width are two separate opinions and the larger wins: height alone
    /// under-reports cropped widescreen film (1920×800 is not 720p), width alone under-reports
    /// 4:3 and anamorphic material (720×576 is 576p, not 480p). The tables cannot be derived
    /// from one another because SD pixels are not square: a DVD frame is 720 wide whether it
    /// holds 480 lines or 576.
    /// </remarks>
    public static string? Label(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return null;

        var byHeight = ByHeight(height);
        var byWidth = ByWidth(width);
        return Rank(byWidth) > Rank(byHeight) ? byWidth : byHeight;
    }

    // Both tables sit below their nominal figures so encodes rounded to a multiple of eight,
    // like 1920x1072, still count as the round number.
    private static string? ByHeight(int height) => height switch
    {
        >= 4000 => "4320p",
        >= 1900 => "2160p",
        >= 1300 => "1440p",
        >= 900 => "1080p",
        >= 620 => "720p",
        >= 520 => "576p",
        >= 420 => "480p",
        >= 300 => "360p",
        _ => null,
    };

    // 720 is where NTSC and PAL DVD share a width and differ in height, so it claims only the
    // lower of the two and lets the height promote it to 576p.
    private static string? ByWidth(int width) => width switch
    {
        >= 7000 => "4320p",
        >= 3400 => "2160p",
        >= 2300 => "1440p",
        >= 1700 => "1080p",
        >= 1100 => "720p",
        >= 700 => "480p",
        >= 600 => "360p",
        _ => null,
    };
}
