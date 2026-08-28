namespace VideoMetadataFiller.Core.Model;

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
