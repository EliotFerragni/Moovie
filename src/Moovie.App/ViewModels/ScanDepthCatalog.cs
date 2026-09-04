using Moovie.Core.Localization;
using Moovie.Core.Settings;

namespace Moovie.App.ViewModels;

/// <summary>How far a folder is opened, as offered in the dropdown.</summary>
public sealed record ScanDepthChoice(int Depth, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>The depths the main view's dropdown offers.</summary>
public static class ScanDepthCatalog
{
    /// <summary>
    /// Shallowest first, ending in "everything below". Built per call rather than held in a static
    /// field, which would be built before the interface language was chosen.
    /// </summary>
    public static IReadOnlyList<ScanDepthChoice> All =>
    [
        new(0, Strings.Get("main.scanDepthNone")),
        .. Enumerable
            .Range(1, AppSettings.MaxNamedScanDepth)
            .Select(depth => new ScanDepthChoice(depth, Label(depth))),
        new(AppSettings.UnlimitedScanDepth, Strings.Get("main.scanDepthUnlimited")),
    ];

    /// <summary>
    /// The ladder, with the stored depth on it. A settings file edited by hand can name a depth
    /// the ladder does not offer, which would otherwise leave the dropdown blank.
    /// </summary>
    public static IReadOnlyList<ScanDepthChoice> Including(int depth)
    {
        var all = All;

        // Any negative depth means unlimited, which the ladder already ends with.
        if (depth < 0 || all.Any(c => c.Depth == depth))
            return all;

        return
        [
            .. all.Take(all.Count - 1),
            new ScanDepthChoice(depth, Label(depth)),
            all[^1],
        ];
    }

    private static string Label(int depth) =>
        depth == 1
            ? Strings.Get("main.scanDepthOneLevel")
            : Strings.Format("main.scanDepthLevels", depth);
}
