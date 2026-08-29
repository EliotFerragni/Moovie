using System.Globalization;
using System.Text.RegularExpressions;
using Moovie.Core.Model;

namespace Moovie.Core.Parsing;

/// <summary>
/// Works out from a file's name (and, when the name is uninformative, its folders) whether it
/// holds a movie or a TV episode, and recovers the title plus season/episode numbers.
/// </summary>
/// <remarks>
/// A cascade of patterns is tried in decreasing order of trustworthiness; the first one that
/// matches wins. Everything the app does downstream depends on this, so the accompanying test
/// corpus is the place to add regressions rather than tweaking patterns blind.
/// </remarks>
public static class FilenameParser
{
    private const RegexOptions Opts =
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // A word boundary that also refuses to start mid-number, unlike \b.
    private const string Edge = @"(?<![\p{L}\p{N}])";

    /// <summary>S01E02, S01E02E03, S01E02-E03, S01 - E02, S2010E05.</summary>
    private static readonly Regex SeasonEpisode = new(
        Edge + @"s(?<season>\d{1,4})[\s._-]*e(?<ep>\d{1,4})(?!\d)" +
        @"(?<extra>(?:[\s._-]*(?:-e|e|-)[\s._-]*\d{1,3}(?!\d))*)", Opts);

    /// <summary>1x02, 01x02, 1x02-03.</summary>
    private static readonly Regex CrossNotation = new(
        Edge + @"(?<season>\d{1,2})x(?<ep>\d{1,4})(?!\d)" +
        @"(?<extra>(?:[\s._-]*-[\s._-]*\d{1,3}(?!\d))*)", Opts);

    /// <summary>Season 1 Episode 2, Saison 1 Ep 2.</summary>
    private static readonly Regex SeasonWordEpisodeWord = new(
        Edge + @"(?:season|saison|series)[\s._-]*(?<season>\d{1,4})[\s._-]*" +
        @"(?:episode|episodio|ep)[\s._-]*(?<ep>\d{1,3})(?!\d)", Opts);

    /// <summary>Daily shows: 2021-03-08 or 08-03-2021.</summary>
    private static readonly Regex DateYmd = new(
        Edge + @"(?<y>(?:19|20)\d{2})[\s._-](?<m>\d{1,2})[\s._-](?<d>\d{1,2})(?!\d)", Opts);

    private static readonly Regex DateDmy = new(
        Edge + @"(?<d>\d{1,2})[\s._-](?<m>\d{1,2})[\s._-](?<y>(?:19|20)\d{2})(?!\d)", Opts);

    /// <summary>A "Season 3" token on its own, used with a separate episode token.</summary>
    private static readonly Regex SeasonWordOnly = new(
        Edge + @"(?:season|saison|series|s)[\s._-]*(?<season>\d{1,2})(?!\d)", Opts);

    /// <summary>An "Episode 4" / "Ep 4" / "E04" token on its own.</summary>
    private static readonly Regex EpisodeWordOnly = new(
        Edge + @"(?:episode|episodio|ep|e)[\s._-]*(?<ep>\d{1,3})(?!\d)", Opts);

    /// <summary>Anime style: "Show Name - 07 - Episode Title".</summary>
    private static readonly Regex DashEpisodeDash = new(
        @"(?<=[\s._-])-[\s._]*(?<ep>\d{1,3})(?!\d)[\s._]*-", Opts);

    private static readonly Regex ResolutionToken = new(
        Edge + @"(?<res>2160p|1440p|1080p|1080i|720p|576p|480p|360p|4k|8k)(?![\p{L}\p{N}])", Opts);

    private static readonly Regex ParenthesisedYear = new(@"[(\[](?<y>(?:19|20)\d{2})[)\]]", Opts);

    private static readonly Regex BareYear = new(@"(?<!\d)(?<y>(?:19|20)\d{2})(?!\d)", Opts);

