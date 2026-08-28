using TagLib;
using File = TagLib.File;

namespace VideoMetadataFiller.Core.Writing;

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
    /// Names a frame size: <c>1080p</c>, <c>2160p</c> and so on, matching the vocabulary the
    /// filename parser recognises so both sources can feed the same field.
    /// </summary>
    /// <remarks>
    /// Height decides, but width may promote it, because widescreen film is cropped in height
    /// rather than letterboxed: a 2.39:1 transfer is 1920×800, which is 1080p by every other
    /// reckoning. Going the other way would demote 4:3 and anamorphic material, where the height
    /// is the honest number — 720×576 is 576p, not 720p — so width only ever raises the answer.
    /// <para>
    /// The bands sit below their nominal heights on purpose: encodes rounded to a multiple of
    /// eight, like 1920×1072, are the same thing as the round number.
    /// </para>
    /// </remarks>
    public static string? Label(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return null;

        var effective = Math.Max(height, (int)Math.Round(width * 9.0 / 16.0));
        return effective switch
        {
            >= 4000 => "4320p",
            >= 1900 => "2160p",
            >= 1300 => "1440p",
            >= 900 => "1080p",
            >= 620 => "720p",
            >= 520 => "576p",
            >= 420 => "480p",
            >= 300 => "360p",
            // Smaller than any label would mean anything: a thumbnail or a broken header.
            _ => null,
        };
    }
}
