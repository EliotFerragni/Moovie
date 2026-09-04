using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Moovie.App.ViewModels;
using Moovie.Core.Writing;

namespace Moovie.App.Views;

public partial class MainView : UserControl
{
    private IAppShell? _shell;

    public MainView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => WireUpViewModel();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    /// <summary>
    /// Set by whoever hosts the view. The view model has no business knowing about windows, so the
    /// pickers and dialogs are handed to it from here once a host is known.
    /// </summary>
    public IAppShell? Shell
    {
        get => _shell;
        set
        {
            _shell = value;
            WireUpViewModel();
        }
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void WireUpViewModel()
    {
        if (ViewModel is not { } viewModel || _shell is not { } shell)
            return;

        viewModel.PickFilesAsync = shell.PickFilesAsync;
        viewModel.PickFolderAsync = shell.PickFolderAsync;
        viewModel.ShowSettingsAsync = shell.ShowSettingsAsync;
        viewModel.ShowAboutAsync = shell.ShowAboutAsync;
    }

    /// <summary>
    /// Closes the scan depth flyout once a depth is clicked. Bound to the pointer rather than to
    /// SelectionChanged, which also fires as the flyout opens and would shut it again at once.
    /// </summary>
    private void OnScanDepthPicked(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
            ScanDepthButton.Flyout?.Hide();
    }

    /// <summary>
    /// Accepts a drop only when it carries files, so the cursor tells the truth before the drop.
    /// </summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer?.Contains(DataFormat.File) == true;
        e.DragEffects = hasFiles && ViewModel?.CanEditList == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (ViewModel is not { CanEditList: true } viewModel)
            return;

        var items = e.DataTransfer?.TryGetFiles();
        if (items is null)
            return;

        var paths = items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0)
            return;

        // Folders are expanded by the view model; only obvious non-media single files are dropped
        // here, so a stray text file in a dragged selection is quietly ignored.
        viewModel.AddPaths(paths.Where(p => Directory.Exists(p) || Mp4TagWriter.IsSupported(p)));
    }

    /// <summary>
    /// Loads a row's poster as the row is realised, so a list of several hundred files fetches
    /// only what somebody scrolls to. Virtualisation fires this again when a row comes back; the
    /// view model makes the repeat call cheap rather than the view tracking what it has asked for.
    /// </summary>
    private void OnFileRowPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container.DataContext is not FileItemViewModel file)
            return;

        file.IsOnScreen = true;
        if (ViewModel is { } viewModel)
            _ = viewModel.EnsureThumbnailAsync(file);
    }

    /// <summary>
    /// The other half of <see cref="OnFileRowPrepared"/>: a row whose container has gone back to
    /// the pool can be skipped by work that only makes sense for what is on screen.
    /// </summary>
    private void OnFileRowCleared(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container.DataContext is FileItemViewModel file)
            file.IsOnScreen = false;
    }

    /// <summary>
    /// Keeps the view model's selection in step. Bound in code rather than XAML because
    /// <see cref="ListBox.SelectedItems"/> is an untyped list that is awkward to bind reliably.
    /// </summary>
    private void OnFileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || ViewModel is not { } viewModel)
            return;

        var selection = list.SelectedItems?.OfType<FileItemViewModel>().ToList() ?? [];
        viewModel.SetSelection(selection);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        (DataContext as MainWindowViewModel)?.Dispose();
    }
}
