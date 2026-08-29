using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Moovie.Core.Model;

namespace Moovie.App.ViewModels;

/// <summary>One image in the artwork picker.</summary>
public sealed partial class ArtworkChoiceViewModel(ArtworkOption option, bool isCurrent) : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _thumbnail;

    /// <summary>Marks the image the file is using now, so the grid shows where you are.</summary>
    [ObservableProperty]
    private bool _isCurrent = isCurrent;

    public ArtworkOption Option { get; } = option;

    public string Path => Option.Path;

    public string KindLabel => Option.KindLabel;

    /// <summary>Kind plus size, e.g. "Season poster · 2000×3000".</summary>
    public string Caption => Option.Dimensions.Length == 0
        ? Option.KindLabel
        : $"{Option.KindLabel} · {Option.Dimensions}";

    /// <summary>Language of any text burned into the image, upper-cased, or null for textless art.</summary>
    public string? LanguageTag => Option.Language?.ToUpperInvariant();

    public bool HasLanguage => Option.Language is not null;

    /// <summary>Stills and backdrops are 16:9; posters are 2:3. Drives the cell size in the grid.</summary>
    public bool IsWide => ArtworkKinds.IsWide(Option.Kind);

    public double CellWidth => IsWide ? 160 : 92;

    public double CellHeight => IsWide ? 90 : 138;
}