    private static readonly Regex BracketGroup = new(@"\[[^\]]*\]|\{[^}]*\}", Opts);

    private static readonly Regex Whitespace = new(@"\s+", Opts);

    /// <summary>Folder names that carry a season rather than a show name.</summary>
    private static readonly Regex SeasonFolder = new(
        @"^\s*(?:season|saison|series|s)[\s._-]*(?<season>\d{1,4})\s*$", Opts);

    private static readonly Regex SpecialsFolder = new(@"^\s*(?:specials?|extras?|saison\s*0)\s*$", Opts);

    /// <summary>A filename that is nothing but a number, e.g. "04.mp4" inside a season folder.</summary>
    private static readonly Regex NumericOnlyName = new(@"^\s*(?<ep>\d{1,3})\s*$", Opts);

    /// <summary>An episode number leading an otherwise descriptive filename, e.g. "04 - Pilot".</summary>
    private static readonly Regex LeadingEpisodeNumber = new(@"^\s*(?<ep>\d{1,3})(?!\d)[\s._-]+\D", Opts);

    /// <summary>
    /// Placeholder filenames produced by rippers and downloaders. They look like titles but
    /// are not, so the containing folder is consulted instead.
    /// </summary>
    private static readonly Regex GenericFileName = new(
        @"^\s*(?:(?:video|movie|film|main|feature|output|untitled|new|index|default|playback|" +
        @"stream|sample|encode)[\s._-]*\d*|(?:vts|title|disc|part|cd)[\s._\-0-9]*)\s*$", Opts);

    /// <summary>H.264 / H 265 written with separators, so the junk list can still see them.</summary>
    private static readonly Regex SplitCodec = new(Edge + @"h[.\s_-]?26(?<n>[45])(?![\p{L}\p{N}])", Opts);

    /// <summary>Years beyond this are title words (Blade Runner 2049), not release years.</summary>
    private static int MaxPlausibleYear => DateTime.Now.Year + 2;

    /// <summary>
    /// Parses <paramref name="path"/>. Only the file and folder names are read — nothing touches disk.
    /// </summary>
    public static ParsedName Parse(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
        var folders = AncestorNames(path);

        var parsed = ParseStem(stem);

        // A "Season 02" parent folder is authoritative: whatever the bare filename looked like,
        // a file sitting in one is an episode.
        if (parsed.Kind != MediaKind.TvEpisode && folders.Count > 0 && IsSeasonFolder(folders[0]))
        {
            var fromSeasonFolder = TryParseUsingSeasonFolder(stem, folders);
            if (fromSeasonFolder is not null)
                parsed = fromSeasonFolder with { Resolution = parsed.Resolution };
        }

        // An episode whose name starts at the season marker ("S01E02.mp4") has no show name in
        // the filename, so recover it from the folder tree.
        if (parsed.Kind == MediaKind.TvEpisode && !LooksLikeUsableTitle(parsed.Title))
            parsed = WithShowFromFolders(parsed, folders);

        // Placeholder names ("video.mp4", "VTS_01_1.mp4") tell us nothing; the folder usually
        // does, and it is very often "Title (Year)".
        if (parsed.Kind != MediaKind.TvEpisode
            && (!LooksLikeUsableTitle(parsed.Title) || GenericFileName.IsMatch(stem))
            && folders.Count > 0
            && !GenericFileName.IsMatch(folders[0]))
        {
            var fromFolder = ParseStem(folders[0]);
            if (LooksLikeUsableTitle(fromFolder.Title))
            {
                parsed = fromFolder with
                {
                    Resolution = parsed.Resolution ?? fromFolder.Resolution,
                    IsLowConfidence = true,
                    MatchedPattern = "FolderName",
                };
            }
        }

        // Anything not recognised as an episode is treated as a movie.
        if (parsed.Kind == MediaKind.Unknown && LooksLikeUsableTitle(parsed.Title))
            parsed = parsed with { Kind = MediaKind.Movie };

        // Still unknown means the leftover text was release noise, not a title. Reporting it as
        // one would send the matcher off to search TMDB for "1080p".
        if (parsed.Kind == MediaKind.Unknown)
            parsed = parsed with { Title = string.Empty };

        return parsed;
    }

