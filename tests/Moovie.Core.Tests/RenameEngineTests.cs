using Moovie.Core.Model;
using Moovie.Core.Settings;
using Moovie.Core.Writing;
using Xunit;

namespace Moovie.Core.Tests;

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
            RenameTemplate.Parse("{title} ({year})"), Movie("Blade Runner"), ".mp4",
            new NamingRules(separator));

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
            "/media/original name.mp4", RenameTemplate.Parse("{title}"), blank);

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
                original, RenameTemplate.Parse("{title} ({year})"), metadata);

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

    // ---------------------------------------------- leaving ordinary resolutions out

    private static readonly MediaMetadata Film = new()
    {
        Kind = MediaKind.Movie,
        Title = "Blade Runner 2049",
        Year = 2017,
    };

    private static MediaMetadata At(string? resolution) =>
        new()
        {
            Kind = Film.Kind,
            Title = Film.Title,
            Year = Film.Year,
            Resolution = resolution,
        };

    private const string OptionalResolution = "{title} ({year})< [{resolution}]>";

    [Theory]
    [InlineData("2160p", "Blade Runner 2049 (2017) [2160p].mp4")]
    [InlineData("1080p", "Blade Runner 2049 (2017) [1080p].mp4")]
    [InlineData("720p", "Blade Runner 2049 (2017) [720p].mp4")]
    // The point of the setting: an ordinary resolution takes its brackets with it.
    [InlineData("576p", "Blade Runner 2049 (2017).mp4")]
    [InlineData("480p", "Blade Runner 2049 (2017).mp4")]
    public void Leaves_out_a_resolution_at_or_below_the_threshold(string resolution, string expected)
    {
        var name = RenameEngine.BuildFileName(
            RenameTemplate.Parse(OptionalResolution), At(resolution), ".mp4",
            new NamingRules(OmitResolutionAtOrBelow: "576p"));

        Assert.Equal(expected, name);
    }

    [Fact]
    public void Writes_every_resolution_when_no_threshold_is_set()
    {
        var template = RenameTemplate.Parse(OptionalResolution);

        Assert.Equal("Blade Runner 2049 (2017) [576p].mp4",
            RenameEngine.BuildFileName(template, At("576p"), ".mp4"));
        Assert.Equal("Blade Runner 2049 (2017) [576p].mp4",
            RenameEngine.BuildFileName(
                template, At("576p"), ".mp4", new NamingRules(OmitResolutionAtOrBelow: string.Empty)));
    }

    /// <summary>
    /// A template that puts the token in bare brackets rather than an optional section keeps the
    /// brackets. That is the template's doing, not the threshold's, but it is worth pinning down
    /// so the guidance to write &lt;[{resolution}]&gt; has something behind it.
    /// </summary>
    [Fact]
    public void Brackets_outside_an_optional_section_are_the_templates_own_business()
    {
        var name = RenameEngine.BuildFileName(
            RenameTemplate.Parse("{title} ({year}) [{resolution}]"), At("576p"), ".mp4",
            new NamingRules(OmitResolutionAtOrBelow: "576p"));

        Assert.Equal("Blade Runner 2049 (2017) [].mp4", name);
    }

    /// <summary>
    /// Hiding a resolution from a name must not hide it from the file: the HD flag is written
    /// from the same field, and a naming preference is not a claim about the video.
    /// </summary>
    [Fact]
    public void Leaving_it_out_of_the_name_does_not_touch_the_metadata()
    {
        var metadata = At("576p");

        RenameEngine.BuildFileName(
            RenameTemplate.Parse(OptionalResolution), metadata, ".mp4",
            new NamingRules(OmitResolutionAtOrBelow: "576p"));

        Assert.Equal("576p", metadata.Resolution);
    }

    [Fact]
    public void A_whole_name_that_empties_out_leaves_the_file_alone()
    {
        var name = RenameEngine.BuildFileName(
            RenameTemplate.Parse("{resolution}"), At("480p"), ".mp4",
            new NamingRules(OmitResolutionAtOrBelow: "576p"));

        Assert.Equal(string.Empty, name);
    }

    [Theory]
    [InlineData("2160p", "Blade Runner 2049 (2017) [4k].mp4")]
    [InlineData("4320p", "Blade Runner 2049 (2017) [8k].mp4")]
    [InlineData("1080p", "Blade Runner 2049 (2017) [1080p].mp4")]
    public void Writes_a_short_resolution_when_the_template_asks_for_one(string resolution, string expected)
    {
        var name = RenameEngine.BuildFileName(
            RenameTemplate.Parse("{title} ({year})< [{resolution:short}]>"), At(resolution), ".mp4");

        Assert.Equal(expected, name);
    }

    /// <summary>The two features are independent: a hidden resolution has no short form either.</summary>
    [Fact]
    public void The_short_form_still_obeys_the_omission_threshold()
    {
        var template = RenameTemplate.Parse("{title} ({year})< [{resolution:short}]>");

        var rules = new NamingRules(OmitResolutionAtOrBelow: "576p");

        Assert.Equal("Blade Runner 2049 (2017).mp4", RenameEngine.BuildFileName(
            template, At("576p"), ".mp4", rules));
        Assert.Equal("Blade Runner 2049 (2017) [4k].mp4", RenameEngine.BuildFileName(
            template, At("2160p"), ".mp4", rules));
    }

    // ---------------------------------------------- what stands in for a forbidden character

    private static NamingRules Replacing(string replacement) =>
        new(IllegalCharacterReplacement: replacement);

    [Theory]
    [InlineData("", "FaceOff (2019).mp4")]
    [InlineData(" ", "Face Off (2019).mp4")]
    [InlineData(".", "Face.Off (2019).mp4")]
    [InlineData("_", "Face_Off (2019).mp4")]
    [InlineData("-", "Face-Off (2019).mp4")]
    public void Writes_the_chosen_stand_in_for_a_forbidden_character(string replacement, string expected)
    {
        var name = RenameEngine.BuildFileName(
            RenameTemplate.Parse("{title} ({year})"), Movie("Face/Off"), ".mp4", Replacing(replacement));

        Assert.Equal(expected, name);
    }

    /// <summary>
    /// The one character the setting does not govern: a colon separates a title from its subtitle
    /// often enough that a dash reads better than whatever the rest are written as.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("_")]
    [InlineData("-")]
    public void A_colon_is_always_a_dash(string replacement)
    {
        var name = RenameEngine.BuildFileName(
            RenameTemplate.Parse("{title}"), Movie("Mission: Impossible"), ".mp4", Replacing(replacement));

        Assert.Equal("Mission - Impossible.mp4", name);
    }

    /// <summary>Otherwise "Who??" would come out as "Who__", and a name would end on one.</summary>
    [Fact]
    public void Stand_ins_neither_pile_up_nor_hang_off_the_end()
    {
        Assert.Equal("Who_ Me", RenameEngine.Sanitize("Who?? Me*", "_"));
        Assert.Equal("Me", RenameEngine.Sanitize("*Me*", "_"));
    }

    /// <summary>A hand-edited settings file must not be able to ask for an unusable name.</summary>
    [Fact]
    public void A_stand_in_that_is_itself_forbidden_is_ignored()
    {
        Assert.Equal("FaceOff", RenameEngine.Sanitize("Face/Off", "/"));
    }

    // ---------------------------------------------- a name given by hand

    [Fact]
    public void A_typed_name_keeps_the_file_s_own_extension()
    {
        Assert.Equal("My Own Name.mp4", RenameEngine.CleanFileName("My Own Name", ".mp4"));
        Assert.Equal("My Own Name.mp4", RenameEngine.CleanFileName("My Own Name.mp4", ".mp4"));
        Assert.Equal("My Own Name.mkv.mp4", RenameEngine.CleanFileName("My Own Name.mkv", ".mp4"));
    }

    [Fact]
    public void A_typed_name_goes_through_the_same_character_rules()
    {
        Assert.Equal("What-Ever.mp4", RenameEngine.CleanFileName("What/Ever", ".mp4", "-"));
        Assert.Equal("WhatEver.mp4", RenameEngine.CleanFileName("What/Ever", ".mp4"));
    }

    /// <summary>
    /// Spaces typed on purpose are not the template's business, so the separator does not reach
    /// them. There is no separator to apply here in any case: a typed name is not rendered.
    /// </summary>
    [Fact]
    public void A_typed_name_keeps_its_spaces()
    {
        Assert.Equal("Two Words.mp4", RenameEngine.CleanFileName("  Two Words  ", ".mp4"));
    }

    [Fact]
    public void Nothing_usable_typed_leaves_the_file_alone()
    {
        var directory = Directory.CreateTempSubdirectory("vmf-typed-empty").FullName;
        try
        {
            var path = Path.Combine(directory, "keep me.mp4");
            File.WriteAllText(path, "x");

            Assert.Equal(string.Empty, RenameEngine.CleanFileName("  ", ".mp4"));
            Assert.Equal(path, RenameEngine.RenameTo(path, "???"));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Renames_to_a_typed_name_and_still_resolves_collisions()
    {
        var directory = Directory.CreateTempSubdirectory("vmf-typed").FullName;
        try
        {
            var taken = Path.Combine(directory, "My Own Name.mp4");
            File.WriteAllText(taken, "x");
            var path = Path.Combine(directory, "arrival.2016.mp4");
            File.WriteAllText(path, "x");

            var renamed = RenameEngine.RenameTo(path, "My Own Name");

            Assert.Equal(Path.Combine(directory, "My Own Name (2).mp4"), renamed);
            Assert.True(File.Exists(renamed));
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
