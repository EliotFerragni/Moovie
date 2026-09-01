using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;

namespace Moovie.App.Web;

/// <summary>
/// The event loop the app's thread runs in, which this host owns rather than the platform.
///
/// The headless platform's <c>ManagedDispatcherImpl</c> sleeps properly with nothing to do, but
/// declines to wait out the last millisecond before a timer and busy-waits it instead. Avalonia's
/// compositor keeps a one-second timer per buffer pool, so even with <see cref="DrawingClock"/>
/// holding still the loop pays that spin a few times a second forever: barely any processor time
/// and, measured on the machine this was written for, five watts all day for an app nobody had
/// open, because each spin ramps a core's clock and keeps a NAS out of its deep idle states.
///
/// So this is the platform's loop minus the spin, sleeping a millisecond where it would busy-wait
/// a sub-millisecond remainder. Timers still fire and continuations still run, which is why the
/// loop is not simply stopped while nobody is connected: an apply reports its progress through
/// dispatcher continuations, and closing the tab in the middle of one is ordinary.
///
/// Avalonia allows application code no part in this: the interface may not be implemented outside
/// its assembly and the registry is internal, hence the proxy and the reflection. The takeover is
/// refused unless every member is exactly the shape known here, in which case the app runs on the
/// platform's loop, which works perfectly well and merely costs more.
/// </summary>
// Not sealed: DispatchProxy builds the instance by deriving from this.
public class QuietDispatcher : DispatchProxy
{
    /// <summary>
    /// The whole of <c>IControlledDispatcherImpl</c> and everything it inherits, as this
    /// understands it. A loop with anything more to it than this is one this cannot honestly
    /// pretend to be, so the takeover is refused instead.
    /// </summary>
    private static readonly string[] KnownShape =
    [
        "get_CurrentThreadIsLoopThread", "get_Now", "Signal", "UpdateTimer",
        "add_Signaled", "remove_Signaled", "add_Timer", "remove_Timer",
        "get_CanQueryPendingInput", "get_HasPendingInput", "RunLoop",
    ];

    private const BindingFlags Anywhere =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private readonly AutoResetEvent _wakeup = new(false);
    private readonly object _lock = new();
    private readonly Stopwatch _running = Stopwatch.StartNew();

    private Thread _loopThread = Thread.CurrentThread;
    private Func<bool>? _idle;
    private TimeSpan _idleFloor = TimeSpan.Zero;
    private bool _signaled;
    private TimeSpan? _dueAt;
    private Action? _onSignal;
    private Action? _onTimer;

    /// <summary>Public because <see cref="DispatchProxy"/> builds these; call <see cref="TakeOver"/>.</summary>
    public QuietDispatcher()
    {
    }

    /// <summary>
    /// While <paramref name="idle"/> says so, no timer is waited for more precisely than
    /// <paramref name="floor"/>: a timer due sooner than that simply fires late.
    ///
    /// This is for the buffer pools, the only thing left ticking once nobody has the page open.
    /// Work is not delayed with it, since anything arriving from another thread (a lookup
    /// answering, a batch finishing a file) comes in as a signal and wakes the loop at once. Only
    /// timers go slow, so this must not be left on while anything the app itself times is running.
    /// </summary>
    public void CoalesceTimersWhile(Func<bool> idle, TimeSpan floor)
    {
        _idle = idle;
        _idleFloor = floor;
    }

