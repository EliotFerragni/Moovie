namespace Moovie.Core.Model;

/// <summary>One image TMDB has for a title, as offered in the artwork picker.</summary>
public sealed record ArtworkOption
{
    public required ArtworkKind Kind { get; init; }

    /// <summary>TMDB-relative path, e.g. <c>/abc123.jpg</c>.</summary>
    public required string Path { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Two-letter tag of any text burned into the image, or null when it has none.</summary>
    public string? Language { get; init; }

    public double VoteAverage { get; init; }

    public string KindLabel => ArtworkKinds.Label(Kind);

    public string Dimensions => Width > 0 && Height > 0 ? $"{Width}×{Height}" : string.Empty;
}
