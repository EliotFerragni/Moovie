using System.Reflection;
using System.Runtime.InteropServices;
using VideoMetadataFiller.Core.Settings;

namespace VideoMetadataFiller.App.ViewModels;

/// <summary>What the About box shows. Everything is read at startup; nothing here is editable.</summary>
public sealed class AboutViewModel
{
    public string AppName => "Video Metadata Filler";

    public string Version { get; } = ReadVersion();

    public string Tagline =>
        "Fills the metadata of movie and TV files from The Movie Database.";

    /// <summary>TMDB's terms require this wording on anything built against their API.</summary>
    public string TmdbAttribution =>
        "This product uses the TMDB API but is not endorsed or certified by TMDB.";

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
