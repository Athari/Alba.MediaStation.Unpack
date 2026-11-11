using MediaStation.Unpack.Cli;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Cli;

public class ExtractOptionsTests
{
    [Fact]
    public void Cli_ParseExtractOptions_GraphicsPreset_EnablesOnlyVisuals()
    {
        var options = Program.ParseExtractOptions("graphics");

        Assert.True(options.ExtractImages);
        Assert.True(options.ExtractSprites);
        Assert.True(options.ExtractMovies);
        Assert.False(options.ExtractAudio);
        Assert.False(options.ExtractSounds);
    }

    [Fact]
    public void Cli_ParseExtractOptions_AllPreset_EnablesAllFiveTypes()
    {
        var options = Program.ParseExtractOptions("all");

        Assert.True(options.ExtractImages);
        Assert.True(options.ExtractSprites);
        Assert.True(options.ExtractMovies);
        Assert.True(options.ExtractAudio);
        Assert.True(options.ExtractSounds);
    }

    [Theory]
    [InlineData("images", true, false, false, false, false)]
    [InlineData("image", true, false, false, false, false)]
    [InlineData("sprites", false, true, false, false, false)]
    [InlineData("sprite", false, true, false, false, false)]
    [InlineData("movies", false, false, true, false, false)]
    [InlineData("movie", false, false, true, false, false)]
    [InlineData("sounds", false, false, false, false, true)]
    [InlineData("sound", false, false, false, false, true)]
    [InlineData("audio", false, false, false, true, false)]
    [InlineData("movies,audio", false, false, true, true, false)]
    [InlineData("sounds,audio", false, false, false, true, true)]
    [InlineData("images,sounds", true, false, false, false, true)]
    public void Cli_ParseExtractOptions_IndividualAndCombinedTokens(
        string input,
        bool expectedImages,
        bool expectedSprites,
        bool expectedMovies,
        bool expectedAudio,
        bool expectedSounds)
    {
        var options = Program.ParseExtractOptions(input);

        Assert.Equal(expectedImages, options.ExtractImages);
        Assert.Equal(expectedSprites, options.ExtractSprites);
        Assert.Equal(expectedMovies, options.ExtractMovies);
        Assert.Equal(expectedAudio, options.ExtractAudio);
        Assert.Equal(expectedSounds, options.ExtractSounds);
    }

    [Theory]
    [InlineData("img")]
    [InlineData("spr")]
    [InlineData("mov")]
    [InlineData("snd")]
    [InlineData("sfx")]
    [InlineData("movie-audio")]
    public void Cli_ParseExtractOptions_DroppedAliases_AreNotRecognized(string input)
    {
        var options = Program.ParseExtractOptions(input);

        Assert.False(options.ExtractImages);
        Assert.False(options.ExtractSprites);
        Assert.False(options.ExtractMovies);
        Assert.False(options.ExtractAudio);
        Assert.False(options.ExtractSounds);
    }
}
