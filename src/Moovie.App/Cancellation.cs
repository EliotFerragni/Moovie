namespace Moovie.App;

/// <summary>Work that is superseded by the next request rather than queued behind it.</summary>
internal static class Cancellation
{
    /// <summary>
    /// Calls off whatever <paramref name="source"/> was covering, disposes it, and hands back a
    /// token for its replacement.
    /// </summary>
    public static CancellationToken Restart(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = new CancellationTokenSource();
        return source.Token;
    }
}
