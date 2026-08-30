using System.Text;
using System.Text.RegularExpressions;
using Moovie.Core.Model;
using Moovie.Core.Settings;

namespace Moovie.Core.Writing;

/// <summary>
/// Turns a <see cref="RenameTemplate"/> plus metadata into a filename that is legal on Windows,
/// macOS and Linux alike, and resolves collisions inside the target folder.
/// </summary>
public static class RenameEngine
{
    /// <summary>
    /// Characters Windows forbids. They are stripped regardless of host OS so a library stays
    /// portable between machines.
    /// </summary>
    private static readonly Regex IllegalCharacters = new(@"[<>:""/\\|?*\x00-\x1f]", RegexOptions.Compiled);

    private static readonly Regex CollapseSpaces = new(@" {2,}", RegexOptions.Compiled);

    /// <summary>Windows device names, which cannot be used as filenames even with an extension.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Kept well under the 255-byte limit common to all three platforms, leaving room for the
    /// collision suffix and for multi-byte characters.
    /// </summary>
    private const int MaxStemLength = 180;

    /// <summary>
    /// Builds the filename (stem plus extension) that <paramref name="metadata"/> should get.
    /// </summary>
    /// <param name="omitResolutionAtOrBelow">
    /// A resolution at or below which <c>{resolution}</c> is left out. See
    /// <see cref="RenameTemplate.Render"/>.
    /// </param>
    public static string BuildFileName(
        RenameTemplate template,
        MediaMetadata metadata,
        string extension,
        SeparatorStyle separator = SeparatorStyle.Space,
        string? omitResolutionAtOrBelow = null)
    {
        var stem = template.Render(metadata, extension, omitResolutionAtOrBelow);
        if (template.UsesExtension && stem.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            stem = stem[..^extension.Length];

        stem = Sanitize(stem);
        stem = ApplySeparator(stem, separator);

        if (stem.Length == 0)
            return string.Empty; // Nothing renderable: the caller keeps the original name.

        return stem + extension;
    }

    /// <summary>Removes characters no filesystem will take and trims the result to a safe length.</summary>
    public static string Sanitize(string name)
    {
        // ':' most often separates a title from its subtitle, so it reads better as a dash.
        var text = name.Replace(": ", " - ").Replace(":", "-");
        text = IllegalCharacters.Replace(text, string.Empty);
        text = CollapseSpaces.Replace(text, " ");

        // Windows silently drops trailing dots and spaces, which would break round-tripping.
        text = text.Trim().TrimEnd('.', ' ');

        if (text.Length > MaxStemLength)
            text = text[..MaxStemLength].TrimEnd('.', ' ', '-');

        if (ReservedNames.Contains(text))
            text += "_";

        return text;
    }

    private static string ApplySeparator(string stem, SeparatorStyle separator) => separator switch
    {
        SeparatorStyle.Dot => stem.Replace(' ', '.'),
        SeparatorStyle.Underscore => stem.Replace(' ', '_'),
        SeparatorStyle.Dash => stem.Replace(' ', '-'),
        _ => stem,
    };

    /// <summary>
    /// Returns the full path <paramref name="fileName"/> should take inside
    /// <paramref name="directory"/>, appending " (2)", " (3)" … if something else is already there.
    /// A file that already has the wanted name keeps it.
    /// </summary>
    public static string ResolveCollision(string directory, string fileName, string? currentPath = null)
    {
        var target = Path.Combine(directory, fileName);
        if (!Exists(target) || SamePath(target, currentPath))
            return target;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (!Exists(candidate) || SamePath(candidate, currentPath))
                return candidate;
        }

        return target;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool SamePath(string a, string? b) =>
        b is not null && string.Equals(
            Path.GetFullPath(a),
            Path.GetFullPath(b),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Renames <paramref name="path"/> in place. Returns the new path, or the original when the
    /// name is already right or nothing renderable came out of the template.
    /// </summary>
    public static string Rename(
        string path, RenameTemplate template, MediaMetadata metadata, SeparatorStyle separator,
        string? omitResolutionAtOrBelow = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            return path;

        var fileName = BuildFileName(
            template, metadata, Path.GetExtension(path), separator, omitResolutionAtOrBelow);
        if (fileName.Length == 0)
            return path;

        var target = ResolveCollision(directory, fileName, path);
        if (SamePath(target, path))
            return path;

        File.Move(path, target);
        return target;
    }

    /// <summary>
    /// A preview of what <paramref name="path"/> would become, without touching the filesystem.
    /// Used for the "old → new" column in the file list.
    /// </summary>
    public static string PreviewFileName(
        string path, RenameTemplate template, MediaMetadata metadata, SeparatorStyle separator,
        string? omitResolutionAtOrBelow = null)
    {
        var fileName = BuildFileName(
            template, metadata, Path.GetExtension(path), separator, omitResolutionAtOrBelow);
        return fileName.Length == 0 ? Path.GetFileName(path) : fileName;
    }

    /// <summary>Stand-in metadata for the live preview in Settings.</summary>
    public static MediaMetadata SampleMovie { get; } = new()
    {
        Kind = MediaKind.Movie,
        Title = "Blade Runner 2049",
        OriginalTitle = "Blade Runner 2049",
        Year = 2017,
        ReleaseDate = new DateTime(2017, 10, 6),
        Genres = ["Science Fiction", "Drama"],
        Studio = "Alcon Entertainment",
        Resolution = "2160p",
        TmdbId = 335984,
        ImdbId = "tt1856101",
    };

    /// <summary>Stand-in metadata for the live preview in Settings.</summary>
    public static MediaMetadata SampleEpisode { get; } = new()
    {
        Kind = MediaKind.TvEpisode,
        ShowName = "Severance",
        Title = "Good News About Hell",
        OriginalTitle = "Good News About Hell",
        Season = 1,
        Episodes = [1],
        Year = 2022,
        ReleaseDate = new DateTime(2022, 2, 18),
        Genres = ["Drama", "Mystery"],
        Network = "Apple TV+",
        Resolution = "2160p",
        TmdbId = 95396,
        ImdbId = "tt11280740",
    };
}
