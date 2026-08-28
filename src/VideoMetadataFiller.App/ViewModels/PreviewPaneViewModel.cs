using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMetadataFiller.App.Services;
using VideoMetadataFiller.Core.Localization;
using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Writing;

namespace VideoMetadataFiller.App.ViewModels;

/// <summary>
/// The right-hand pane: previews the metadata for the current selection, lets every field be
/// edited, and resolves ambiguous matches.
/// </summary>
/// <remarks>
/// With one file selected this is a plain form. With several selected, each field shows the value
/// they share, or blanks with a "multiple values" watermark when they differ; typing then applies
/// to all of them. See <see cref="FieldEditor"/> for the mechanics.
/// </remarks>
public sealed partial class PreviewPaneViewModel : ObservableObject
{
    private readonly IMediaLookup _lookup;
    private readonly ArtworkLoader _artwork;

    private List<FileItemViewModel> _selection = [];

    /// <summary>Guards the Movie/TV toggle and language dropdown against firing while refilling.</summary>
    private bool _rebinding;

    private CancellationTokenSource? _posterLoad;

    private readonly Mp4TagReader _reader = new();

    private CancellationTokenSource? _diffLoad;

    /// <summary>What the selected file holds right now, kept so edits re-diff without re-reading it.</summary>
    private ExistingTags? _existing;

    /// <summary>Why the file's own tags could not be read, when they could not.</summary>
    private string? _existingError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private int _selectionCount;

    [ObservableProperty]
    private string _headerTitle = Strings.Get("pane.nothingSelected");

    [ObservableProperty]
    private string _headerSubtitle = Strings.Get("pane.pickAFile");

    [ObservableProperty]
    private Bitmap? _poster;

    /// <summary>Whether the current artwork is 16:9 rather than a 2:3 poster, so it is laid out unclipped.</summary>
    [ObservableProperty]
    private bool _posterIsWide;

    /// <summary>
    /// What the artwork is and what size it will be embedded at, e.g. "Episode still · w780".
    /// Sharpness alone is a poor signal at preview size, so the size is spelled out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtworkCaption))]
    private string? _artworkCaption;

    /// <summary>The Type toggle and the artwork are not fields, so they carry their own markers.</summary>
    [ObservableProperty]
    private bool _isKindUserEdited;

    [ObservableProperty]
    private bool _isArtworkUserEdited;

    /// <summary>Whether anything in the selection was changed by hand, enabling Discard.</summary>
    [ObservableProperty]
    private bool _hasManualChanges;

    /// <summary>One line saying what applying would do to the file: the diff's headline.</summary>
    [ObservableProperty]
    private string? _diffSummary;

    [ObservableProperty]
    private bool _isLoadingDiff;

    [ObservableProperty]
    private bool _isArtworkPickerOpen;

    [ObservableProperty]
    private bool _isLoadingArtwork;

    /// <summary>Why the picker is empty, when it is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtworkMessage))]
    private string? _artworkMessage;

    /// <summary>The "needs a choice" / "nothing found" banner, or null when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCandidates))]
    private ObservableCollection<CandidateViewModel> _candidates = [];

    /// <summary>0 = Movie, 1 = TV show. Bound to the toggle at the top of the form.</summary>
    [ObservableProperty]
    private int _kindIndex;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    /// <summary>True when the selected files are not all in the same language.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguagePlaceholder))]
    private bool _languagesDiffer;

    /// <summary>Free-text TMDB search, for when the filename was too mangled to match.</summary>
    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private string? _tmdbIdText;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string? _searchMessage;

    public PreviewPaneViewModel(IMediaLookup lookup, ArtworkLoader artwork)
    {
        _lookup = lookup;
        _artwork = artwork;
        Fields = BuildFields();
    }

    public ObservableCollection<FieldEditor> Fields { get; }

    public IReadOnlyList<LanguageOption> Languages => _lookup.Languages;

    public bool HasSelection => SelectionCount > 0;

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>True for a single file, where per-file controls (language, TMDB id) make sense.</summary>
    public bool IsSingleSelection => SelectionCount == 1;

    /// <summary>
    /// What applying would change in the file, compared against the tags it already carries.
    /// Only shown for a single file: the answer is per-file, and a merged one would mean nothing.
    /// </summary>
    public ObservableCollection<MetadataChangeViewModel> Changes { get; } = [];

