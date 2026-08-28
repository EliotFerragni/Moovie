using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Tmdb;

namespace VideoMetadataFiller.App.ViewModels;

/// <summary>
/// What the preview pane needs from the main window: TMDB access and the lookup operations that
/// belong to the batch as a whole. Keeps the pane free of scanning and applying logic.
/// </summary>
public interface IMediaLookup
{
    /// <summary>TMDB access, or null when no API key is configured yet.</summary>
    ITmdbService? Tmdb { get; }

    /// <summary>The default language for lookups.</summary>
    string Language { get; }

    /// <summary>Languages offered in the per-file dropdown.</summary>
    IReadOnlyList<LanguageOption> Languages { get; }

    /// <summary>
    /// TMDB image size for artwork. The preview pane downloads this same size, so the pane shows
    /// the image that will actually be embedded rather than a stand-in.
    /// </summary>
    string ArtworkSize { get; }

    /// <summary>The preferred artwork kind for a medium, used to mark the current pick.</summary>
    ArtworkKind PreferredArtwork(MediaKind kind);

    /// <summary>Applies an artwork the user picked by hand, replacing whatever was chosen for them.</summary>
    void SetArtwork(FileItemViewModel file, string artworkPath);

    /// <summary>Applies a user-chosen title to a file and fetches its full metadata.</summary>
    Task ChooseCandidateAsync(FileItemViewModel file, Candidate candidate);

    /// <summary>Re-fetches a file's metadata, keeping fields the user has edited by hand.</summary>
    Task RefetchAsync(FileItemViewModel file);

    /// <summary>Looks up a title by its TMDB id when searching by name got nowhere.</summary>
    Task ApplyTmdbIdAsync(FileItemViewModel file, int tmdbId, MediaKind kind);

    /// <summary>A file's metadata changed, so the rename preview and counters need refreshing.</summary>
    void NotifyMetadataChanged(FileItemViewModel file);
}

/// <summary>A language the user can pick, for the global setting and the per-file override.</summary>
public sealed record LanguageOption(string Tag, string DisplayName)
{
    public override string ToString() => DisplayName;
}
