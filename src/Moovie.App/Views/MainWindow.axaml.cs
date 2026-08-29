using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Moovie.App.ViewModels;
using Moovie.Core.Writing;

namespace Moovie.App.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType VideoFiles = new("Video files")
    {
        Patterns = ["*.mp4", "*.m4v", "*.MP4", "*.M4V"],
        AppleUniformTypeIdentifiers = ["public.mpeg-4"],
        MimeTypes = ["video/mp4"],
    };

    public MainWindow()
    {
        InitializeComponent();

        // The platform pickers and the settings dialog need a window, which the view model has no
        // business knowing about, so they are handed in from here.
        DataContextChanged += (_, _) => WireUpViewModel();
        WireUpViewModel();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void WireUpViewModel()
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.PickFilesAsync = PickFilesAsync;
        viewModel.PickFolderAsync = PickFolderAsync;
        viewModel.ShowSettingsAsync = ShowSettingsAsync;
        viewModel.ShowAboutAsync = ShowAboutAsync;
    }

    private async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add video files",
            AllowMultiple = true,
            FileTypeFilter = [VideoFiles],
        });

        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a folder of videos",
            AllowMultiple = false,
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private async Task<bool> ShowSettingsAsync(SettingsViewModel editor)
    {
        var dialog = new SettingsWindow { DataContext = editor };
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task ShowAboutAsync(AboutViewModel about)
    {
        var dialog = new AboutWindow { DataContext = about };
        await dialog.ShowDialog(this);
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

    private async void OnDrop(object? sender, DragEventArgs e)
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
    /// only what somebody scrolls to. The list virtualises, so this fires again when a row comes
    /// back; the view model makes the repeat call cheap rather than the view tracking what it
    /// has already asked for.
    /// </summary>
    private void OnFileRowPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container.DataContext is FileItemViewModel file && ViewModel is { } viewModel)
            _ = viewModel.EnsureThumbnailAsync(file);
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
