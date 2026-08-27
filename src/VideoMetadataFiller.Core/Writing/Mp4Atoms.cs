using TagLib;

namespace VideoMetadataFiller.Core.Writing;

/// <summary>
/// The iTunes-style MP4 atoms this app writes. Names beginning <c>0xA9</c> are the classic
/// "©" atoms; the rest are plain four-character codes.
/// </summary>
/// <remarks>
/// These particular atoms are what Plex, Jellyfin, Emby, Infuse and the Apple TV app read.
/// <c>stik</c> matters most: without it a player has no idea whether the file is a film or an
/// episode, and ignores the TV atoms entirely.
/// </remarks>
internal static class Mp4Atoms
{
    private const byte Copyright = 0xA9;

    private static ReadOnlyByteVector Fourcc(string code) =>
        new((byte)code[0], (byte)code[1], (byte)code[2], (byte)code[3]);

    private static ReadOnlyByteVector CopyrightFourcc(string code) =>
        new(Copyright, (byte)code[0], (byte)code[1], (byte)code[2]);

    /// <summary>Title — the episode title for a TV file.</summary>
    internal static readonly ReadOnlyByteVector Title = CopyrightFourcc("nam");

    /// <summary>Release or air date.</summary>
    internal static readonly ReadOnlyByteVector Date = CopyrightFourcc("day");

    internal static readonly ReadOnlyByteVector Genre = CopyrightFourcc("gen");

    internal static readonly ReadOnlyByteVector Artist = CopyrightFourcc("ART");

    internal static readonly ReadOnlyByteVector AlbumArtist = Fourcc("aART");

    internal static readonly ReadOnlyByteVector Album = CopyrightFourcc("alb");

    internal static readonly ReadOnlyByteVector Writer = CopyrightFourcc("wrt");

    internal static readonly ReadOnlyByteVector Comment = CopyrightFourcc("cmt");

    /// <summary>Short description, capped at 255 characters by convention.</summary>
    internal static readonly ReadOnlyByteVector ShortDescription = Fourcc("desc");

    /// <summary>Long description — the full overview.</summary>
    internal static readonly ReadOnlyByteVector LongDescription = Fourcc("ldes");

    /// <summary>Media type: 9 = movie, 10 = TV show.</summary>
    internal static readonly ReadOnlyByteVector MediaType = Fourcc("stik");

    internal static readonly ReadOnlyByteVector ShowName = Fourcc("tvsh");

    internal static readonly ReadOnlyByteVector SeasonNumber = Fourcc("tvsn");

    internal static readonly ReadOnlyByteVector EpisodeNumber = Fourcc("tves");

    /// <summary>Episode id, a free-text label such as "S01E02".</summary>
    internal static readonly ReadOnlyByteVector EpisodeId = Fourcc("tven");

    internal static readonly ReadOnlyByteVector Network = Fourcc("tvnn");

    /// <summary>HD flag: 0 = SD, 1 = 720p, 2 = 1080p, 3 = 2160p.</summary>
    internal static readonly ReadOnlyByteVector HdVideo = Fourcc("hdvd");

    /// <summary>Content advisory: 0 = none, 1 = explicit, 2 = clean.</summary>
    internal static readonly ReadOnlyByteVector Advisory = Fourcc("rtng");

    internal static readonly ReadOnlyByteVector SortAlbum = Fourcc("soal");

    internal const string DashBoxMean = "com.apple.iTunes";

    /// <summary>Cast, directors and writers, as an Apple property-list blob.</summary>
    internal const string MovieInfoName = "iTunMOVI";

    /// <summary>Content rating string, e.g. <c>us-tv|TV-MA|500|</c>.</summary>
    internal const string ContentRatingName = "iTunEXTC";

    /// <summary>Media type values for <see cref="MediaType"/>.</summary>
    internal const byte MediaTypeMovie = 9;

    internal const byte MediaTypeTvShow = 10;

    /// <summary>
    /// Apple's "well-known type" for a big-endian signed integer. TagLib# spells this
    /// <c>FlagType.ForTempo</c> because the tempo atom was the first integer it supported.
    /// </summary>
    internal const uint IntegerFlag = (uint)TagLib.Mpeg4.AppleDataBox.FlagType.ForTempo;
}
