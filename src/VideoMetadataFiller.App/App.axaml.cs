using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VideoMetadataFiller.App.ViewModels;
using VideoMetadataFiller.App.Views;
using VideoMetadataFiller.Core.Settings;

namespace VideoMetadataFiller.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new SettingsStore();
            var viewModel = new MainWindowViewModel(store);
            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;

            // Files and folders named on the command line are loaded straight away, so the app
            // works as an "Open with" target and accepts files dropped onto its icon.
            if (desktop.Args is { Length: > 0 } args)
                window.Opened += (_, _) => _ = viewModel.AddPathsAsync(args);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
