using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Input;
using Avalonia.Remote.Protocol;
using Avalonia.Remote.Protocol.Input;
using Avalonia.Remote.Protocol.Viewport;
using Key = Avalonia.Remote.Protocol.Input.Key;
using MouseButton = Avalonia.Remote.Protocol.Input.MouseButton;
using PhysicalKey = Avalonia.Remote.Protocol.Input.PhysicalKey;

namespace Moovie.App.Web;

/// <summary>
/// Serves the app's rendered frames to a browser and feeds the browser's input back in.
///
/// Avalonia already ships a transport of this shape — the one behind the XAML previewer's
/// <c>--method html</c> — but its client sends pointer and wheel events only, and its parser
/// drops anything else, so a text field can be focused and never typed into. That is fatal for
/// this app, which is mostly text fields, hence this replacement. It speaks the same
/// <see cref="IAvaloniaRemoteTransportConnection"/> contract, so
/// <see cref="Avalonia.Controls.Remote.RemoteServer"/> drives it unchanged; only the wire format
/// and the client page differ.
///
/// Three other things the stock transport gets wrong for this use are fixed here: only the part
/// of the window that changed is sent, and PNG-encoded rather than raw; the encoding happens off
/// the thread that draws, so a slow frame cannot stall the interface; and the browser reports its
/// own size, so the window follows the tab instead of being fixed at startup.
/// </summary>
public sealed class BrowserTransport : IAvaloniaRemoteTransportConnection
{
    /// <summary>Frame width, frame height and patch count, as little-endian ints.</summary>
    private const int HeaderBytes = 12;


    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<WebSocket, byte> _clients = new();
    private readonly FrameDiffer _differ = new();
    private readonly Lock _frameLock = new();
    private readonly string _page;
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// Holds one slot. Frames are not queued up behind a slow encode: a newer one simply widens
    /// the rectangle still owed to the browser, so falling behind costs detail in one patch rather
    /// than a backlog of them, and nothing is ever lost.
    /// </summary>
    private readonly Channel<bool> _wake =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly List<PixelRect> _owed = [];

    public BrowserTransport(string host, int port)
    {
        _page = ReadEmbeddedPage();

        // "+" binds every interface. Unlike Avalonia's transport there is no Origin check here, so
        // the address the browser uses need not match the address bound — which is what makes
        // reaching this over a LAN by hostname work at all.
        var prefix = $"http://{host}:{port}/";
        _listener.Prefixes.Add(prefix);
        Listen(prefix);

        _ = Task.Run(AcceptLoop);
        _ = Task.Run(SendLoop);
    }