    /// <summary>
    /// Takes the loop over. Must run before the platform is set up, and on the thread that will
    /// run the loop.
    ///
    /// Unlike the clock there is no slot left open to fill: the platform binds its own loop while
    /// setting itself up, overwriting whatever it finds. What it cannot undo is a dispatcher that
    /// already exists, since a dispatcher resolves its loop once and keeps it. So this binds and
    /// then immediately asks for the dispatcher, leaving the platform's overwrite to land on
    /// something nobody reads again.
    /// </summary>
    public static QuietDispatcher? TakeOver()
    {
        try
        {
            var avalonia = typeof(Dispatcher).Assembly;
            var controlled = avalonia.GetType("Avalonia.Threading.IControlledDispatcherImpl")
                ?? throw new MissingMemberException("Avalonia.Threading.IControlledDispatcherImpl");
            var impl = avalonia.GetType("Avalonia.Threading.IDispatcherImpl")
                ?? throw new MissingMemberException("Avalonia.Threading.IDispatcherImpl");

            var members = Faces(controlled).SelectMany(face => face.GetMethods()).Select(m => m.Name);
            if (!members.OrderBy(name => name).SequenceEqual(KnownShape.OrderBy(name => name)))
                throw new NotSupportedException("this Avalonia's event loop is not the shape expected");

            var create = typeof(DispatchProxy).GetMethods()
                             .SingleOrDefault(m => m.Name == "Create"
                                                   && m.GetGenericArguments().Length == 2
                                                   && m.GetParameters().Length == 0)
                         ?? throw new MissingMemberException("DispatchProxy", "Create");

            var proxy = create.MakeGenericMethod(controlled, typeof(QuietDispatcher)).Invoke(null, null)
                ?? throw new MissingMemberException("DispatchProxy", "Create");
            ((QuietDispatcher)proxy)._loopThread = Thread.CurrentThread;

            // Nothing above changes state Avalonia can see, so failing until here leaves the
            // platform to set itself up exactly as it would have. Past this point it does not.
            Bind(impl, proxy);
            _ = Dispatcher.UIThread;
            return (QuietDispatcher)proxy;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(
                $"Could not take this Avalonia's event loop over ({e.Message}), so the app will " +
                "use more power than it needs to while idle. Nothing else is affected.");
            return null;
        }
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method?.Name)
        {
            case "get_CurrentThreadIsLoopThread":
                return _loopThread == Thread.CurrentThread;
            case "get_Now":
                return _running.ElapsedMilliseconds;

            // No input arrives this way: the browser's events are posted to the dispatcher by
            // BrowserTransport like any other work, so there is no queue here to ask about.
            case "get_CanQueryPendingInput":
            case "get_HasPendingInput":
                return false;

            case "add_Signaled":
                _onSignal = (Action?)Delegate.Combine(_onSignal, (Delegate?)args?[0]);
                return null;
            case "remove_Signaled":
                _onSignal = (Action?)Delegate.Remove(_onSignal, (Delegate?)args?[0]);
                return null;
            case "add_Timer":
                _onTimer = (Action?)Delegate.Combine(_onTimer, (Delegate?)args?[0]);
                return null;
            case "remove_Timer":
                _onTimer = (Action?)Delegate.Remove(_onTimer, (Delegate?)args?[0]);
                return null;

            case "Signal":
                lock (_lock)
                {
                    _signaled = true;
                    _wakeup.Set();
                }

                return null;

            case "UpdateTimer":
                lock (_lock)
                {
                    var dueInMs = (long?)args?[0];
                    _dueAt = dueInMs is null ? null : TimeSpan.FromMilliseconds(dueInMs.Value);

                    // Set from the loop thread this is a note to self, read on the way round. Set
                    // from anywhere else the loop is asleep on a wait that is now too long.
                    if (_loopThread != Thread.CurrentThread)
                        _wakeup.Set();
                }

                return null;

            case "RunLoop":
                RunLoop(args?[0] is CancellationToken token ? token : CancellationToken.None);
                return null;

            default:
                return null;
        }
    }

    private void RunLoop(CancellationToken until)
    {
        var wake = until.CanBeCanceled ? until.Register(() => _wakeup.Set()) : default;
        try
        {
            while (!until.IsCancellationRequested)
            {
                bool signaled;
                lock (_lock)
                {
                    signaled = _signaled;
                    _signaled = false;
                }

                if (signaled)
                {
                    _onSignal?.Invoke();
                    continue;
                }

                var due = false;
                lock (_lock)
                {
                    if (_dueAt < _running.Elapsed)
                    {
                        due = true;
                        _dueAt = null;
                    }
                }

                if (due)
                {
                    _onTimer?.Invoke();
                    continue;
                }

                TimeSpan? next;
                lock (_lock)
                {
                    next = _dueAt;
                }

                if (next is null)
                {
                    _wakeup.WaitOne();
                    continue;
                }

                // The whole point: where the platform spins out a sub-millisecond remainder, wait
                // a millisecond. A timer can only be late by less than it was rounded to anyway.
                var left = next.Value - _running.Elapsed;
                if (left < TimeSpan.FromMilliseconds(1))
                    left = TimeSpan.FromMilliseconds(1);
                if (left < _idleFloor && Idle())
                    left = _idleFloor;

                _wakeup.WaitOne(left);
            }
        }
        finally
        {
            wake.Dispose();
        }
    }

    /// <summary>
    /// Never throws: an exception here would come out of the event loop and hang the app, which is
    /// more than a power saving is allowed to cost. A broken predicate reads as "not idle".
    /// </summary>
    private bool Idle()
    {
        try
        {
            return _idle?.Invoke() == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The interface and everything it inherits, since an interface lists only its own.</summary>
    private static IEnumerable<Type> Faces(Type face) => [face, ..face.GetInterfaces()];

    private static void Bind(Type service, object instance)
    {
        var locator = typeof(Dispatcher).Assembly.GetType("Avalonia.AvaloniaLocator")
            ?? throw new MissingMemberException("Avalonia.AvaloniaLocator");
        var registry = locator.GetProperty("CurrentMutable", Anywhere)?.GetValue(null)
            ?? throw new MissingMemberException(locator.Name, "CurrentMutable");
        var binding = locator.GetMethod("Bind", Anywhere)?.MakeGenericMethod(service).Invoke(registry, null)
            ?? throw new MissingMemberException(locator.Name, "Bind");

        if (binding.GetType().GetMethod("ToConstant", Anywhere) is not { } toConstant)
            throw new MissingMemberException(binding.GetType().Name, "ToConstant");

        toConstant.MakeGenericMethod(instance.GetType()).Invoke(binding, [instance]);
    }
}
