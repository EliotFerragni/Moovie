using Moovie.App.ViewModels;

namespace Moovie.App.Views;

/// <summary>
/// The handful of things <see cref="MainView"/> cannot do for itself because they need a host:
/// asking the platform for files, and opening a dialog. A desktop window answers these with native
/// pickers and real windows; the web host answers them inside its single view. Keeping them behind
/// an interface is what lets one view serve both.
/// </summary>
public interface IAppShell
{
    Task<IReadOnlyList<string>> PickFilesAsync();

    Task<string?> PickFolderAsync();

    Task<bool> ShowSettingsAsync(SettingsViewModel editor);

    Task ShowAboutAsync(AboutViewModel about);
}
