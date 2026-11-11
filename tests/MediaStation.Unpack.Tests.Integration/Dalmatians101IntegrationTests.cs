using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaStation.Unpack.Cli;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Metadata;
using MediaStation.Unpack.Model;
using MediaStation.Unpack.Tests.Integration.Infrastructure;
using Xunit;

namespace MediaStation.Unpack.Tests.Integration;

public class Dalmatians101IntegrationTests
{
    [SkippableFact]
    public void ProfileMetadata_LoadsFromDiscIfAvailable()
    {
        string discPath = TestDataConfig.Require101Dalmatians();
        string profilePath = Path.Combine(discPath, "DATA", "PROFILE._ST");

        if (!File.Exists(profilePath))
        {
            TestDataConfig.Skip($"PROFILE._ST not found at '{profilePath}'.");
        }

        var profile = ProfileMetadata.LoadFromFile(profilePath);
        Assert.NotNull(profile);
        Assert.NotEmpty(profile.Assets);
        Assert.NotEmpty(profile.ParamTokens);

        Assert.Equal("image_6c14_Human1", profile.GetAssetName(278));
        Assert.Equal("spritemovie_6c14_Door3", profile.GetAssetName(234));
        Assert.Equal("open", profile.GetClipName(10024));
        Assert.Equal("sound_6c14_Leaves", profile.GetAssetName(2022));
    }

    [SkippableFact]
    public void Context174_ResolveActivePalette_MatchesReferenceColors()
    {
        string discPath = TestDataConfig.Require101Dalmatians();

        var title = new MediaStationTitle(discPath);
        title.LoadManifest();
        var ctx174 = title.LoadContext(174);
        Assert.NotNull(ctx174);

        var palette = ctx174.ResolveActivePalette();
        Assert.NotNull(palette);

        // Index 166 should be human skin tone (251, 211, 180)
        var skin = palette.GetColor(166);
        Assert.Equal(251, skin.R);
        Assert.Equal(211, skin.G);
        Assert.Equal(180, skin.B);

        // Index 180 should be fish orange tone (249, 167, 15)
        var fish = palette.GetColor(180);
        Assert.Equal(249, fish.R);
        Assert.Equal(167, fish.G);
        Assert.Equal(15, fish.B);
    }

    [SkippableFact]
    public void ContextFile_SpecifiedAsInput_ResolvesTargetContextAndMetadata()
    {
        string discPath = TestDataConfig.Require101Dalmatians();
        string cxtPath = Path.Combine(discPath, "DATA", "174.CXT");

        if (!File.Exists(cxtPath))
        {
            TestDataConfig.Skip($"174.CXT not found at '{cxtPath}'.");
        }

        var title = new MediaStationTitle(cxtPath);
        Assert.NotNull(title.BootStmPath);
        Assert.True(File.Exists(title.BootStmPath));

        title.LoadManifest();
        Assert.Equal((ushort)174, title.TargetContextId);
        Assert.NotNull(title.Profile);
        Assert.Equal("101Dalmatian", title.GameTitle);

        using var tempDir = new DisposableDirectory("ms_test_cxt_input_");

        var options = new ExtractOptions
        {
            OutputPath = tempDir,
            ExtractImages = true,
            ExtractSprites = false,
            ExtractMovies = false,
            ExtractAudio = false
        };

        var extractor = new Extractor(title, options);
        extractor.Run();

        string contextDir = Path.Combine(tempDir, "Context_174");
        Assert.True(Directory.Exists(contextDir));

        // Verify human-readable asset file names from PROFILE._ST are detected correctly
        string namedBonePath = Path.Combine(contextDir, "Image_6c06_Bone.png");
        Assert.True(File.Exists(namedBonePath));
    }

    [SkippableFact]
    public void BootStm_OrDirectory_SpecifiedAsInput_HasNullTargetContextId()
    {
        string discPath = TestDataConfig.Require101Dalmatians();
        string bootPath = Path.Combine(discPath, "DATA", "BOOT.STM");

        if (!File.Exists(bootPath))
        {
            TestDataConfig.Skip($"BOOT.STM not found at '{bootPath}'.");
        }

        var titleBoot = new MediaStationTitle(bootPath);
        titleBoot.LoadManifest();
        Assert.Null(titleBoot.TargetContextId);

        var titleDir = new MediaStationTitle(discPath);
        titleDir.LoadManifest();
        Assert.Null(titleDir.TargetContextId);
    }

