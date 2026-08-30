using Moovie.Core.Writing;

namespace Moovie.Core.Files;

/// <summary>
/// Finds the taggable files under whatever was dropped, picked or named on the command line.
/// </summary>
public static class MediaFiles
{
    /// <summary>
    /// Directories a NAS keeps beside the media, which hold no library of their own.
    ///
    /// Synology's @eaDir is the one that matters: it sits in every shared folder, it is often
    /// unreadable, and it holds transcoded copies that really are .mp4 files. Listed as if they
    /// were the library's own, they would be tagged and renamed.
    /// </summary>
    private static readonly string[] PrivateFolders =
        ["@eaDir", "#recycle", "@Recycle", ".@__thumb", "$RECYCLE.BIN", "#snapshot", ".DS_Store"];

    /// <summary>
    /// Expands folders recursively, keeping only files that can be tagged. Anything unreadable is
    /// skipped rather than allowed to stop the walk.
    /// </summary>
    public static IEnumerable<string> Expand(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Under(path))
                    yield return file;
            }
            else if (File.Exists(path) && Mp4TagWriter.IsSupported(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>
    /// The result is built inside the try rather than returned lazily. Enumeration is deferred, so
    /// a guard around the call that composes the query catches nothing at all: the failure arrives
    /// later, while somebody else is iterating.
    /// </summary>
    private static List<string> Under(string directory)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,

                    // Without this, one directory the app may not read ends the whole walk. A
                    // Synology share always has at least one.
                    IgnoreInaccessible = true,
                })
                .Where(Mp4TagWriter.IsSupported)
                .Where(file => !IsInPrivateFolder(directory, file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Only the part below what was asked for is examined, so pointing the app straight at one of
    /// these folders still works. It is skipping them on the way past that matters.
    /// </summary>
    private static bool IsInPrivateFolder(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments[..^1].Any(segment =>
            PrivateFolders.Contains(segment, StringComparer.OrdinalIgnoreCase)
            || segment.StartsWith(".Trash-", StringComparison.OrdinalIgnoreCase));
    }
}