    private static bool IsSeasonFolder(string folder) =>
        SeasonFolder.IsMatch(folder) || SpecialsFolder.IsMatch(folder);

    private static ParsedName ParseStem(string stem)
    {
        var normalized = Normalize(stem);
        var resolution = ExtractResolution(normalized);

        var tv = TryParseTv(normalized);
        if (tv is not null)
            return tv with { Resolution = resolution };

        var (title, year) = ParseMovieTitle(normalized);
        return new ParsedName
        {
            Kind = LooksLikeUsableTitle(title) ? MediaKind.Movie : MediaKind.Unknown,
            Title = title,
            Year = year,
            Resolution = resolution,
            MatchedPattern = year is null ? "MovieNoYear" : "MovieWithYear",
        };
    }

    private static ParsedName? TryParseTv(string text)
    {
        if (SeasonEpisode.Match(text) is { Success: true } m)
            return BuildTvResult(text, m.Index, int.Parse(m.Groups["season"].Value),
                CollectEpisodes(m), "SxxExx");

        if (CrossNotation.Match(text) is { Success: true } m2)
            return BuildTvResult(text, m2.Index, int.Parse(m2.Groups["season"].Value),
                CollectEpisodes(m2), "NxNN");

        if (SeasonWordEpisodeWord.Match(text) is { Success: true } m3)
            return BuildTvResult(text, m3.Index, int.Parse(m3.Groups["season"].Value),
                [int.Parse(m3.Groups["ep"].Value)], "SeasonEpisodeWords");

        if (TryParseAirDate(text) is { } dated)
            return dated;

        // Season and episode written as separate tokens, in either order.
        var seasonOnly = SeasonWordOnly.Match(text);
        var episodeOnly = EpisodeWordOnly.Match(text);
        if (seasonOnly.Success && episodeOnly.Success && seasonOnly.Index != episodeOnly.Index)
        {
            var cut = Math.Min(seasonOnly.Index, episodeOnly.Index);
            return BuildTvResult(text, cut, int.Parse(seasonOnly.Groups["season"].Value),
                [int.Parse(episodeOnly.Groups["ep"].Value)], "SeparateSeasonEpisode");
        }

        // What is left are episode markers with no season. They are genuinely ambiguous against
        // movie titles ("Star Wars Episode 4 ... 1977"), so a trailing release year vetoes them.
        if (HasTrailingReleaseYear(text))
            return null;

        if (episodeOnly.Success && episodeOnly.Index > 0)
            return BuildTvResult(text, episodeOnly.Index, season: null,
                [int.Parse(episodeOnly.Groups["ep"].Value)], "BareEpisode", lowConfidence: true);

        if (DashEpisodeDash.Match(text) is { Success: true } m4 && m4.Index > 0)
            return BuildTvResult(text, m4.Index, season: null,
                [int.Parse(m4.Groups["ep"].Value)], "DashEpisodeDash", lowConfidence: true);

        return null;
    }

    /// <summary>
    /// True when a plausible release year sits at the end of the meaningful part of the name,
    /// which points at a movie far more strongly than a season-less episode number does.
    /// </summary>
    private static bool HasTrailingReleaseYear(string text)
    {
        var stripped = JunkTokens.StripTrailingWeak(JunkTokens.StripFromStrong(text)).TrimEnd(' ', '-', '_', '.');
        var match = BareYear.Matches(stripped).LastOrDefault(m => m.Index > 0);
        return match is not null
            && int.Parse(match.Groups["y"].Value) <= MaxPlausibleYear
            && match.Index + match.Length >= stripped.Length - 1;
    }

