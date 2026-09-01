namespace Moovie.Core.Model;

/// <summary>
/// Turns the user's preferred artwork kind into an actual image path. The preference is a wish
/// rather than a guarantee: plenty of episodes have no still, and older shows have no season
/// poster, so a missing kind falls through to the next best one for that medium.
/// </summary>
public static class ArtworkSelector
{
    public static string? Resolve(MediaMetadata metadata, ArtworkKind preferred)
    {
        foreach (var kind in Order(metadata.Kind, preferred))
        {
            if (metadata.ArtworkByKind.TryGetValue(kind, out var path) && !string.IsNullOrWhiteSpace(path))
                return path;
        }

        // A kind outside this medium's usual set is still better than no artwork at all.
        return metadata.ArtworkByKind.Values.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
    }

    /// <summary>
    /// Whether TMDB gave every artwork kind this medium can have. Artwork is not a translation, so
    /// a season can have a full French entry and no French poster, leaving <see cref="Resolve"/>
    /// to drop quietly to another kind. A caller uses this to decide whether the original-language
    /// record is worth fetching too.
    /// </summary>
    public static bool HasEveryKind(MediaMetadata metadata)
    {
        var expected = metadata.Kind == MediaKind.Movie ? ArtworkKinds.ForMovies : ArtworkKinds.ForTv;
        return expected.All(metadata.ArtworkByKind.ContainsKey);
    }

    /// <summary>
    /// Adds the kinds <paramref name="target"/> is missing from <paramref name="source"/>.
    /// Whatever the requested language did supply always wins, so a French poster is never
    /// replaced by an English one.
    /// </summary>
    public static void FillMissingKinds(MediaMetadata target, MediaMetadata source)
    {
        foreach (var (kind, path) in source.ArtworkByKind)
        {
            if (!string.IsNullOrWhiteSpace(path))
                target.ArtworkByKind.TryAdd(kind, path);
        }

        if (string.IsNullOrWhiteSpace(target.ArtworkPath) && !string.IsNullOrWhiteSpace(source.ArtworkPath))
            target.ArtworkPath = source.ArtworkPath;
    }

    private static IEnumerable<ArtworkKind> Order(MediaKind media, ArtworkKind preferred)
    {
        yield return preferred;

        var rest = media == MediaKind.Movie ? ArtworkKinds.ForMovies : ArtworkKinds.ForTv;
        foreach (var kind in rest)
        {
            if (kind != preferred)
                yield return kind;
        }
    }
}
