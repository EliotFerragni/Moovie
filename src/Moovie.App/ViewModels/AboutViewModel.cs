using System.Reflection;
using System.Runtime.InteropServices;
using Moovie.Core.Localization;
using Moovie.Core.Settings;

namespace Moovie.App.ViewModels;

/// <summary>What the About box shows. Everything is read at startup; nothing here is editable.</summary>
public sealed class AboutViewModel
{
    public string AppName => "Moovie";

    /// <summary>
    /// The title bar. Built here rather than in the XAML so the app's name stays out of the
    /// translations: only the word around it is a translator's business.
    /// </summary>
    public string WindowTitle => Strings.Format("about.windowTitle", AppName);

    public string Version { get; } = ReadVersion();

    public string Tagline =>
        Strings.Get("about.tagline");

    /// <summary>TMDB's terms require this wording on anything built against their API.</summary>
    public string TmdbAttribution =>
        Strings.Get("about.tmdbAttribution");

    public string Runtime =>
        $"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription.Trim()} " +
        $"({RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()})";

    public string SettingsPath { get; } = SettingsStore.DefaultFilePath();

    public string TmdbUrl => "https://www.themoviedb.org";

    private static string ReadVersion()
    {
        var assembly = typeof(AboutViewModel).Assembly;

        // The SDK appends "+<commit>" to the informational version; that is noise in a dialog.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
