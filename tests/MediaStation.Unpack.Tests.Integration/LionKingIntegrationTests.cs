using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Model;
using MediaStation.Unpack.Tests.Integration.Infrastructure;
using Xunit;

namespace MediaStation.Unpack.Tests.Integration;

public class LionKingIntegrationTests
{
    [SkippableFact]
    public void Context218_LionKing_SkipDuplicates_SkipsDuplicateGifsWhileKeepingUniqueAudio()
    {
        string discPath = TestDataConfig.RequireLionKing();
        string cxtPath = Path.Combine(discPath, "DATA", "218.CXT");
        if (!File.Exists(cxtPath))
        {
            TestDataConfig.Skip($"218.CXT not found at '{cxtPath}'.");
        }

        var title = new MediaStationTitle(cxtPath);
        title.LoadManifest();

        using var tempDir = new DisposableDirectory("ms_test_dedupe_");
        var logs = new List<string>();

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

    [SkippableFact]
    public void Context218_LionKing_WithoutSkipDuplicates_ExtractsAllFiles()
    {
        string discPath = TestDataConfig.RequireLionKing();
        string cxtPath = Path.Combine(discPath, "DATA", "218.CXT");
        if (!File.Exists(cxtPath))
        {
            TestDataConfig.Skip($"218.CXT not found at '{cxtPath}'.");
        }

        var title = new MediaStationTitle(cxtPath);
        title.LoadManifest();

        using var tempDir = new DisposableDirectory("ms_test_no_dedupe_");
        var logs = new List<string>();

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

    [SkippableFact]
    public void Context218_LionKing_MovieGif_WithoutAudio_DoesNotExportWav()
    {
        string discPath = TestDataConfig.RequireLionKing();
        string cxtPath = Path.Combine(discPath, "DATA", "218.CXT");
        if (!File.Exists(cxtPath))
        {
            TestDataConfig.Skip($"218.CXT not found at '{cxtPath}'.");
        }

        var title = new MediaStationTitle(cxtPath);
        title.LoadManifest();

        using var tempDir = new DisposableDirectory("ms_test_no_wav_");
        var logs = new List<string>();

        var options = new ExtractOptions
        {
            OutputPath = tempDir,
            ExtractImages = false,
            ExtractSprites = false,
            ExtractMovies = true,
            ExtractAudio = false,
            AnimationFormats = [GraphicFormat.Gif],
            SkipDuplicates = DuplicateScope.None,
            Verbose = true
        };

        var extractor = new Extractor(title, options, logs.Add);
        extractor.Run();

        string contextDir = Path.Combine(tempDir, "Context_218");
        Assert.True(Directory.Exists(contextDir));

        // Movie 1022 gif should exist
        Assert.True(File.Exists(Path.Combine(contextDir, "Movie_1022.gif")));

        // Zero WAV audio tracks should exist
        Assert.Empty(Directory.EnumerateFiles(contextDir, "*.wav"));
    }
}
