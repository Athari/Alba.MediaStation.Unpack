using System.Text.Json;
using MediaStation.Unpack.Actors;
using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Metadata;
using MediaStation.Unpack.Model;
using Xunit;

namespace MediaStation.Unpack.Tests;

public class AnimateJsonExporterTests
{
    [Fact]
    public void AnimateJsonExporter_ExportsValidStructureAndAssets()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ms_test_animate_json_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var palette = new Palette();
            var context = new MediaStationContext { ContextId = 100, Name = "TestScene" };

            // 1. Create a SpriteMovieActor
            var sprite = new SpriteMovieActor
            {
                Id = 1,
                Name = "sprite_Door",
                FrameRate = 10
            };
            byte[] rawP1 = new byte[4 * 4];
            Array.Fill(rawP1, (byte)1);
            var pix1 = new PixMapImage(new ImageInfo(4, 4), rawP1);

            byte[] rawP2 = new byte[4 * 4];
            Array.Fill(rawP2, (byte)2);
            var pix2 = new PixMapImage(new ImageInfo(4, 4), rawP2);

            sprite.Frames.Add((pix1, new Point(10, 20), 101));
            sprite.Frames.Add((pix2, new Point(15, 25), 102));
            sprite.Clips[0x10] = new SpriteMovieClip(0x10, 0, 0, "open");
            context.Actors[1] = sprite;

            // 2. Create a StreamMovieActor
            var movie = new StreamMovieActor
            {
                Id = 2,
                Name = "movie_Intro",
                FrameRate = 0 // test auto-detection
            };
            movie.Images.Add(new MovieImageData(201, 100, pix1));
            movie.Images.Add(new MovieImageData(202, 200, pix2));

            // Layer 1 frame
            movie.Frames.Add(new MovieFrame
            {
                LayerId = 1,
                Index = 201,
                StartInMilliseconds = 0,
                EndInMilliseconds = 100,
                LeftTop = new Point(50, 60),
                ZIndex = 0
            });
            // Layer 2 frame
            movie.Frames.Add(new MovieFrame
            {
                LayerId = 2,
                Index = 202,
                StartInMilliseconds = 100,
                EndInMilliseconds = 200,
                LeftTop = new Point(70, 80),
                ZIndex = 1
            });
            // Add a mock audio sequence
            movie.Audio.AppendPcm(new byte[100]);
            context.Actors[2] = movie;

            // Export to Animate JSON
            AnimateJsonExporter.ExportContext(
                context,
                palette,
                tempDir,
                new AnimateJsonExportOptions { MasterFrameRate = 60.0 },
                getParamTokenName: id => id == 0x10 ? "open" : null);

            // Verify Animation.json exists
            string jsonPath = Path.Combine(tempDir, "Animation.json");
            Assert.True(File.Exists(jsonPath));

            string jsonContent = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // Verify Root Metadata
            Assert.True(root.TryGetProperty("metadata", out var meta));
            Assert.Equal(60.0, meta.GetProperty("framerate").GetDouble());
            Assert.Equal("1.0", meta.GetProperty("version").GetString());
            Assert.Equal(640, meta.GetProperty("width").GetInt32());
            Assert.Equal(480, meta.GetProperty("height").GetInt32());

            // Verify Master Comp Timeline
            Assert.True(root.TryGetProperty("ANIMATION", out var anim));
            var animTl = anim.GetProperty("TIMELINE");
            var masterLayers = animTl.GetProperty("LAYERS");
            Assert.Equal(2, masterLayers.GetArrayLength()); // Movie_Intro and Sprite_Door

            // Verify Symbol Dictionary
            Assert.True(root.TryGetProperty("SYMBOL_DICTIONARY", out var symDict));
            var symbols = symDict.GetProperty("Symbols");
            Assert.Equal(3, symbols.GetArrayLength()); // Movie_Intro, Sprite_Door, Sprite_Door_open

