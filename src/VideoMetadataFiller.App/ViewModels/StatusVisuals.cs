using Avalonia.Media;
using VideoMetadataFiller.Core.Localization;
using VideoMetadataFiller.Core.Model;

namespace VideoMetadataFiller.App.ViewModels;

/// <summary>
/// The icon and colour shown at the right of each row in the file list.
/// </summary>
/// <remarks>
/// Icons are vector path data rather than font glyphs so they render identically on Windows,
/// macOS and Linux without depending on a symbol font being installed.
/// </remarks>
public static class StatusVisuals
{
    private const string Check =
        "M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z";

    private const string DoubleCheck =
        "M18 7l-1.41-1.41-6.34 6.34 1.41 1.41zm4.24-1.41L11.66 16.17 7.48 12l-1.41 1.41L11.66 19l12-12zM.41 " +
        "13.41 6 19l1.41-1.41L1.83 12z";

    private const string Question =
        "M11 18h2v-2h-2zm1-16A10 10 0 0 0 2 12a10 10 0 0 0 10 10 10 10 0 0 0 10-10A10 10 0 0 0 12 2m0 18a8 " +
        "8 0 0 1-8-8 8 8 0 0 1 8-8 8 8 0 0 1 8 8 8 8 0 0 1-8 8M12 6a4 4 0 0 0-4 4h2a2 2 0 0 1 2-2 2 2 0 0 1 " +
        "2 2c0 2-3 1.75-3 5h2c0-2.25 3-2.5 3-5a4 4 0 0 0-4-4";

    private const string Cross =
        "M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z";

    private const string Pencil =
        "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75zM20.71 7.04a1 1 0 0 0 0-1.41l-2.34-2.34a1 1 0 0 0-1.41 " +
        "0l-1.83 1.83 3.75 3.75z";

    private const string Warning =
        "M1 21h22L12 2zm12-3h-2v-2h2zm0-4h-2v-4h2z";

    private const string Hourglass =
        "M6 2v6h.01L6 8.01 10 12l-4 4 .01.01H6V22h12v-5.99h-.01L18 16l-4-4 4-3.99-.01-.01H18V2zm10 " +
        "14.5V20H8v-3.5l4-4zm-4-5-4-4V4h8v1.5z";

    private const string Circle =
        "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16m0-2a10 10 0 1 1 0 20 10 10 0 0 1 0-20";

    private const string ArrowUp =
        "M4 12l1.41 1.41L11 7.83V20h2V7.83l5.58 5.59L20 12l-8-8z";

    /// <summary>Vector path for a status, in a 24×24 box.</summary>
    public static string IconFor(FileStatus status) => status switch
    {
        FileStatus.Pending => Circle,
        FileStatus.Searching => Hourglass,
        FileStatus.Matched => Check,
        FileStatus.NeedsChoice => Question,
        FileStatus.NotFound => Cross,
        FileStatus.Edited => Pencil,
        FileStatus.Applying => ArrowUp,
        FileStatus.Applied => DoubleCheck,
        FileStatus.Failed => Warning,
        _ => Circle,
    };

    private static readonly Dictionary<FileStatus, Geometry> Parsed = [];

    /// <summary>Parsed geometry for a status, built once and reused for every row.</summary>
    public static Geometry GeometryFor(FileStatus status)
    {
        lock (Parsed)
        {
            if (!Parsed.TryGetValue(status, out var geometry))
            {
                geometry = Geometry.Parse(IconFor(status));
                Parsed[status] = geometry;
            }

            return geometry;
        }
    }

    /// <summary>Tooltip text for a status.</summary>
    public static string DescriptionFor(FileStatus status) => status switch
    {
        FileStatus.Pending => Strings.Get("status.pending"),
        FileStatus.Searching => Strings.Get("status.searching"),
        FileStatus.Matched => Strings.Get("status.matched"),
        FileStatus.NeedsChoice => Strings.Get("status.needsChoice"),
        FileStatus.NotFound => Strings.Get("status.notFound"),
        FileStatus.Edited => Strings.Get("status.edited"),
        FileStatus.Applying => Strings.Get("status.applying"),
        FileStatus.Applied => Strings.Get("status.applied"),
        FileStatus.Failed => Strings.Get("status.failed"),
        _ => string.Empty,
    };
}
