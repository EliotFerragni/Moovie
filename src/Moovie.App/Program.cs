using Avalonia;
using Moovie.App.Web;

namespace Moovie.App;

internal static class Program
{
    // Initialisation must not touch Avalonia types before AppMain runs, per SynchronizationContext setup.
    [STAThread]
    public static int Main(string[] args)
    {
        CommandLine options;
        try
        {
            options = CommandLine.Parse(args);
        }
        catch (ArgumentException e)
        {
            Terminal.Attach();
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLine.Usage);
            return 1;
        }

        if (options.Help)
        {
            Terminal.Attach();
            Console.WriteLine(CommandLine.Usage);
            return 0;
        }

        if (options.Web)
        {
            Terminal.Attach();
            try
            {
                WebHost.Run(options.Host, options.Port, options.Paths);
            }
            catch (Exception e)
            {
                // Without this the process would die silently, which on Windows (where it has no
                // console of its own) is indistinguishable from it never having started.
                Console.Error.WriteLine(e.Message);
                return 1;
            }

            return 0;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>Also used by the Avalonia designer, which requires this exact signature.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
