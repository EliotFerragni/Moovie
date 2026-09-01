using System.Diagnostics;
using System.Reflection;
using Avalonia.Headless;
using Avalonia.Rendering;

namespace Moovie.App.Web;

/// <summary>
/// The clock the app draws by, which this host owns rather than the platform.
///
/// The headless platform's own clock ticks sixty times a second for as long as the app runs,
/// connected or not. The app's event loop busy-waits the last millisecond before any timer rather
/// than sleeping into it, so each tick costs a millisecond and a half: an untouched app with no
/// page open cost a tenth of a core, which on a NAS is a fan that turns all night for nothing.
///
/// This clock ticks only when <see cref="WebHost"/> asks, which it does when the browser sends
/// something and while the picture is still changing. With nobody watching, the app's thread
/// sleeps until a browser knocks.
///
/// Avalonia allows application code no part in this: the interface may not be implemented outside
/// its assembly and the registry is internal, hence the proxy and the reflection. The takeover is
/// refused unless the clock is exactly the shape known here, in which case the app draws on the
/// platform's clock, which works perfectly well and merely costs more.
/// </summary>
// Not sealed: DispatchProxy builds the instance by deriving from this.
public class DrawingClock : DispatchProxy
{
    /// <summary>
    /// The whole of <c>IRenderTimer</c> as this understands it. A clock with anything more to it
    /// than this is one this cannot honestly pretend to be, so the takeover is refused instead.
    /// </summary>
    private static readonly string[] KnownShape = ["add_Tick", "remove_Tick", "get_RunsInBackground"];

    private const BindingFlags Anywhere =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private readonly Stopwatch _running = Stopwatch.StartNew();

    private Action<TimeSpan>? _draw;

    private bool _isTheClock;

    /// <summary>Public because <see cref="DispatchProxy"/> builds these; call <see cref="TakeOver"/>.</summary>
    public DrawingClock()
    {
    }

    /// <summary>
    /// Must run before the platform is set up. Setting it up builds the compositor, and the
    /// compositor takes whichever clock it finds at that moment and keeps it for the whole run.
    /// </summary>
    public static DrawingClock TakeOver()
    {
        try
        {
            if (!typeof(IRenderTimer).GetMethods().Select(method => method.Name).OrderBy(name => name)
                    .SequenceEqual(KnownShape.OrderBy(name => name)))
                throw new NotSupportedException("this Avalonia's drawing clock is not the shape expected");

            var proxy = DispatchProxy.Create<IRenderTimer, DrawingClock>();
            var clock = (DrawingClock)(object)proxy;

            var avalonia = typeof(IRenderTimer).Assembly;
            var loop = Activator.CreateInstance(
                    avalonia.GetType("Avalonia.Rendering.RenderLoop")
                    ?? throw new MissingMemberException("Avalonia.Rendering.RenderLoop"), proxy)
                ?? throw new MissingMemberException("Avalonia.Rendering.RenderLoop");

            // Registering the loop rather than the clock is what makes this hold: the platform
            // overwrites the clock while setting itself up, but it never builds a loop of its own
            // if one is already registered, and a loop keeps the clock it was built with.
            Bind(
                avalonia.GetType("Avalonia.Rendering.IRenderLoop")
                ?? throw new MissingMemberException("Avalonia.Rendering.IRenderLoop"), loop);

            clock._isTheClock = true;
            return clock;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(
                $"Could not take this Avalonia's drawing clock over ({e.Message}), so the app will " +
                "use more processor time than it needs to while idle. Nothing else is affected.");
            return new DrawingClock();
        }
    }

    /// <summary>Draws one frame, now, on the calling thread, which has to be the UI thread.</summary>
    public void Draw()
    {
        if (_isTheClock)
            _draw?.Invoke(_running.Elapsed);
        else
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method?.Name)
        {
            case "add_Tick":
                _draw = (Action<TimeSpan>?)Delegate.Combine(_draw, (Delegate?)args?[0]);
                return null;
            case "remove_Tick":
                _draw = (Action<TimeSpan>?)Delegate.Remove(_draw, (Delegate?)args?[0]);
                return null;
            case "get_RunsInBackground":
                // Frames are drawn wherever Draw was called, which is only ever the UI thread.
                return false;
            default:
                return null;
        }
    }

    private static void Bind(Type service, object instance)
    {
        var locator = typeof(IRenderTimer).Assembly.GetType("Avalonia.AvaloniaLocator")
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
