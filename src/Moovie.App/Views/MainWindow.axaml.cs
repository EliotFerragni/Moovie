using Avalonia.Controls;

namespace Moovie.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        View.Shell = new DesktopShell(this);
    }
}
