using Moovie.Core.Localization;
using Moovie.Core.Model;

namespace Moovie.Core.Writing;

/// <summary>
/// The written form of the fields that are not stored verbatim: the date, the episode label, the
/// joined lists and the HD flag.
/// </summary>
/// <remarks>
/// Shared deliberately. <see cref="Mp4TagWriter"/> produces these values, <see cref="Mp4TagReader"/>
/// parses them back, and <see cref="MetadataDiff"/> compares them — so a change to how a date is
/// written cannot leave the reader or the diff behind.
/// </remarks>
internal static class TagFormat
{
    /// <summary>The <c>©day</c> value: a full date when there is one, otherwise the bare year.</summary>
    internal static string? Date(MediaMetadata metadata)
    {
        if (metadata.ReleaseDate is { } date)
            return date.ToString("yyyy-MM-dd");
        return metadata.Year?.ToString();
    }

    /// <summary>The <c>tven</c> label, e.g. <c>S01E01</c> or <c>S01E01-E02</c>.</summary>
    internal static string? EpisodeLabel(MediaMetadata metadata)
    {
        if (metadata.Episodes.Count == 0)
            return null;
        var episodes = string.Join("-E", metadata.Episodes.Select(e => e.ToString("00")));
        return metadata.Season is null ? $"E{episodes}" : $"S{metadata.Season:00}E{episodes}";
    }

    /// <summary>How a list of names or genres is stored in a single text atom.</summary>
    internal static string? Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? null : string.Join(", ", values);

    /// <summary>Splits a text atom back into the list it was joined from.</summary>
    internal static List<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Apple's HD flag, derived from the resolution we lifted off the filename.</summary>
    internal static int HdFlag(string? resolution) => resolution switch
    {
        "4320p" or "2160p" => 3,
        "1440p" or "1080p" or "1080i" => 2,
        "720p" => 1,
        _ => 0,
    };

    /// <summary>
    /// Names an HD flag for the diff. The flag is coarser than the resolution it came from —
    /// 1080i and 1440p share one — so it is shown as the flag rather than pretending otherwise.
    /// </summary>
    internal static string HdFlagLabel(int flag) => flag switch
    {
        3 => Strings.Get("hd.uhd"),
        2 => Strings.Get("hd.fullHd"),
        1 => Strings.Get("hd.hd"),
        _ => Strings.Get("hd.sd"),
    };

    internal static string? Truncate(string? text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= limit)
            return text;
        return string.Concat(text.AsSpan(0, limit - 1).TrimEnd(), "…");
    }
}
