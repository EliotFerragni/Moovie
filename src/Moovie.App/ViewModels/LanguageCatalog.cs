namespace Moovie.App.ViewModels;

/// <summary>
/// The languages offered for metadata, as TMDB language tags.
/// </summary>
/// <remarks>
/// A curated list rather than every tag TMDB accepts: these are the ones with enough coverage to
/// be worth choosing. The per-file override exists precisely because coverage is uneven, so a file
/// can fall back to another language without changing the global setting.
/// </remarks>
public static class LanguageCatalog
{
    public static readonly IReadOnlyList<LanguageOption> All =
    [
        new("en-US", "English (US)"),
        new("en-GB", "English (UK)"),
        new("fr-FR", "French"),
        new("de-DE", "German"),
        new("it-IT", "Italian"),
        new("es-ES", "Spanish (Spain)"),
        new("es-MX", "Spanish (Latin America)"),
        new("pt-BR", "Portuguese (Brazil)"),
        new("pt-PT", "Portuguese (Portugal)"),
        new("nl-NL", "Dutch"),
        new("sv-SE", "Swedish"),
        new("da-DK", "Danish"),
        new("nb-NO", "Norwegian"),
        new("fi-FI", "Finnish"),
        new("pl-PL", "Polish"),
        new("cs-CZ", "Czech"),
        new("hu-HU", "Hungarian"),
        new("ro-RO", "Romanian"),
        new("el-GR", "Greek"),
        new("tr-TR", "Turkish"),
        new("ru-RU", "Russian"),
        new("uk-UA", "Ukrainian"),
        new("he-IL", "Hebrew"),
        new("ar-SA", "Arabic"),
        new("hi-IN", "Hindi"),
        new("ja-JP", "Japanese"),
        new("ko-KR", "Korean"),
        new("zh-CN", "Chinese (Simplified)"),
        new("zh-TW", "Chinese (Traditional)"),
    ];

    /// <summary>Finds an option by tag, falling back to English so the dropdown is never empty.</summary>
    public static LanguageOption Find(string? tag) =>
        All.FirstOrDefault(l => string.Equals(l.Tag, tag, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
