using System.Globalization;
using System.Text.Json;

namespace Moovie.Core.Localization;

/// <summary>A language the app's own interface can be shown in.</summary>
public sealed record LanguageChoice(string Tag, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// The app's own text, in whichever language it is running in. Separate from the TMDB language,
/// which decides what metadata is fetched: someone may well want a French interface and English
/// metadata, or the other way round.
/// </summary>
/// <remarks>
/// English is compiled in as the fallback, so a key missing from a translation shows the English
/// text rather than a blank. Nothing is loaded until <see cref="Use"/> is called, which only the
/// app does at startup: tests and any other caller get English deterministically, whatever the
/// machine's locale happens to be.
/// </remarks>
public static class Strings
{
    /// <summary>Stored in settings when the app should follow the operating system.</summary>
    public const string SystemTag = "system";

    private const string FallbackTag = "en";

    private static readonly Dictionary<string, string> Fallback = Load(FallbackTag);

    private static Dictionary<string, string> _active = Fallback;

    private static readonly string[] Tags = [SystemTag, "en", "fr", "de", "it"];

    /// <summary>
    /// Built on each read, because the "same as the system" entry is itself translated and the
    /// list is usually asked for after <see cref="Use"/> has run. Each language is named in its
    /// own words, as language pickers conventionally are.
    /// </summary>
    public static IReadOnlyList<LanguageChoice> Available =>
    [
        new(SystemTag, Get("settings.sameAsSystem")),
        new("en", "English"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("it", "Italiano"),
    ];

    /// <summary>The language actually in use, after resolving "system" and any unknown tag.</summary>
    public static string ActiveTag { get; private set; } = FallbackTag;

    /// <summary>
    /// Switches the interface language. Called once at startup: the app does not retranslate a
    /// running window, so the setting says it takes effect on restart.
    /// </summary>
    public static void Use(string? tag)
    {
        ActiveTag = Resolve(tag);
        _active = ActiveTag == FallbackTag ? Fallback : Load(ActiveTag);
    }

    public static string Get(string key)
    {
        if (_active.TryGetValue(key, out var translated) && !string.IsNullOrEmpty(translated))
            return translated;

        // An untranslated key falls back to English, and an unknown one shows as itself so a
        // typo is obvious on screen instead of leaving a hole.
        return Fallback.TryGetValue(key, out var english) ? english : key;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    private static string Resolve(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag == SystemTag)
            tag = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        tag = tag.Split('-')[0].ToLowerInvariant();
        return Tags.Contains(tag) ? tag : FallbackTag;
    }

    private static Dictionary<string, string> Load(string tag)
    {
        var resource = $"Moovie.Core.Localization.{tag}.json";
        using var stream = typeof(Strings).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
            return [];

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
