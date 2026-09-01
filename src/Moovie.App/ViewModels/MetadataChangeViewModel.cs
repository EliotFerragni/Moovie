using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moovie.Core.Localization;
using Moovie.Core.Model;
using Moovie.Core.Writing;

namespace Moovie.App.ViewModels;

/// <summary>
/// One row of the "what will be written" list: a field, the value already in the file, and the
/// value the lookup returned, either of which can be clicked to become the one that gets written.
/// Both sides always show something, so a row never looks like a rendering fault; the cover art
/// row is the same shape with images in place of the text.
/// </summary>
public sealed partial class MetadataChangeViewModel : ObservableObject
{
    private readonly MetadataChange _change;
    private readonly Action<MetadataChange, bool> _adopt;

    /// <summary>The file's own cover, decoded from the bytes read out of the container.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentImage))]
    private Bitmap? _currentImage;

    /// <summary>The cover the lookup returned, fetched at thumbnail size.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFetchedImage))]
    private Bitmap? _fetchedImage;

    /// <param name="adopt">
    /// Called with this row's change and which side was clicked: true for the file's own value,
    /// false for the lookup's.
    /// </param>
    public MetadataChangeViewModel(MetadataChange change, Action<MetadataChange, bool> adopt)
    {
        _change = change;
        _adopt = adopt;
    }

    public string Key => _change.Key;

    public string Label => _change.Label;

    public string Current => _change.Current ?? Strings.Get("diff.empty");

    public string Fetched => _change.HasFetched
        ? _change.Fetched ?? Strings.Get("diff.empty")
        : Strings.Get("diff.noTmdbValue");

    /// <summary>Struck through when applying will empty this field.</summary>
    public bool IsCleared => _change.Kind == ChangeKind.Cleared;

    /// <summary>The file holds nothing here, so its side is a placeholder rather than a value.</summary>
    public bool IsAdded => _change.Kind == ChangeKind.Added;

    /// <summary>Whether applying changes this field, as opposed to the row being here to be put back.</summary>
    public bool IsChange => _change.IsChange;

    public bool IsCurrentChosen => _change.PendingMatchesCurrent;

    public bool IsFetchedChosen => _change.PendingMatchesFetched;

    public bool CanUseFile => _change.CanAdoptCurrent && !IsCurrentChosen;

    public bool CanUseTmdb => _change.HasFetched && !IsFetchedChosen;

    /// <summary>
    /// A side that is neither chosen nor choosable, dimmed so a click that would do nothing does
    /// not look available. The HD flag's file side is the only one, since several resolutions
    /// share a stored flag.
    /// </summary>
    public bool IsCurrentUnavailable => !_change.CanAdoptCurrent && !IsCurrentChosen;

    public bool IsFetchedUnavailable => !_change.HasFetched;

    public bool HasCurrentImage => CurrentImage is not null;

    public bool HasFetchedImage => FetchedImage is not null;

    /// <summary>The cover art row draws images; every other row draws text.</summary>
    public bool IsArtwork => _change.Key == nameof(MediaMetadata.ArtworkPath);

    public bool IsText => !IsArtwork;

    /// <summary>
    /// What will be written, spelled out only when it is neither of the two offered values, which
    /// means the field was typed into by hand.
    /// </summary>
    public string? WillWrite => IsCurrentChosen || IsFetchedChosen
        ? null
        : Strings.Format("diff.willWrite", _change.Pending ?? Strings.Get("diff.cleared"));

    public bool HasWillWrite => WillWrite is not null;

    public string UseFileTip => Strings.Get("diff.useFile");

    public string UseTmdbTip => Strings.Get("diff.useTmdb");

    [RelayCommand]
    private void UseFile() => _adopt(_change, true);

    [RelayCommand]
    private void UseTmdb() => _adopt(_change, false);
}
