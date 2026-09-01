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
/// Avalonia ships a transport of this shape (behind the XAML previewer's <c>--method html</c>),
/// but it handles pointer and wheel events only, so a text field can be focused and never typed
/// into: fatal for an app that is mostly text fields. This replacement speaks the same
/// <see cref="IAvaloniaRemoteTransportConnection"/> contract, so
/// <see cref="Avalonia.Controls.Remote.RemoteServer"/> drives it unchanged; only the wire format
/// and the client page differ. It also sends just the changed part of the window, PNG-encoded off
/// the drawing thread, and lets the browser report its own size so the window follows the tab.
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
    /// Holds one slot, so frames never queue up behind a slow encode: a newer one widens the
    /// rectangle still owed to the browser instead of adding to a backlog.
    /// </summary>
    private readonly Channel<bool> _wake =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly List<PixelRect> _owed = [];

    /// <summary>
    /// A socket may not have two sends in flight at once, and frames and clipboard messages come
    /// from different threads, so every send waits its turn here.
    /// </summary>
    private readonly SemaphoreSlim _sending = new(1, 1);

    private bool _drew;

    public BrowserTransport(string host, int port)
    {
        _page = ReadEmbeddedPage();

        // No Origin check, unlike Avalonia's transport, so the address the browser uses need not
        // match the address bound: that is what makes reaching this by LAN hostname work.
        var prefix = $"http://{host}:{port}/";
        _listener.Prefixes.Add(prefix);
        Listen(prefix);

        _ = Task.Run(AcceptLoop);
        _ = Task.Run(SendLoop);
    }

    /// <summary>
    /// Binds the listener. Windows refuses an unreserved address to a non-administrator with a
    /// bare "Access is denied" naming neither the address nor the remedy, so it is spelled out.
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
                 Could not listen on {prefix}: {e.Message}

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
                $"Could not listen on {prefix}: {e.Message}. Another program may already be using that port.", e);
        }
    }

    public event Action<IAvaloniaRemoteTransportConnection, object>? OnMessage;
    public event Action<IAvaloniaRemoteTransportConnection, Exception>? OnException;

    /// <summary>
    /// Raised on the socket thread whenever the browser sends something. The host draws in
    /// response rather than polling: the headless platform only draws when told to.
    /// </summary>
    public event Action? InputReceived;

    /// <summary>
    /// Raised on the socket thread when the browser asks for a clipboard action. The text is the
    /// browser's clipboard for a paste and empty otherwise.
    /// </summary>
    public event Action<ClipboardAction, string>? ClipboardRequested;

    /// <summary>
    /// Raised on the socket thread when a tab arrives. A new tab knows nothing the app has not
    /// told it since, so anything it is owed besides the picture is owed here.
    /// </summary>
    public event Action? ViewerArrived;

    /// <summary>Whether any browser is currently looking, so idle work can be skipped entirely.</summary>
    public bool HasViewers => !_clients.IsEmpty;

    /// <summary>
    /// Whether the app has drawn anything since this was last asked; reading it forgets it. A
    /// frame arrives only when there was something new to show, which makes this the host's
    /// answer to "is anything happening". Drawing and asking both happen on the UI thread.
    /// </summary>
    public bool DrewSinceLastAsked()
    {
        var drew = _drew;
        _drew = false;
        return drew;
    }

    public void Start() =>
        OnMessage?.Invoke(this, new ClientSupportedPixelFormatsMessage
        {
            Formats = [Avalonia.Remote.Protocol.Viewport.PixelFormat.Rgba8888],
        });

    /// <summary>
    /// Sends a small JSON message to every browser looking. Frames go out as binary and
    /// everything else as text, which is all the page needs to tell the two apart.
    /// </summary>
    public void Post(object message)
    {
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, Json));
        _ = Task.Run(async () =>
        {
            try
            {
                await BroadcastAsync(json, WebSocketMessageType.Text).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                OnException?.Invoke(this, e);
            }
        });
    }

    /// <summary>
    /// Takes a rendered frame off the drawing thread: only the comparison against the last frame
    /// happens here, encoding and sending elsewhere. The frame is acknowledged straight away
    /// rather than when the browser has it, because the top level will not draw again until it is
    /// acknowledged: waiting on the round trip would tie the frame rate to the network, and a tab
    /// closed mid-send would never answer at all.
    /// </summary>
    public Task Send(object data)
    {
        if (data is not FrameMessage frame)
        {
            // MeasureViewportMessage and RequestViewportResizeMessage ask the client to resize.
            // The browser is the authority on its own size, so they are dropped.
            return Task.CompletedTask;
        }

        _drew = true;

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

                await BroadcastAsync(message, WebSocketMessageType.Binary).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                OnException?.Invoke(this, e);
            }
        }
    }

    private async Task BroadcastAsync(byte[] message, WebSocketMessageType type)
    {
        await _sending.WaitAsync(_stopping.Token).ConfigureAwait(false);
        try
        {
            foreach (var client in _clients.Keys)
                await SendToAsync(client, message, type).ConfigureAwait(false);
        }
        finally
        {
            _sending.Release();
        }
    }

    private async Task SendToAsync(WebSocket client, byte[] message, WebSocketMessageType type)
    {
        try
        {
            if (client.State == WebSocketState.Open)
                await client.SendAsync(message, type, true, _stopping.Token);
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
        ViewerArrived?.Invoke();

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

            var client = ReadClientMessage(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (client is null)
                continue;

            // A clipboard action is not part of the remote protocol and means nothing to the top
            // level, so it goes to the app rather than through OnMessage.
            if (ClipboardActionOf(client.Type) is { } clipboard)
                ClipboardRequested?.Invoke(clipboard, client.Text ?? string.Empty);
            else if (InputMessage(client) is { } message)
                OnMessage?.Invoke(this, message);
            else
                continue;

            InputReceived?.Invoke();
        }
    }

    private static ClientMessage? ReadClientMessage(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<ClientMessage>(text, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ClipboardAction? ClipboardActionOf(string? type) => type switch
    {
        "copy" => ClipboardAction.Copy,
        "cut" => ClipboardAction.Cut,
        "paste" => ClipboardAction.Paste,
        _ => null,
    };

    private static object? InputMessage(ClientMessage message)
    {
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
    /// Translates a browser key event. <c>KeyboardEvent.code</c> is the W3C physical key name and
    /// <see cref="Avalonia.Input.PhysicalKey"/> matches it but for a letter being <c>KeyA</c>
    /// there and <c>A</c> here, and the keypad being cased <c>NumPad</c>. Without that fixup every
    /// letter key is dropped: typing still works, since that arrives as text, but Ctrl+A does not.
    /// The protocol's enums are distinct types from Avalonia's own but share their numeric values,
    /// which is what makes the casts safe.
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
        public int Buttons { get; set; }
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
        /// <see cref="InputModifiers"/> is declared <c>[Flags]</c> but numbered sequentially, so
        /// it is an array of individual values rather than one OR-ed value. The held mouse buttons
        /// go in here too, and this is the only place the app is told about them: a text box will
        /// not extend its selection unless a pointer move says the left button is down.
        /// </summary>
        public InputModifiers[] ModifierList
        {
            get
            {
                var modifiers = new List<InputModifiers>(7);
                if (Alt) modifiers.Add(InputModifiers.Alt);
                if (Control) modifiers.Add(InputModifiers.Control);
                if (Shift) modifiers.Add(InputModifiers.Shift);
                if (Meta) modifiers.Add(InputModifiers.Windows);

                // The bitmask is numbered unlike the button index: 1 is left, 2 is right and 4 is
                // middle, where Button calls them 0, 2 and 1.
                if ((Buttons & 1) != 0) modifiers.Add(InputModifiers.LeftMouseButton);
                if ((Buttons & 2) != 0) modifiers.Add(InputModifiers.RightMouseButton);
                if ((Buttons & 4) != 0) modifiers.Add(InputModifiers.MiddleMouseButton);

                return modifiers.ToArray();
            }
        }
    }
}
