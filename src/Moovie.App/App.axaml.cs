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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new SettingsStore();

            // Before anything is constructed: the windows resolve their text as they load, so a
            // language chosen after that point would only show up on the next run anyway.
            Strings.Use(store.Load().AppLanguage);

            var viewModel = new MainWindowViewModel(store);
            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;

            // Files and folders named on the command line are loaded straight away, so the app
            // works as an "Open with" target and accepts files dropped onto its icon.
            if (desktop.Args is { Length: > 0 } args)
                window.Opened += (_, _) => viewModel.AddPaths(args);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
