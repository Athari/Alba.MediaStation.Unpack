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

    [Fact]
    public void Deoptimizer_UnsharesFramesAndClearsBaseImage()
    {
        var movie = new StreamMovieActor
        {
            Id = 1,
            Name = "movie_TestUnshare"
        };

        // Shared base image: 20x20, all pixels = 1
        byte[] basePixels = new byte[20 * 20];
        Array.Fill(basePixels, (byte)1);
        var basePixMap = new PixMapImage(new ImageInfo(20, 20), basePixels, transparentColorIndex: 0);

        // Dependent head 1: 10x10, all pixels = 2
        byte[] head1Pixels = new byte[10 * 10];
        Array.Fill(head1Pixels, (byte)2);
        var head1PixMap = new PixMapImage(new ImageInfo(10, 10), head1Pixels, transparentColorIndex: 0);

        // Dependent head 2: 10x10, all pixels = 3
        byte[] head2Pixels = new byte[10 * 10];
        Array.Fill(head2Pixels, (byte)3);
        var head2PixMap = new PixMapImage(new ImageInfo(10, 10), head2Pixels, transparentColorIndex: 0);

        movie.Images.Add(new MovieImageData(1, 200, basePixMap));
        movie.Images.Add(new MovieImageData(2, 100, head1PixMap));
        movie.Images.Add(new MovieImageData(3, 200, head2PixMap));

        // Frame 0: Base image spanning 0..200 ms at (10, 10)
        movie.Frames.Add(new MovieFrame
        {
            Index = 1,
            StartInMilliseconds = 0,
            EndInMilliseconds = 200,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(10, 10)
        });

        // Frame 1: Head 1 spanning 0..100 ms at (15, 5)
        movie.Frames.Add(new MovieFrame
        {
            Index = 2,
            StartInMilliseconds = 0,
            EndInMilliseconds = 100,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(15, 5)
        });

        // Frame 2: Head 2 spanning 100..200 ms at (15, 6)
        movie.Frames.Add(new MovieFrame
        {
            Index = 3,
            StartInMilliseconds = 100,
            EndInMilliseconds = 200,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(15, 6)
        });

        AnimateJsonExporter.DeoptimizeMovieSharedImages(movie);

        // 1. Shared base image must be cleared (fully transparent, original 20x20 size)
        var clearedBase = movie.Images.First(i => i.Index == 1).Image;
        Assert.Equal(20, clearedBase.Width);
        Assert.Equal(20, clearedBase.Height);
        Assert.All(clearedBase.Pixels, p => Assert.Equal(0, p));

        // 2. Dependent image 2 must be merged with base image:
        // Union: min(10, 15)=10, min(10, 5)=5, max(10+20, 15+10)=30, max(10+20, 5+10)=30 => 20x25
        var mergedHead1 = movie.Images.First(i => i.Index == 2).Image;
        Assert.Equal(20, mergedHead1.Width);
        Assert.Equal(25, mergedHead1.Height);
        var frameHead1 = movie.Frames.First(f => f.Index == 2);
        Assert.Equal(new Point(10, 5), frameHead1.LeftTop);

        // Verify head 1 pixels at local offset (5, 0) within merged canvas
        Assert.Equal(2, mergedHead1.Pixels[0 * 20 + 5]); // head pixel
        Assert.Equal(1, mergedHead1.Pixels[5 * 20 + 0]); // base body pixel

        // 3. Dependent image 3 must be merged with base image:
        // Union: min(10, 15)=10, min(10, 6)=6, max(30, 25)=30, max(30, 16)=30 => 20x24
        var mergedHead2 = movie.Images.First(i => i.Index == 3).Image;
        Assert.Equal(20, mergedHead2.Width);
        Assert.Equal(24, mergedHead2.Height);
        var frameHead2 = movie.Frames.First(f => f.Index == 3);
        Assert.Equal(new Point(10, 6), frameHead2.LeftTop);
        Assert.Equal(3, mergedHead2.Pixels[0 * 20 + 5]); // head pixel
    }

    [Fact]
    public void Deoptimizer_PreservesImagesThatAppearInIsolation()
    {
        var movie = new StreamMovieActor
        {
            Id = 2,
            Name = "movie_IsolatedTest"
        };

        byte[] p1 = new byte[10 * 10];
        Array.Fill(p1, (byte)5);
        var pix1 = new PixMapImage(new ImageInfo(10, 10), p1, transparentColorIndex: 0);

        byte[] p2 = new byte[10 * 10];
        Array.Fill(p2, (byte)6);
        var pix2 = new PixMapImage(new ImageInfo(10, 10), p2, transparentColorIndex: 0);

        movie.Images.Add(new MovieImageData(1, 200, pix1));
        movie.Images.Add(new MovieImageData(2, 200, pix2));

        // Image 1 active alone during 0..100 ms
        movie.Frames.Add(new MovieFrame
        {
            Index = 1,
            StartInMilliseconds = 0,
            EndInMilliseconds = 100,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(0, 0)
        });

        // Image 1 and Image 2 active together during 100..200 ms
        movie.Frames.Add(new MovieFrame
        {
            Index = 1,
            StartInMilliseconds = 100,
            EndInMilliseconds = 200,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(0, 0)
        });
        movie.Frames.Add(new MovieFrame
        {
            Index = 2,
            StartInMilliseconds = 100,
            EndInMilliseconds = 200,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(5, 5)
        });

        AnimateJsonExporter.DeoptimizeMovieSharedImages(movie);

        // Image 1 appeared in isolation during 0..100 ms, so it must NOT be cleared!
        var img1 = movie.Images.First(i => i.Index == 1).Image;
        Assert.Contains(img1.Pixels, p => p == 5);
    }

    [Fact]
    public void Deoptimizer_LuckyPopPickAPage_DeoptimizesCorrectly()
    {
        string discPath = @"Z:\";
        if (!Directory.Exists(discPath))
            return;

        var title = new MediaStationTitle(discPath);
        title.LoadManifest();
        var ctx122 = title.LoadContext(122);
        var movie = ctx122.Movies.FirstOrDefault(m => m.Name.Contains("LuckyPopPickAPage", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(movie);

        int originalImageCount = movie.Images.Count;

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
}
