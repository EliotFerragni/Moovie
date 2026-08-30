using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moovie.Core.Localization;
using Moovie.Core.Model;
using Moovie.Core.Settings;
using Moovie.Core.Tmdb;
using Moovie.Core.Writing;

namespace Moovie.App.ViewModels;

/// <summary>An artwork kind offered in one of the artwork dropdowns.</summary>
public sealed record ArtworkKindChoice(ArtworkKind Kind, string DisplayName)
{
    public static ArtworkKindChoice For(ArtworkKind kind) => new(kind, ArtworkKinds.Label(kind));

    public override string ToString() => DisplayName;
}

/// <summary>A separator choice offered in the dropdown.</summary>
public sealed record SeparatorChoice(SeparatorStyle Style, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// A resolution the rename templates may leave out, plus the "never" entry that turns the
/// threshold off. The value is the label itself, or empty for never.
/// </summary>
public sealed record ResolutionFloorChoice(string Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// The settings dialog: TMDB key, default language, artwork size, and the two rename templates.
/// Edits a copy, so cancelling leaves the live settings untouched.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _original;

    [ObservableProperty]
    private string _apiKey;

    [ObservableProperty]
    private LanguageOption _language;

    [ObservableProperty]
    private bool _renameEnabled;

    [ObservableProperty]
    private SeparatorChoice _separator;

    [ObservableProperty]
    private string _artworkSize;

    [ObservableProperty]
    private LanguageChoice _appLanguage;

    [ObservableProperty]
    private ThemeChoice _theme;

    [ObservableProperty]
    private ArtworkKindChoice _tvArtwork;

    [ObservableProperty]
    private ArtworkKindChoice _movieArtwork;

    [ObservableProperty]
    private bool _createBackup;

    [ObservableProperty]
    private ResolutionFloorChoice _omitResolutionAtOrBelow;

    /// <summary>Result of the Strings.Get("settings.testKey") button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyStatus))]
    private string? _keyStatus;

    [ObservableProperty]
    private bool _keyIsValid;

    [ObservableProperty]
    private bool _isTestingKey;

    public SettingsViewModel(AppSettings settings)
    {
        _original = settings;
        _apiKey = settings.TmdbApiKey;
        _language = LanguageCatalog.Find(settings.Language);
        _renameEnabled = settings.RenameEnabled;
        _artworkSize = settings.ArtworkSize;
        _createBackup = settings.CreateBackup;
        _appLanguage = AppLanguages.FirstOrDefault(l => l.Tag == settings.AppLanguage) ?? AppLanguages[0];
        _theme = Themes.FirstOrDefault(t => t.Theme == settings.Theme) ?? Themes[0];
        _tvArtwork = TvArtworkKinds.First(c => c.Kind == settings.TvArtwork);
        _movieArtwork = MovieArtworkKinds.First(c => c.Kind == settings.MovieArtwork);
        _separator = Separators.FirstOrDefault(s => s.Style == settings.Separator) ?? Separators[0];
        _omitResolutionAtOrBelow =
            ResolutionFloors.FirstOrDefault(c => c.Value == settings.OmitResolutionAtOrBelow)
            ?? ResolutionFloors[0];

        MovieTemplate = new RenameTemplateEditorViewModel(
            Strings.Get("settings.movies"), MediaKind.Movie, settings.MovieRenameTemplate, AppSettings.DefaultMovieTemplate,
            RenameEngine.SampleMovie, settings.Separator, settings.OmitResolutionAtOrBelow);

        TvTemplate = new RenameTemplateEditorViewModel(
            Strings.Get("settings.tvShows"), MediaKind.TvEpisode, settings.TvRenameTemplate, AppSettings.DefaultTvTemplate,
            RenameEngine.SampleEpisode, settings.Separator, settings.OmitResolutionAtOrBelow);
    }

    public RenameTemplateEditorViewModel MovieTemplate { get; }

    public RenameTemplateEditorViewModel TvTemplate { get; }

    public IReadOnlyList<LanguageOption> Languages => LanguageCatalog.All;

    public IReadOnlyList<SeparatorChoice> Separators { get; } =
    [
        new(SeparatorStyle.Space, Strings.Get("settings.sepSpace")),
        new(SeparatorStyle.Dot, Strings.Get("settings.sepDot")),
        new(SeparatorStyle.Underscore, Strings.Get("settings.sepUnderscore")),
        new(SeparatorStyle.Dash, Strings.Get("settings.sepDash")),
    ];

    /// <summary>
    /// Resolutions a name may leave out. Ordered smallest first so the list reads as a ladder,
    /// with "never" — the default — at the top.
    /// </summary>
    public IReadOnlyList<ResolutionFloorChoice> ResolutionFloors { get; } =
    [
        new(string.Empty, Strings.Get("settings.omitResolutionNever")),
        // The direction rides on the option rather than the label above it, so it is still
        // there once the dropdown is closed and only the chosen value shows.
        .. VideoResolution.Ladder.Select(label =>
            new ResolutionFloorChoice(label, Strings.Format("settings.omitResolutionOption", label))),
    ];

    /// <summary>TMDB image widths, largest first. Bigger artwork means bigger files.</summary>
    public IReadOnlyList<string> ArtworkSizes { get; } = ["original", "w780", "w500", "w342", "w185"];

    /// <summary>Languages this window and the rest of the interface can be shown in.</summary>
    public IReadOnlyList<LanguageChoice> AppLanguages { get; } = Strings.Available;

    public IReadOnlyList<ThemeChoice> Themes { get; } = ThemeCatalog.All;

    public IReadOnlyList<ArtworkKindChoice> TvArtworkKinds { get; } =
        [.. ArtworkKinds.ForTv.Select(ArtworkKindChoice.For)];

    public IReadOnlyList<ArtworkKindChoice> MovieArtworkKinds { get; } =
        [.. ArtworkKinds.ForMovies.Select(ArtworkKindChoice.For)];

    public bool HasKeyStatus => !string.IsNullOrWhiteSpace(KeyStatus);

    /// <summary>Templates must parse before the dialog can be saved.</summary>
    public bool CanSave => !MovieTemplate.HasErrors && !TvTemplate.HasErrors;

    public string SettingsFileNote { get; } = Strings.Format("settings.storedIn", SettingsStore.DefaultFilePath());

    /// <summary>Checks the key against TMDB so a typo is caught here rather than on every file.</summary>
    [RelayCommand]
    private async Task TestKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            KeyStatus = Strings.Get("settings.enterKeyFirst");
            KeyIsValid = false;
            return;
        }

        IsTestingKey = true;
        KeyStatus = null;
        try
        {
            using var probe = new TmdbService(ApiKey.Trim());
            var failure = await probe.ValidateApiKeyAsync();
            KeyIsValid = failure is null;
            KeyStatus = failure ?? Strings.Get("settings.keyWorks");
        }
        catch (Exception e)
        {
            KeyIsValid = false;
            KeyStatus = e.Message;
        }
        finally
        {
            IsTestingKey = false;
        }
    }

    partial void OnSeparatorChanged(SeparatorChoice value)
    {
        // Both previews show the separator, so keep them in step with the dropdown.
        MovieTemplate.Separator = value.Style;
        TvTemplate.Separator = value.Style;
    }

    partial void OnOmitResolutionAtOrBelowChanged(ResolutionFloorChoice value)
    {
        MovieTemplate.OmitResolutionAtOrBelow = value.Value;
        TvTemplate.OmitResolutionAtOrBelow = value.Value;
    }

    /// <summary>Folds the edits back into a settings object ready to persist.</summary>
    public AppSettings ToSettings()
    {
        var settings = _original.Clone();
        settings.TmdbApiKey = ApiKey.Trim();
        settings.Language = Language.Tag;
        settings.RenameEnabled = RenameEnabled;
        settings.Separator = Separator.Style;
        settings.AppLanguage = AppLanguage.Tag;
        settings.Theme = Theme.Theme;
        settings.ArtworkSize = ArtworkSize;
        settings.TvArtwork = TvArtwork.Kind;
        settings.MovieArtwork = MovieArtwork.Kind;
        settings.CreateBackup = CreateBackup;
        settings.OmitResolutionAtOrBelow = OmitResolutionAtOrBelow.Value;
        settings.MovieRenameTemplate = MovieTemplate.Template;
        settings.TvRenameTemplate = TvTemplate.Template;
        return settings;
    }
}