    private static ParsedName? TryParseAirDate(string text)
    {
        foreach (var regex in (Regex[])[DateYmd, DateDmy])
        {
            var match = regex.Match(text);
            if (!match.Success)
                continue;

            var year = int.Parse(match.Groups["y"].Value);
            var month = int.Parse(match.Groups["m"].Value);
            var day = int.Parse(match.Groups["d"].Value);
            if (month is < 1 or > 12 || day is < 1 or > 31 || year < 1900 || year > MaxPlausibleYear)
                continue;
            if (match.Index == 0)
                continue; // No show name in front of it — probably not an episode at all.

            var date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
            return new ParsedName
            {
                Kind = MediaKind.TvEpisode,
                Title = CleanTitle(text[..match.Index]).Title,
                AirDate = date,
                MatchedPattern = "AirDate",
            };
        }

        return null;
    }

    private static ParsedName BuildTvResult(
        string text, int cutIndex, int? season, IReadOnlyList<int> episodes,
        string pattern, bool lowConfidence = false)
    {
        var (show, year) = CleanTitle(text[..cutIndex]);
        return new ParsedName
        {
            Kind = MediaKind.TvEpisode,
            Title = show,
            Year = year,
            Season = season,
            Episodes = episodes,
            MatchedPattern = pattern,
            IsLowConfidence = lowConfidence || season is null,
        };
    }

    /// <summary>Pulls the first episode number plus any extras from a multi-episode match.</summary>
    private static List<int> CollectEpisodes(Match match)
    {
        var episodes = new List<int> { int.Parse(match.Groups["ep"].Value) };
        var extra = match.Groups["extra"].Value;
        if (!string.IsNullOrEmpty(extra))
        {
            foreach (var number in Regex.Matches(extra, @"\d{1,3}"))
                episodes.Add(int.Parse(((Match)number).Value));
        }

        return episodes;
    }

    private static (string Title, int? Year) ParseMovieTitle(string text)
    {
        // A parenthesised year is deliberate and beats everything else.
        var parenthesised = ParenthesisedYear.Matches(text).LastOrDefault(m => m.Index > 0);
        if (parenthesised is not null)
        {
            var year = int.Parse(parenthesised.Groups["y"].Value);
            if (year <= MaxPlausibleYear)
                return (CleanTitle(text[..parenthesised.Index]).Title, year);
        }

        var trimmed = JunkTokens.StripTrailingWeak(JunkTokens.StripFromStrong(text));
        return CleanTitle(trimmed);
    }

    /// <summary>
    /// Trims release noise off a candidate title and lifts a trailing year out of it.
    /// The year is returned separately so "The Office 2005" searches TMDB as "The Office".
    /// </summary>
    private static (string Title, int? Year) CleanTitle(string text)
    {
        var work = JunkTokens.StripFromStrong(text);
        work = JunkTokens.StripTrailingWeak(work);
        work = TrimEdges(work);

        int? year = null;

        // Only a year at the very end is a release year; one in the middle belongs to the title.
        var candidates = BareYear.Matches(work)
            .Where(m => m.Index > 0 && int.Parse(m.Groups["y"].Value) <= MaxPlausibleYear)
            .ToList();
        var trailing = candidates.LastOrDefault(m => m.Index + m.Length >= work.Length - 1);
        if (trailing is not null)
        {
            year = int.Parse(trailing.Groups["y"].Value);
            work = TrimEdges(work[..trailing.Index]);
        }

        return (Prettify(work), year);
    }

    private static string TrimEdges(string text) =>
        Whitespace.Replace(text, " ").Trim(' ', '-', '_', '.', ',', '(', '[', '{');

