using Moovie.Core.Settings;
using Xunit;

namespace Moovie.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("moovie-settings").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_directory, name);

    [Fact]
    public void Round_trips_settings_through_the_file()
    {
        new SettingsStore(Path("settings.json"))
            .Save(new AppSettings { TmdbApiKey = "abc123", OmitResolutionAtOrBelow = "576p" });

        var loaded = new SettingsStore(Path("settings.json")).Load();

        Assert.Equal("abc123", loaded.TmdbApiKey);
        Assert.Equal("576p", loaded.OmitResolutionAtOrBelow);
    }

    [Fact]
    public void A_missing_file_comes_back_as_defaults_rather_than_an_error()
    {
        var loaded = new SettingsStore(Path("nothing-here.json")).Load();

        Assert.Equal(string.Empty, loaded.TmdbApiKey);
        Assert.Equal("en-US", loaded.Language);
    }

    /// <summary>A corrupt file must not stop the app from starting.</summary>
    [Fact]
    public void An_unreadable_file_comes_back_as_defaults_too()
    {
        System.IO.File.WriteAllText(Path("broken.json"), "{ this is not json");

        Assert.Equal(string.Empty, new SettingsStore(Path("broken.json")).Load().TmdbApiKey);
    }

    [Fact]
    public void The_default_location_is_named_after_the_app()
    {
        Assert.Contains("Moovie", SettingsStore.DefaultFilePath());
    }
}
