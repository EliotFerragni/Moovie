using Avalonia.Controls;

namespace Moovie.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        View.Completed += saved => Close(saved);
    }
}
