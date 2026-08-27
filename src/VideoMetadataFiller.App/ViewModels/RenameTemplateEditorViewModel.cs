using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Settings;
using VideoMetadataFiller.Core.Writing;

namespace VideoMetadataFiller.App.ViewModels;

/// <summary>A clickable token in the palette under a template box.</summary>
public sealed record TokenChip(string Caption, string Insertion, string Tooltip);

/// <summary>
/// Editor for one rename template, with a token palette, digit spinners and a live preview.
/// </summary>
/// <remarks>
/// The point of the palette and the spinners is that the common adjustments — which fields appear,
/// and how many digits the season and episode get — never require knowing the template syntax. The
/// text box is still there for anything more involved.
/// </remarks>
public sealed partial class RenameTemplateEditorViewModel : ObservableObject
{
    private readonly MediaKind _kind;
    private readonly MediaMetadata _sample;
    private readonly string _defaultTemplate;

    /// <summary>Set while a spinner is rewriting the text, so the change is not read back as manual.</summary>
    private bool _updatingFromSpinner;

    [ObservableProperty]
    private string _template;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    private string? _errors;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private int _seasonDigits = 2;

    [ObservableProperty]
    private int _episodeDigits = 2;

    [ObservableProperty]
    private SeparatorStyle _separator;

    public RenameTemplateEditorViewModel(
        string title, MediaKind kind, string template, string defaultTemplate,
        MediaMetadata sample, SeparatorStyle separator)
    {
        Title = title;
        _kind = kind;
        _sample = sample;
        _defaultTemplate = defaultTemplate;
        _template = template;
        _separator = separator;

        Tokens =
        [
            .. RenameTokens.All
                .Where(t => t.IsRelevantTo(kind) && t.Name != "ext")
                .Select(t => new TokenChip($"{{{t.Name}}}", t.Insertion, t.Description)),
        ];

        ReadDigitsFromTemplate();
        Revalidate();
    }

    public string Title { get; }

    public ObservableCollection<TokenChip> Tokens { get; }

    /// <summary>Digit choices offered by the spinners.</summary>
    public IReadOnlyList<int> DigitOptions { get; } = [1, 2, 3, 4];

    /// <summary>Only TV templates have a season and an episode to pad.</summary>
    public bool ShowsDigitSpinners => _kind == MediaKind.TvEpisode;

    public bool HasErrors => !string.IsNullOrEmpty(Errors);

    /// <summary>
    /// Raised when a palette chip is clicked. The view handles it, because only the view knows
    /// where the caret is.
    /// </summary>
    public event Action<string>? InsertRequested;

    [RelayCommand]
    private void InsertToken(TokenChip? chip)
    {
        if (chip is not null)
            InsertRequested?.Invoke(chip.Insertion);
    }

    [RelayCommand]
    private void ResetToDefault() => Template = _defaultTemplate;

    partial void OnTemplateChanged(string value)
    {
        if (!_updatingFromSpinner)
            ReadDigitsFromTemplate();
        Revalidate();
    }

    partial void OnSeparatorChanged(SeparatorStyle value) => Revalidate();

    partial void OnSeasonDigitsChanged(int value) => ApplyDigits("season", value);

    partial void OnEpisodeDigitsChanged(int value) => ApplyDigits("episode", value);

    /// <summary>Rewrites a numeric token's format in place, so the spinner needs no syntax knowledge.</summary>
    private void ApplyDigits(string token, int digits)
    {
        if (_updatingFromSpinner || digits < 1)
            return;

        var pattern = $@"\{{{token}(?::[^}}]*)?\}}";
        if (!Regex.IsMatch(Template, pattern, RegexOptions.IgnoreCase))
            return;

        var replacement = $"{{{token}:{new string('0', digits)}}}";
        _updatingFromSpinner = true;
        try
        {
            Template = Regex.Replace(Template, pattern, replacement, RegexOptions.IgnoreCase);
        }
        finally
        {
            _updatingFromSpinner = false;
        }
    }

    /// <summary>Keeps the spinners in step when the template is edited by hand.</summary>
    private void ReadDigitsFromTemplate()
    {
        _updatingFromSpinner = true;
        try
        {
            SeasonDigits = DigitsOf("season") ?? SeasonDigits;
            EpisodeDigits = DigitsOf("episode") ?? EpisodeDigits;
        }
        finally
        {
            _updatingFromSpinner = false;
        }
    }

    private int? DigitsOf(string token)
    {
        var match = Regex.Match(Template, $@"\{{{token}:(?<format>[^}}]*)\}}", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        var zeros = match.Groups["format"].Value.Count(c => c == '0');
        return zeros is >= 1 and <= 4 ? zeros : null;
    }

    private void Revalidate()
    {
        var parsed = RenameTemplate.Parse(Template);
        Errors = parsed.Validation.IsValid ? null : string.Join("  ", parsed.Validation.Errors);
        Preview = parsed.Validation.IsValid
            ? RenameEngine.BuildFileName(parsed, _sample, ".mp4", Separator)
            : "—";
    }
}
