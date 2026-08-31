using Avalonia.Media;
using Avalonia.Media.Imaging;
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

    /// <summary>The chosen artwork as shown in the list, loaded once the row is on screen.</summary>
    [ObservableProperty]
    private Bitmap? _thumbnail;

    /// <summary>
    /// Which artwork the thumbnail was loaded for, so a file that matches something else
    /// reloads and one that has not changed does not.
    /// </summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Whether the list is currently showing this row, set by the view as it realises and
    /// releases containers.
    ///
    /// It is here so that work triggered by something other than scrolling can ask whether
    /// anybody can actually see the row. Reading a file's cover costs a second open of the file
    /// and a decode, and a library can hold tens of thousands of rows, so the difference between
    /// doing that for the ones on screen and doing it for all of them is the difference between
    /// a list that appears at once and a NAS that thrashes for a quarter of an hour.
    /// </summary>
    public bool IsOnScreen { get; set; }

    /// <summary>What the file would be renamed to, or null when renaming is off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRenamePreview))]
    private string? _renamePreview;

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
    /// The tags the file itself carries, read when it was added. The form starts from these, so a
    /// file that is already tagged shows its own content rather than a blank sheet, and a lookup
    /// only fills what is missing or replaces what the user lets it.
    /// </summary>
    public ExistingTags? FileTags { get; set; }

    /// <summary>Whether the description below came out of the file rather than out of its name.</summary>
    private bool DescribedByFileTags => FileTags is { IsEmpty: false };

    /// <summary>
    /// Whether a lookup has run for this file, whatever came of it.
    /// </summary>
    /// <remarks>
    /// Not a question the status can answer any more: editing a field before a lookup moves a file
    /// out of Pending, so "not Pending" would claim a lookup that never happened. A fetched
    /// snapshot is the real evidence; the three statuses beside it are the outcomes that leave no
    /// snapshot behind but did cost a round trip.
    /// </remarks>
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

    /// <summary>The one-line description under the filename: what we think this file holds.</summary>
    /// <remarks>
    /// A description guessed from the filename alone says so with a trailing question mark. One
    /// read out of the file's own tags does not: it is what the file actually claims to hold,
    /// whether or not a lookup has confirmed it against TMDB.
    /// </remarks>
    public string Subtitle
    {
        get
        {
            var description = Describe();
            if (description is null)
                return Strings.Get("status.notRecognised");

            // A lookup that found nothing leaves the guess a guess, so the snapshot is what
            // settles this rather than whether a lookup happened to run.
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

    /// <summary>Records that the user changed <paramref name="fieldName"/> by hand.</summary>
    /// <remarks>
    /// A file waiting to be looked up counts too. The form is filled from its own tags, so an edit
    /// there is a deliberate change to real content, and leaving it unappliable would be refusing
    /// to write something the user just typed in front of the value it replaces.
    /// </remarks>
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
    /// Forgets one hand-edit, for a change that a fresh fetch has already undone. Artwork is the
    /// case that matters: a hand-picked image is deliberately not carried across a refetch, so
    /// leaving it marked would show the file as edited when nothing of the user's survives.
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
