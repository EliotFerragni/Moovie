using Avalonia.Controls;
using Avalonia.Input;
using Moovie.App.ViewModels;

namespace Moovie.App.Views;

public partial class FileBrowserView : UserControl
{
    public FileBrowserView() => InitializeComponent();

    private FileBrowserViewModel? ViewModel => DataContext as FileBrowserViewModel;

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && ViewModel is { } viewModel)
            viewModel.Selection = list.SelectedItems?.OfType<FileBrowserEntry>().ToList() ?? [];
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: FileBrowserEntry entry } && ViewModel is { } viewModel)
            viewModel.Open(entry);
    }
}
