using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMetadataFiller.App.Services;
using VideoMetadataFiller.Core.Model;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private int _selectionCount;

    [ObservableProperty]
    private string _headerTitle = "Nothing selected";

    [ObservableProperty]
    private string _headerSubtitle = "Pick a file on the left to see and edit its metadata.";

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

    /// <summary>Images offered by the picker for the selected file.</summary>
    public ObservableCollection<ArtworkChoiceViewModel> ArtworkChoices { get; } = [];

    public bool HasArtworkMessage => !string.IsNullOrWhiteSpace(ArtworkMessage);

    public bool HasArtworkCaption => !string.IsNullOrWhiteSpace(ArtworkCaption);

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

        _ = LoadPosterAsync();
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
                HeaderTitle = "Nothing selected";
                HeaderSubtitle = "Pick a file on the left to see and edit its metadata.";
                break;

            case 1:
                HeaderTitle = _selection[0].FileName;
                HeaderSubtitle = _selection[0].Subtitle;
                break;

            default:
                HeaderTitle = $"{_selection.Count} files selected";
                HeaderSubtitle = "Fields showing a value are the same across all of them. "
                                 + "Editing one applies it to every selected file.";
                break;
        }
    }

    private void UpdateNoticeAndCandidates()
    {
        Candidates.Clear();

        if (_selection.Count != 1)
        {
            // Choosing a title is a per-file decision, so the chooser only appears for one file.
            var needing = _selection.Count(f => f.Status == FileStatus.NeedsChoice);
            Notice = needing > 0
                ? $"{needing} of these files still need a title chosen. Select one on its own to choose."
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
        if (_selection.Count != 1)
        {
            SelectedLanguage = null;
            return;
        }

        var tag = _selection[0].EffectiveLanguage(_lookup.Language);
        SelectedLanguage = Languages.FirstOrDefault(l => l.Tag == tag) ?? Languages.FirstOrDefault();
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
            ArtworkMessage = "Add a TMDB API key in Settings first.";
            return;
        }

        if (file.Metadata.TmdbId is not { } tmdbId)
        {
            ArtworkMessage = "Match this file to a title first.";
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
                ArtworkMessage = "TMDB has no artwork for this title.";
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

        IsArtworkPickerOpen = false;
        _ = LoadPosterAsync();
    }

    [RelayCommand]
    private async Task ChooseAsync(CandidateViewModel? candidate)
    {
        if (candidate is null || _selection.Count != 1)
            return;

        await _lookup.ChooseCandidateAsync(_selection[0], candidate.Candidate);
        Refresh();
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
            SearchMessage = "Add a TMDB API key in Settings first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchText) || _selection.Count != 1)
            return;

        IsSearching = true;
        SearchMessage = null;
        try
        {
            var language = _selection[0].EffectiveLanguage(_lookup.Language);
            var results = KindIndex == 1
                ? await _lookup.Tmdb.SearchShowsAsync(SearchText, null, language)
                : await _lookup.Tmdb.SearchMoviesAsync(SearchText, null, language);

            Candidates.Clear();
            foreach (var candidate in results.Take(10))
                Candidates.Add(new CandidateViewModel(candidate));

            OnPropertyChanged(nameof(HasCandidates));
            SearchMessage = results.Count == 0 ? "No results." : $"{results.Count} result(s).";
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
        if (_selection.Count != 1)
            return;

        if (!int.TryParse(TmdbIdText, out var id) || id <= 0)
        {
            SearchMessage = "Enter a numeric TMDB id.";
            return;
        }

        IsSearching = true;
        SearchMessage = null;
        try
        {
            var kind = KindIndex == 1 ? MediaKind.TvEpisode : MediaKind.Movie;
            await _lookup.ApplyTmdbIdAsync(_selection[0], id, kind);
            Refresh();
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
        if (_rebinding || value is null || _selection.Count != 1)
            return;

        // Only store an override when it actually differs from the global setting.
        _selection[0].LanguageOverride = value.Tag == _lookup.Language ? null : value.Tag;
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
        }

        return
        [
            new FieldEditor(nameof(MediaMetadata.ShowName), "Show",
                m => m.ShowName, (m, v) => m.ShowName = v, Edited, FieldScope.ShowsOnly),

            new FieldEditor(nameof(MediaMetadata.Title), "Title",
                m => m.Title, (m, v) => m.Title = v, Edited,
                hint: "For an episode this is the episode title."),

            new FieldEditor(nameof(MediaMetadata.OriginalTitle), "Original title",
                m => m.OriginalTitle, (m, v) => m.OriginalTitle = v, Edited),

            new FieldEditor(nameof(MediaMetadata.Season), "Season",
                m => Number(m.Season), (m, v) => m.Season = ParseNumber(v), Edited, FieldScope.ShowsOnly),

            new FieldEditor(nameof(MediaMetadata.Episodes), "Episode",
                m => FormatEpisodes(m.Episodes), (m, v) => m.Episodes = ParseEpisodes(v), Edited,
                FieldScope.ShowsOnly, perFileOnly: true,
                hint: "Use 1-2 for a file holding two episodes."),

            new FieldEditor(nameof(MediaMetadata.Year), "Year",
                m => Number(m.EffectiveYear), (m, v) => m.Year = ParseNumber(v), Edited),

            new FieldEditor(nameof(MediaMetadata.ReleaseDate), "Release / air date",
                m => FormatDate(m.ReleaseDate), (m, v) => m.ReleaseDate = ParseDate(v), Edited,
                hint: "yyyy-mm-dd"),

            new FieldEditor(nameof(MediaMetadata.Overview), "Overview",
                m => m.Overview, (m, v) => m.Overview = v, Edited, isMultiline: true),

            new FieldEditor(nameof(MediaMetadata.Genres), "Genres",
                m => Join(m.Genres), (m, v) => m.Genres = SplitList(v), Edited, hint: "Comma-separated."),

            new FieldEditor(nameof(MediaMetadata.Cast), "Cast",
                m => Join(m.Cast), (m, v) => m.Cast = SplitList(v), Edited, hint: "Comma-separated."),

            new FieldEditor(nameof(MediaMetadata.Directors), "Director(s)",
                m => Join(m.Directors), (m, v) => m.Directors = SplitList(v), Edited),

            new FieldEditor(nameof(MediaMetadata.Writers), "Writer(s)",
                m => Join(m.Writers), (m, v) => m.Writers = SplitList(v), Edited),

            new FieldEditor(nameof(MediaMetadata.Studio), "Studio",
                m => m.Studio, (m, v) => m.Studio = v, Edited),

            new FieldEditor(nameof(MediaMetadata.Network), "Network",
                m => m.Network, (m, v) => m.Network = v, Edited, FieldScope.ShowsOnly),

            new FieldEditor(nameof(MediaMetadata.ContentRating), "Content rating",
                m => m.ContentRating, (m, v) => m.ContentRating = v, Edited, hint: "e.g. PG-13 or TV-MA."),

            new FieldEditor(nameof(MediaMetadata.Resolution), "Resolution",
                m => m.Resolution, (m, v) => m.Resolution = v, Edited,
                hint: "Used by the {resolution} rename token and the HD flag."),

            new FieldEditor(nameof(MediaMetadata.TmdbId), "TMDB id",
                m => Number(m.TmdbId), (m, v) => m.TmdbId = ParseNumber(v), Edited, perFileOnly: true),

            new FieldEditor(nameof(MediaMetadata.ImdbId), "IMDb id",
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
