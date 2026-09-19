using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
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
    /// And how often while nothing is. Only a change nobody asked for waits this long, since input
    /// draws at once, and five wakes a second rather than thirty is the difference between a
    /// machine that idles and one whose fan never stops.
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
        // itself, and work from other threads arrives as a signal rather than a timer. The only
        // timers left to be late are Avalonia's own pool trimming, which does not care.
        loop?.CoalesceTimersWhile(() => !transport.HasViewers, TimeSpan.FromSeconds(IdleFloorSeconds));

        var viewModel = App.CreateShellViewModel();
        var view = new MainView { DataContext = viewModel };
        view.Shell = new WebShell(view);
        server.Content = view;

        var top = TopLevel.GetTopLevel(view)
            ?? throw new InvalidOperationException("The view is not in a top level, so its focus cannot be read.");

        // The clipboard the user shares with the rest of their machine is the browser's, so
        // copying and pasting is a conversation with the page. ClipboardBridge explains the split.
        var clipboard = new ClipboardBridge(top);
        clipboard.SelectionChanged += text => transport.Post(new { type = "selection", text });
        clipboard.Copied += text => transport.Post(new { type = "copied", text });
        transport.ClipboardRequested += (action, text) =>
            Dispatcher.UIThread.Post(() => clipboard.Apply(action, text));

        // Where the focus is is the page's business too, so a tablet knows when to offer its
        // on-screen keyboard.
        var keyboard = new SoftKeyboard(top);
        keyboard.Changed += active => transport.Post(new { type = "text-focus", active });

        transport.ViewerArrived += () => Dispatcher.UIThread.Post(() =>
        {
            clipboard.Announce();
            keyboard.Announce();
        });

        transport.Start();

        if (paths.Count > 0)
        {
            try
            {
                viewModel.AddPaths(paths);
            }
            catch (Exception e)
            {
                // A server that stays up with an empty list beats one its supervisor restarts
                // forever: the page still opens and files can still be added by hand.
                Console.Error.WriteLine($"Could not read everything under the paths given: {e.Message}");
            }
        }

        Console.WriteLine($"Moovie is serving its window on http://{(host == "+" ? "<this machine>" : host)}:{port}/");
        Console.WriteLine("The files it works on are this machine's. Press Ctrl+C to stop.");

        // Input draws exactly one frame, posted below the input itself so it runs once the app
        // has reacted. Everything else (a caret, a progress bar, a reply from the network) arrives
        // without anybody asking, so the timer below looks for it: quickly while the window is
        // still changing, slowly once it has settled, not at all with nobody watching.
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

                // Drawing tick after tick is an animation and worth following closely. A single
                // frame is not: a blinking caret would otherwise hold the window at thirty frames
                // a second for as long as any field has the cursor in it.
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
