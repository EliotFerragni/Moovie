using Avalonia;
using Avalonia.Styling;
using Moovie.Core.Localization;
using Moovie.Core.Settings;

namespace Moovie.App.ViewModels;

/// <summary>A theme offered in the appearance dropdown.</summary>
public sealed record ThemeChoice(AppTheme Theme, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class ThemeCatalog
{
    /// <summary>
    /// Built per call rather than held in a static field: the labels are translated, and a static
    /// would be built before the interface language was chosen.
    /// </summary>
    public static IReadOnlyList<ThemeChoice> All =>
    [
        new(AppTheme.System, Strings.Get("settings.sameAsSystem")),
        new(AppTheme.Light, Strings.Get("settings.themeLight")),
        new(AppTheme.Dark, Strings.Get("settings.themeDark")),
    ];

    /// <summary>
    /// Points the running app at a theme. <see cref="ThemeVariant.Default"/> follows the operating
    /// system, and keeps following it while the app is open. Unlike the interface language this
    /// needs no restart: colours are resolved through DynamicResource, so the window repaints on
    /// the spot.
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
