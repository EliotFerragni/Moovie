using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VideoMetadataFiller.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
