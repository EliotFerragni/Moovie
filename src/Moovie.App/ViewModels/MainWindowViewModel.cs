using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moovie.App.Services;
using Moovie.Core.Files;
using Moovie.Core.Localization;
using Moovie.Core.Matching;
using Moovie.Core.Model;
using Moovie.Core.Parsing;
using Moovie.Core.Settings;
using Moovie.Core.Tmdb;
using Moovie.Core.Writing;

namespace Moovie.App.ViewModels;

/// <summary>
/// The window's view model: owns the file list, drives lookups and the batch write, and hands the
/// current selection to the preview pane.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IMediaLookup, IDisposable
{
    /// <summary>How many files are looked up at once. TMDB is also gated inside the service.</summary>
    private const int MaxParallelLookups = 4;

    private readonly SettingsStore _settingsStore;
    private readonly ArtworkLoader _artwork = new();
    private readonly Mp4TagWriter _writer = new();

    private readonly Mp4TagReader _reader = new();

    private TmdbService? _tmdb;
    private MatchResolver? _resolver;
    private CancellationTokenSource? _work;

    /// <summary>
    /// Cancelled when the list is emptied. Examining is not a command anybody started, so this is
    /// its only way to be called off.
    /// </summary>
    private CancellationTokenSource _listAlive = new();

    /// <summary>
    /// How many files are examined between handing results back to the list: small enough that
    /// emptying the list stops the work promptly, large enough that the hop back to the UI thread
    /// is not most of the cost.
    /// </summary>
    private const int ExamineBatch = 64;

    [ObservableProperty]
    private AppSettings _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditList))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _progressText;

    /// <summary>Top-of-window banner for things the user has to know, such as a missing API key.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner))]
    private string? _banner;

    [ObservableProperty]
    private string _statusSummary = Strings.Get("main.noFiles");

    public MainWindowViewModel(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
        Preview = new PreviewPaneViewModel(this, _artwork);

        RebuildTmdbClient();
        UpdateBanner();
    }

    public ObservableCollection<FileItemViewModel> Files { get; } = [];

    public PreviewPaneViewModel Preview { get; }

    /// <summary>Set by the view, which owns the window needed for the platform file pickers.</summary>
    public Func<string?, Task<IReadOnlyList<string>>>? PickFilesAsync { get; set; }

    public Func<string?, Task<string?>>? PickFolderAsync { get; set; }

    /// <summary>Set by the view. Shows the settings dialog and returns true when it was saved.</summary>
    public Func<SettingsViewModel, Task<bool>>? ShowSettingsAsync { get; set; }

    /// <summary>Set by the view. Shows the About box.</summary>
    public Func<AboutViewModel, Task>? ShowAboutAsync { get; set; }

    public ITmdbService? Tmdb => _tmdb;

    public string ArtworkSize => Settings.ArtworkSize;

    public ArtworkKind PreferredArtwork(MediaKind kind) => Settings.PreferredArtwork(kind);

    /// <summary>
    /// A hand-picked image wins over the preferred kind until the file is refetched. Artwork
    /// already downloaded is dropped so the new path is fetched on the next Apply.
    /// </summary>
    public void SetArtwork(FileItemViewModel file, string artworkPath)
    {
        if (file.Metadata.ArtworkPath == artworkPath)
            return;

        file.Metadata.ArtworkPath = artworkPath;
        file.Metadata.ArtworkData = null;
        file.MarkFieldEdited(nameof(MediaMetadata.ArtworkPath));
        NotifyMetadataChanged(file);
    }

    /// <summary>
    /// Puts a file back to exactly what TMDB returned, from the copy kept at fetch time, so this
    /// costs no request and works offline.
    /// </summary>
    public void DiscardManualChanges(FileItemViewModel file)
    {
        // With no TMDB result to go back to there is still a state before the edits: the file's
        // own tags, and its name for the gaps.
        if (file.FetchedMetadata is null)
        {
            if (!file.HasManualChanges)
                return;

            Seed(file);
            file.ClearUserEdits();
            file.Message = null;
            file.Status = FileStatus.Pending;
            NotifyMetadataChanged(file);
            return;
        }

        if (!file.DiscardManualChanges())
            return;

        file.Status = FileStatus.Matched;
        NotifyMetadataChanged(file);
    }

    public string Language => Settings.Language;

    public IReadOnlyList<LanguageOption> Languages => LanguageCatalog.All;

    public bool HasBanner => !string.IsNullOrWhiteSpace(Banner);

    public bool HasFiles => Files.Count > 0;

    /// <summary>Files may only be added or removed while nothing is in flight.</summary>
    public bool CanEditList => !IsBusy;

    public bool RenameEnabled
    {
        get => Settings.RenameEnabled;
        set
        {
            if (Settings.RenameEnabled == value)
                return;
            Settings.RenameEnabled = value;
            _settingsStore.Save(Settings);
            OnPropertyChanged();
            RefreshRenamePreviews();
        }
    }

    // ---------------------------------------------------------------- file list

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task AddFilesAsync()
    {
        if (PickFilesAsync is null)
            return;
        var paths = await PickFilesAsync(_lastFolder);
        AddPaths(paths);
    }

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task AddFolderAsync()
    {
        if (PickFolderAsync is null)
            return;
        var folder = await PickFolderAsync(_lastFolder);
        if (folder is not null)
            AddPaths([folder]);
    }

    /// <summary>
    /// Where the next picker should open. On a first run this is the folder named on the command
    /// line, without which the container's picker would open on HOME, the app's config directory.
    /// </summary>
    private string? _lastFolder;

    private void RememberFolder(IReadOnlyList<string> paths)
    {
        // What was asked for rather than what was added, so a folder holding nothing taggable
        // still opens there next time.
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                _lastFolder = path;
                return;
            }

            if (Path.GetDirectoryName(path) is { Length: > 0 } parent && Directory.Exists(parent))
            {
                _lastFolder = parent;
                return;
            }
        }
    }

    /// <summary>
    /// Adds files and folders, expanding folders recursively and keeping only containers we can
    /// tag. Used by the toolbar and by drag-and-drop alike. Adding looks nothing up: dropping a
    /// folder is not a decision to spend an API call on every file in it.
    /// </summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        var requested = paths as IReadOnlyList<string> ?? paths.ToList();
        RememberFolder(requested);

        var existing = Files.Select(f => f.Path).ToHashSet(PathComparer);
        var added = new List<FileItemViewModel>();

        foreach (var path in MediaFiles.Expand(requested))
        {
            if (!existing.Add(path))
                continue;
            var item = new FileItemViewModel(path);
            Files.Add(item);
            added.Add(item);
        }

        UpdateSummary();

        if (added.Count > 0)
            _ = ExamineAsync(added);
    }

    /// <summary>
    /// Reads everything about a file that does not need the network: the tags it already carries,
    /// what its name says, and how big its video really is. Runs on adding, so a file that has
    /// been tagged before opens with its own content in the form rather than a blank sheet.
    /// </summary>
    private async Task ExamineAsync(IReadOnlyList<FileItemViewModel> items)
    {
        var token = _listAlive.Token;

        // A batch at a time: nothing accumulates, emptying the list stops the work within a
        // batch, and the rows fill in as they are read rather than all at the end.
        for (var start = 0; start < items.Count && !token.IsCancellationRequested; start += ExamineBatch)
        {
            var batch = new List<FileItemViewModel>(ExamineBatch);
            for (var i = start; i < Math.Min(start + ExamineBatch, items.Count); i++)
            {
                if (!items[i].IsRemoved)
                    batch.Add(items[i]);
            }

            if (batch.Count == 0)
                continue;

            // The probe and the tag read each open the file, so this stays off the UI thread.
            List<(ParsedName Parsed, ExistingTags Tags)> examined;
            try
            {
                examined = await Task.Run(
                    () => batch.Select(i => (Parsed: Examine(i.Path), Tags: ReadTags(i.Path))).ToList(),
                    token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            for (var i = 0; i < batch.Count; i++)
            {
                if (batch[i].IsRemoved)
                    continue;

                batch[i].Parsed = examined[i].Parsed;
                batch[i].FileTags = examined[i].Tags;
                Seed(batch[i]);
            }

            UpdateSummary();
            Preview.Refresh();

            // Only the rows somebody is looking at: they asked for a thumbnail before their tags
            // were read and got nothing, so they have to be asked again. Every other row asks for
            // itself when it is scrolled to, which is the point of doing this per row.
            foreach (var item in batch.Where(i => i.IsOnScreen))
                _ = EnsureThumbnailAsync(item);
        }
    }

    /// <summary>
    /// The file's own tags, or nothing at all when it will not open. An unreadable file is still a
    /// row: the preview pane says why when it is selected, and the list should not lose it here.
    /// </summary>
    private ExistingTags ReadTags(string path)
    {
        try
        {
            return _reader.Read(path);
        }
        catch (Exception)
        {
            return ExistingTags.None;
        }
    }


    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private void RemoveSelected()
    {
        foreach (var file in SelectedFiles.ToList())
        {
            file.IsRemoved = true;
            file.ReleaseThumbnail();
            Files.Remove(file);
        }
        SelectedFiles.Clear();
        Preview.SetSelection([]);
        UpdateSummary();
    }

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private void ClearAll()
    {
        // Nothing on the list means nothing in flight for it either.
        _listAlive.Cancel();
        _listAlive.Dispose();
        _listAlive = new CancellationTokenSource();
        _work?.Cancel();

        foreach (var file in Files)
        {
            file.IsRemoved = true;
            file.ReleaseThumbnail();
        }

        Files.Clear();
        SelectedFiles.Clear();
        Preview.SetSelection([]);
        UpdateSummary();
    }

    /// <summary>The current selection, kept in step by the view's SelectionChanged handler.</summary>
    public List<FileItemViewModel> SelectedFiles { get; } = [];

    public void SetSelection(IEnumerable<FileItemViewModel> selection)
    {
        SelectedFiles.Clear();
        SelectedFiles.AddRange(selection);
        Preview.SetSelection(SelectedFiles);
        LookUpSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- lookup

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task LookUpAllAsync() => await ScanAsync(Files.ToList(), force: true);

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task LookUpSelectedAsync() => await ScanAsync(SelectedFiles.ToList(), force: true);

    /// <summary>Parses each filename and looks the result up on TMDB, a few at a time.</summary>
    /// <param name="force">Re-look-up files that already have metadata, discarding hand edits.</param>
    private async Task ScanAsync(IReadOnlyList<FileItemViewModel> items, bool force = false)
    {
        if (items.Count == 0)
            return;

        if (_resolver is null)
        {
            // The probe opens every file, so it does not run on the UI thread.
            await Task.Run(() =>
            {
                foreach (var item in items)
                {
                    item.Parsed = Examine(item.Path);
                    item.FileTags ??= ReadTags(item.Path);
                }
            });

            foreach (var item in items)
            {
                Seed(item);
                item.Status = FileStatus.Pending;
                item.Message = Strings.Get("main.waitingForKey");
            }

            UpdateSummary();
            Preview.Refresh();
            return;
        }

        _work?.Cancel();
        _work?.Dispose();
        _work = new CancellationTokenSource();
        var token = _work.Token;

        IsBusy = true;
        Progress = 0;
        var done = 0;

        try
        {
            using var gate = new SemaphoreSlim(MaxParallelLookups, MaxParallelLookups);
            var tasks = items.Select(async item =>
            {
                await gate.WaitAsync(token);
                try
                {
                    await LookupAsync(item, force, token);
                }
                finally
                {
                    gate.Release();
                    done++;
                    Progress = (double)done / items.Count * 100;
                    ProgressText = Strings.Format("main.lookedUp", done, items.Count);
                    UpdateSummary();
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            ProgressText = Strings.Get("main.cancelled");
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
            ProgressText = null;
            UpdateSummary();
            RefreshRenamePreviews();
            Preview.Refresh();
        }
    }

    private async Task LookupAsync(FileItemViewModel item, bool force, CancellationToken token)
    {
        if (!force && item.Status is FileStatus.Matched or FileStatus.Edited or FileStatus.Applied)
            return;

        item.Parsed = await Task.Run(() => Examine(item.Path), token);

        // Re-read on a forced pass: the file may have been written since it was added.
        if (force || item.FileTags is null)
            item.FileTags = await Task.Run(() => ReadTags(item.Path), token);

        // Forcing a file that has already been looked up means "start this one over", so hand
        // edits go with it. On one that never has, they are all the user has told us: they stay.
        if (force && item.FetchedMetadata is not null)
            item.ClearUserEdits();

        Seed(item);

        item.Status = FileStatus.Searching;
        item.Message = null;

        try
        {
            var outcome = await _resolver!.ResolveAsync(
                item.Parsed, item.EffectiveLanguage(Language), token);
            ApplyOutcome(item, outcome);
        }
        catch (OperationCanceledException)
        {
            item.Status = FileStatus.Pending;
            throw;
        }
        catch (Exception e)
        {
            item.Status = FileStatus.Failed;
            item.Message = e.Message;
        }
    }

    /// <summary>
    /// What the path says about a file, corrected by what the file itself says. The resolution is
    /// the one field the container knows better than the name; the name stays the fallback for a
    /// video track that cannot be read.
    /// </summary>
    private static ParsedName Examine(string path)
    {
        var parsed = FilenameParser.Parse(path);
        return VideoResolution.Read(path) is { } resolution
            ? parsed with { Resolution = resolution }
            : parsed;
    }

    /// <summary>
    /// Fills the form with what is known before any lookup. See <see cref="MetadataSeed"/> for the
    /// order the two sources are merged in.
    /// </summary>
    private static void Seed(FileItemViewModel item)
    {
        item.Metadata = MetadataSeed.From(item.FileTags, item.Parsed);
        item.Candidates = [];
    }

    /// <summary>
    /// Points freshly fetched metadata at the artwork kind the user asked for, since TMDB hands
    /// back its own default. Every path taking metadata from TMDB must go through this, or the
    /// preference applies to some files and not others depending on how they were matched.
    /// </summary>
    private void ApplyArtworkPreference(MediaMetadata metadata) =>
        metadata.ArtworkPath =
            ArtworkSelector.Resolve(metadata, Settings.PreferredArtwork(metadata.Kind))
            ?? metadata.ArtworkPath;

    private void ApplyOutcome(FileItemViewModel item, MatchOutcome outcome)
    {
        if (outcome.Metadata is not null)
        {
            ApplyArtworkPreference(outcome.Metadata);
            item.RememberFetched(outcome.Metadata);

            // A hand-picked image does not survive a fetch, so it stops counting as an edit.
            item.ClearFieldEdit(nameof(MediaMetadata.ArtworkPath));

            // Anything the user typed by hand outranks what TMDB just returned.
            item.Metadata = MergeKeepingUserEdits(item, outcome.Metadata);
        }

        item.Candidates = [.. outcome.Candidates];
        item.Status = item.UserEditedFields.Count > 0 && outcome.Status == FileStatus.Matched
            ? FileStatus.Edited
            : outcome.Status;
        item.Message = outcome.Message;
        item.RefreshSubtitle();
        _ = EnsureThumbnailAsync(item);
    }

    /// <summary>
    /// Overlays fresh TMDB metadata onto a file while preserving every field the user edited,
    /// which is what makes "refetch in another language" safe.
    /// </summary>
    private static MediaMetadata MergeKeepingUserEdits(FileItemViewModel item, MediaMetadata fresh)
    {
        if (item.UserEditedFields.Count == 0)
            return fresh;

        var merged = fresh.Clone();
        var previous = item.Metadata;

        foreach (var field in item.UserEditedFields)
        {
            switch (field)
            {
                case nameof(MediaMetadata.Kind): merged.Kind = previous.Kind; break;
                case nameof(MediaMetadata.Title): merged.Title = previous.Title; break;
                case nameof(MediaMetadata.OriginalTitle): merged.OriginalTitle = previous.OriginalTitle; break;
                case nameof(MediaMetadata.ShowName): merged.ShowName = previous.ShowName; break;
                case nameof(MediaMetadata.Season): merged.Season = previous.Season; break;
                case nameof(MediaMetadata.Episodes): merged.Episodes = [.. previous.Episodes]; break;
                case nameof(MediaMetadata.Year): merged.Year = previous.Year; break;
                case nameof(MediaMetadata.ReleaseDate): merged.ReleaseDate = previous.ReleaseDate; break;
                case nameof(MediaMetadata.Overview): merged.Overview = previous.Overview; break;
                case nameof(MediaMetadata.Genres): merged.Genres = [.. previous.Genres]; break;
                case nameof(MediaMetadata.Cast): merged.Cast = [.. previous.Cast]; break;
                case nameof(MediaMetadata.Directors): merged.Directors = [.. previous.Directors]; break;
                case nameof(MediaMetadata.Writers): merged.Writers = [.. previous.Writers]; break;
                case nameof(MediaMetadata.Studio): merged.Studio = previous.Studio; break;
                case nameof(MediaMetadata.Network): merged.Network = previous.Network; break;
                case nameof(MediaMetadata.ContentRating): merged.ContentRating = previous.ContentRating; break;
                case nameof(MediaMetadata.Resolution): merged.Resolution = previous.Resolution; break;
                case nameof(MediaMetadata.TmdbId): merged.TmdbId = previous.TmdbId; break;
                case nameof(MediaMetadata.ImdbId): merged.ImdbId = previous.ImdbId; break;
            }

            // A field the user set is not a translation gap.
            merged.FallbackFields.Remove(field);
        }

        return merged;
    }

    public async Task ChooseCandidateAsync(FileItemViewModel file, Candidate candidate)
    {
        if (_tmdb is null)
            return;

        var language = file.EffectiveLanguage(Language);
        file.Status = FileStatus.Searching;
        file.Message = null;

        try
        {
            MediaMetadata? metadata;
            if (candidate.Kind == MediaKind.TvEpisode)
            {
                var season = file.Metadata.Season ?? file.Parsed?.Season ?? 1;
                var episodes = file.Metadata.Episodes.Count > 0
                    ? file.Metadata.Episodes
                    : file.Parsed?.Episodes ?? [];

                if (episodes.Count == 0 && file.Parsed?.AirDate is { } airDate)
                {
                    var located = await _tmdb.FindEpisodeByAirDateAsync(candidate.TmdbId, airDate, language);
                    if (located is { } coordinate)
                    {
                        season = coordinate.Season;
                        episodes = [coordinate.Episode];
                    }
                }

                if (episodes.Count == 0)
                {
                    // We know the show now; the user still has to say which episode.
                    file.Metadata.Kind = MediaKind.TvEpisode;
                    file.Metadata.ShowName = candidate.Title;
                    file.Metadata.TmdbId = candidate.TmdbId;
                    file.Metadata.ArtworkPath = candidate.PosterPath;
                    file.Status = FileStatus.NeedsChoice;
                    file.Message = Strings.Get("pane.setSeasonEpisode");
                    file.RefreshSubtitle();
                    NotifyMetadataChanged(file);
                    return;
                }

                metadata = await _tmdb.GetEpisodeAsync(candidate.TmdbId, season, episodes, language);
            }
            else
            {
                metadata = await _tmdb.GetMovieAsync(candidate.TmdbId, language);
            }

            if (metadata is null)
            {
                file.Status = FileStatus.NotFound;
                file.Message = Strings.Get("tmdb.noDetails");
                return;
            }

            metadata.Resolution = file.Metadata.Resolution ?? file.Parsed?.Resolution;
            ApplyArtworkPreference(metadata);
            file.RememberFetched(metadata);
            file.ClearFieldEdit(nameof(MediaMetadata.ArtworkPath));
            file.Metadata = MergeKeepingUserEdits(file, metadata);
            file.Status = file.UserEditedFields.Count > 0 ? FileStatus.Edited : FileStatus.Matched;
            file.Message = null;
            file.Candidates = [];
            file.RefreshSubtitle();
            NotifyMetadataChanged(file);
        }
        catch (Exception e)
        {
            file.Status = FileStatus.Failed;
            file.Message = e.Message;
        }
    }

    /// <summary>
    /// Re-reads a file's metadata from TMDB in its current language, keeping hand edits. This is
    /// the "the French overview is missing, try English" path.
    /// </summary>
    public async Task RefetchAsync(FileItemViewModel file)
    {
        if (_tmdb is null || file.Metadata.TmdbId is not { } id)
        {
            await ScanAsync([file], force: false);
            return;
        }

        var candidate = new Candidate
        {
            TmdbId = id,
            Kind = file.Metadata.Kind,
            Title = file.Metadata.ShowName ?? file.Metadata.Title ?? string.Empty,
            PosterPath = file.Metadata.ArtworkPath,
        };

        await ChooseCandidateAsync(file, candidate);
    }

    public async Task ApplyTmdbIdAsync(FileItemViewModel file, int tmdbId, MediaKind kind)
    {
        await ChooseCandidateAsync(file, new Candidate
        {
            TmdbId = tmdbId,
            Kind = kind,
            Title = file.Metadata.ShowName ?? file.Metadata.Title ?? string.Empty,
        });
    }

    public void NotifyMetadataChanged(FileItemViewModel file)
    {
        RefreshRenamePreview(file);
        UpdateSummary();
        _ = EnsureThumbnailAsync(file);
    }

    /// <summary>
    /// Loads the thumbnail for one row: the same image the file is going to carry, so the list
    /// never shows one picture and the preview another. Called when a row scrolls into view and
    /// whenever a file's artwork changes; an unchanged file costs a string comparison.
    /// </summary>
    public async Task EnsureThumbnailAsync(FileItemViewModel file)
    {
        var path = file.Metadata.ArtworkPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            // No path but bytes in hand means the file's own cover was kept from the preview
            // diff, so the row shows that rather than emptying over a cover that is staying put.
            // The length stands in for a path: it only has to tell this file's own covers apart.
            if (file.Metadata.ArtworkData is { Length: > 0 } kept)
            {
                Take($"embedded:{kept.Length}", () => ArtworkLoader.Decode(kept, ArtworkLoader.ThumbnailHeight));
                return;
            }

            // Nothing chosen yet, but the file's own cover is what the row shows until a lookup
            // offers a different one.
            if (file.FileTags is { HasArtwork: true } && file.ThumbnailPath != $"file:{file.Path}")
            {
                file.ThumbnailPath = $"file:{file.Path}";
                file.ShowThumbnail(await LoadEmbeddedThumbnailAsync(file.Path), owned: true);
            }
            else if (file.FileTags is not { HasArtwork: true })
            {
                file.ReleaseThumbnail();
            }

            return;
        }

        void Take(string marker, Func<Bitmap?> decode)
        {
            if (file.ThumbnailPath == marker)
                return;

            file.ThumbnailPath = marker;
            file.ShowThumbnail(decode(), owned: true);
        }

        if (file.ThumbnailPath == path)
            return;

        file.ThumbnailPath = path;

        // Straight from the cache, so it is not this row's to free: the same poster is very
        // likely on every other episode of the same show.
        file.ShowThumbnail(await _artwork.LoadAsync(_tmdb, path, ArtworkLoader.ThumbnailSize), owned: false);
    }

    /// <summary>
    /// The cover embedded in one file, at tile size. Read per row rather than for the whole list:
    /// adding a folder should not decode three hundred covers before one is looked at.
    /// </summary>
    private async Task<Bitmap?> LoadEmbeddedThumbnailAsync(string path)
    {
        var bytes = await Task.Run(() =>
        {
            try
            {
                return _reader.Read(path, includeArtwork: true).Metadata.ArtworkData;
            }
            catch (Exception)
            {
                return null;
            }
        });

        return ArtworkLoader.Decode(bytes, ArtworkLoader.ThumbnailHeight);
    }

    // ---------------------------------------------------------------- apply

    public bool CanApply => !IsBusy && Files.Any(f => f.Status.IsReadyToApply());

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var targets = Files.Where(f => f.Status.IsReadyToApply()).ToList();
        if (targets.Count == 0)
            return;

        _work?.Cancel();
        _work?.Dispose();
        _work = new CancellationTokenSource();
        var token = _work.Token;

        var movieTemplate = RenameTemplate.Parse(Settings.MovieRenameTemplate);
        var tvTemplate = RenameTemplate.Parse(Settings.TvRenameTemplate);

        if (Settings.RenameEnabled && (!movieTemplate.Validation.IsValid || !tvTemplate.Validation.IsValid))
        {
            Banner = Strings.Get("main.badTemplate");
            return;
        }

        IsBusy = true;
        Progress = 0;
        var written = 0;
        var failed = 0;

        try
        {
            for (var index = 0; index < targets.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var file = targets[index];

                ProgressText = Strings.Format("main.writingFile", index + 1, targets.Count, file.FileName);
                Progress = (double)index / targets.Count * 100;

                if (await ApplyOneAsync(file, movieTemplate, tvTemplate, token))
                    written++;
                else
                    failed++;
            }

            ProgressText = failed == 0
                ? Strings.Format("main.doneWritten", written)
                : Strings.Format("main.doneWithFailures", written, failed);
        }
        catch (OperationCanceledException)
        {
            ProgressText = Strings.Format("main.cancelledAfter", written);
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
            UpdateSummary();
            Preview.Refresh();
        }
    }

    private async Task<bool> ApplyOneAsync(
        FileItemViewModel file, RenameTemplate movieTemplate, RenameTemplate tvTemplate, CancellationToken token)
    {
        file.Status = FileStatus.Applying;
        file.Message = null;

        try
        {
            // Fetch artwork lazily: only the files actually being written pay for it.
            if (file.Metadata.ArtworkData is null && _tmdb is not null)
            {
                file.Metadata.ArtworkData = await _tmdb.GetArtworkAsync(
                    file.Metadata.ArtworkPath, Settings.ArtworkSize, token);
            }

            await Task.Run(() => _writer.Write(file.Path, file.Metadata, Settings.CreateBackup), token);

            if (Settings.RenameEnabled)
            {
                var template = file.Metadata.Kind == MediaKind.TvEpisode ? tvTemplate : movieTemplate;
                var renamed = await Task.Run(
                    () => RenameEngine.Rename(
                        file.Path, template, file.Metadata,
                        Settings.Separator, Settings.OmitResolutionAtOrBelow), token);
                file.UpdatePath(renamed);
            }

            file.Status = FileStatus.Applied;
            file.RenamePreview = null;
            return true;
        }
        catch (OperationCanceledException)
        {
            file.Status = FileStatus.Edited;
            throw;
        }
        catch (Exception e)
        {
            file.Status = FileStatus.Failed;
            file.Message = e.Message;
            return false;
        }
    }

    [RelayCommand]
    private void Cancel() => _work?.Cancel();

    // ---------------------------------------------------------------- settings

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (ShowSettingsAsync is null)
            return;

        var editor = new SettingsViewModel(Settings.Clone());
        if (!await ShowSettingsAsync(editor))
            return;

        var previousKey = Settings.TmdbApiKey;
        var previousLanguage = Settings.Language;
        var previousArtworkSize = Settings.ArtworkSize;
        var previousTvArtwork = Settings.TvArtwork;
        var previousMovieArtwork = Settings.MovieArtwork;

        Settings = editor.ToSettings();
        _settingsStore.Save(Settings);

        // The theme is the one setting that does not wait for a restart.
        ThemeCatalog.Apply(Settings.Theme);
        OnPropertyChanged(nameof(RenameEnabled));

        if (Settings.TmdbApiKey != previousKey)
            RebuildTmdbClient();

        // Artwork already downloaded is the wrong size now, so make Apply fetch it again.
        if (Settings.ArtworkSize != previousArtworkSize)
        {
            foreach (var file in Files)
                file.Metadata.ArtworkData = null;
        }

        if (Settings.TvArtwork != previousTvArtwork || Settings.MovieArtwork != previousMovieArtwork)
            ReapplyArtworkPreference();

        UpdateBanner();
        RefreshRenamePreviews();
        Preview.Refresh();

        // A different language means everything on screen is in the wrong one. Only what has
        // already been looked up, though: a file still waiting is waiting on purpose.
        if (Settings.Language != previousLanguage && _resolver is not null)
        {
            var lookedUp = Files.Where(f => f.HasBeenLookedUp).ToList();
            if (lookedUp.Count > 0)
                await ScanAsync(lookedUp, force: true);
        }
    }

    /// <summary>
    /// Moves every already-matched file onto the newly preferred artwork kind. Changing the
    /// setting is explicit enough to override an earlier hand-picked image.
    /// </summary>
    private void ReapplyArtworkPreference()
    {
        foreach (var file in Files)
        {
            if (file.Metadata.ArtworkByKind.Count == 0)
                continue;

            var resolved = ArtworkSelector.Resolve(
                file.Metadata, Settings.PreferredArtwork(file.Metadata.Kind));

            if (resolved is null || resolved == file.Metadata.ArtworkPath)
                continue;

            file.Metadata.ArtworkPath = resolved;
            file.Metadata.ArtworkData = null;
            file.ClearFieldEdit(nameof(MediaMetadata.ArtworkPath));
            _ = EnsureThumbnailAsync(file);
        }
    }

    [RelayCommand]
    private async Task OpenAboutAsync()
    {
        if (ShowAboutAsync is not null)
            await ShowAboutAsync(new AboutViewModel());
    }

    private void RebuildTmdbClient()
    {
        _tmdb?.Dispose();
        _tmdb = null;
        _resolver = null;

        if (!Settings.HasApiKey)
            return;

        try
        {
            _tmdb = new TmdbService(Settings.TmdbApiKey);
            _resolver = new MatchResolver(_tmdb);
        }
        catch (TmdbException e)
        {
            Banner = e.Message;
        }
    }

    private void UpdateBanner() =>
        Banner = Settings.HasApiKey
            ? null
            : Strings.Get("main.noApiKey");

    // ---------------------------------------------------------------- rename previews

    private void RefreshRenamePreviews()
    {
        foreach (var file in Files)
            RefreshRenamePreview(file);
    }

    private void RefreshRenamePreview(FileItemViewModel file)
    {
        if (!Settings.RenameEnabled)
        {
            file.RenamePreview = null;
            return;
        }

        var template = RenameTemplate.Parse(file.Metadata.Kind == MediaKind.TvEpisode
            ? Settings.TvRenameTemplate
            : Settings.MovieRenameTemplate);

        if (!template.Validation.IsValid)
        {
            file.RenamePreview = null;
            return;
        }

        var preview = RenameEngine.PreviewFileName(
            file.Path, template, file.Metadata, Settings.Separator, Settings.OmitResolutionAtOrBelow);
        file.RenamePreview = string.Equals(preview, file.FileName, StringComparison.Ordinal) ? null : preview;
    }

    // ---------------------------------------------------------------- summary

    private void UpdateSummary()
    {
        if (Files.Count == 0)
        {
            StatusSummary = Strings.Get("main.noFiles");
            OnPropertyChanged(nameof(HasFiles));
            ApplyCommand.NotifyCanExecuteChanged();
            return;
        }

        var matched = Files.Count(f => f.Status is FileStatus.Matched or FileStatus.Edited);
        var choose = Files.Count(f => f.Status == FileStatus.NeedsChoice);
        var missing = Files.Count(f => f.Status == FileStatus.NotFound);
        var waiting = Files.Count(f => f.Status == FileStatus.Pending);
        var failed = Files.Count(f => f.Status == FileStatus.Failed);
        var applied = Files.Count(f => f.Status == FileStatus.Applied);

        var parts = new List<string> { Strings.Format("summary.files", Files.Count) };
        if (matched > 0) parts.Add(Strings.Format("summary.ready", matched));
        if (choose > 0) parts.Add(Strings.Format("summary.needChoice", choose));
        if (missing > 0) parts.Add(Strings.Format("summary.notFound", missing));
        if (waiting > 0) parts.Add(Strings.Format("summary.notLookedUp", waiting));
        if (failed > 0) parts.Add(Strings.Format("summary.failed", failed));
        if (applied > 0) parts.Add(Strings.Format("summary.written", applied));

        StatusSummary = string.Join(" · ", parts);
        OnPropertyChanged(nameof(HasFiles));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        ClearAllCommand.NotifyCanExecuteChanged();
        LookUpAllCommand.NotifyCanExecuteChanged();
        LookUpSelectedCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _work?.Cancel();
        _work?.Dispose();
        _listAlive.Cancel();
        _listAlive.Dispose();
        _tmdb?.Dispose();

        // The rows first, since a row's own cover is nobody else's to free, then the cache, which
        // owns everything that came from TMDB.
        foreach (var file in Files)
            file.ReleaseThumbnail();

        _artwork.Dispose();
    }
}
