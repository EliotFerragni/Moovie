using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Moovie.Core.Localization;
using Moovie.Core.Model;
using Moovie.Core.Writing;

namespace Moovie.App.ViewModels;

/// <summary>One row in the file list, and the single source of truth for that file's metadata.</summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    /// <summary>
    /// Fields the user has typed into. A refetch in another language replaces everything except
    /// these, so hand corrections are never silently thrown away.
    /// </summary>
    private readonly HashSet<string> _userEditedFields = new(StringComparer.Ordinal);

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(StatusGeometry), nameof(StatusDescription), nameof(Subtitle),
        nameof(IsStatusOk), nameof(IsStatusWarning), nameof(IsStatusError),
        nameof(IsStatusInfo), nameof(IsStatusIdle))]
    private FileStatus _status = FileStatus.Pending;

    /// <summary>Why this file needs attention, or what went wrong.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private MediaMetadata _metadata = new();

    /// <summary>Alternatives to offer when the match is not certain.</summary>
    [ObservableProperty]
    private List<Candidate> _candidates = [];

    /// <summary>Language for this file alone, overriding the global setting when set.</summary>
    [ObservableProperty]
    private string? _languageOverride;

    /// <summary>
    /// The chosen artwork as shown in the list, loaded once the row is on screen. Set it through
    /// <see cref="ShowThumbnail"/> rather than directly, so the one it replaces is released.
    /// </summary>
    [ObservableProperty]
    private Bitmap? _thumbnail;

    /// <summary>
    /// Whether <see cref="Thumbnail"/> is this row's own bitmap or one borrowed from the shared
    /// artwork cache, which other rows and the preview pane may be showing at the same time.
    /// </summary>
    private bool _ownsThumbnail;

    /// <summary>
    /// Which artwork the thumbnail was loaded for, so a file that matches something else
    /// reloads and one that has not changed does not.
    /// </summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Whether the list is currently showing this row, set by the view as it realises and releases
    /// containers. Work triggered by something other than scrolling asks this before reading a
    /// file's cover, which costs a second open and a decode across a library of tens of thousands.
    /// </summary>
    public bool IsOnScreen { get; set; }

    /// <summary>
    /// Set when the row has been taken off the list, so work already in flight for it can drop it
    /// rather than finish into a row nobody will ever see again.
    /// </summary>
    public bool IsRemoved { get; set; }

    /// <summary>
    /// Shows a bitmap in this row, releasing the one it replaces if this row owned it. Ownership
    /// has to travel with the bitmap: a cover decoded out of the file belongs to this row alone
    /// and leaks an unmanaged Skia surface if it is dropped undisposed, while artwork from the
    /// shared cache is very likely on screen elsewhere and disposing it takes that drawing down.
    /// </summary>
    public void ShowThumbnail(Bitmap? bitmap, bool owned)
    {
        var replaced = _ownsThumbnail ? Thumbnail : null;

        _ownsThumbnail = owned;
        Thumbnail = bitmap;

        if (replaced is null || ReferenceEquals(replaced, bitmap))
            return;

        // Not disposed here: a frame composed before this call may still hold the old bitmap, and
        // freeing a Skia surface under a draw in progress takes the window down. Background sits
        // below render, so by the time this runs that frame is done.
        Dispatcher.UIThread.Post(replaced.Dispose, DispatcherPriority.Background);
    }

    /// <summary>
    /// Lets go of the row's artwork, for a row leaving the list. The cache's images are left
    /// alone; only a cover decoded for this row is freed.
    /// </summary>
    public void ReleaseThumbnail()
    {
        ShowThumbnail(null, owned: false);
        ThumbnailPath = null;
    }

    /// <summary>
    /// The name Apply will give this file, or null when nothing is planned: renaming is off, or
    /// the file is already called that. Written by the view model that owns the templates.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRenamePreview))]
    private string? _renamePreview;

    /// <summary>
    /// A name typed over the preview in the list. It wins over whatever the template renders and
    /// survives a refetch, so a file named by hand stays named by hand until it is applied or the
    /// edit is dropped. Null when this file follows its template.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenameOverridden))]
    private string? _renameOverride;

    public FileItemViewModel(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        Folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
    }

    /// <summary>Current full path. Changes when the file is renamed.</summary>
    public string Path { get; private set; }

    public string FileName { get; private set; }

    public string Folder { get; private set; }

    /// <summary>What the filename parser worked out, kept so a rescan can reuse it.</summary>
    public ParsedName? Parsed { get; set; }

    /// <summary>
    /// The tags the file itself carries, read when it was added. The form starts from these, so an
    /// already tagged file shows its own content rather than a blank sheet.
    /// </summary>
    public ExistingTags? FileTags { get; set; }

    /// <summary>Whether the description below came out of the file rather than out of its name.</summary>
    private bool DescribedByFileTags => FileTags is { IsEmpty: false };

    /// <summary>
    /// Whether a lookup has run for this file, whatever came of it. The status cannot answer this:
    /// editing a field before a lookup moves a file out of Pending. A fetched snapshot is the
    /// evidence, plus the three statuses that cost a round trip and leave no snapshot behind.
    /// </summary>
    public bool HasBeenLookedUp =>
        FetchedMetadata is not null
        || Status is FileStatus.NotFound or FileStatus.Failed or FileStatus.NeedsChoice;

    public string Extension => System.IO.Path.GetExtension(Path);

    public Geometry StatusGeometry => StatusVisuals.GeometryFor(Status);

    public string StatusDescription => StatusVisuals.DescriptionFor(Status);

    // The icon's colour is applied through style classes rather than a brush from here, so it
    // follows the light/dark theme without any converter.
    public bool IsStatusOk => Status is FileStatus.Matched or FileStatus.Applied;

    public bool IsStatusWarning => Status is FileStatus.NeedsChoice;

    public bool IsStatusError => Status is FileStatus.NotFound or FileStatus.Failed;

    public bool IsStatusInfo => Status is FileStatus.Edited or FileStatus.Applying;

    public bool IsStatusIdle => Status is FileStatus.Pending or FileStatus.Searching;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public bool HasRenamePreview => !string.IsNullOrWhiteSpace(RenamePreview);

    public bool IsRenameOverridden => RenameOverride is not null;

    /// <summary>
    /// The one-line description under the filename: what we think this file holds. A description
    /// guessed from the filename alone says so with a trailing question mark; one read out of the
    /// file's own tags does not, confirmed against TMDB or not.
    /// </summary>
    public string Subtitle
    {
        get
        {
            var description = Describe();
            if (description is null)
                return Strings.Get("status.notRecognised");

            // A lookup that found nothing leaves the guess a guess, so the snapshot settles this
            // rather than whether a lookup happened to run.
            return FetchedMetadata is not null || DescribedByFileTags ? description : $"{description}?";
        }
    }

    private string? Describe()
    {
        if (Metadata.Kind == MediaKind.TvEpisode)
        {
            var show = Metadata.ShowName ?? Parsed?.Title ?? Strings.Get("pane.unknownShow");
            var number = FormatEpisodeNumber();
            var title = string.IsNullOrWhiteSpace(Metadata.Title) ? null : $" · {Metadata.Title}";
            return $"{show}{number}{title}";
        }

        if (!string.IsNullOrWhiteSpace(Metadata.Title))
            return Metadata.EffectiveYear is { } year ? $"{Metadata.Title} ({year})" : Metadata.Title;

        if (Parsed is { Title.Length: > 0 } parsed)
            return parsed.Year is { } parsedYear ? $"{parsed.Title} ({parsedYear})" : parsed.Title;

        return null;
    }

    private string FormatEpisodeNumber()
    {
        if (Metadata.Season is null && Metadata.Episodes.Count == 0)
            return string.Empty;

        var season = Metadata.Season is { } s ? $"S{s:00}" : "S??";
        if (Metadata.Episodes.Count == 0)
            return $" {season}E??";

        var episodes = string.Join("-E", Metadata.Episodes.Select(e => e.ToString("00")));
        return $" {season}E{episodes}";
    }

    /// <summary>
    /// Records that the user changed <paramref name="fieldName"/> by hand. A file still waiting to
    /// be looked up counts too: its form was filled from its own tags, so an edit there is a
    /// deliberate change to real content and has to stay appliable.
    /// </summary>
    public void MarkFieldEdited(string fieldName)
    {
        _userEditedFields.Add(fieldName);
        if (Status is FileStatus.Pending or FileStatus.Matched or FileStatus.NotFound
                   or FileStatus.Applied or FileStatus.Failed)
            Status = FileStatus.Edited;
        Message = null;
        RefreshSubtitle();
        OnPropertyChanged(nameof(HasManualChanges));
    }

    public bool IsFieldUserEdited(string fieldName) => _userEditedFields.Contains(fieldName);

    public IReadOnlyCollection<string> UserEditedFields => _userEditedFields;

    /// <summary>Whether anything on this file was changed by hand and could be discarded.</summary>
    public bool HasManualChanges => _userEditedFields.Count > 0;

    /// <summary>
    /// Forgets one hand-edit that a fresh fetch has already undone. Artwork is the case that
    /// matters: a hand-picked image does not survive a refetch, so leaving it marked would show
    /// the file as edited when nothing of the user's is left.
    /// </summary>
    public void ClearFieldEdit(string fieldName)
    {
        if (_userEditedFields.Remove(fieldName))
            OnPropertyChanged(nameof(HasManualChanges));
    }

    /// <summary>Forgets the hand-edit history, e.g. after the user picks a different title outright.</summary>
    public void ClearUserEdits()
    {
        _userEditedFields.Clear();
        OnPropertyChanged(nameof(HasManualChanges));
    }

    /// <summary>
    /// The metadata exactly as TMDB last returned it, before any hand edits were laid over the
    /// top. Kept so those edits can be discarded without going back to the network.
    /// </summary>
    public MediaMetadata? FetchedMetadata { get; private set; }

    /// <summary>Takes a fresh TMDB result, remembering it as the thing edits can be undone back to.</summary>
    public void RememberFetched(MediaMetadata fetched) => FetchedMetadata = fetched.Clone();

    /// <summary>
    /// Throws away every hand edit and goes back to what TMDB returned. Returns false when there
    /// is nothing to go back to, which is the case for a file that never matched.
    /// </summary>
    public bool DiscardManualChanges()
    {
        if (FetchedMetadata is null)
            return false;

        Metadata = FetchedMetadata.Clone();
        ClearUserEdits();
        Message = null;
        RefreshSubtitle();
        return true;
    }

    /// <summary>The metadata object changed in place, so tell the UI to re-read the derived text.</summary>
    public void RefreshSubtitle() => OnPropertyChanged(nameof(Subtitle));

    /// <summary>Called after a successful rename so the row shows the file's new name.</summary>
    public void UpdatePath(string newPath)
    {
        Path = newPath;
        FileName = System.IO.Path.GetFileName(newPath);
        Folder = System.IO.Path.GetDirectoryName(newPath) ?? string.Empty;
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(Folder));
    }

    /// <summary>The language to use for this file: its own override, or the global default.</summary>
    public string EffectiveLanguage(string globalLanguage) =>
        string.IsNullOrWhiteSpace(LanguageOverride) ? globalLanguage : LanguageOverride;
}
