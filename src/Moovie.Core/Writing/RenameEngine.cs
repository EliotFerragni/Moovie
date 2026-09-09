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
    /// Characters Windows forbids. They are replaced regardless of host OS so a library stays
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
    public static string BuildFileName(
        RenameTemplate template,
        MediaMetadata metadata,
        string extension,
        NamingRules? rules = null)
    {
        rules ??= NamingRules.Default;

        var stem = template.Render(metadata, extension, rules.OmitResolutionAtOrBelow);
        if (template.UsesExtension && stem.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            stem = stem[..^extension.Length];

        stem = Sanitize(stem, rules.IllegalCharacterReplacement);
        stem = ApplySeparator(stem, rules.Separator);

        if (stem.Length == 0)
            return string.Empty; // Nothing renderable: the caller keeps the original name.

        return stem + extension;
    }

    /// <summary>
    /// Puts a name typed by hand through the same character rules a rendered one goes through,
    /// and gives it back <paramref name="extension"/>: renaming a file does not change what is
    /// inside it, so the extension is not the user's to drop. The separator is left alone, since
    /// spaces typed on purpose are not the template's doing. Empty when nothing usable is left.
    /// </summary>
    public static string CleanFileName(string? typed, string extension, string replacement = "")
    {
        var text = (typed ?? string.Empty).Trim();
        if (extension.Length > 0 && text.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            text = text[..^extension.Length];

        var stem = Sanitize(text, replacement);
        return stem.Length == 0 ? string.Empty : stem + extension;
    }

    /// <summary>
    /// Replaces the characters no filesystem will take and trims the result to a safe length.
    /// </summary>
    /// <param name="replacement">
    /// What each forbidden character becomes. Empty drops them. Anything forbidden in the
    /// replacement itself is ignored, so a hand-edited settings file cannot ask for an
    /// unusable name.
    /// </param>
    public static string Sanitize(string name, string replacement = "")
    {
        // ':' most often separates a title from its subtitle, so it reads as a dash whatever the
        // rest are replaced with: "Mission_ Impossible" would be the odd one out.
        var text = name.Replace(": ", " - ").Replace(":", "-");

        replacement = IllegalCharacters.Replace(replacement, string.Empty);
        text = IllegalCharacters.Replace(text, replacement);
        text = CollapseSpaces.Replace(text, " ");

        // Windows silently drops trailing dots and spaces, which would break round-tripping.
        text = text.Trim().TrimEnd('.', ' ');
        text = TidyReplacements(text, replacement);

        if (text.Length > MaxStemLength)
            text = text[..MaxStemLength].TrimEnd('.', ' ', '-');

        if (ReservedNames.Contains(text))
            text += "_";

        return text;
    }

    /// <summary>
    /// Keeps replaced characters from piling up: "Who?!" with underscores would otherwise come
    /// out as "Who__", and one at either end of a name is noise rather than punctuation.
    /// </summary>
    private static string TidyReplacements(string text, string replacement)
    {
        if (replacement.Length == 0)
            return text;

        var doubled = replacement + replacement;
        while (text.Contains(doubled, StringComparison.Ordinal))
            text = text.Replace(doubled, replacement, StringComparison.Ordinal);

        while (text.StartsWith(replacement, StringComparison.Ordinal))
            text = text[replacement.Length..];
        while (text.EndsWith(replacement, StringComparison.Ordinal))
            text = text[..^replacement.Length];

        return text.Trim();
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
        string path, RenameTemplate template, MediaMetadata metadata, NamingRules? rules = null) =>
        MoveTo(path, BuildFileName(template, metadata, Path.GetExtension(path), rules));

    /// <summary>
    /// Renames <paramref name="path"/> to a name given by hand, cleaned by
    /// <see cref="CleanFileName"/>. Returns the new path, or the original when nothing usable was
    /// typed or the file already has that name.
    /// </summary>
    public static string RenameTo(string path, string? typed, string replacement = "") =>
        MoveTo(path, CleanFileName(typed, Path.GetExtension(path), replacement));

    private static string MoveTo(string path, string fileName)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || fileName.Length == 0)
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
        string path, RenameTemplate template, MediaMetadata metadata, NamingRules? rules = null)
    {
        var fileName = BuildFileName(template, metadata, Path.GetExtension(path), rules);
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
