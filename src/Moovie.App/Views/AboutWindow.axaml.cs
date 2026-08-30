using Avalonia.Controls;

namespace Moovie.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        View.Completed += Close;
    }
}
