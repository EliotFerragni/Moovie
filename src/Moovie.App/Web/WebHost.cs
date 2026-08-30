using Avalonia;
using Avalonia.Controls.Remote;
using Avalonia.Headless;
using Avalonia.Threading;
using Moovie.App.Views;

namespace Moovie.App.Web;

/// <summary>
/// Runs the app with no window, drawing into a frame buffer that <see cref="BrowserTransport"/>
/// serves to a browser. The app itself is unchanged and, importantly, still runs here (on the
/// machine holding the files), so the pickers, the tag writer and the rename engine all act on
/// this machine's disks rather than on those of whoever opened the page.
/// </summary>
public static class WebHost
{
    /// <summary>
    /// Carries what moves on its own: a blinking caret, a progress bar. Everything else is drawn
    /// in response to input instead, so this can be slow enough not to matter.
    /// </summary>
    private const int AnimationTickMs = 33;


    public static void Run(string host, int port, IReadOnlyList<string> paths)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .LogToTrace()
            .SetupWithoutStarting();

        var transport = new BrowserTransport(host, port);
        using var server = new RemoteServer(transport);

        var viewModel = App.CreateShellViewModel();
        var view = new MainView { DataContext = viewModel };
        view.Shell = new WebShell(view);
        server.Content = view;

        transport.Start();

        if (paths.Count > 0)
            viewModel.AddPaths(paths);

        Console.WriteLine($"Moovie is serving its window on http://{(host == "+" ? "<this machine>" : host)}:{port}/");
        Console.WriteLine("The files it works on are this machine's. Press Ctrl+C to stop.");

        // The headless platform draws only when something advances its render timer, and a forced
        // tick repaints the window whether or not anything changed. Polling for that is what makes
        // a remote window expensive at rest, so it is driven by input instead: a click or a key
        // schedules exactly one repaint, posted below the input itself so it runs once the app has
        // finished reacting. Nothing to react to costs nothing.
        // What animates without being touched (a caret, a progress bar) still needs a heartbeat,
        // but only while somebody is there to see it. Merely waking to check is expensive enough
        // to be worth stopping outright, so a server nobody has open costs nothing at all.
        var animation = new DispatcherTimer(
            TimeSpan.FromMilliseconds(AnimationTickMs),
            DispatcherPriority.Background,
            (sender, _) =>
            {
                if (transport.HasViewers)
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                else
                    ((DispatcherTimer)sender!).Stop();
            });

        transport.InputReceived += () => Dispatcher.UIThread.Post(
            () =>
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                // A browser announces its size the moment it connects, so this is also where a new
                // viewer starts the heartbeat again.
                if (transport.HasViewers && !animation.IsEnabled)
                    animation.Start();
            },
            DispatcherPriority.Background);

        Dispatcher.UIThread.MainLoop(CancellationToken.None);
    }
}
