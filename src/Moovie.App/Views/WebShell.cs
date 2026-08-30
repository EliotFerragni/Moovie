using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Moovie.App.ViewModels;
using Moovie.Core.Localization;

namespace Moovie.App.Views;

/// <summary>
/// Answers <see cref="IAppShell"/> for <c>--web</c>, where neither of the two things a desktop
/// host takes for granted exists.
///
/// There is no second window, so a dialog is shown over the view instead; and there is no
/// platform picker, so files are chosen with <see cref="FileBrowserView"/>, which lists this
/// machine's disks. The browser's own file dialog would have been the wrong answer twice over:
/// it would offer the wrong machine's files, and it hands back a stream rather than a path,
/// which is not something the tag writer or the rename engine could use.
/// </summary>
public sealed class WebShell(Visual root) : IAppShell
{
    private const double Inset = 40;

    public async Task<IReadOnlyList<string>> PickFilesAsync(string? startIn) =>
        await BrowseAsync(foldersOnly: false, Strings.Get("picker.addFiles"), startIn) ?? [];

    public async Task<string?> PickFolderAsync(string? startIn) =>
        (await BrowseAsync(foldersOnly: true, Strings.Get("picker.addFolder"), startIn))?.FirstOrDefault();

    public Task<bool> ShowSettingsAsync(SettingsViewModel editor)
    {
        var view = new SettingsView { DataContext = editor, Width = 840, Height = 780 };
        var completion = new TaskCompletionSource<bool>();
        view.Completed += saved => completion.TrySetResult(saved);
        return ShowAsync(view, completion.Task);
    }

    public Task ShowAboutAsync(AboutViewModel about)
    {
        var view = new AboutView { DataContext = about };
        var completion = new TaskCompletionSource<bool>();
        view.Completed += () => completion.TrySetResult(true);
        return ShowAsync(view, completion.Task);
    }

    private Task<IReadOnlyList<string>?> BrowseAsync(bool foldersOnly, string title, string? startIn)
    {
        var viewModel = new FileBrowserViewModel(foldersOnly, title, startIn);
        var view = new FileBrowserView { DataContext = viewModel };
        var completion = new TaskCompletionSource<IReadOnlyList<string>?>();
        viewModel.Completed += paths => completion.TrySetResult(paths);
        return ShowAsync(view, completion.Task);
    }

    /// <summary>
    /// Covers the view with <paramref name="content"/> until <paramref name="completion"/> finishes.
    /// </summary>
    private async Task<T> ShowAsync<T>(Control content, Task<T> completion)
    {
        var layer = OverlayLayer.GetOverlayLayer(root)
            ?? throw new InvalidOperationException("The view has no top level to open a dialog over.");

        // The shade both dims the view and swallows clicks meant for it, which is the part of
        // being modal that matters here.
        var shade = new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.5),
            Child = new ContentControl
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        // The overlay layer is a canvas, so it stretches nothing: the shade has to be told the
        // size, and told again whenever the browser window changes.
        void Fit()
        {
            shade.Width = layer.Bounds.Width;
            shade.Height = layer.Bounds.Height;
            content.MaxWidth = Math.Max(0, layer.Bounds.Width - Inset);
            content.MaxHeight = Math.Max(0, layer.Bounds.Height - Inset);
        }

        void OnLayerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.BoundsProperty)
                Fit();
        }

        Fit();
        layer.PropertyChanged += OnLayerChanged;
        layer.Children.Add(shade);
        content.Focus();

        try
        {
            return await completion;
        }
        finally
        {
            layer.PropertyChanged -= OnLayerChanged;
            layer.Children.Remove(shade);
        }
    }
}
