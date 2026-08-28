using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMetadataFiller.App.Services;
using VideoMetadataFiller.Core.Matching;
using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Parsing;
using VideoMetadataFiller.Core.Settings;
using VideoMetadataFiller.Core.Tmdb;
using VideoMetadataFiller.Core.Writing;

namespace VideoMetadataFiller.App.ViewModels;

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

    private TmdbService? _tmdb;
    private MatchResolver? _resolver;
    private CancellationTokenSource? _work;

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
    private string _statusSummary = "No files loaded.";

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
    public Func<Task<IReadOnlyList<string>>>? PickFilesAsync { get; set; }

    public Func<Task<string?>>? PickFolderAsync { get; set; }

    /// <summary>Set by the view. Shows the settings dialog and returns true when it was saved.</summary>
    public Func<SettingsViewModel, Task<bool>>? ShowSettingsAsync { get; set; }

    public ITmdbService? Tmdb => _tmdb;

    public string ArtworkSize => Settings.ArtworkSize;

    public ArtworkKind PreferredArtwork(MediaKind kind) => Settings.PreferredArtwork(kind);

    /// <summary>
    /// A hand-picked image wins over the preferred kind until the file is refetched. Any artwork
    /// already downloaded is dropped so the new path is fetched on the next Apply.
    /// </summary>
    public void SetArtwork(FileItemViewModel file, string artworkPath)
    {
        if (file.Metadata.ArtworkPath == artworkPath)
            return;

        file.Metadata.ArtworkPath = artworkPath;
        file.Metadata.ArtworkData = null;
        NotifyMetadataChanged(file);
    }

    public string Language => Settings.Language;

    public IReadOnlyList<LanguageOption> Languages => LanguageCatalog.All;

    public bool HasBanner => !string.IsNullOrWhiteSpace(Banner);

    /// <summary>Drives the empty-state hint in the file pane.</summary>
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
        var paths = await PickFilesAsync();
        await AddPathsAsync(paths);
    }

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task AddFolderAsync()
    {
        if (PickFolderAsync is null)
            return;
        var folder = await PickFolderAsync();
        if (folder is not null)
            await AddPathsAsync([folder]);
    }

    /// <summary>
    /// Adds files and folders, expanding folders recursively and keeping only containers we can tag.
    /// Used by the toolbar and by drag-and-drop alike.
    /// </summary>
    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var existing = Files.Select(f => f.Path).ToHashSet(PathComparer);
        var added = new List<FileItemViewModel>();

        foreach (var path in Expand(paths))
        {
            if (!existing.Add(path))
                continue;
            var item = new FileItemViewModel(path);
            Files.Add(item);
            added.Add(item);
        }

        UpdateSummary();
        if (added.Count > 0)
            await ScanAsync(added);
    }

    private static IEnumerable<string> Expand(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                IEnumerable<string> found;
                try
                {
                    found = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                        .Where(Mp4TagWriter.IsSupported)
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in found)
                    yield return file;
            }
            else if (File.Exists(path) && Mp4TagWriter.IsSupported(path))
            {
                yield return path;
            }
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private void RemoveSelected()
    {
        foreach (var file in SelectedFiles.ToList())
            Files.Remove(file);
        SelectedFiles.Clear();
        Preview.SetSelection([]);
        UpdateSummary();
    }

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private void ClearAll()
    {
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
        RescanSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- lookup

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task RescanAllAsync() => await ScanAsync(Files.ToList(), force: true);

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task RescanSelectedAsync() => await ScanAsync(SelectedFiles.ToList(), force: true);

    /// <summary>
    /// Parses each filename and looks the result up on TMDB, a few at a time.
    /// </summary>
    /// <param name="force">Re-look-up files that already have metadata, discarding hand edits.</param>
    private async Task ScanAsync(IReadOnlyList<FileItemViewModel> items, bool force = false)
    {
        if (items.Count == 0)
            return;

        if (_resolver is null)
        {
            foreach (var item in items)
            {
                item.Parsed = FilenameParser.Parse(item.Path);
                SeedFromParse(item);
                item.Status = FileStatus.Pending;
                item.Message = "Waiting on a TMDB API key. Add one in Settings, then rescan.";
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
                    ProgressText = $"Looked up {done} of {items.Count}";
                    UpdateSummary();
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Cancelled.";
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

        item.Parsed = FilenameParser.Parse(item.Path);
        if (force)
            item.ClearUserEdits();
        SeedFromParse(item);

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
    /// Fills the metadata with what the filename alone told us, so the row reads sensibly even if
    /// TMDB has nothing.
    /// </summary>
    private static void SeedFromParse(FileItemViewModel item)
    {
        if (item.Parsed is not { } parsed)
            return;

        var metadata = new MediaMetadata
        {
            Kind = parsed.Kind == MediaKind.Unknown ? MediaKind.Movie : parsed.Kind,
            Season = parsed.Season,
            Episodes = [.. parsed.Episodes],
            Year = parsed.Year,
            Resolution = parsed.Resolution,
            ReleaseDate = parsed.AirDate,
        };

        if (parsed.Kind == MediaKind.TvEpisode)
            metadata.ShowName = parsed.Title;
        else
            metadata.Title = parsed.Title;

        item.Metadata = metadata;
        item.Candidates = [];
    }

    private void ApplyOutcome(FileItemViewModel item, MatchOutcome outcome)
    {
        if (outcome.Metadata is not null)
        {
            // Anything the user typed by hand outranks what TMDB just returned.
            item.Metadata = MergeKeepingUserEdits(item, outcome.Metadata);
        }

        item.Candidates = [.. outcome.Candidates];
        item.Status = item.UserEditedFields.Count > 0 && outcome.Status == FileStatus.Matched
            ? FileStatus.Edited
            : outcome.Status;
        item.Message = outcome.Message;
        item.RefreshSubtitle();
    }

    /// <summary>
    /// Overlays fresh TMDB metadata onto a file while preserving every field the user edited.
    /// This is what makes "refetch in another language" safe.
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
                    file.Message = "Set the season and episode, then use Refetch.";
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
                file.Message = "TMDB has no details for that title.";
                return;
            }

            metadata.Resolution = file.Metadata.Resolution ?? file.Parsed?.Resolution;
            metadata.ArtworkPath =
                ArtworkSelector.Resolve(metadata, Settings.PreferredArtwork(metadata.Kind))
                ?? metadata.ArtworkPath;
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
            Banner = "Renaming is on but a rename template is invalid. Fix it in Settings, or turn renaming off.";
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

                ProgressText = $"Writing {index + 1} of {targets.Count}: {file.FileName}";
                Progress = (double)index / targets.Count * 100;

                if (await ApplyOneAsync(file, movieTemplate, tvTemplate, token))
                    written++;
                else
                    failed++;
            }

            ProgressText = failed == 0
                ? $"Done — {written} file(s) written."
                : $"Done — {written} written, {failed} failed.";
        }
        catch (OperationCanceledException)
        {
            ProgressText = $"Cancelled after {written} file(s).";
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
                    () => RenameEngine.Rename(file.Path, template, file.Metadata, Settings.Separator), token);
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

        // A different language means everything on screen is in the wrong one.
        if (Settings.Language != previousLanguage && Files.Count > 0 && _resolver is not null)
            await ScanAsync(Files.ToList(), force: true);
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
        }
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
            : "No TMDB API key yet. Open Settings and paste your key to start matching files.";

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

        var preview = RenameEngine.PreviewFileName(file.Path, template, file.Metadata, Settings.Separator);
        file.RenamePreview = string.Equals(preview, file.FileName, StringComparison.Ordinal) ? null : preview;
    }

    // ---------------------------------------------------------------- summary

    private void UpdateSummary()
    {
        if (Files.Count == 0)
        {
            StatusSummary = "No files loaded.";
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

        var parts = new List<string> { $"{Files.Count} file(s)" };
        if (matched > 0) parts.Add($"{matched} ready");
        if (choose > 0) parts.Add($"{choose} need a choice");
        if (missing > 0) parts.Add($"{missing} not found");
        if (waiting > 0) parts.Add($"{waiting} not looked up yet");
        if (failed > 0) parts.Add($"{failed} failed");
        if (applied > 0) parts.Add($"{applied} written");

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
        RescanAllCommand.NotifyCanExecuteChanged();
        RescanSelectedCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _work?.Cancel();
        _work?.Dispose();
        _tmdb?.Dispose();
        _artwork.Dispose();
    }
}
