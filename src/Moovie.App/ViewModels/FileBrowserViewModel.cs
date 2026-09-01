using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moovie.Core.Localization;
using Moovie.Core.Writing;

namespace Moovie.App.ViewModels;

public sealed record FileBrowserEntry(string Name, string Path, bool IsDirectory);

/// <summary>
/// Browses the filesystem of the machine the app is running on. The desktop build asks the
/// operating system for a picker instead; this exists for <c>--web</c>, where the browser's own
/// file dialog would offer the files of whoever opened the page rather than the ones being served.
/// </summary>
public sealed partial class FileBrowserViewModel : ObservableObject
{
    private readonly bool _foldersOnly;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public FileBrowserViewModel(bool foldersOnly, string title, string? startIn = null)
    {
        _foldersOnly = foldersOnly;
        Title = title;
        Navigate(StartingDirectory(startIn));
    }

    public string Title { get; }

    public bool AllowsFileSelection => !_foldersOnly;

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Named for what it does rather than "OK", which never says what is about to happen.</summary>
    public string AcceptLabel => Strings.Get(_foldersOnly ? "browser.addFolder" : "browser.addFiles");

    public ObservableCollection<FileBrowserEntry> Entries { get; } = [];

    /// <summary>Kept in step by the view; a list box's selection is awkward to bind reliably.</summary>
    public IReadOnlyList<FileBrowserEntry> Selection { get; set; } = [];

    /// <summary>Raised with the chosen paths, or null if the user cancelled.</summary>
    public event Action<IReadOnlyList<string>?>? Completed;

    [RelayCommand]
    public void Open(FileBrowserEntry? entry)
    {
        if (entry is { IsDirectory: true })
            Navigate(entry.Path);
    }

    [RelayCommand]
    private void GoUp()
    {
        var parent = Path.GetDirectoryName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar));
        if (!string.IsNullOrEmpty(parent))
            Navigate(parent);
    }

    [RelayCommand]
    private void Accept()
    {
        // A folder is chosen by standing in it, so no selection is needed; files are chosen by
        // selecting them.
        if (_foldersOnly)
        {
            Completed?.Invoke([CurrentPath]);
            return;
        }

        var files = Selection.Where(e => !e.IsDirectory).Select(e => e.Path).ToList();
        if (files.Count > 0)
            Completed?.Invoke(files);
    }

    [RelayCommand]
    private void Cancel() => Completed?.Invoke(null);

    public void Navigate(string path)
    {
        try
        {
            var entries = new List<FileBrowserEntry>();

            foreach (var directory in Directory.EnumerateDirectories(path).Order(StringComparer.OrdinalIgnoreCase))
                entries.Add(new FileBrowserEntry(Path.GetFileName(directory), directory, true));

            if (!_foldersOnly)
            {
                foreach (var file in Directory.EnumerateFiles(path).Order(StringComparer.OrdinalIgnoreCase))
                    if (Mp4TagWriter.IsSupported(file))
                        entries.Add(new FileBrowserEntry(Path.GetFileName(file), file, false));
            }

            Entries.Clear();
            foreach (var entry in entries)
                Entries.Add(entry);

            CurrentPath = Path.GetFullPath(path);
            Error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A folder that cannot be read must not empty the list or strand the user, so the
            // listing stays as it was and the reason is shown instead.
            Error = Strings.Format("browser.cannotOpen", e.Message);
        }
    }

    /// <summary>
    /// Wherever the caller was last working, falling back to the home directory. The caller gets
    /// to say because in a container HOME is the app's own config directory, not the media.
    /// </summary>
    private static string StartingDirectory(string? startIn)
    {
        if (!string.IsNullOrEmpty(startIn) && Directory.Exists(startIn))
            return startIn;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(home) ? home : Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
    }
}
