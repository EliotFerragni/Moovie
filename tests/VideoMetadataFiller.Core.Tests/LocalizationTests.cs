using VideoMetadataFiller.Core.Localization;
using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Writing;
using Xunit;

namespace VideoMetadataFiller.Core.Tests;

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
        var resource = $"VideoMetadataFiller.Core.Localization.{tag}.json";
        using var stream = typeof(Strings).Assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);

        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream!);
        Assert.NotNull(map);
        return [.. map!.Keys];
    }
}
