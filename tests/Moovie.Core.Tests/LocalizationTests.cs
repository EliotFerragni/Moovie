using Moovie.Core.Localization;
using Moovie.Core.Matching;
using Moovie.Core.Model;
using Moovie.Core.Parsing;
using Moovie.Core.Tmdb;
using Moovie.Core.Writing;
using Xunit;

namespace Moovie.Core.Tests;

public class LocalizationTests : IDisposable
{
    // Strings is global state, so every test here puts English back afterwards. Parallelism is
    // off for the assembly (see AssemblyInfo) because other tests assert on English messages.
    public void Dispose() => Strings.Use("en");

    [Fact]
    public void Defaults_to_english_without_being_told_otherwise()
    {
        Assert.Equal("Refetch", Strings.Get("pane.refetch"));
    }

    [Theory]
    [InlineData("fr", "Actualiser")]
    [InlineData("de", "Neu laden")]
    [InlineData("it", "Ricarica")]
    public void Translates_once_a_language_is_chosen(string tag, string expected)
    {
        Strings.Use(tag);
        Assert.Equal(expected, Strings.Get("pane.refetch"));
    }

    [Fact]
    public void A_regional_tag_resolves_to_its_language()
    {
        Strings.Use("fr-CH");
        Assert.Equal("fr", Strings.ActiveTag);
    }

    [Fact]
    public void An_unsupported_language_falls_back_to_english()
    {
        Strings.Use("sv");
        Assert.Equal("en", Strings.ActiveTag);
        Assert.Equal("Refetch", Strings.Get("pane.refetch"));
    }

    [Fact]
    public void An_unknown_key_shows_as_itself_rather_than_blank()
    {
        Assert.Equal("no.such.key", Strings.Get("no.such.key"));
    }

    [Fact]
    public void Format_fills_in_the_placeholders()
    {
        Assert.Equal("Applied to 24 files.", Strings.Format("pane.appliedTo", 24));

        Strings.Use("fr");
        Assert.Equal("Appliqué à 24 fichiers.", Strings.Format("pane.appliedTo", 24));
    }

