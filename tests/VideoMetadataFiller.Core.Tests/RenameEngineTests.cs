using VideoMetadataFiller.Core.Model;
using VideoMetadataFiller.Core.Settings;
using VideoMetadataFiller.Core.Writing;
using Xunit;

namespace VideoMetadataFiller.Core.Tests;

public class RenameEngineTests
{
    private static MediaMetadata Movie(string title) => new()
    {
        Kind = MediaKind.Movie,
        Title = title,
        Year = 2019,
    };

    [Theory]
    [InlineData("Mission: Impossible", "Mission - Impossible (2019).mp4")]
    [InlineData("What/Ever", "WhatEver (2019).mp4")]
    [InlineData("Who? Me*", "Who Me (2019).mp4")]
    [InlineData("A \"Quoted\" Title", "A Quoted Title (2019).mp4")]
    [InlineData("Pipe|Dream", "PipeDream (2019).mp4")]
    public void StripsCharactersNoFilesystemWillTake(string title, string expected)
    {
        var name = RenameEngine.BuildFileName(
            RenameTemplate.Parse("{title} ({year})"), Movie(title), ".mp4");

        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(SeparatorStyle.Space, "Blade Runner (2019).mp4")]
    [InlineData(SeparatorStyle.Dot, "Blade.Runner.(2019).mp4")]
    [InlineData(SeparatorStyle.Underscore, "Blade_Runner_(2019).mp4")]
    [InlineData(SeparatorStyle.Dash, "Blade-Runner-(2019).mp4")]
    public void AppliesTheSeparatorStyle(SeparatorStyle separator, string expected)
    {
        var name = RenameEngine.BuildFileName(
            RenameTemplate.Parse("{title} ({year})"), Movie("Blade Runner"), ".mp4", separator);

        Assert.Equal(expected, name);
    }

    [Fact]
    public void KeepsTheExtensionWhenTheTemplateWritesItExplicitly()
    {
        var name = RenameEngine.BuildFileName(
            RenameTemplate.Parse("{title}{ext}"), Movie("Arrival"), ".mp4");

        Assert.Equal("Arrival.mp4", name);
    }

    [Fact]
    public void TrimsOverlongNamesToASafeLength()
    {
        var name = RenameEngine.BuildFileName(
            RenameTemplate.Parse("{title}"), Movie(new string('x', 400)), ".mp4");

        Assert.True(name.Length <= 185, $"name was {name.Length} characters");
        Assert.EndsWith(".mp4", name);
    }

    [Fact]
    public void EscapesWindowsDeviceNames()
    {
        Assert.Equal("NUL_", RenameEngine.Sanitize("NUL"));
        Assert.Equal("Nul_", RenameEngine.Sanitize("Nul"));
    }

    [Fact]
    public void ReturnsEmptyWhenTheTemplateRendersNothing()
    {
        var blank = new MediaMetadata { Kind = MediaKind.Movie };

        Assert.Equal(string.Empty, RenameEngine.BuildFileName(
            RenameTemplate.Parse("{title}"), blank, ".mp4"));
    }

    [Fact]
    public void PreviewFallsBackToTheCurrentNameWhenNothingRenders()
    {
        var blank = new MediaMetadata { Kind = MediaKind.Movie };

        var preview = RenameEngine.PreviewFileName(
            "/media/original name.mp4", RenameTemplate.Parse("{title}"), blank, SeparatorStyle.Space);

        Assert.Equal("original name.mp4", preview);
    }

    [Fact]
    public void CollisionsGetANumberedSuffix()
    {
        var directory = Directory.CreateTempSubdirectory("vmf-collision").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Arrival (2016).mp4"), string.Empty);

            var target = RenameEngine.ResolveCollision(directory, "Arrival (2016).mp4");

            Assert.Equal(Path.Combine(directory, "Arrival (2016) (2).mp4"), target);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AFileAlreadyCorrectlyNamedKeepsItsName()
    {
        var directory = Directory.CreateTempSubdirectory("vmf-samename").FullName;
        try
        {
            var path = Path.Combine(directory, "Arrival (2016).mp4");
            File.WriteAllText(path, string.Empty);

            var target = RenameEngine.ResolveCollision(directory, "Arrival (2016).mp4", path);

            Assert.Equal(path, target);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RenamesOnDiskAndReturnsTheNewPath()
    {
        var directory = Directory.CreateTempSubdirectory("vmf-rename").FullName;
        try
        {
            var original = Path.Combine(directory, "arrival.2016.1080p.mp4");
            File.WriteAllText(original, "x");

            var metadata = new MediaMetadata { Kind = MediaKind.Movie, Title = "Arrival", Year = 2016 };
            var renamed = RenameEngine.Rename(
                original, RenameTemplate.Parse("{title} ({year})"), metadata, SeparatorStyle.Space);

            Assert.Equal(Path.Combine(directory, "Arrival (2016).mp4"), renamed);
            Assert.True(File.Exists(renamed));
            Assert.False(File.Exists(original));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SampleMetadataRendersUnderTheShippedDefaults()
    {
        Assert.Equal("Blade Runner 2049 (2017).mp4", RenameEngine.BuildFileName(
            RenameTemplate.Parse(AppSettings.DefaultMovieTemplate), RenameEngine.SampleMovie, ".mp4"));

        Assert.Equal("Severance - S01E01 - Good News About Hell.mp4", RenameEngine.BuildFileName(
            RenameTemplate.Parse(AppSettings.DefaultTvTemplate), RenameEngine.SampleEpisode, ".mp4"));
    }
}
