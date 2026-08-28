using CommunityToolkit.Mvvm.ComponentModel;
using VideoMetadataFiller.Core.Localization;
using VideoMetadataFiller.Core.Model;

namespace VideoMetadataFiller.App.ViewModels;

/// <summary>Which kinds of file a field is meaningful for.</summary>
public enum FieldScope
{
    Both,
    MoviesOnly,
    ShowsOnly,
}

/// <summary>
/// One editable metadata field, bound to however many files are selected.
/// </summary>
/// <remarks>
/// This is what makes multi-selection editing work. On every selection change the editor reads the
/// field from each selected file: if they all agree it shows that value, otherwise it blanks out and
/// says so. Typing a value writes it to every selected file at once and marks each of them edited.
/// <para>
/// Everything is handled as text, including numbers and dates, so the whole form can be built from
/// one list and one template. The typed round-trip lives in the read/write pair each field is
/// constructed with.
/// </para>
/// </remarks>
public sealed partial class FieldEditor : ObservableObject
{
    /// <summary>Shown instead of a value when the selected files disagree.</summary>
    public static string MultipleValuesWatermark => Strings.Get("pane.multipleValues");

    private readonly Func<MediaMetadata, string?> _read;
    private readonly Action<MediaMetadata, string?> _write;
    private readonly Action<FileItemViewModel, string> _onEdited;

    private IReadOnlyList<FileItemViewModel> _targets = [];

    /// <summary>True while the editor is being refilled from the selection, so writes are not echoed back.</summary>
    private bool _rebinding;

    [ObservableProperty]
    private string? _value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Watermark))]
    private bool _hasMultipleValues;

    /// <summary>False for fields that make no sense to edit across a multi-selection.</summary>
    [ObservableProperty]
    private bool _isEditable = true;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>True when TMDB had no translation for this field and it was filled from English.</summary>
    [ObservableProperty]
    private bool _isFallback;

    /// <summary>True when the user typed this value rather than TMDB supplying it.</summary>
    [ObservableProperty]
    private bool _isUserEdited;

    public FieldEditor(
        string key,
        string label,
        Func<MediaMetadata, string?> read,
        Action<MediaMetadata, string?> write,
        Action<FileItemViewModel, string> onEdited,
        FieldScope scope = FieldScope.Both,
        bool isMultiline = false,
        bool perFileOnly = false,
        string? hint = null)
    {
        Key = key;
        Label = label;
        Scope = scope;
        IsMultiline = isMultiline;
        PerFileOnly = perFileOnly;
        Hint = hint;
        _read = read;
        _write = write;
        _onEdited = onEdited;
    }

    /// <summary>Field name, matching <see cref="MediaMetadata"/> so fallback hints can be looked up.</summary>
    public string Key { get; }

    public string Label { get; }

    public FieldScope Scope { get; }

    public bool IsMultiline { get; }

    /// <summary>
    /// True for fields that are inherently per-file — the episode number, above all. They are shown
    /// read-only while several files are selected rather than silently stamping one value over a
    /// whole season.
    /// </summary>
    public bool PerFileOnly { get; }

    /// <summary>Extra guidance shown under the field, e.g. how a list is separated.</summary>
    public string? Hint { get; }

    public bool HasHint => !string.IsNullOrEmpty(Hint);

    public string? Watermark => HasMultipleValues ? MultipleValuesWatermark : null;

    /// <summary>
    /// Refills the editor from <paramref name="targets"/>, showing the shared value when they all
    /// agree and blanking out when they do not.
    /// </summary>
    public void Rebind(IReadOnlyList<FileItemViewModel> targets, MediaKind selectionKind)
    {
        _targets = targets;
        _rebinding = true;
        try
        {
            IsVisible = Scope switch
            {
                FieldScope.MoviesOnly => selectionKind != MediaKind.TvEpisode,
                FieldScope.ShowsOnly => selectionKind == MediaKind.TvEpisode,
                _ => true,
            };

            IsEditable = targets.Count == 1 || !PerFileOnly;

            if (targets.Count == 0)
            {
                Value = null;
                HasMultipleValues = false;
                IsFallback = false;
                IsUserEdited = false;
                return;
            }

            var values = targets.Select(t => Normalize(_read(t.Metadata))).ToList();
            var shared = values.Distinct(StringComparer.Ordinal).Count() == 1;

            HasMultipleValues = !shared;
            Value = shared ? values[0] : null;
            IsFallback = targets.Any(t => t.Metadata.FallbackFields.Contains(Key));
            IsUserEdited = targets.Any(t => t.IsFieldUserEdited(Key));
        }
        finally
        {
            _rebinding = false;
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    partial void OnValueChanged(string? value)
    {
        if (_rebinding || _targets.Count == 0)
            return;

        // The user typed: fan the value out to every selected file.
        foreach (var target in _targets)
        {
            _write(target.Metadata, Normalize(value));
            _onEdited(target, Key);
        }

        HasMultipleValues = false;
        IsUserEdited = true;
        IsFallback = false;
    }
}
