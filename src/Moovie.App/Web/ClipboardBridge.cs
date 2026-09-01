using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Moovie.App.Web;

/// <summary>What the browser has asked the app to do with the text under the cursor.</summary>
public enum ClipboardAction
{
    Copy,
    Cut,
    Paste,
}

/// <summary>
/// Copy, cut and paste for <c>--web</c>.
///
/// Avalonia's text boxes work through <see cref="TopLevel.Clipboard"/>, which is null behind
/// <see cref="Avalonia.Controls.Remote.RemoteServer"/>, so every copy, cut and paste in the app
/// silently does nothing. The clipboard that matters is the browser's anyway, and the app cannot
/// write to it: script may only do that from inside a gesture the browser has just handled, or
/// over a secure connection. So the page keeps the app's selected text ready in a hidden field and
/// lets the browser's own Ctrl+C and Ctrl+V act on that, which asks no permission and works over
/// plain HTTP on a LAN. This is the app's half: it answers the three events Avalonia raises before
/// it would touch the clipboard, and remembers the last text copied so the app can paste it back.
///
/// The handlers are class handlers, so at most one of these may exist in a process; only
/// <see cref="WebHost"/> makes one.
/// </summary>
public sealed class ClipboardBridge
{
    private readonly TopLevel _top;

    /// <summary>
    /// The last text copied here or pasted into here, which is the whole of what the app itself
    /// can paste: what the browser holds is only ever known when the browser says so.
    /// </summary>
    private string _text = string.Empty;

    private string _selection = string.Empty;
    private bool _pushQueued;

    public ClipboardBridge(TopLevel top)
    {
        _top = top;

        // Avalonia raises each of these before reaching for the clipboard and drops its own
        // handling when one comes back handled. The context menu's items call the very methods
        // that raise them, so answering the events serves the menu and the keyboard at once.
        TextBox.CopyingToClipboardEvent.AddClassHandler<TextBox>((box, e) =>
        {
            e.Handled = true;
            if (Copyable(box) is { Length: > 0 } text)
                Remember(text);
        });

        TextBox.CuttingToClipboardEvent.AddClassHandler<TextBox>((box, e) =>
        {
            e.Handled = true;
            if (Copyable(box) is not { Length: > 0 } text)
                return;

            Remember(text);

            // Handling the event also stops the box deleting the selection itself. The setter
            // does it instead, honouring read-only and leaving the deletion on the undo stack.
            box.SelectedText = string.Empty;
        });

        TextBox.PastingFromClipboardEvent.AddClassHandler<TextBox>((box, e) =>
        {
            e.Handled = true;
            if (_text.Length == 0)
                return;

            // As text input rather than through the text property: that is how the selection gets
            // replaced, and how the box's own read-only and length limits still apply.
            box.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Text = _text,
            });
        });

        // The page has to hold the selection before the user reaches for Ctrl+C, since answering
        // the keypress would already be too late, so every change is pushed as it happens.
        TextBox.SelectionStartProperty.Changed.AddClassHandler<TextBox>((_, _) => PushSelection());
        TextBox.SelectionEndProperty.Changed.AddClassHandler<TextBox>((_, _) => PushSelection());
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>((_, _) => PushSelection());
        InputElement.GotFocusEvent.AddClassHandler<InputElement>((_, _) => PushSelection());
        InputElement.LostFocusEvent.AddClassHandler<InputElement>((_, _) => PushSelection());
    }

    /// <summary>The app's selected text, whenever it changes, for the page to keep ready to copy.</summary>
    public event Action<string>? SelectionChanged;

    /// <summary>Text the app has just copied, for the page to put on the clipboard if it is allowed to.</summary>
    public event Action<string>? Copied;

    /// <summary>
    /// Tells the page the selection again from scratch, for a tab that has just arrived and holds
    /// nothing. Runs on the UI thread.
    /// </summary>
    public void Announce()
    {
        _selection = string.Empty;
        PushSelection();
    }

    /// <summary>Carries out what the browser asked for. Runs on the UI thread.</summary>
    public void Apply(ClipboardAction action, string text)
    {
        // Remembered even with nothing focused to paste into, since the app's own menu may be
        // reached for next and this is the only word it gets on what the browser holds.
        if (action == ClipboardAction.Paste)
            _text = text;

        if (_top.FocusManager?.GetFocusedElement() is not TextBox box)
            return;

        switch (action)
        {
            case ClipboardAction.Copy:
                box.Copy();
                break;
            case ClipboardAction.Cut:
                box.Cut();
                break;
            case ClipboardAction.Paste:
                box.Paste();
                break;
        }
    }

    /// <summary>
    /// What a box is willing to give up. Avalonia refuses to copy out of a masked box, and so does
    /// this, all the more so: the app's one masked box holds the user's TMDB key, and the page it
    /// would be handed to is served over plain HTTP.
    /// </summary>
    private static string Copyable(TextBox? box) =>
        box is not null && box.PasswordChar == default ? box.SelectedText : string.Empty;

    private void Remember(string text)
    {
        _text = text;
        Copied?.Invoke(text);
    }

    /// <summary>
    /// Queues a push rather than making one: a single edit moves the selection more than once
    /// (replacing text selects it, then collapses the caret) and the page only wants where it
    /// ended up. The dispatcher still runs down long before the user's next keypress.
    /// </summary>
    private void PushSelection()
    {
        if (_pushQueued)
            return;

        _pushQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _pushQueued = false;

            var text = Copyable(_top.FocusManager?.GetFocusedElement() as TextBox);
            if (text == _selection)
                return;

            _selection = text;
            SelectionChanged?.Invoke(text);
        }, DispatcherPriority.Input);
    }
}
