using TagLib;
using File = TagLib.File;

namespace Moovie.Core.Writing;

/// <summary>
/// The frame size of the video itself, named the way a release would name it.
/// </summary>
/// <remarks>
/// The filename is only a claim about resolution, and one that does not survive being renamed:
/// a template without <c>{resolution}</c> drops the token, and the next pass over the same file
/// would find nothing and write an SD <c>hdvd</c> flag over a 4K film. The container knows, so
/// it is asked.
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
    /// </summary>
    /// <remarks>
    /// <c>1080i</c> is understood but not offered: it is the same frame size as <c>1080p</c>, so
    /// picking between them as a threshold would be a distinction without a difference.
    /// </remarks>
    public static IReadOnlyList<string> Ladder { get; } =
        ["360p", "480p", "576p", "720p", "1080p", "1440p", "2160p", "4320p"];

    /// <summary>
    /// Orders the vocabulary so one resolution can be compared against another. Frame size is
    /// what is being ranked, so <c>1080i</c> and <c>1080p</c> tie. Anything unrecognised (a
    /// value typed into the field by hand, say) ranks 0 and compares as unknown, not as small.
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
    /// The short name a filename would use, for <c>{resolution:short}</c>.
    /// </summary>
    /// <remarks>
    /// Only the two that have one: 2160p is written <c>4k</c> and 4320p <c>8k</c>. Everything
    /// below is already how people write it (nobody calls 1080p anything shorter) and 1440p is
    /// deliberately left alone, since "2K" properly means a 1080p-class frame and using it here
    /// would name the file wrongly. Anything unrecognised comes back untouched.
    /// </remarks>
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
    /// the test behind leaving ordinary resolutions out of a filename.
    /// </summary>
    /// <remarks>
    /// Both unknowns mean "no": no threshold is the default and hides nothing, and a resolution
    /// this app does not recognise is left alone rather than hidden on a guess.
    /// </remarks>
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
    /// Height and width are read as two separate opinions and the larger wins, because either
    /// one alone is wrong for material that is common.
    /// <para>
    /// Height alone under-reports widescreen film, which is cropped rather than letterboxed: a
    /// 2.39:1 transfer is 1920×800, and 800 is not 720p. Width alone under-reports 4:3 and
    /// anamorphic material, where the height is the honest number: 720×576 is 576p, not 480p.
    /// </para>
    /// <para>
    /// The two tables are separate rather than one converted into the other because the aspect
    /// ratio to convert by is not a constant. HD is square-pixel 16:9, so a width does divide
    /// cleanly into a height there; SD is not, and a DVD frame is 720 wide whether it holds
    /// 480 lines, 576, or the 406 of a cropped widescreen transfer. Scaling 720 by 9/16 gives
    /// 405, which is short of the 480p it plainly is.
    /// </para>
    /// <para>
    /// Both tables sit below their nominal figures on purpose: encodes rounded to a multiple of
    /// eight, like 1920×1072, are the same thing as the round number.
    /// </para>
    /// </remarks>
    public static string? Label(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return null;

        var byHeight = ByHeight(height);
        var byWidth = ByWidth(width);
        return Rank(byWidth) > Rank(byHeight) ? byWidth : byHeight;
    }

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

    /// <summary>
    /// The standard frame widths. 720 is where NTSC and PAL DVD share a width and differ in
    /// height, so it claims only the lower of the two and lets the height promote it to 576p.
    /// </summary>
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
