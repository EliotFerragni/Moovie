using System.Text.RegularExpressions;

namespace Moovie.Core.Parsing;

/// <summary>
/// The vocabulary of release-metadata noise that shows up in scene and web filenames.
/// This is the single place to tune parsing quality.
/// </summary>
/// <remarks>
/// Tokens are split into two tiers because plenty of release words are also real title
/// words: "Charlotte's Web", "The Italian Job", "Uncut Gems", "DC League of Super-Pets".
/// <list type="bullet">
/// <item><b>Strong</b> tokens never appear in a title, so the parser cuts the name at the
/// earliest one it finds.</item>
/// <item><b>Weak</b> tokens are ambiguous, so they are only removed from the <i>tail</i> of
/// what is left: by then a strong token or the year has already established that we are
/// past the title.</item>
/// </list>
/// </remarks>
public static class JunkTokens
{
    /// <summary>Unambiguous technical tokens. The title ends at the first one of these.</summary>
    public static readonly string[] Strong =
    [
        // Resolution
        "2160p", "1440p", "1080p", "1080i", "720p", "576p", "480p", "360p", "4k", "8k",

        // Source
        "bluray", "blu-ray", "bdrip", "brrip", "bdremux", "remux", "dvdrip", "dvdscr",
        "webrip", "web-dl", "webdl", "hdtv", "pdtv", "sdtv", "hdrip", "hdcam", "telesync",
        "screener", "vodrip", "uhdrip", "amzn", "dsnp", "hmax", "atvp", "pcok", "crav",

        // Video codec / format
        "x264", "x265", "h264", "h265", "h-264", "h-265", "hevc", "avc", "xvid", "divx",
        "mpeg2", "10bit", "8bit", "10-bit", "hdr10", "hdr10plus", "dovi", "dolbyvision",

        // Audio
        "aac", "aac2", "ac3", "eac3", "dd5", "ddp5", "ddp", "dts", "dtshd", "dts-hd", "dts-x",
        "truehd", "atmos", "flac", "lpcm", "2ch", "6ch", "8ch",

        // Release status / packaging
        "proper", "repack", "rerip", "readnfo", "remastered", "theatrical", "criterion", "imax",
        "multisubs", "vostfr", "subfrench", "truefrench", "vff", "vfq", "vfi", "vf2",
        "60fps", "hfr", "sbs", "hsbs", "half-ou",

        // Container
        "mkv", "mp4", "m4v", "avi",
    ];

    /// <summary>
    /// Tokens that are also plausible title words, so they are only stripped from the tail.
    /// </summary>
    public static readonly string[] Weak =
    [
        "web", "cam", "ts", "dvd", "dvd5", "dvd9", "r5", "uhd", "hdr", "sdr", "hybrid",
        "internal", "limited", "extended", "unrated", "uncut", "directors", "director's", "dc",
        "complete", "subbed", "dubbed", "multi", "dual", "dualaudio", "anniversary", "restored",
        "french", "german", "italian", "spanish", "latino", "nordic", "english", "3d", "mp3",
    ];

    private static Regex BuildRegex(IEnumerable<string> tokens, bool anchorAtEnd)
    {
        var alternation = string.Join('|', tokens.Select(Regex.Escape).OrderByDescending(t => t.Length));
        // Tokens sit between non-alphanumerics; a bare "web" inside "Spiderweb" must not match.
        var pattern = $@"(?<![\p{{L}}\p{{N}}])(?:{alternation})(?![\p{{L}}\p{{N}}])";
        if (anchorAtEnd)
            pattern += @"[\s._'\-]*$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static readonly Lazy<Regex> StrongRegex = new(() => BuildRegex(Strong, anchorAtEnd: false));
    private static readonly Lazy<Regex> TrailingWeakRegex = new(() => BuildRegex(Weak, anchorAtEnd: true));

    /// <summary>Index of the earliest strong junk token, or -1 if there is none.</summary>
    public static int FirstStrongIndex(string text)
    {
        var match = StrongRegex.Value.Match(text);
        // Never cut at the very start: a film really called "Remux" would otherwise vanish.
        return match.Success && match.Index > 0 ? match.Index : -1;
    }

    /// <summary>Truncates <paramref name="text"/> at its first strong junk token.</summary>
    public static string StripFromStrong(string text)
    {
        var index = FirstStrongIndex(text);
        return index < 0 ? text : text[..index];
    }

    /// <summary>
    /// Removes every strong junk token, wherever it sits. Used to decide whether a name holds
    /// anything beyond release noise, not for building a title.
    /// </summary>
    public static string RemoveAllStrong(string text) => StrongRegex.Value.Replace(text, " ");

    /// <summary>
    /// Repeatedly removes weak junk tokens from the end of <paramref name="text"/>, never
    /// consuming the whole string (so "Cam" or "Uncut" survives as a title on its own).
    /// </summary>
    public static string StripTrailingWeak(string text)
    {
        while (true)
        {
            var match = TrailingWeakRegex.Value.Match(text);
            if (!match.Success || match.Index == 0)
                return text;
            text = text[..match.Index];
        }
    }
}