            // Verify Symbol Names & Native Framerates
            var symNames = new List<string>();
            foreach (var sym in symbols.EnumerateArray())
            {
                symNames.Add(sym.GetProperty("SYMBOL_name").GetString()!);
                if (sym.GetProperty("SYMBOL_name").GetString() == "Sprite_Door")
                {
                    Assert.Equal(10.0, sym.GetProperty("framerate").GetDouble());
                }
                else if (sym.GetProperty("SYMBOL_name").GetString() == "Movie_Intro")
                {
                    Assert.Equal(10.0, sym.GetProperty("framerate").GetDouble()); // 100ms auto-detected to 10.0 fps
                }
            }
            Assert.Contains("Movie_Intro", symNames);
            Assert.Contains("Sprite_Door", symNames);
            Assert.Contains("Sprite_Door_open", symNames);

            // Verify Sprites folder contents
            string spritesDir = Path.Combine(tempDir, "sprites");
            Assert.True(Directory.Exists(spritesDir));
            Assert.True(File.Exists(Path.Combine(spritesDir, "Sprite_Door_img_0101.png")));
            Assert.True(File.Exists(Path.Combine(spritesDir, "Sprite_Door_img_0102.png")));
            Assert.True(File.Exists(Path.Combine(spritesDir, "Movie_Intro_img_0201.png")));
            Assert.True(File.Exists(Path.Combine(spritesDir, "Movie_Intro_img_0202.png")));
            Assert.True(File.Exists(Path.Combine(spritesDir, "Movie_Intro.wav")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AnimateJsonExporter_RoundKeyframeIndices_AppliesRoundingProperly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ms_test_animate_round_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var palette = new Palette();
            var context = new MediaStationContext { ContextId = 101 };

            // 8 fps sprite has 60 / 8 = 7.5 frames per tick (fractional)
            var sprite = new SpriteMovieActor
            {
                Id = 10,
                Name = "sprite_Walker",
                FrameRate = 8
            };
            byte[] rawP = new byte[2 * 2];
            var pix = new PixMapImage(new ImageInfo(2, 2), rawP);
            sprite.Frames.Add((pix, new Point(0, 0), 1));
            sprite.Frames.Add((pix, new Point(0, 0), 2));
            context.Actors[10] = sprite;

            // Test 1: With RoundKeyframeIndices = false
            AnimateJsonExporter.ExportContext(
                context,
                palette,
                tempDir,
                new AnimateJsonExportOptions { MasterFrameRate = 60.0, RoundKeyframeIndices = false });

            string jsonUnrounded = File.ReadAllText(Path.Combine(tempDir, "Animation.json"));
            using (var doc1 = JsonDocument.Parse(jsonUnrounded))
            {
                var sym = doc1.RootElement.GetProperty("SYMBOL_DICTIONARY").GetProperty("Symbols")[0];
                var frames = sym.GetProperty("TIMELINE").GetProperty("LAYERS")[0].GetProperty("Frames");
                // Frame 1 starts at 7.5
                Assert.Equal(7.5, frames[1].GetProperty("index").GetDouble());
            }

            // Test 2: With RoundKeyframeIndices = true
            AnimateJsonExporter.ExportContext(
                context,
                palette,
                tempDir,
                new AnimateJsonExportOptions { MasterFrameRate = 60.0, RoundKeyframeIndices = true });

            string jsonRounded = File.ReadAllText(Path.Combine(tempDir, "Animation.json"));
            using (var doc2 = JsonDocument.Parse(jsonRounded))
            {
                var sym = doc2.RootElement.GetProperty("SYMBOL_DICTIONARY").GetProperty("Symbols")[0];
                var frames = sym.GetProperty("TIMELINE").GetProperty("LAYERS")[0].GetProperty("Frames");
                // Frame 1 starts at 8 (rounded)
                Assert.Equal(8, frames[1].GetProperty("index").GetInt32());
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Cli_AnimateJsonOption_ExportsExpectedContextPackage()
    {
        string cxtPath = @"Z:\DATA\174.CXT";
        if (!File.Exists(cxtPath))
            return;

        string tempDir = Path.Combine(Path.GetTempPath(), "ms_cli_json_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            int exitCode = MediaStation.Unpack.Cli.Program.Main([cxtPath, "-o", tempDir, "-af", "json-animate"]);
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
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