    [SkippableFact]
    public void Deoptimizer_LuckyPopPickAPage_DeoptimizesCorrectly()
    {
        string discPath = TestDataConfig.Require101Dalmatians();

        var title = new MediaStationTitle(discPath);
        title.LoadManifest();
        var ctx122 = title.LoadContext(122);
        if (ctx122 == null)
        {
            TestDataConfig.Skip("Context 122 was not found in the loaded 101 Dalmatians title.");
        }

        var movie = ctx122!.Movies.FirstOrDefault(m => m.Name.Contains("LuckyPopPickAPage", StringComparison.OrdinalIgnoreCase));
        if (movie == null)
        {
            TestDataConfig.Skip("Movie 'LuckyPopPickAPage' was not found in Context 122.");
        }

        int originalImageCount = movie!.Images.Count;

        AnimateJsonExporter.DeoptimizeMovieSharedImages(movie);

        // Total images count must remain exactly the same
        Assert.Equal(originalImageCount, movie.Images.Count);

        // Image 0x0001 (body) and 0x000F (body 2) must be fully transparent
        var img1 = movie.Images.First(i => i.Index == 1).Image;
        Assert.All(img1.Pixels, p => Assert.Equal(0, p));

        var img15 = movie.Images.First(i => i.Index == 0x000F).Image;
        Assert.All(img15.Pixels, p => Assert.Equal(0, p));

        // Dependent image 0x0002 must contain pixels from both base body and head
        var img2 = movie.Images.First(i => i.Index == 2).Image;
        Assert.Contains(img2.Pixels, p => p != 0);
        Assert.True(img2.Width >= 223);
        Assert.True(img2.Height >= 279);
    }

    [SkippableFact]
    public void Cli_SpecifyingContextFileDirectly_Succeeds()
    {
        string discPath = TestDataConfig.Require101Dalmatians();
        string cxtPath = Path.Combine(discPath, "DATA", "174.CXT");
        if (!File.Exists(cxtPath))
        {
            TestDataConfig.Skip($"174.CXT not found at '{cxtPath}'.");
        }

        using var tempDir = new DisposableDirectory("ms_cli_test_");

        int exitCode = Program.Main([cxtPath, "-o", tempDir, "-e", "images"]);
        Assert.Equal(0, exitCode);

        string contextDir = Path.Combine(tempDir, "Context_174");
        Assert.True(Directory.Exists(contextDir));

        string namedBonePath = Path.Combine(contextDir, "Image_6c06_Bone.png");
        Assert.True(File.Exists(namedBonePath));
    }

    [SkippableFact]
    public void Cli_FormatOptions_ExportsSelectedFormats()
    {
        string discPath = TestDataConfig.Require101Dalmatians();
        string cxtPath = Path.Combine(discPath, "DATA", "174.CXT");
        if (!File.Exists(cxtPath))
        {
            TestDataConfig.Skip($"174.CXT not found at '{cxtPath}'.");
        }

        using var tempDir = new DisposableDirectory("ms_cli_fmt_test_");

        int exitCode = Program.Main([cxtPath, "-o", tempDir, "-e", "images", "-if", "png,webp,gif"]);
        Assert.Equal(0, exitCode);

        string contextDir = Path.Combine(tempDir, "Context_174");
        Assert.True(Directory.Exists(contextDir));

        // Verify that all 3 formats were exported for Bone
        string pngBone = Path.Combine(contextDir, "Image_6c06_Bone.png");
        string webpBone = Path.Combine(contextDir, "Image_6c06_Bone.webp");
        string gifBone = Path.Combine(contextDir, "Image_6c06_Bone.gif");

        Assert.True(File.Exists(pngBone), "PNG file should exist");
        Assert.True(File.Exists(webpBone), "WebP file should exist");
        Assert.True(File.Exists(gifBone), "GIF file should exist");
    }

    [SkippableFact]
    public void Cli_AnimateJsonOption_ExportsExpectedContextPackage()
    {
        string discPath = TestDataConfig.Require101Dalmatians();
        string cxtPath = Path.Combine(discPath, "DATA", "174.CXT");
        if (!File.Exists(cxtPath))
        {
            TestDataConfig.Skip($"174.CXT not found at '{cxtPath}'.");
        }

        using var tempDir = new DisposableDirectory("ms_cli_json_test_");

        int exitCode = Program.Main([cxtPath, "-o", tempDir, "-af", "json-animate"]);
        Assert.Equal(0, exitCode);

        string contextDir = Path.Combine(tempDir, "Context_174");
        Assert.True(Directory.Exists(contextDir));

        string jsonPath = Path.Combine(contextDir, "Animation.json");
        Assert.True(File.Exists(jsonPath));

        string jsonText = File.ReadAllText(jsonPath);
        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        Assert.Equal(60.0, root.GetProperty("metadata").GetProperty("framerate").GetDouble());
        var symbols = root.GetProperty("SYMBOL_DICTIONARY").GetProperty("Symbols");
        Assert.True(symbols.GetArrayLength() > 0);

        string spritesDir = Path.Combine(contextDir, "sprites");
        Assert.True(Directory.Exists(spritesDir));
        var pngFiles = Directory.GetFiles(spritesDir, "*.png");
        Assert.NotEmpty(pngFiles);
    }
}