    /// <summary>
    /// Windows will not let a program that is not an administrator listen on an address unless
    /// that address has been reserved for it, and says so with a bare "Access is denied" that
    /// names neither the address nor the remedy. Every other platform simply binds.
    /// </summary>
    private void Listen(string prefix)
    {
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException e) when (OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                $"""
                 Could not listen on {prefix} — {e.Message}

                 Windows requires an address to be reserved before a program running as an
                 ordinary user may listen on it. Either start this from an administrator prompt,
                 or make the reservation once, from an administrator prompt, and it will work as
                 an ordinary user from then on:

                     netsh http add urlacl url={prefix} user=Everyone
                 """, e);
        }
        catch (HttpListenerException e)
        {
            throw new InvalidOperationException(
                $"Could not listen on {prefix} — {e.Message}. Another program may already be using that port.", e);
        }
    }

    public event Action<IAvaloniaRemoteTransportConnection, object>? OnMessage;
    public event Action<IAvaloniaRemoteTransportConnection, Exception>? OnException;

    /// <summary>
    /// Raised on the socket thread whenever the browser sends something. The host draws in
    /// response to this rather than polling for it: the headless platform only draws when told
    /// to, and being told the moment input lands is both quicker and far cheaper than asking
    /// a hundred times a second whether anything has happened.
    /// </summary>
    public event Action? InputReceived;

    /// <summary>Whether any browser is currently looking, so idle work can be skipped entirely.</summary>
    public bool HasViewers => !_clients.IsEmpty;

    public void Start() =>
        OnMessage?.Invoke(this, new ClientSupportedPixelFormatsMessage
        {
            Formats = [Avalonia.Remote.Protocol.Viewport.PixelFormat.Rgba8888],
        });

    /// <summary>
    /// Takes a rendered frame off the drawing thread as quickly as possible. Only the comparison
    /// against the last frame happens here; encoding and sending are somebody else's problem.
    ///
    /// The frame is acknowledged straight away rather than when the browser has it. The top level
    /// will not draw again until the frame it just produced is acknowledged, so waiting on a
    /// round trip would tie the app's frame rate to the network — and a tab closed between a send
    /// and a draw would never answer at all, stopping the app from ever redrawing again.
    /// </summary>
    public Task Send(object data)
    {
        if (data is not FrameMessage frame)
        {
            // MeasureViewportMessage and RequestViewportResizeMessage are the top level asking the
            // client to resize it. The browser is the authority on its own size, so they are dropped.
            return Task.CompletedTask;
        }

        var any = false;
        try
        {
            lock (_frameLock)
            {
                var wasWidth = _differ.Width;
                var wasHeight = _differ.Height;
                var damaged = _differ.Absorb(frame.Data, frame.Width, frame.Height, frame.Stride);

                // A rectangle measured against the old size cannot be cut out of the new one, and
                // asking for it would read past the end of the frame.
                if (_differ.Width != wasWidth || _differ.Height != wasHeight)
                    _owed.Clear();

                foreach (var damage in damaged)
                    Owe(damage);

                any = _owed.Count > 0;
            }

            if (any)
                _wake.Writer.TryWrite(true);
        }
        catch (Exception e)
        {
            OnException?.Invoke(this, e);
        }
        finally
        {
            OnMessage?.Invoke(this, new FrameReceivedMessage { SequenceId = frame.SequenceId });
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _stopping.Cancel();
        foreach (var client in _clients.Keys)
            client.Dispose();
        _listener.Close();
    }

    /// <summary>
    /// Adds a rectangle to what the browser is owed, merging it into any it already overlaps so
    /// the same pixels are never encoded twice in one message. Must be called under the lock.
    /// </summary>
    private void Owe(PixelRect rect)
    {
        for (var i = _owed.Count - 1; i >= 0; i--)
        {
            if (!_owed[i].Intersects(rect))
                continue;

            rect = rect.Union(_owed[i]);
            _owed.RemoveAt(i);
        }

        _owed.Add(rect);
    }

    private static string ReadEmbeddedPage()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Moovie.App.Web.WebClientPage.html")
            ?? throw new InvalidOperationException("The web client page is missing from the build.");
        return new StreamReader(stream).ReadToEnd();
    }

    private async Task SendLoop()
    {
        while (await _wake.Reader.WaitToReadAsync(_stopping.Token).ConfigureAwait(false))
        {
            while (_wake.Reader.TryRead(out _))
            {
            }

            List<(PixelRect Rect, byte[] Pixels)> patches;
            int width, height;

            lock (_frameLock)
            {
                if (_owed.Count == 0 || _clients.IsEmpty)
                {
                    // Nobody is watching. What is owed stays owed, and the first tab to arrive is
                    // sent the whole window anyway.
                    continue;
                }

                patches = _owed.Select(rect => (rect, _differ.Crop(rect))).ToList();
                _owed.Clear();
                width = _differ.Width;
                height = _differ.Height;
            }

            try
            {
                var encoded = patches
                    .Select(p => (p.Rect, Png: PngWriter.Encode(p.Pixels, p.Rect.Width, p.Rect.Height)))
                    .ToList();

                var message = new byte[HeaderBytes + encoded.Sum(p => 12 + p.Png.Length)];
                BitConverter.GetBytes(width).CopyTo(message, 0);
                BitConverter.GetBytes(height).CopyTo(message, 4);
                BitConverter.GetBytes(encoded.Count).CopyTo(message, 8);

                var at = HeaderBytes;
                foreach (var (rect, png) in encoded)
                {
                    BitConverter.GetBytes(rect.X).CopyTo(message, at);
                    BitConverter.GetBytes(rect.Y).CopyTo(message, at + 4);
                    BitConverter.GetBytes(png.Length).CopyTo(message, at + 8);
                    png.CopyTo(message, at + 12);
                    at += 12 + png.Length;
                }

                foreach (var client in _clients.Keys)
                    await SendToAsync(client, message).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                OnException?.Invoke(this, e);
            }
        }
    }

    private async Task SendToAsync(WebSocket client, byte[] message)
    {
        try
        {
            if (client.State == WebSocketState.Open)
                await client.SendAsync(message, WebSocketMessageType.Binary, true, _stopping.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            _clients.TryRemove(client, out _);
        }
    }

    private async Task AcceptLoop()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            if (context.Request.IsWebSocketRequest)
                _ = Task.Run(() => ServeSocketAsync(context));
            else
                ServePage(context);
        }
    }

    private void ServePage(HttpListenerContext context)
    {
        var body = Encoding.UTF8.GetBytes(_page);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        context.Response.OutputStream.Write(body);
        context.Response.Close();
    }

    private async Task ServeSocketAsync(HttpListenerContext context)
    {
        WebSocket socket;
        try
        {
            socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
        }
        catch (Exception e)
        {
            OnException?.Invoke(this, e);
            return;
        }

        _clients[socket] = 0;

        // A tab that opens after the last redraw holds no picture to patch, so it is owed the
        // whole window rather than the next small change to it.
        lock (_frameLock)
        {
            if (_differ.Width > 0)
            {
                _owed.Clear();
                _owed.Add(_differ.Whole);
            }
        }

        _wake.Writer.TryWrite(true);

        try
        {
            await ReceiveLoopAsync(socket);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
            // A browser tab closing is an ordinary end to a connection, not a fault.
        }
        catch (Exception e)
        {
            OnException?.Invoke(this, e);
        }
        finally
        {
            _clients.TryRemove(socket, out _);
            socket.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(WebSocket socket)
    {
        var buffer = new byte[16 * 1024];
        while (socket.State == WebSocketState.Open && !_stopping.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, _stopping.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                return;

            var message = ParseClientMessage(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (message is null)
                continue;

            OnMessage?.Invoke(this, message);
            InputReceived?.Invoke();
        }
    }

    private static object? ParseClientMessage(string text)
    {
        ClientMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ClientMessage>(text, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (message is null)
            return null;

        return message.Type switch
        {
            "size" => new ClientViewportAllocatedMessage
            {
                Width = message.Width,
                Height = message.Height,
                DpiX = message.Dpi,
                DpiY = message.Dpi,
            },
            "pointer-moved" => new PointerMovedEventMessage
            {
                Modifiers = message.ModifierList, X = message.X, Y = message.Y,
            },
            "pointer-pressed" => new PointerPressedEventMessage
            {
                Modifiers = message.ModifierList, X = message.X, Y = message.Y, Button = message.MouseButton,
            },
            "pointer-released" => new PointerReleasedEventMessage
            {
                Modifiers = message.ModifierList, X = message.X, Y = message.Y, Button = message.MouseButton,
            },
            "scroll" => new ScrollEventMessage
            {
                Modifiers = message.ModifierList, X = message.X, Y = message.Y,
                DeltaX = message.DeltaX, DeltaY = message.DeltaY,
            },
            "key" => KeyMessage(message),
            "text" => new TextInputEventMessage { Text = message.Text ?? string.Empty },
            _ => null,
        };
    }

    /// <summary>
    /// The browser's <c>KeyboardEvent.code</c> is the W3C physical key name and Avalonia names
    /// <see cref="Avalonia.Input.PhysicalKey"/> the same way, with two exceptions: a letter is
    /// <c>KeyA</c> there and plain <c>A</c> here, and the keypad is cased <c>NumPad</c> rather
    /// than <c>Numpad</c>. Both have to be dealt with, or every letter key is silently dropped —
    /// typing still works, because that arrives as text, but Ctrl+A and friends never fire.
    ///
    /// The logical key then comes from Avalonia's own QWERTY lookup. The protocol's enums are
    /// distinct types from Avalonia's own but share their numeric values, which is why the remote
    /// top level casts between them and why these casts are safe.
    /// </summary>
    private static object? KeyMessage(ClientMessage message)
    {
        var code = message.Code ?? string.Empty;
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal))
            code = code[3..];

        if (!Enum.TryParse<Avalonia.Input.PhysicalKey>(code, ignoreCase: true, out var physical))
            return null;

        return new KeyEventMessage
        {
            IsDown = message.Down,
            Key = (Key)physical.ToQwertyKey(),
            PhysicalKey = (PhysicalKey)physical,
            KeySymbol = message.Key,
            Modifiers = message.ModifierList,
        };
    }

    private sealed class ClientMessage
    {
        public string? Type { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double DeltaX { get; set; }
        public double DeltaY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Dpi { get; set; } = 96;
        public int Button { get; set; }
        public bool Down { get; set; }
        public string? Code { get; set; }
        public string? Key { get; set; }
        public string? Text { get; set; }
        public bool Alt { get; set; }
        public bool Control { get; set; }
        public bool Shift { get; set; }
        public bool Meta { get; set; }

        public MouseButton MouseButton => Button switch
        {
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            _ => MouseButton.Left,
        };

        /// <summary>
        /// <see cref="InputModifiers"/> is declared <c>[Flags]</c> but numbered sequentially, so it
        /// is an array of individual values rather than one OR-ed value.
        /// </summary>
        public InputModifiers[] ModifierList
        {
            get
            {
                var modifiers = new List<InputModifiers>(4);
                if (Alt) modifiers.Add(InputModifiers.Alt);
                if (Control) modifiers.Add(InputModifiers.Control);
                if (Shift) modifiers.Add(InputModifiers.Shift);
                if (Meta) modifiers.Add(InputModifiers.Windows);
                return modifiers.ToArray();
            }
        }
    }
}
