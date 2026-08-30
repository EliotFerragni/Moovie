using Moovie.Core.Files;
using Xunit;

namespace Moovie.Core.Tests;

/// <summary>
/// The walk runs over somebody's whole library, on a NAS, with directories in it that the app may
/// not read. Getting this wrong took the app down on startup rather than skipping a folder.
/// </summary>
public class MediaFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("moovie-scan").FullName;

    public void Dispose()
    {
        // A directory made unreadable has to be opened again or the cleanup cannot descend into it.
        if (!OperatingSystem.IsWindows())
        {
            foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories))
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Directory.Delete(_root, recursive: true);
    }

    private string Make(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Fact]
    public void Finds_taggable_files_and_leaves_everything_else()
    {
        Make("Season 1/one.mp4");
        Make("Season 1/two.m4v");
        Make("Season 1/poster.jpg");
        Make("notes.txt");

        var found = MediaFiles.Expand([_root]).Select(Path.GetFileName).ToList();

        Assert.Equal(["one.mp4", "two.m4v"], found);
    }

    /// <summary>
    /// Synology puts an @eaDir beside everything, and it holds transcoded copies that really are
    /// .mp4 files. Tagging and renaming those would be quietly destructive.
    /// </summary>
    [Fact]
    public void Skips_the_private_folders_a_NAS_keeps_beside_the_media()
    {
        Make("keep.mp4");
        Make("@eaDir/SYNOVIDEO_TRANSCODE.mp4");
        Make("Season 1/@eaDir/thumb.mp4");
        Make("#recycle/deleted.mp4");
        Make(".Trash-1000/gone.mp4");

        var found = MediaFiles.Expand([_root]).Select(Path.GetFileName).ToList();

        Assert.Equal(["keep.mp4"], found);
    }

    /// <summary>Asked for one of them directly, it should still list what is inside.</summary>
    [Fact]
    public void Lists_a_private_folder_when_that_is_what_was_asked_for()
    {
        Make("@eaDir/inside.mp4");

        var found = MediaFiles.Expand([Path.Combine(_root, "@eaDir")]).Select(Path.GetFileName).ToList();

        Assert.Equal(["inside.mp4"], found);
    }

    /// <summary>
    /// The one that took the container down: enumeration is deferred, so a guard around building
    /// the query catches nothing, and the default options stop at the first unreadable directory.
    /// </summary>
    [Fact]
    public void Walks_past_a_directory_it_cannot_read()
    {
        Make("before.mp4");
        Make("locked/hidden.mp4");
        Make("Season 1/after.mp4");
        // Windows has no equivalent that is worth reaching for here, so there the directory stays
        // readable and the assertions below take the other branch.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(_root, "locked"), UnixFileMode.None);

        var found = MediaFiles.Expand([_root]).Select(Path.GetFileName).ToList();

        Assert.Contains("before.mp4", found);
        Assert.Contains("after.mp4", found);

        // Running as root the directory is readable anyway, and then its contents are simply
        // found. What must not happen either way is the walk ending.
        if (found.Contains("hidden.mp4"))
            Assert.Equal(3, found.Count);
        else
            Assert.Equal(2, found.Count);
    }

    /// <summary>
    /// The folder handed in being unreadable is the one case IgnoreInaccessible does not cover: it
    /// skips what it finds inside, but the walk still has to start. That failure lands in the
    /// guard, and the answer is an empty list rather than an exception.
    /// </summary>
    [Fact]
    public void An_unreadable_folder_of_its_own_yields_nothing_rather_than_throwing()
    {
        Make("locked/inside.mp4");
        var locked = Path.Combine(_root, "locked");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(locked, UnixFileMode.None);

        var found = MediaFiles.Expand([locked]).ToList();

        // Readable as root, in which case the file is simply found. Either way it returns.
        Assert.True(found.Count is 0 or 1);
    }

    /// <summary>One bad path must not cost the good ones that come after it.</summary>
    [Fact]
    public void An_unreadable_folder_does_not_stop_the_paths_after_it()
    {
        Make("locked/inside.mp4");
        Make("good/keep.mp4");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(_root, "locked"), UnixFileMode.None);

        var found = MediaFiles
            .Expand([Path.Combine(_root, "locked"), Path.Combine(_root, "good")])
            .Select(Path.GetFileName)
            .ToList();

        Assert.Contains("keep.mp4", found);
    }

    [Fact]
    public void A_file_named_directly_is_taken_as_it_is()
    {
        var file = Make("Movie.mp4");
        Make("Ignored.txt");

        Assert.Equal([file], MediaFiles.Expand([file, Path.Combine(_root, "Ignored.txt")]));
    }
}
