using System.Text.Json;
using MediaStation.Unpack.Actors;
using MediaStation.Unpack.Cli;
using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Metadata;
using MediaStation.Unpack.Model;
using Xunit;

namespace MediaStation.Unpack.Tests;

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

    [Fact]
    public void AnimateJsonExporter_ExportAudioFalse_DoesNotWriteWav()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ms_test_animate_no_audio_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var context = new MediaStationContext { ContextId = 1, Name = "TestContext" };
            var palette = new Palette();

            var pix = new PixMapImage(new ImageInfo(10, 10), new byte[10 * 10]);
            var movie = new StreamMovieActor { Id = 10, Name = "Movie_Sample" };
            movie.Images.Add(new MovieImageData(1, 100, pix));
            movie.Frames.Add(new MovieFrame
            {
                LayerId = 1,
                Index = 1,
                StartInMilliseconds = 0,
                EndInMilliseconds = 100,
                LeftTop = new Point(0, 0),
                ZIndex = 0
            });
            movie.Audio.AppendPcm(new byte[100]);
            context.Actors[10] = movie;

            var options = new AnimateJsonExportOptions { ExportAudio = false };
            AnimateJsonExporter.ExportContext(context, palette, tempDir, options);

            string spritesDir = Path.Combine(tempDir, "sprites");
            Assert.True(Directory.Exists(spritesDir));
            Assert.True(File.Exists(Path.Combine(spritesDir, "Movie_Sample_img_0001.png")));
            Assert.False(File.Exists(Path.Combine(spritesDir, "Movie_Sample.wav")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
