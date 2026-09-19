using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Moovie.App.Web;

/// <summary>
/// Tells the page whether the app's focus is somewhere the user can type, so a touch device can
/// offer its on-screen keyboard while it is and put it away again when it is not.
///
/// The page cannot work this out for itself: the app's fields reach it as pictures, and a browser
/// will only raise that keyboard for a field of its own. <see cref="ClipboardBridge"/> keeps one
/// hidden, and this is what says when focusing it is worth doing.
///
/// The handlers are class handlers, so at most one of these may exist in a process; only
/// <see cref="WebHost"/> makes one.
/// </summary>
public sealed class SoftKeyboard
{
    private readonly TopLevel _top;

    private bool _wanted;
    private bool _pushQueued;

    public SoftKeyboard(TopLevel top)
    {
        _top = top;

        InputElement.GotFocusEvent.AddClassHandler<InputElement>((_, _) => Push());
        InputElement.LostFocusEvent.AddClassHandler<InputElement>((_, _) => Push());
    }

    /// <summary>Whether a keyboard would have somewhere to type, whenever that changes.</summary>
    public event Action<bool>? Changed;

    /// <summary>
    /// Says it again from scratch, for a tab that has just arrived and holds nothing. Runs on the
    /// UI thread.
    /// </summary>
    public void Announce()
    {
        _wanted = false;
        Push();
    }

    /// <summary>
    /// Queues the answer rather than giving one: focus moves in two events, out of one element and
    /// into the next, and only where it came to rest matters here.
    /// </summary>
    private void Push()
    {
        if (_pushQueued)
            return;

        _pushQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _pushQueued = false;

            var wanted = _top.FocusManager?.GetFocusedElement() is TextBox { IsReadOnly: false };
            if (wanted == _wanted)
                return;

            _wanted = wanted;
            Changed?.Invoke(wanted);
        }, DispatcherPriority.Input);
    }
}
