using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Moovie.App.Views;

public partial class AboutView : UserControl
{
    public AboutView() => InitializeComponent();

    /// <summary>Raised when the user dismisses the box; the host decides what that means.</summary>
    public event Action? Completed;

    private void OnClose(object? sender, RoutedEventArgs e) => Completed?.Invoke();
}