    public bool HasChanges => Changes.Count > 0;

    public bool ShowDiff => IsSingleSelection;

    /// <summary>Images offered by the picker for the selected file.</summary>
    public ObservableCollection<ArtworkChoiceViewModel> ArtworkChoices { get; } = [];

    public bool HasArtworkMessage => !string.IsNullOrWhiteSpace(ArtworkMessage);

    public bool HasArtworkCaption => !string.IsNullOrWhiteSpace(ArtworkCaption);

    /// <summary>Mirrors how a field reads when the selected files disagree.</summary>
    public string? LanguagePlaceholder => LanguagesDiffer ? FieldEditor.MultipleValuesWatermark : null;

    /// <summary>
    /// Refills the whole pane for a new selection. Called on every selection change.
    /// </summary>
    public void SetSelection(IReadOnlyList<FileItemViewModel> selection)
    {
        _selection = [.. selection];
        _rebinding = true;
        try
        {
            SelectionCount = _selection.Count;
            OnPropertyChanged(nameof(IsSingleSelection));
            OnPropertyChanged(nameof(ShowDiff));

            var kind = DominantKind();
            KindIndex = kind == MediaKind.TvEpisode ? 1 : 0;

            foreach (var field in Fields)
                field.Rebind(_selection, kind);

            UpdateHeader();
            UpdateNoticeAndCandidates();
            UpdateLanguageSelection();
        }
        finally
        {
            _rebinding = false;
        }

        IsArtworkPickerOpen = false;
        ArtworkChoices.Clear();
        ArtworkMessage = null;

        UpdateEditMarkers();
        _ = LoadPosterAsync();
        _ = LoadDiffAsync();
    }

    /// <summary>Re-reads the form from the files without disturbing the selection.</summary>
    public void Refresh() => SetSelection(_selection);

    /// <summary>
    /// The kind the form is laid out for. A mixed selection is shown as movies, since the TV-only
    /// fields would be meaningless for half of it.
    /// </summary>
    private MediaKind DominantKind()
    {
        if (_selection.Count == 0)
            return MediaKind.Unknown;
        return _selection.All(f => f.Metadata.Kind == MediaKind.TvEpisode)
            ? MediaKind.TvEpisode
            : MediaKind.Movie;
    }

    private void UpdateHeader()
    {
        switch (_selection.Count)
        {
            case 0:
                HeaderTitle = Strings.Get("pane.nothingSelected");
                HeaderSubtitle = Strings.Get("pane.pickAFile");
                break;

            case 1:
                HeaderTitle = _selection[0].FileName;
                HeaderSubtitle = _selection[0].Subtitle;
                break;

            default:
                HeaderTitle = Strings.Format("pane.filesSelected", _selection.Count);
                HeaderSubtitle = Strings.Get("pane.sharedFields");
                break;
        }
    }

    private void UpdateNoticeAndCandidates()
    {
        Candidates.Clear();

        if (_selection.Count != 1)
        {
            // Per-file suggestions differ from one another, so only a hand search makes sense
            // across a selection: one title, applied to all of them.
            var needing = _selection.Count(f => f.Status == FileStatus.NeedsChoice);
            Notice = needing > 0
                ? Strings.Format("pane.needsChoice", needing)
                : null;
            OnPropertyChanged(nameof(HasCandidates));
            return;
        }

        var file = _selection[0];
        Notice = file.Message;

        foreach (var candidate in file.Candidates)
            Candidates.Add(new CandidateViewModel(candidate));

        OnPropertyChanged(nameof(HasCandidates));
        _ = LoadCandidatePostersAsync();
    }