    /// <summary>Title-cases a name that arrived entirely in lower case, for a tidier list.</summary>
    private static string Prettify(string text)
    {
        text = TrimEdges(text);
        if (text.Length == 0 || text.Any(char.IsUpper))
            return text;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text);
    }

    private static string Normalize(string stem)
    {
        var text = stem;

        // Bracketed groups are release-group tags — drop them, unless that leaves nothing.
        var withoutBrackets = BracketGroup.Replace(text, " ");
        if (withoutBrackets.Any(char.IsLetterOrDigit))
            text = withoutBrackets;

        text = SplitCodec.Replace(text, m => "h26" + m.Groups["n"].Value);
        text = text.Replace('_', ' ').Replace('.', ' ').Replace('+', ' ');
        return Whitespace.Replace(text, " ").Trim();
    }

    private static string? ExtractResolution(string text)
    {
        var match = ResolutionToken.Match(text);
        if (!match.Success)
            return null;
        var value = match.Groups["res"].Value.ToLowerInvariant();
        return value switch { "4k" => "2160p", "8k" => "4320p", _ => value };
    }

    /// <summary>
    /// Folder names from the closest ancestor outwards, at most three deep. A bare filename with
    /// no directory part yields nothing — resolving it against the current directory would invent
    /// folder hints that do not exist.
    /// </summary>
    private static List<string> AncestorNames(string path)
    {
        var names = new List<string>();
        try
        {
            var directory = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(directory) && names.Count < 3)
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrEmpty(name))
                    break;
                names.Add(name);
                directory = Path.GetDirectoryName(directory);
            }
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // An unusable path simply yields no folder hints.
        }

        return names;
    }

    private static ParsedName WithShowFromFolders(ParsedName parsed, List<string> folders)
    {
        foreach (var folder in folders)
        {
            if (SeasonFolder.IsMatch(folder) || SpecialsFolder.IsMatch(folder))
                continue;
            var (title, year) = CleanTitle(Normalize(folder));
            if (!LooksLikeUsableTitle(title))
                continue;
            return parsed with { Title = title, Year = parsed.Year ?? year };
        }

        return parsed;
    }

    /// <summary>
    /// Handles trees like <c>Show Name/Season 02/04 - Pilot.mkv</c>, where the season lives in the
    /// folder and the filename carries only the episode number.
    /// </summary>
    private static ParsedName? TryParseUsingSeasonFolder(string stem, List<string> folders)
    {
        if (folders.Count == 0)
            return null;

        int? season = null;
        var showFolderIndex = 0;
        var seasonMatch = SeasonFolder.Match(folders[0]);
        if (seasonMatch.Success)
        {
            season = int.Parse(seasonMatch.Groups["season"].Value);
            showFolderIndex = 1;
        }
        else if (SpecialsFolder.IsMatch(folders[0]))
        {
            season = 0;
            showFolderIndex = 1;
        }

        if (season is null)
            return null;

        var normalized = Normalize(stem);
        var episodes = new List<int>();
        if (NumericOnlyName.Match(normalized) is { Success: true } numeric)
            episodes.Add(int.Parse(numeric.Groups["ep"].Value));
        else if (LeadingEpisodeNumber.Match(normalized) is { Success: true } leading)
            episodes.Add(int.Parse(leading.Groups["ep"].Value));

        var show = string.Empty;
        int? year = null;
        if (showFolderIndex < folders.Count)
            (show, year) = CleanTitle(Normalize(folders[showFolderIndex]));

        return new ParsedName
        {
            Kind = MediaKind.TvEpisode,
            Title = show,
            Year = year,
            Season = season,
            Episodes = episodes,
            MatchedPattern = "SeasonFolder",
            IsLowConfidence = true,
        };
    }

    /// <summary>
    /// Whether a recovered title is worth searching TMDB with. "1080p" is not, "2012" is —
    /// films really are named after nothing but a number.
    /// </summary>
    private static bool LooksLikeUsableTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var trimmed = title.Trim();
        if (trimmed.All(c => char.IsDigit(c) || c == ' '))
            return true;

        return JunkTokens.RemoveAllStrong(trimmed).Any(char.IsLetter);
    }
}
