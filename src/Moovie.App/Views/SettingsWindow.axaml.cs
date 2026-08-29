using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Moovie.App.ViewModels;

namespace Moovie.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    /// <summary>
    /// Hooks a template box up to its editor's insert requests. Only the view knows where the caret
    /// is, so token insertion has to happen here rather than in the view model.
    /// </summary>
    private void OnTemplateBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not RenameTemplateEditorViewModel editor)
            return;

        editor.InsertRequested += token => InsertAtCaret(box, token);
    }

    private static void InsertAtCaret(TextBox box, string token)
    {
        var text = box.Text ?? string.Empty;
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);

        // Replace the selection when there is one, so a chip can overwrite a token being edited.
        var start = box.SelectionStart;
        var end = box.SelectionEnd;
        if (start != end)
        {
            var from = Math.Clamp(Math.Min(start, end), 0, text.Length);
            var to = Math.Clamp(Math.Max(start, end), 0, text.Length);
            box.Text = string.Concat(text.AsSpan(0, from), token, text.AsSpan(to));
            box.CaretIndex = from + token.Length;
        }
        else
        {
            box.Text = string.Concat(text.AsSpan(0, caret), token, text.AsSpan(caret));
            box.CaretIndex = caret + token.Length;
        }

        box.Focus();
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