    private void UpdateLanguageSelection()
    {
        if (_selection.Count == 0)
        {
            LanguagesDiffer = false;
            SelectedLanguage = null;
            return;
        }

        var tags = _selection
            .Select(f => f.EffectiveLanguage(_lookup.Language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        LanguagesDiffer = tags.Count > 1;
        SelectedLanguage = LanguagesDiffer
            ? null
            : Languages.FirstOrDefault(l => l.Tag == tags[0]) ?? Languages.FirstOrDefault();
    }

    private async Task LoadPosterAsync()
    {
        _posterLoad?.Cancel();
        _posterLoad?.Dispose();
        _posterLoad = new CancellationTokenSource();
        var token = _posterLoad.Token;

        var path = _selection.Count == 1 ? _selection[0].Metadata.ArtworkPath : null;
        UpdateArtworkCaption(path);
        if (path is null)
        {
            Poster = null;
            PosterIsWide = false;
            return;
        }

        try
        {
            // The configured size, not a preview-only one: the pane is meant to show the exact
            // image that will be written into the file.
            var bitmap = await _artwork.LoadAsync(_lookup.Tmdb, path, _lookup.ArtworkSize, token);
            if (!token.IsCancellationRequested)
            {
                Poster = bitmap;
                PosterIsWide = bitmap is not null && bitmap.PixelSize.Width > bitmap.PixelSize.Height;
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection.
        }
    }

    /// <summary>
    /// Names the kind currently in use. A hand-picked image is looked up in the picker's list,
    /// since it need not be one of the kinds the lookup chose between.
    /// </summary>
    private void UpdateArtworkCaption(string? path)
    {
        if (path is null || _selection.Count != 1)
        {
            ArtworkCaption = null;
            return;
        }

        var byKind = _selection[0].Metadata.ArtworkByKind.FirstOrDefault(p => p.Value == path);
        var label = byKind.Value is not null
            ? ArtworkKinds.Label(byKind.Key)
            : ArtworkChoices.FirstOrDefault(c => c.Path == path)?.KindLabel;

        ArtworkCaption = label is null ? _lookup.ArtworkSize : $"{label} · {_lookup.ArtworkSize}";
    }

    /// <summary>
    /// Reads what the selected file already holds, then works out what applying would change.
    /// The read is the only expensive part and happens once per selection; editing a field
    /// re-compares against the copy kept from it.
    /// </summary>
    private async Task LoadDiffAsync()
    {
        _diffLoad?.Cancel();
        _diffLoad?.Dispose();
        _diffLoad = new CancellationTokenSource();
        var token = _diffLoad.Token;

        _existing = null;
        _existingError = null;
        IsLoadingDiff = _selection.Count == 1;
        RecomputeDiff();

        if (_selection.Count != 1)
            return;

        var path = _selection[0].Path;
        try
        {
            var (tags, error) = await Task.Run(() => ReadExisting(path), token);
            if (token.IsCancellationRequested)
                return;

            _existing = tags;
            _existingError = error;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection, which owns the state from here.
            return;
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoadingDiff = false;
        }

        RecomputeDiff();
    }

    /// <summary>
    /// A file that cannot be read is a message in the pane, not an exception: it is usually a
    /// file that moved or is being written to right now, and neither should break the form.
    /// </summary>
    private (ExistingTags? Tags, string? Error) ReadExisting(string path)
    {
        try
        {
            // The cover is loaded too, for the one selected file: comparing the bytes is what
            // lets a file that was just applied report that there is nothing left to write.
            return (_reader.Read(path, includeArtwork: true), null);
        }
        catch (TagReadException e)
        {
            return (null, e.Message);
        }
        catch (Exception e)
        {
            return (null, Strings.Format("read.failed", e.Message));
        }
    }

    /// <summary>
    /// Re-compares the form against the file without touching the disk. Called on every edit, so
    /// the diff answers for what is on screen rather than for what TMDB last returned.
    /// </summary>
    private void RecomputeDiff()
    {
        Changes.Clear();
        DiffSummary = BuildChanges();
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>Fills <see cref="Changes"/> and returns the line that heads them.</summary>
    private string? BuildChanges()
    {
        if (_selection.Count != 1)
            return null;

        if (IsLoadingDiff)
            return Strings.Get("diff.loading");

        if (_existingError is { } error)
            return Strings.Format("diff.unreadable", error);

        if (_existing is not { } existing)
            return null;

        // Nothing has been matched yet, so there is no "after" to compare against.
        var pending = _selection[0].Metadata;
        if (string.IsNullOrWhiteSpace(pending.Title) && string.IsNullOrWhiteSpace(pending.ShowName))
            return Strings.Get("diff.nothingToWrite");

        foreach (var change in MetadataDiff.Between(existing, pending))
            Changes.Add(new MetadataChangeViewModel(change));

        return Changes.Count == 0
            ? Strings.Get("diff.upToDate")
            : existing.IsEmpty
                ? Strings.Get("diff.untagged")
                : Strings.Format("diff.changeCount", Changes.Count);
    }

    private async Task LoadCandidatePostersAsync()
    {
        foreach (var candidate in Candidates.ToList())
        {
            var bitmap = await _artwork.LoadAsync(
                _lookup.Tmdb, candidate.Candidate.PosterPath, ArtworkLoader.ThumbnailSize);
            candidate.Poster = bitmap;
        }
    }

    /// <summary>
    /// Opens the artwork picker, asking TMDB for every image it has for this title. That costs
    /// extra requests, so it happens on demand rather than as part of the match.
    /// </summary>
    [RelayCommand]
    private async Task OpenArtworkPickerAsync()
    {
        if (_selection.Count != 1)
            return;

        IsArtworkPickerOpen = true;
        ArtworkMessage = null;
        ArtworkChoices.Clear();

        var file = _selection[0];
        if (_lookup.Tmdb is null)
        {
            ArtworkMessage = Strings.Get("pane.addKeyFirst");
            return;
        }

        if (file.Metadata.TmdbId is not { } tmdbId)
        {
            ArtworkMessage = Strings.Get("pane.matchFirst");
            return;
        }

        IsLoadingArtwork = true;
        try
        {
            var language = file.EffectiveLanguage(_lookup.Language);
            var options = await _lookup.Tmdb.GetArtworkOptionsAsync(
                file.Metadata.Kind, tmdbId, file.Metadata.Season, file.Metadata.FirstEpisode, language);

            if (options.Count == 0)
            {
                ArtworkMessage = Strings.Get("artwork.none");
                return;
            }

            foreach (var option in options)
                ArtworkChoices.Add(new ArtworkChoiceViewModel(option, option.Path == file.Metadata.ArtworkPath));

            await LoadArtworkThumbnailsAsync();
        }
        catch (Exception e)
        {
            ArtworkMessage = e.Message;
        }
        finally
        {
            IsLoadingArtwork = false;
        }
    }

    private async Task LoadArtworkThumbnailsAsync()
    {
        foreach (var choice in ArtworkChoices.ToList())
            choice.Thumbnail = await _artwork.LoadAsync(_lookup.Tmdb, choice.Path, ArtworkLoader.ThumbnailSize);
    }

    private void UpdateEditMarkers()
    {
        IsKindUserEdited = _selection.Any(f => f.IsFieldUserEdited(nameof(MediaMetadata.Kind)));
        IsArtworkUserEdited = _selection.Any(f => f.IsFieldUserEdited(nameof(MediaMetadata.ArtworkPath)));
        HasManualChanges = _selection.Any(f => f.HasManualChanges);
    }

    /// <summary>
    /// Puts the selection back to what TMDB returned. Restores from the copy taken at fetch time
    /// rather than fetching again, so it is instant and works with no network.
    /// </summary>
    [RelayCommand]
    private void DiscardEdits()
    {
        foreach (var file in _selection.ToList())
            _lookup.DiscardManualChanges(file);

        Refresh();
    }

    [RelayCommand]
    private void CloseArtworkPicker() => IsArtworkPickerOpen = false;

    [RelayCommand]
    private void ChooseArtwork(ArtworkChoiceViewModel? choice)
    {
        if (choice is null || _selection.Count != 1)
            return;

        _lookup.SetArtwork(_selection[0], choice.Path);

        foreach (var other in ArtworkChoices)
            other.IsCurrent = ReferenceEquals(other, choice);

        UpdateEditMarkers();
        IsArtworkPickerOpen = false;
        _ = LoadPosterAsync();
    }

    [RelayCommand]
    private async Task ChooseAsync(CandidateViewModel? candidate)
    {
        if (candidate is not null)
            await ApplyCandidateAsync(candidate.Candidate);
    }

    /// <summary>
    /// Puts one title onto every selected file. Each keeps its own season and episode numbers
    /// and fetches its own entry, so this is how a whole show that matched the wrong series is
    /// corrected in one go rather than a file at a time.
    /// </summary>
    private async Task ApplyCandidateAsync(Candidate candidate)
    {
        var targets = _selection.ToList();
        if (targets.Count == 0)
            return;

        IsSearching = true;
        SearchMessage = targets.Count > 1 ? Strings.Format("pane.applyingTo", targets.Count) : null;
        try
        {
            foreach (var file in targets)
                await _lookup.ChooseCandidateAsync(file, candidate);

            if (targets.Count > 1)
                SearchMessage = Strings.Format("pane.appliedTo", targets.Count);
        }
        catch (Exception e)
        {
            SearchMessage = e.Message;
        }
        finally
        {
            IsSearching = false;
            Refresh();
        }
    }

    [RelayCommand]
    private async Task RefetchAsync()
    {
        foreach (var file in _selection.ToList())
            await _lookup.RefetchAsync(file);
        Refresh();
    }

    /// <summary>Searches TMDB by hand, for files whose names defeated the parser.</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (_lookup.Tmdb is null)
        {
            SearchMessage = Strings.Get("pane.addKeyFirst");
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchText) || _selection.Count == 0)
            return;

        IsSearching = true;
        SearchMessage = null;
        try
        {
            // The language the dropdown is showing, which is null when the files disagree.
            var language = SelectedLanguage?.Tag ?? _lookup.Language;
            var results = KindIndex == 1
                ? await _lookup.Tmdb.SearchShowsAsync(SearchText, null, language)
                : await _lookup.Tmdb.SearchMoviesAsync(SearchText, null, language);

            Candidates.Clear();
            foreach (var candidate in results.Take(10))
                Candidates.Add(new CandidateViewModel(candidate));

            OnPropertyChanged(nameof(HasCandidates));
            SearchMessage = results.Count == 0 ? Strings.Get("pane.noResults") : Strings.Format("pane.resultCount", results.Count);
            _ = LoadCandidatePostersAsync();
        }
        catch (Exception e)
        {
            SearchMessage = e.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Applies a TMDB id typed in by hand — the escape hatch when search cannot find it.</summary>
    [RelayCommand]
    private async Task ApplyTmdbIdAsync()
    {
        if (_selection.Count == 0)
            return;

        if (!int.TryParse(TmdbIdText, out var id) || id <= 0)
        {
            SearchMessage = Strings.Get("pane.enterNumericId");
            return;
        }

        var targets = _selection.ToList();
        IsSearching = true;
        SearchMessage = targets.Count > 1 ? Strings.Format("pane.applyingTo", targets.Count) : null;
        try
        {
            var kind = KindIndex == 1 ? MediaKind.TvEpisode : MediaKind.Movie;
            foreach (var file in targets)
                await _lookup.ApplyTmdbIdAsync(file, id, kind);

            if (targets.Count > 1)
                SearchMessage = Strings.Format("pane.appliedTo", targets.Count);
        }
        catch (Exception e)
        {
            SearchMessage = e.Message;
        }
        finally
        {
            IsSearching = false;
            Refresh();
        }
    }

    partial void OnKindIndexChanged(int value)
    {
        if (_rebinding || _selection.Count == 0)
            return;

        var kind = value == 1 ? MediaKind.TvEpisode : MediaKind.Movie;
        foreach (var file in _selection)
        {
            file.Metadata.Kind = kind;
            file.MarkFieldEdited(nameof(MediaMetadata.Kind));
            _lookup.NotifyMetadataChanged(file);
        }

        Refresh();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (_rebinding || value is null || _selection.Count == 0)
            return;

        // Only store an override when it actually differs from the global setting.
        var tag = value.Tag == _lookup.Language ? null : value.Tag;
        foreach (var file in _selection)
            file.LanguageOverride = tag;

        // Picking one for the batch settles the disagreement; Refetch then applies it.
        LanguagesDiffer = false;
    }

    /// <summary>
    /// Builds the form. Each entry pairs a label with how to read and write that field on
    /// <see cref="MediaMetadata"/>; everything else — multi-selection, fallback hints, visibility —
    /// is handled by <see cref="FieldEditor"/>.
    /// </summary>
    private ObservableCollection<FieldEditor> BuildFields()
    {
        void Edited(FileItemViewModel file, string key)
        {
            file.MarkFieldEdited(key);
            _lookup.NotifyMetadataChanged(file);
            UpdateEditMarkers();
            RecomputeDiff();
        }

        return
        [
            new FieldEditor(nameof(MediaMetadata.ShowName), Strings.Get("field.show"),
                m => m.ShowName, (m, v) => m.ShowName = v, Edited, FieldScope.ShowsOnly),

            new FieldEditor(nameof(MediaMetadata.Title), Strings.Get("field.title"),
                m => m.Title, (m, v) => m.Title = v, Edited,
                hint: Strings.Get("field.titleHint")),

            new FieldEditor(nameof(MediaMetadata.OriginalTitle), Strings.Get("field.originalTitle"),
                m => m.OriginalTitle, (m, v) => m.OriginalTitle = v, Edited),

            new FieldEditor(nameof(MediaMetadata.Season), Strings.Get("field.season"),
                m => Number(m.Season), (m, v) => m.Season = ParseNumber(v), Edited, FieldScope.ShowsOnly),

            new FieldEditor(nameof(MediaMetadata.Episodes), Strings.Get("field.episode"),
                m => FormatEpisodes(m.Episodes), (m, v) => m.Episodes = ParseEpisodes(v), Edited,
                FieldScope.ShowsOnly, perFileOnly: true,
                hint: Strings.Get("field.episodeHint")),

            new FieldEditor(nameof(MediaMetadata.Year), Strings.Get("field.year"),
                m => Number(m.EffectiveYear), (m, v) => m.Year = ParseNumber(v), Edited),

            new FieldEditor(nameof(MediaMetadata.ReleaseDate), Strings.Get("field.releaseDate"),
                m => FormatDate(m.ReleaseDate), (m, v) => m.ReleaseDate = ParseDate(v), Edited,
                hint: "yyyy-mm-dd"),

            new FieldEditor(nameof(MediaMetadata.Overview), Strings.Get("field.overview"),
                m => m.Overview, (m, v) => m.Overview = v, Edited, isMultiline: true),

            new FieldEditor(nameof(MediaMetadata.Genres), Strings.Get("field.genres"),
                m => Join(m.Genres), (m, v) => m.Genres = SplitList(v), Edited, hint: Strings.Get("field.commaSeparated")),

            new FieldEditor(nameof(MediaMetadata.Cast), Strings.Get("field.cast"),
                m => Join(m.Cast), (m, v) => m.Cast = SplitList(v), Edited, hint: Strings.Get("field.commaSeparated")),

            new FieldEditor(nameof(MediaMetadata.Directors), Strings.Get("field.directors"),
                m => Join(m.Directors), (m, v) => m.Directors = SplitList(v), Edited),

            new FieldEditor(nameof(MediaMetadata.Writers), Strings.Get("field.writers"),
                m => Join(m.Writers), (m, v) => m.Writers = SplitList(v), Edited),

            new FieldEditor(nameof(MediaMetadata.Studio), Strings.Get("field.studio"),
                m => m.Studio, (m, v) => m.Studio = v, Edited),

            new FieldEditor(nameof(MediaMetadata.Network), Strings.Get("field.network"),
                m => m.Network, (m, v) => m.Network = v, Edited, FieldScope.ShowsOnly),

            new FieldEditor(nameof(MediaMetadata.ContentRating), Strings.Get("field.contentRating"),
                m => m.ContentRating, (m, v) => m.ContentRating = v, Edited, hint: Strings.Get("field.contentRatingHint")),

            new FieldEditor(nameof(MediaMetadata.Resolution), Strings.Get("field.resolution"),
                m => m.Resolution, (m, v) => m.Resolution = v, Edited,
                hint: Strings.Get("field.resolutionHint")),

            new FieldEditor(nameof(MediaMetadata.TmdbId), Strings.Get("field.tmdbId"),
                m => Number(m.TmdbId), (m, v) => m.TmdbId = ParseNumber(v), Edited, perFileOnly: true),

            new FieldEditor(nameof(MediaMetadata.ImdbId), Strings.Get("field.imdbId"),
                m => m.ImdbId, (m, v) => m.ImdbId = v, Edited, perFileOnly: true),
        ];
    }

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static int? ParseNumber(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string? FormatDate(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Accept the canonical form first, then anything the user's locale understands.
        if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var exact))
            return exact;
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var loose)
            ? loose
            : null;
    }

    private static string? Join(List<string> values) => values.Count == 0 ? null : string.Join(", ", values);

    private static List<string> SplitList(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string? FormatEpisodes(List<int> episodes) =>
        episodes.Count == 0 ? null : string.Join('-', episodes);

    /// <summary>
    /// Reads an episode field: a single number, a range ("1-2") or a list ("1, 2"), so a
    /// multi-episode file can be corrected by hand the same way it is written.
    /// </summary>
    private static List<int> ParseEpisodes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var parts = text.Split([',', '-', 'e', 'E', ' '], StringSplitOptions.RemoveEmptyEntries);
        var episodes = new List<int>();
        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                episodes.Add(value);
        }

        return episodes;
    }
}
