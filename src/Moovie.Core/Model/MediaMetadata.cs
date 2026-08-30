namespace Moovie.Core.Model;

/// <summary>
/// The editable metadata for one file: seeded from TMDB, then freely modified by the user
/// before being written into the container. Mutable by design: the UI binds straight to it.
/// </summary>
public sealed class MediaMetadata
{
    public MediaKind Kind { get; set; } = MediaKind.Unknown;

    /// <summary>Movie title, or the episode title for a TV episode.</summary>
    public string? Title { get; set; }

    public string? OriginalTitle { get; set; }

    /// <summary>Show name. TV only.</summary>
    public string? ShowName { get; set; }

    public int? Season { get; set; }

    /// <summary>Episode numbers covered by this file (several for a multi-episode file).</summary>
    public List<int> Episodes { get; set; } = [];

    public DateTime? ReleaseDate { get; set; }

    /// <summary>Release year. Kept separate so a user can set a year without a full date.</summary>
    public int? Year { get; set; }

    public string? Overview { get; set; }

    public List<string> Genres { get; set; } = [];

    public List<string> Cast { get; set; } = [];

    public List<string> Directors { get; set; } = [];

    public List<string> Writers { get; set; } = [];

    public string? Studio { get; set; }

    public string? Network { get; set; }

    /// <summary>Certification such as <c>PG-13</c> or <c>TV-MA</c>.</summary>
    public string? ContentRating { get; set; }

    public int? TmdbId { get; set; }

    public string? ImdbId { get; set; }

    /// <summary>TMDB-relative artwork path: whichever image is currently chosen for this file.</summary>
    public string? ArtworkPath { get; set; }

    /// <summary>
    /// Every artwork kind TMDB offered for this title, so the preferred-artwork setting can be
    /// honoured without another round trip. Populated during the lookup.
    /// </summary>
    public Dictionary<ArtworkKind, string> ArtworkByKind { get; set; } = [];

    /// <summary>Downloaded artwork bytes, filled in just before writing.</summary>
    public byte[]? ArtworkData { get; set; }

    /// <summary>Resolution token carried over from the source filename, for rename templates.</summary>
    public string? Resolution { get; set; }

    /// <summary>Language the TMDB text fields were fetched in, e.g. <c>fr-FR</c>.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Names of fields TMDB had no translation for, which were filled from the original language
    /// instead. The preview pane hints at these so a blank-looking translation is explained.
    /// </summary>
    public List<string> FallbackFields { get; set; } = [];

    public int? FirstEpisode => Episodes.Count > 0 ? Episodes[0] : null;

    /// <summary>Best available year: the explicit one, else the year of the release date.</summary>
    public int? EffectiveYear => Year ?? ReleaseDate?.Year;

    public MediaMetadata Clone() => new()
    {
        Kind = Kind,
        Title = Title,
        OriginalTitle = OriginalTitle,
        ShowName = ShowName,
        Season = Season,
        Episodes = [.. Episodes],
        ReleaseDate = ReleaseDate,
        Year = Year,
        Overview = Overview,
        Genres = [.. Genres],
        Cast = [.. Cast],
        Directors = [.. Directors],
        Writers = [.. Writers],
        Studio = Studio,
        Network = Network,
        ContentRating = ContentRating,
        TmdbId = TmdbId,
        ImdbId = ImdbId,
        ArtworkPath = ArtworkPath,
        ArtworkByKind = new Dictionary<ArtworkKind, string>(ArtworkByKind),
        ArtworkData = ArtworkData,
        Resolution = Resolution,
        Language = Language,
        FallbackFields = [.. FallbackFields],
    };
}