    /// <summary>
    /// The guard that matters: a translation that quietly loses a key would show English in the
    /// middle of a French window, and nothing else would notice.
    /// </summary>
    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    public void Every_translation_covers_every_english_key(string tag)
    {
        var english = AllKeys("en");
        var translated = AllKeys(tag);

        Assert.Empty(english.Except(translated));
        Assert.Empty(translated.Except(english));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    public void Translations_keep_the_same_placeholders(string tag)
    {
        Strings.Use("en");
        var english = AllKeys("en").ToDictionary(k => k, Strings.Get);

        Strings.Use(tag);
        foreach (var (key, source) in english)
        {
            var target = Strings.Get(key);
            Assert.Equal(Placeholders(source), Placeholders(target));
        }
    }

    /// <summary>
    /// Catches the mistake that a key can exist and still never be used: the artwork labels sat
    /// in the catalogue while the enum kept returning hardcoded English.
    /// </summary>
    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    public void Every_artwork_kind_is_translated(string tag)
    {
        Strings.Use("en");
        var english = Enum.GetValues<ArtworkKind>().ToDictionary(k => k, ArtworkKinds.Label);

        Strings.Use(tag);
        foreach (var (kind, inEnglish) in english)
        {
            var translated = ArtworkKinds.Label(kind);
            Assert.NotEqual(kind.ToString(), translated);
            Assert.NotEqual(inEnglish, translated);
        }
    }

    /// <summary>
    /// Every message a bad template produces has to be translated. Key parity cannot catch this:
    /// the key can sit in all four files while the parser returns a hardcoded English sentence,
    /// which is exactly what it did.
    /// </summary>
    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    public void Every_template_error_is_translated(string tag)
    {
        // Between them these reach every complaint the parser knows how to make.
        string[] broken =
        [
            "",
            "sub/{title}",
            "{title",
            "{title}>",
            "{title}}",
            "<{title}",
            "{nonsense}",
            "{season:zz}",
            "{airDate:yyyy'}",   // an unterminated quoted literal, which the date formatter rejects
            "{title:sideways}",
            "{resolution:sideways}",
        ];

        // The reasons are checked separately because a bad format reports as "'{season:zz}' is
        // not valid: <reason>": the wrapper was translated while the reason was not, so comparing
        // whole strings saw a difference and passed the untranslated half.
        Strings.Use("en");
        var english = broken.Select(t => RenameTemplate.Validate(t).Errors.ToList()).ToList();
        var englishReasons = new[]
            {
                "template.numberFormats", "template.dateFormats",
                "template.textFormats", "template.textFormatsExtra",
            }
            .Select(key => Strings.Get(key).Split('{')[0].Trim())
            .Where(fragment => fragment.Length > 10)
            .ToList();

        Strings.Use(tag);
        var translated = broken.Select(t => RenameTemplate.Validate(t).Errors.ToList()).ToList();

        for (var i = 0; i < broken.Length; i++)
        {
            Assert.True(english[i].Count > 0, $"'{broken[i]}' was expected to be rejected but produced no errors");
            Assert.Equal(english[i].Count, translated[i].Count);

            for (var e = 0; e < english[i].Count; e++)
            {
                Assert.True(
                    english[i][e] != translated[i][e],
                    $"'{broken[i]}' reports the same thing in {tag} as in English: {english[i][e]}");

                foreach (var fragment in englishReasons)
                    Assert.False(
                        translated[i][e].Contains(fragment, StringComparison.Ordinal),
                        $"'{broken[i]}' still carries the English \"{fragment}\" in {tag}: {translated[i][e]}");
            }
        }
    }

    /// <summary>
    /// The same guard for the messages that come back from matching, which is where an English
    /// sentence in a French window was actually spotted.
    /// </summary>
    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    public async Task Every_match_message_is_translated(string tag)
    {
        Strings.Use("en");
        var english = await AllMatchMessages();

        Strings.Use(tag);
        var translated = await AllMatchMessages();

        foreach (var (scenario, inEnglish) in english)
        {
            Assert.False(string.IsNullOrWhiteSpace(inEnglish));
            Assert.True(
                inEnglish != translated[scenario],
                $"{scenario} reports the same thing in {tag} as in English: {inEnglish}");
        }
    }

    /// <summary>One of each outcome that carries a message of its own.</summary>
    private static async Task<Dictionary<string, string>> AllMatchMessages()
    {
        static Candidate Movie(int id, string title, int? year = null) =>
            new() { TmdbId = id, Kind = MediaKind.Movie, Title = title, Year = year, Popularity = 1 };

        static Candidate Show(int id, string title) =>
            new() { TmdbId = id, Kind = MediaKind.TvEpisode, Title = title, Popularity = 1 };

        static async Task<string> Resolve(ITmdbService tmdb, ParsedName parsed) =>
            (await new MatchResolver(tmdb).ResolveAsync(parsed, "en-US")).Message ?? string.Empty;

        return new Dictionary<string, string>
        {
            ["no movie"] = await Resolve(
                new FakeTmdbService(),
                FilenameParser.Parse("Some.Obscure.Thing.2019.mp4")),

            ["several movies"] = await Resolve(
                new FakeTmdbService { MovieResults = [Movie(1, "The Italian Job", 1969), Movie(2, "The Italian Job", 2003)] },
                FilenameParser.Parse("The.Italian.Job.1080p.BluRay.mp4")),

            ["no show"] = await Resolve(
                new FakeTmdbService(),
                new ParsedName { Kind = MediaKind.TvEpisode, Title = "Nothing At All", Season = 1, Episodes = [1] }),

            ["several shows"] = await Resolve(
                new FakeTmdbService { ShowResults = [Show(10, "The Office"), Show(11, "The Office")] },
                new ParsedName { Kind = MediaKind.TvEpisode, Title = "The Office", Season = 1, Episodes = [1] }),

            ["show but no episode number"] = await Resolve(
                new FakeTmdbService { ShowResults = [Show(10, "Some Show")] },
                new ParsedName { Kind = MediaKind.TvEpisode, Title = "Some Show" }),

            ["no such episode"] = await Resolve(
                new FakeTmdbService { ShowResults = [Show(10, "Some Show")] },
                new ParsedName { Kind = MediaKind.TvEpisode, Title = "Some Show", Season = 1, Episodes = [5] }),
        };
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    public void Every_rename_token_is_described_in_each_language(string tag)
    {
        Strings.Use(tag);
        foreach (var token in RenameTokens.All)
        {
            var description = token.Description;

            // Get falls back to the key itself, so the key leaking through means it is missing.
            Assert.NotEqual($"token.{token.Name}", description);
            Assert.NotEmpty(description);
        }
    }

    private static int Placeholders(string text) =>
        new[] { "{0}", "{1}", "{2}" }.Count(text.Contains);

    private static HashSet<string> AllKeys(string tag)
    {
        var resource = $"Moovie.Core.Localization.{tag}.json";
        using var stream = typeof(Strings).Assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);

        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream!);
        Assert.NotNull(map);
        return [.. map!.Keys];
    }
}
