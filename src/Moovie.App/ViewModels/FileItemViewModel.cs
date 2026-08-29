using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Moovie.Core.Localization;
using Moovie.Core.Model;

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
        nameof(StatusGeometry), nameof(StatusDescription),
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
    public string Subtitle
    {
        get
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
                return parsed.Year is { } parsedYear ? $"{parsed.Title} ({parsedYear})?" : $"{parsed.Title}?";

            return Strings.Get("status.notRecognised");
        }
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
    public void MarkFieldEdited(string fieldName)
    {
        _userEditedFields.Add(fieldName);
        if (Status is FileStatus.Matched or FileStatus.NotFound or FileStatus.Applied or FileStatus.Failed)
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
