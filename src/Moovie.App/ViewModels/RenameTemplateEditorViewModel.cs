using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moovie.Core.Localization;
using Moovie.Core.Model;
using Moovie.Core.Settings;
using Moovie.Core.Writing;

namespace Moovie.App.ViewModels;

/// <summary>
/// A clickable token in the palette under a template box, and the reference for it: every form
/// it can be written in, each shown against what it would produce for this template's sample.
/// </summary>
/// <param name="Forms">
/// Pre-aligned into two columns and shown in a monospace tooltip, so the formats a token takes
/// can be read off the palette instead of guessed at or looked up.
/// </param>
public sealed record TokenChip(string Caption, string Insertion, string Description, string Forms);

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

    /// <summary>The tokens the palette shows, in the order <see cref="Tokens"/> holds them.</summary>
    private readonly IReadOnlyList<RenameToken> _paletteTokens;
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

    /// <summary>
    /// The same template rendered for a file at the omission threshold, shown only when one is
    /// set. Without it the setting is invisible here — the samples are 2160p, so nothing in the
    /// preview moves — and a template writing <c>[{resolution}]</c> outside an optional section
    /// would leave empty brackets behind with nothing to warn you.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThresholdPreview))]
    private string? _thresholdPreview;

    [ObservableProperty]
    private int _seasonDigits = 2;

    [ObservableProperty]
    private int _episodeDigits = 2;

    [ObservableProperty]
    private SeparatorStyle _separator;

    /// <summary>Mirrors the setting above, so the preview shows what the threshold does.</summary>
    [ObservableProperty]
    private string? _omitResolutionAtOrBelow;

    public RenameTemplateEditorViewModel(
        string title, MediaKind kind, string template, string defaultTemplate,
        MediaMetadata sample, SeparatorStyle separator, string? omitResolutionAtOrBelow = null)
    {
        Title = title;
        _kind = kind;
        _sample = sample;
        _defaultTemplate = defaultTemplate;
        _template = template;
        _separator = separator;
        _omitResolutionAtOrBelow = omitResolutionAtOrBelow;

        // {ext} is added automatically when a template leaves it out, so the palette does not
        // offer it.
        _paletteTokens = [.. RenameTokens.All.Where(t => t.IsRelevantTo(kind) && t.Name != "ext")];
        Tokens =
        [
            .. _paletteTokens.Select(t =>
                new TokenChip($"{{{t.Name}}}", t.Insertion, t.Description, DescribeForms(t))),
        ];

        ReadDigitsFromTemplate();
        Revalidate();
    }

    /// <summary>
    /// Lists every form of <paramref name="token"/> beside what it renders for the sample, so
    /// the difference between <c>:upper</c> and <c>:title</c> is shown rather than described.
    /// </summary>
    private string DescribeForms(RenameToken token)
    {
        var forms = token.OfferedFormats
            .Select(format => (Written: token.Written(format), Value: RenderSample(token, format)))
            .ToList();

        // Padded here rather than laid out in the view: a monospace tooltip lines the columns up
        // with no measuring, and the tooltip stays a plain string.
        var width = forms.Max(f => f.Written.Length);
        var lines = forms.Select(f => $"{f.Written.PadRight(width)}   {f.Value}");

        return token.HasOpenEndedFormats
            ? string.Join('\n', lines) + "\n" + Strings.Get("settings.anyDatePattern")
            : string.Join('\n', lines);
    }

    private string RenderSample(RenameToken token, string? format)
    {
        var rendered = RenameTemplate.Parse(token.Written(format))
            .Render(_sample, ".mp4", OmitResolutionAtOrBelow);

        // A sample with nothing in that field says so, rather than trailing off into blank space.
        return string.IsNullOrEmpty(rendered) ? "—" : rendered;
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

    partial void OnOmitResolutionAtOrBelowChanged(string? value)
    {
        RefreshTokenForms();
        Revalidate();
    }

    /// <summary>Re-renders the palette's examples, which show the threshold at work too.</summary>
    private void RefreshTokenForms()
    {
        for (var i = 0; i < Tokens.Count; i++)
            Tokens[i] = Tokens[i] with { Forms = DescribeForms(_paletteTokens[i]) };
    }

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

    public bool HasThresholdPreview => !string.IsNullOrEmpty(ThresholdPreview);

    private void Revalidate()
    {
        var parsed = RenameTemplate.Parse(Template);
        Errors = parsed.Validation.IsValid ? null : string.Join("  ", parsed.Validation.Errors);
        Preview = parsed.Validation.IsValid
            ? RenameEngine.BuildFileName(parsed, _sample, ".mp4", Separator, OmitResolutionAtOrBelow)
            : "—";
        ThresholdPreview = BuildThresholdPreview(parsed);
    }

    private string? BuildThresholdPreview(RenameTemplate parsed)
    {
        if (!parsed.Validation.IsValid || string.IsNullOrEmpty(OmitResolutionAtOrBelow))
            return null;

        var atThreshold = _sample.Clone();
        atThreshold.Resolution = OmitResolutionAtOrBelow;
        var name = RenameEngine.BuildFileName(
            parsed, atThreshold, ".mp4", Separator, OmitResolutionAtOrBelow);

        // A template with no resolution in it renders the same either way, so there is nothing
        // to show and a second identical line would only be noise.
        return name == Preview
            ? null
            : Strings.Format("settings.previewAtThreshold", OmitResolutionAtOrBelow, name);
    }
}
