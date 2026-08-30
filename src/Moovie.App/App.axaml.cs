using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Moovie.App.ViewModels;
using Moovie.App.Views;
using Moovie.Core.Localization;
using Moovie.Core.Settings;

namespace Moovie.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // In --web mode there is no lifetime: the web host builds the same view model below and
        // hands it to a RemoteServer instead of to a window.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = CreateShellViewModel();
            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;

            // Files and folders named on the command line are loaded straight away, so the app
            // works as an "Open with" target and accepts files dropped onto its icon.
            if (desktop.Args is { Length: > 0 } args)
                window.Opened += (_, _) => viewModel.AddPaths(args);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The one view model both ways of running show. Reading the settings has to come first:
    /// the views resolve their text as they load, so a language chosen after that point would
    /// only show up on the next run anyway.
    /// </summary>
    public static MainWindowViewModel CreateShellViewModel()
    {
        var store = new SettingsStore();
        Strings.Use(store.Load().AppLanguage);
        return new MainWindowViewModel(store);
    }
}
