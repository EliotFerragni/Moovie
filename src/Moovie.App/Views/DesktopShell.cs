using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Moovie.App.ViewModels;
using Moovie.Core.Localization;

namespace Moovie.App.Views;

/// <summary>
/// Answers <see cref="IAppShell"/> the way a desktop app should: with the operating system's own
/// file pickers and real modal windows.
/// </summary>
public sealed class DesktopShell(Window owner) : IAppShell
{
    /// <summary>
    /// The filter the file picker offers. Built per call rather than held in a static field: its
    /// name is translated, and a static would be built before the language was chosen.
    /// </summary>
    private static FilePickerFileType VideoFiles => new(Strings.Get("picker.videoFiles"))
    {
        Patterns = ["*.mp4", "*.m4v", "*.MP4", "*.M4V"],
        AppleUniformTypeIdentifiers = ["public.mpeg-4"],
        MimeTypes = ["video/mp4"],
    };

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("picker.addFiles"),
            AllowMultiple = true,
            FileTypeFilter = [VideoFiles],
        });

        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickFolderAsync()
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.Get("picker.addFolder"),
            AllowMultiple = false,
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public Task<bool> ShowSettingsAsync(SettingsViewModel editor) =>
        new SettingsWindow { DataContext = editor }.ShowDialog<bool>(owner);

    public Task ShowAboutAsync(AboutViewModel about) =>
        new AboutWindow { DataContext = about }.ShowDialog(owner);
}
