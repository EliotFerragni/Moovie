using System.Diagnostics;
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
    /// How often the window is drawn while something on it is moving: a progress bar filling, a
    /// caret blinking, a button fading under the cursor.
    /// </summary>
    private const int MovingTickMs = 33;

    /// <summary>
    /// And how often while nothing is. Only a change nobody asked for waits this long, because
    /// input draws at once; a search result appearing a fifth of a second late is not something
    /// anyone can see, while waking five times a second rather than thirty is the difference
    /// between a machine that idles and a machine whose fan never stops.
    /// </summary>
    private const int StillTickMs = 200;

    /// <summary>How long after the last thing somebody did the window is still treated as moving.</summary>
    private const int SettleMs = 500;

    /// <summary>
    /// How coarsely timers are honoured while nobody has the page open. Long enough that a NAS
    /// gets to stay in its deep idle states between wakes, short enough that Avalonia's pools are
    /// still trimmed within a minute of the last person leaving.
    /// </summary>
    private const int IdleFloorSeconds = 30;


    public static void Run(string host, int port, IReadOnlyList<string> paths)
    {
        // Both have to come before the platform is set up, which is the whole story in each.
        var clock = DrawingClock.TakeOver();
        var loop = QuietDispatcher.TakeOver();

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .LogToTrace()
            .SetupWithoutStarting();

        var transport = new BrowserTransport(host, port);
        using var server = new RemoteServer(transport);

        // Nothing the app itself times runs while nobody is connected: the heartbeat below stops
        // itself, and work arriving from other threads arrives as a signal rather than a timer.
        // So the only timers left to be late are Avalonia's own pool trimming, which does not care.
        loop?.CoalesceTimersWhile(() => !transport.HasViewers, TimeSpan.FromSeconds(IdleFloorSeconds));

        var viewModel = App.CreateShellViewModel();
        var view = new MainView { DataContext = viewModel };
        view.Shell = new WebShell(view);
        server.Content = view;

        transport.Start();

        if (paths.Count > 0)
        {
            try
            {
                viewModel.AddPaths(paths);
            }
            catch (Exception e)
            {
                // Whatever went wrong reading one folder, a server that stays up with an empty
                // list is worth far more than one that exits and is restarted forever by its
                // supervisor. The page still opens and files can still be added by hand.
                Console.Error.WriteLine($"Could not read everything under the paths given: {e.Message}");
            }
        }

        Console.WriteLine($"Moovie is serving its window on http://{(host == "+" ? "<this machine>" : host)}:{port}/");
        Console.WriteLine("The files it works on are this machine's. Press Ctrl+C to stop.");

        // Input is the cheap half of knowing when to draw: a click or a key draws exactly one
        // frame, posted below the input itself so it runs once the app has finished reacting.
        // The rest has to be looked for, since a caret, a progress bar or a reply from the network
        // all arrive without anybody asking. That is what the timer below does: quickly while the
        // window is still changing, slowly once it has settled, and not at all while nobody has
        // the page open.
        var sinceInput = Stopwatch.StartNew();
        var drewInARow = 0;
        DispatcherTimer heartbeat = null!;
        heartbeat = new DispatcherTimer(
            TimeSpan.FromMilliseconds(MovingTickMs),
            DispatcherPriority.Background,
            (_, _) =>
            {
                if (!transport.HasViewers)
                {
                    heartbeat.Stop();
                    return;
                }

                clock.Draw();

                // Something that draws tick after tick is an animation and worth following
                // closely. A single frame on its own is not, and treating it as one would be
                // expensive: a caret blinking twice a second would hold the window at thirty
                // frames a second for as long as any field has the cursor in it.
                drewInARow = transport.DrewSinceLastAsked() ? drewInARow + 1 : 0;

                var moving = drewInARow > 1 || sinceInput.ElapsedMilliseconds < SettleMs;
                var wanted = TimeSpan.FromMilliseconds(moving ? MovingTickMs : StillTickMs);
                if (heartbeat.Interval != wanted)
                    heartbeat.Interval = wanted;
            });

        transport.InputReceived += () => Dispatcher.UIThread.Post(
            () =>
            {
                clock.Draw();

                // Somebody is interacting, so whatever this frame does or does not change, the
                // next few hundred milliseconds are worth watching closely.
                sinceInput.Restart();

                // A browser announces its size the moment it connects, so this is also where a new
                // viewer starts the timer again.
                if (transport.HasViewers && !heartbeat.IsEnabled)
                    heartbeat.Start();
            },
            DispatcherPriority.Background);

        Dispatcher.UIThread.MainLoop(CancellationToken.None);
    }
}
