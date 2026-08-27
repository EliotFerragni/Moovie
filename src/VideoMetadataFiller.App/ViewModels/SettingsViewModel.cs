using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Settings;
using VideoMetadataFiller.Core.Tmdb;
using VideoMetadataFiller.Core.Writing;

namespace VideoMetadataFiller.App.ViewModels;

/// <summary>A separator choice offered in the dropdown.</summary>
public sealed record SeparatorChoice(SeparatorStyle Style, string DisplayName)
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
    private bool _createBackup;

    /// <summary>Result of the "Test key" button.</summary>
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
        _separator = Separators.FirstOrDefault(s => s.Style == settings.Separator) ?? Separators[0];

        MovieTemplate = new RenameTemplateEditorViewModel(
            "Movies", MediaKind.Movie, settings.MovieRenameTemplate, AppSettings.DefaultMovieTemplate,
            RenameEngine.SampleMovie, settings.Separator);

        TvTemplate = new RenameTemplateEditorViewModel(
            "TV shows", MediaKind.TvEpisode, settings.TvRenameTemplate, AppSettings.DefaultTvTemplate,
            RenameEngine.SampleEpisode, settings.Separator);
    }

    public RenameTemplateEditorViewModel MovieTemplate { get; }

    public RenameTemplateEditorViewModel TvTemplate { get; }

    public IReadOnlyList<LanguageOption> Languages => LanguageCatalog.All;

    public IReadOnlyList<SeparatorChoice> Separators { get; } =
    [
        new(SeparatorStyle.Space, "Spaces  —  Blade Runner (2017).mp4"),
        new(SeparatorStyle.Dot, "Dots  —  Blade.Runner.(2017).mp4"),
        new(SeparatorStyle.Underscore, "Underscores  —  Blade_Runner_(2017).mp4"),
        new(SeparatorStyle.Dash, "Dashes  —  Blade-Runner-(2017).mp4"),
    ];

    /// <summary>TMDB image widths, largest first. Bigger artwork means bigger files.</summary>
    public IReadOnlyList<string> ArtworkSizes { get; } = ["original", "w780", "w500", "w342", "w185"];

    public bool HasKeyStatus => !string.IsNullOrWhiteSpace(KeyStatus);

    /// <summary>Templates must parse before the dialog can be saved.</summary>
    public bool CanSave => !MovieTemplate.HasErrors && !TvTemplate.HasErrors;

    public string SettingsFileNote { get; } = $"Settings are stored in {SettingsStore.DefaultFilePath()}";

    /// <summary>Checks the key against TMDB so a typo is caught here rather than on every file.</summary>
    [RelayCommand]
    private async Task TestKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            KeyStatus = "Enter a key first.";
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
            KeyStatus = failure ?? "That key works.";
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

    /// <summary>Folds the edits back into a settings object ready to persist.</summary>
    public AppSettings ToSettings()
    {
        var settings = _original.Clone();
        settings.TmdbApiKey = ApiKey.Trim();
        settings.Language = Language.Tag;
        settings.RenameEnabled = RenameEnabled;
        settings.Separator = Separator.Style;
        settings.ArtworkSize = ArtworkSize;
        settings.CreateBackup = CreateBackup;
        settings.MovieRenameTemplate = MovieTemplate.Template;
        settings.TvRenameTemplate = TvTemplate.Template;
        return settings;
    }
}
