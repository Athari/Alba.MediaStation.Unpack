using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Model;
using Xunit;

namespace MediaStation.Unpack.Tests;

public class DeduplicationTests
{
    [Fact]
    public void Context218_LionKing_SkipDuplicates_SkipsDuplicateGifsWhileKeepingUniqueAudio()
    {
        string cxtPath = @"Z:\DATA\218.CXT";
        if (!File.Exists(cxtPath))
            return;

        var title = new MediaStationTitle(cxtPath);
        title.LoadManifest();

        string tempDir = Path.Combine(Path.GetTempPath(), "ms_test_dedupe_" + Guid.NewGuid().ToString("N"));
        var logs = new List<string>();

        try
        {
            var options = new ExtractOptions
            {
                OutputPath = tempDir,
                ExtractImages = true,
                ExtractSprites = true,
                ExtractMovies = true,
                ExtractAudio = true,
                ImageFormats = [GraphicFormat.Gif],
                AnimationFormats = [GraphicFormat.Gif],
                SkipDuplicates = DuplicateScope.Context,
                Verbose = true
            };

            var extractor = new Extractor(title, options, logs.Add);
            extractor.Run();

            string contextDir = Path.Combine(tempDir, "Context_218");
            Assert.True(Directory.Exists(contextDir));

            // Movie 1022 and 1031:
            // Visual GIF for 1022 should exist, but duplicate GIF for 1031 should be skipped
            Assert.True(File.Exists(Path.Combine(contextDir, "Movie_1022.gif")));
            Assert.False(File.Exists(Path.Combine(contextDir, "Movie_1031.gif")));

            // Distinct audio tracks for 1022 and 1031 should BOTH exist
            Assert.True(File.Exists(Path.Combine(contextDir, "Movie_1022.wav")));
            Assert.True(File.Exists(Path.Combine(contextDir, "Movie_1031.wav")));

            // Image 1019 and 1020: 1019 exists, 1020 is duplicate and skipped
            Assert.True(File.Exists(Path.Combine(contextDir, "Image_1019.gif")));
            Assert.False(File.Exists(Path.Combine(contextDir, "Image_1020.gif")));

            // Check summary log contains simplified counter format: ", 8 skipped"
            string summaryLog = logs.Last(l => l.Contains("[Context_218]"));
            Assert.Contains("8 skipped", summaryLog);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Context218_LionKing_WithoutSkipDuplicates_ExtractsAllFiles()
    {
        string cxtPath = @"Z:\DATA\218.CXT";
        if (!File.Exists(cxtPath))
            return;

        var title = new MediaStationTitle(cxtPath);
        title.LoadManifest();

        string tempDir = Path.Combine(Path.GetTempPath(), "ms_test_no_dedupe_" + Guid.NewGuid().ToString("N"));
        var logs = new List<string>();

        try
        {
            var options = new ExtractOptions
            {
                OutputPath = tempDir,
                ExtractImages = true,
                ExtractSprites = true,
                ExtractMovies = true,
                ExtractAudio = true,
                ImageFormats = [GraphicFormat.Gif],
                AnimationFormats = [GraphicFormat.Gif],
                SkipDuplicates = DuplicateScope.None,
                Verbose = false
            };

            var extractor = new Extractor(title, options, logs.Add);
            extractor.Run();

            string contextDir = Path.Combine(tempDir, "Context_218");
            Assert.True(Directory.Exists(contextDir));

            // Both Movie 1022 and 1031 gifs should exist when deduplication is disabled
            Assert.True(File.Exists(Path.Combine(contextDir, "Movie_1022.gif")));
            Assert.True(File.Exists(Path.Combine(contextDir, "Movie_1031.gif")));

            // Summary log should not mention skipped
            string summaryLog = logs.Last(l => l.Contains("[Context_218]"));
            Assert.DoesNotContain("skipped", summaryLog);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("global", DuplicateScope.Global)]
    [InlineData("GLOBAL", DuplicateScope.Global)]
    [InlineData("context", DuplicateScope.Context)]
    [InlineData("Context", DuplicateScope.Context)]
    [InlineData(null, DuplicateScope.None)]
    [InlineData("", DuplicateScope.None)]
    [InlineData("   ", DuplicateScope.None)]
    public void Cli_ParseDuplicateScope_ValidValues_ReturnsExpectedScope(string? input, DuplicateScope expected)
    {
        // Invoke private static ParseDuplicateScope via reflection or test logic
        var method = typeof(MediaStation.Unpack.Cli.Program).GetMethod(
            "ParseDuplicateScope",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var result = (DuplicateScope)method.Invoke(null, [input])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Cli_ParseDuplicateScope_InvalidValue_ThrowsArgumentException()
    {
        var method = typeof(MediaStation.Unpack.Cli.Program).GetMethod(
            "ParseDuplicateScope",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, ["invalid_scope"]));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }
}

