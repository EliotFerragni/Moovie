using Avalonia;

namespace VideoMetadataFiller.App;

internal static class Program
{
    // Initialisation must not touch Avalonia types before AppMain runs, per SynchronizationContext setup.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>Also used by the Avalonia designer, which requires this exact signature.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
