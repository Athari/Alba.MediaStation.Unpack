using System.Buffers.Binary;
using System.Text;
using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Audio;
using MediaStation.Unpack.Metadata;
using MediaStation.Unpack.Model;
using MediaStation.Unpack.Actors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MediaStation.Unpack.Tests;

public class CoreReaderTests
{
    [Fact]
    public void ParameterBinaryReader_ReadsTypedDatumsCorrectly()
    {
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            // Uint8
            writer.Write((ushort)DatumType.Uint8);
            writer.Write((byte)42);

            // Uint16
            writer.Write((ushort)DatumType.Uint16);
            writer.Write((ushort)12345);

            // Uint32
            writer.Write((ushort)DatumType.Uint32);
            writer.Write((uint)0xDEADBEEF);

            // Point (GraphicUnit, GraphicUnit)
            writer.Write((ushort)DatumType.Point);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)100);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)200);

            // Rect
            writer.Write((ushort)DatumType.Rect);
            writer.Write((ushort)DatumType.Point);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)10);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)20);
            writer.Write((ushort)DatumType.GraphicSize);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)300);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)400);

            // String
            writer.Write((ushort)DatumType.String);
            writer.Write((ushort)DatumType.Uint32);
            string testStr = "Hello MediaStation";
            writer.Write((uint)(testStr.Length + 1));
            writer.Write(Encoding.ASCII.GetBytes(testStr));
            writer.Write((byte)0);

            // ChannelIdent (always Big-Endian)
            writer.Write((ushort)DatumType.ChannelIdent);
            Span<byte> ch = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(ch, Chunk.MakeTag('i', 'g', 'o', 'd'));
            writer.Write(ch);
        }

        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new ParameterBinaryReader(ms);

        Assert.Equal(42, reader.ReadTypedByte());
        Assert.Equal(12345, reader.ReadTypedUint16());
        Assert.Equal(0xDEADBEEF, reader.ReadTypedUint32());

        var pt = reader.ReadTypedPoint();
        Assert.Equal(100, pt.X);
        Assert.Equal(200, pt.Y);

        var rc = reader.ReadTypedRect();
        Assert.Equal(10, rc.X);
        Assert.Equal(20, rc.Y);
        Assert.Equal(300, rc.Width);
        Assert.Equal(400, rc.Height);

        Assert.Equal("Hello MediaStation", reader.ReadTypedString());
        Assert.Equal(Chunk.MakeTag('i', 'g', 'o', 'd'), reader.ReadTypedChannelIdent());
    }

    [Fact]
    public void Chunk_ParsesHeaderAndRemainingCorrectly()
    {
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            Span<byte> tag = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(tag, Chunk.MakeTag('a', '0', '0', '1'));
            writer.Write(tag);
            writer.Write((uint)4); // length
            writer.Write(new byte[] { 1, 2, 3, 4 });
        }

        ms.Seek(0, SeekOrigin.Begin);
        var chunk = new Chunk(ms);

        Assert.Equal(Chunk.MakeTag('a', '0', '0', '1'), chunk.Id);
        Assert.Equal("a001", chunk.TagString);
        Assert.Equal(4u, chunk.Length);
        Assert.Equal(4, chunk.ChunkBytesRemaining);

        byte[] data = chunk.ReadBytes(4);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, data);
        Assert.Equal(0, chunk.ChunkBytesRemaining);
    }
}

public class DecompressorTests
{
    [Fact]
    public void Rle8_DecompressesLiteralAndRepeatRuns()
    {
        // 4x2 image
        // Line 1: repeat 4 of color 7 (operation=4, color=7), eol (0x00, 0x00)
        // Line 2: literal 4 of [1, 2, 3, 4] (operation=0x00, length=4, 1, 2, 3, 4), eoi (0x00, 0x01)
        var stream = new List<byte>
        {
            // Line 1
            4, 7, // repeat 4 of color 7
            0, 0, // end of line

            // Line 2
            0, 4, 1, 2, 3, 4, // literal run of 4
            0, 1  // end of image
        };

        byte[] result = Decompressor.DecompressRle8(stream.ToArray(), 4, 2);

        byte[] expected =
        [
            7, 7, 7, 7,
            1, 2, 3, 4
        ];

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Ccc_Decompresses4x4BlockCorrectly()
    {
        // 4x4 image with color1=10, color2=20
        // bitmask: 0b1010_0101_1100_0011 (0xA5C3)
        // Row 0: 1 0 1 0 -> 20, 10, 20, 10
        // Row 1: 0 1 0 1 -> 10, 20, 10, 20
        // Row 2: 1 1 0 0 -> 20, 20, 10, 10
        // Row 3: 0 0 1 1 -> 10, 10, 20, 20
        byte[] compressed =
        [
            10, 20,
            0xA5, 0xC3
        ];

        byte[] result = Decompressor.DecompressCcc(compressed, 4, 4);

        byte[] expected =
        [
            20, 10, 20, 10,
            10, 20, 10, 20,
            20, 20, 10, 10,
            10, 10, 20, 20
        ];

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CccTransparent_DecompressesCommandStream()
    {
        // Command stream:
        // 0x00010000: 1 block at (0, 0)
        // Block: color1=5, color2=9, bitmask=0xFFFF (all color2)
        // 0xFF000000: End
        byte[] compressed =
        [
            0x00, 0x01, 0x00, 0x00, // 1 block at (0, 0)
            5, 9, 0xFF, 0xFF,       // Block data
            0xFF, 0x00, 0x00, 0x00  // End command
        ];

        byte[] result = Decompressor.DecompressCccTransparent(compressed, 4, 4);

        Assert.Equal(16, result.Length);
        Assert.All(result, b => Assert.Equal(9, b));
    }
}

public class AudioTests
{
    [Fact]
    public void WavWriter_ProducesValidRiffHeader()
    {
        byte[] pcm = new byte[200];
        var ms = new MemoryStream();
        WavWriter.WritePcmWav(ms, pcm, sampleRate: 22050, channels: 1, bitsPerSample: 16);

        byte[] wav = ms.ToArray();
        Assert.Equal((int)'R', wav[0]);
        Assert.Equal((int)'I', wav[1]);
        Assert.Equal((int)'F', wav[2]);
        Assert.Equal((int)'F', wav[3]);

        Assert.Equal((int)'W', wav[8]);
        Assert.Equal((int)'A', wav[9]);
        Assert.Equal((int)'V', wav[10]);
        Assert.Equal((int)'E', wav[11]);

        // Total file size: 44 header + 200 data = 244
        Assert.Equal(244, wav.Length);
    }

    [Fact]
    public void ImaAdpcmDecoder_DecodesBlockWithoutErrors()
    {
        // 8 bytes block:
        // Header: 0x0000 (last = 0, stepIndex = 0)
        // 6 data bytes: 12 nibbles of 0 (minimal delta)
        byte[] block = new byte[8];
        byte[] pcm = ImaAdpcmDecoder.DecodeDviIma(block, blockAlign: 8);

        // 12 samples * 2 bytes = 24 bytes PCM
        Assert.Equal(24, pcm.Length);
    }
}

public class ImageExporterTests
{
    [Fact]
    public void Palette_ResolvesColorsAndTransparency()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[0] = 255; rgb[1] = 0; rgb[2] = 0; // index 0: Red
        rgb[3] = 0; rgb[4] = 255; rgb[5] = 0; // index 1: Green

        var palette = new Palette(rgb);
        var color0 = palette.GetColor(0);
        Assert.Equal(255, color0.R);
        Assert.Equal(0, color0.G);
        Assert.Equal(0, color0.B);
        Assert.Equal(255, color0.A);

        var transColor = palette.GetColor(0, transparentIndex: 0);
        Assert.Equal(0, transColor.A); // Transparent
    }

    [Fact]
    public void ImageExporter_ExportsStaticPngAndApngCorrectly()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[4] = 255; // Color 1 = Green (0, 255, 0)
        rgb[6] = 255; rgb[7] = 255; // Color 2 = Yellow (255, 255, 0)
        var palette = new Palette(rgb);

        var tempDir = Path.Combine(Path.GetTempPath(), "ms_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // 1. Static PNG
            byte[] rawPixels = new byte[8 * 8];
            Array.Fill(rawPixels, (byte)1);
            var info = new MediaStation.Unpack.Graphics.ImageInfo(8, 8);
            var pixmap = new PixMapImage(info, rawPixels, transparentColorIndex: 0);
            string pngPath = Path.Combine(tempDir, "test.png");
            ImageExporter.SaveImage(pixmap, palette, pngPath);

            Assert.True(File.Exists(pngPath));
            using (var loadedPng = Image.Load<Rgba32>(pngPath))
            {
                Assert.Equal(8, loadedPng.Width);
                Assert.Equal(8, loadedPng.Height);
                Assert.Equal(1, loadedPng.Frames.Count);
                Assert.Equal(255, loadedPng[0, 0].G);
            }

            // 2. Multi-frame APNG
            byte[] frame2Pixels = new byte[8 * 8];
            Array.Fill(frame2Pixels, (byte)2);
            var info2 = new MediaStation.Unpack.Graphics.ImageInfo(8, 8);
            var pixmap2 = new PixMapImage(info2, frame2Pixels, transparentColorIndex: 0);

            var frames = new List<(PixMapImage Pixmap, Core.Point Origin)>
            {
                (pixmap, new Core.Point(0, 0)),
                (pixmap2, new Core.Point(2, 2))
            };

            string apngPath = Path.Combine(tempDir, "test_anim.png");
            ImageExporter.SaveSpriteApng(frames, palette, frameRate: 10, apngPath);

            Assert.True(File.Exists(apngPath));
            using (var loadedApng = Image.Load<Rgba32>(apngPath))
            {
                Assert.Equal(2, loadedApng.Frames.Count);
                // Canvas should fit (0,0) 8x8 and (2,2) 8x8 -> 10x10
                Assert.Equal(10, loadedApng.Width);
                Assert.Equal(10, loadedApng.Height);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ImageExporter_ExportsStaticWebpAndGifCorrectly()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[3] = 0; rgb[4] = 200; rgb[5] = 100; // Color 1
        var palette = new Palette(rgb);

        var tempDir = Path.Combine(Path.GetTempPath(), "ms_test_webp_gif_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            byte[] rawPixels = new byte[8 * 8];
            Array.Fill(rawPixels, (byte)1);
            rawPixels[0] = 0; // Transparent pixel
            var info = new MediaStation.Unpack.Graphics.ImageInfo(8, 8);
            var pixmap = new PixMapImage(info, rawPixels, transparentColorIndex: 0);

            // 1. WebP
            string webpPath = Path.Combine(tempDir, "test.webp");
            ImageExporter.SaveImage(pixmap, palette, webpPath, GraphicFormat.Webp);
            Assert.True(File.Exists(webpPath));
            using (var loadedWebp = Image.Load<Rgba32>(webpPath))
            {
                Assert.Equal(8, loadedWebp.Width);
                Assert.Equal(8, loadedWebp.Height);
                Assert.Equal(0, loadedWebp[0, 0].A); // Transparent
                Assert.Equal(200, loadedWebp[1, 0].G);
                Assert.Equal(100, loadedWebp[1, 0].B);
                Assert.Equal(255, loadedWebp[1, 0].A);
            }

            // 2. GIF
            string gifPath = Path.Combine(tempDir, "test.gif");
            ImageExporter.SaveImage(pixmap, palette, gifPath, GraphicFormat.Gif);
            Assert.True(File.Exists(gifPath));
            using (var loadedGif = Image.Load<Rgba32>(gifPath))
            {
                Assert.Equal(8, loadedGif.Width);
                Assert.Equal(8, loadedGif.Height);
                Assert.Equal(0, loadedGif[0, 0].A); // Transparent
                Assert.Equal(200, loadedGif[1, 0].G);
                Assert.Equal(100, loadedGif[1, 0].B);
                Assert.Equal(255, loadedGif[1, 0].A);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ImageExporter_ExportsSpriteAnimationWebpAndGifCorrectly()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[3] = 10; rgb[4] = 20; rgb[5] = 30; // Color 1
        rgb[6] = 40; rgb[7] = 50; rgb[8] = 60; // Color 2
        var palette = new Palette(rgb);

        var tempDir = Path.Combine(Path.GetTempPath(), "ms_test_anim_wg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            byte[] p1 = new byte[4 * 4];
            Array.Fill(p1, (byte)1);
            var pix1 = new PixMapImage(new MediaStation.Unpack.Graphics.ImageInfo(4, 4), p1, transparentColorIndex: 0);

            byte[] p2 = new byte[4 * 4];
            Array.Fill(p2, (byte)2);
            var pix2 = new PixMapImage(new MediaStation.Unpack.Graphics.ImageInfo(4, 4), p2, transparentColorIndex: 0);

            var frames = new List<(PixMapImage Pixmap, Core.Point Origin)>
            {
                (pix1, new Core.Point(0, 0)),
                (pix2, new Core.Point(2, 2))
            };

            // 1. WebP animation
            string webpPath = Path.Combine(tempDir, "anim.webp");
            ImageExporter.SaveSpriteAnimation(frames, palette, frameRate: 10, webpPath, GraphicFormat.Webp);
            Assert.True(File.Exists(webpPath));
            using (var loadedWebp = Image.Load<Rgba32>(webpPath))
            {
                Assert.Equal(2, loadedWebp.Frames.Count);
                Assert.Equal(6, loadedWebp.Width);
                Assert.Equal(6, loadedWebp.Height);
            }

            // 2. GIF animation
            string gifPath = Path.Combine(tempDir, "anim.gif");
            ImageExporter.SaveSpriteAnimation(frames, palette, frameRate: 10, gifPath, GraphicFormat.Gif);
            Assert.True(File.Exists(gifPath));
            using (var loadedGif = Image.Load<Rgba32>(gifPath))
            {
                Assert.Equal(2, loadedGif.Frames.Count);
                Assert.Equal(6, loadedGif.Width);
                Assert.Equal(6, loadedGif.Height);
                // Verify delay is clamped/rounded (100 / 10 = 10 centiseconds)
                var rootMeta = loadedGif.Frames.RootFrame.Metadata.GetGifMetadata();
                Assert.Equal(10, rootMeta.FrameDelay);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ImageExporter_ExportsMovieAnimationWebpAndGifCorrectly()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[3] = 255; // Color 1 = Red
        rgb[7] = 255; // Color 2 = Green
        var palette = new Palette(rgb);

        var tempDir = Path.Combine(Path.GetTempPath(), "ms_test_mov_wg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            byte[] p1 = new byte[4 * 4];
            Array.Fill(p1, (byte)1);
            var pix1 = new PixMapImage(new MediaStation.Unpack.Graphics.ImageInfo(4, 4), p1, transparentColorIndex: 0);

            byte[] p2 = new byte[4 * 4];
            Array.Fill(p2, (byte)2);
            var pix2 = new PixMapImage(new MediaStation.Unpack.Graphics.ImageInfo(4, 4), p2, transparentColorIndex: 0);

            var items = new List<MovieFrameItem>
            {
                new() { Pixmap = pix1, Origin = new Core.Point(0, 0), StartTimeMs = 0, EndTimeMs = 50, ZIndex = 0, Order = 0 },
                new() { Pixmap = pix2, Origin = new Core.Point(2, 0), StartTimeMs = 50, EndTimeMs = 100, ZIndex = 0, Order = 1 }
            };

            // WebP
            string webpPath = Path.Combine(tempDir, "movie.webp");
            ImageExporter.SaveMovieAnimation(items, palette, webpPath, GraphicFormat.Webp);
            Assert.True(File.Exists(webpPath));
            using (var imgWebp = Image.Load<Rgba32>(webpPath))
            {
                Assert.Equal(2, imgWebp.Frames.Count);
                Assert.Equal(6, imgWebp.Width);
            }

            // GIF
            string gifPath = Path.Combine(tempDir, "movie.gif");
            ImageExporter.SaveMovieAnimation(items, palette, gifPath, GraphicFormat.Gif);
            Assert.True(File.Exists(gifPath));
            using (var imgGif = Image.Load<Rgba32>(gifPath))
            {
                Assert.Equal(2, imgGif.Frames.Count);
                Assert.Equal(6, imgGif.Width);
                var meta = imgGif.Frames.RootFrame.Metadata.GetGifMetadata();
                // 50ms / 10 = 5 centiseconds
                Assert.Equal(5, meta.FrameDelay);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ProfileMetadata_LoadsFromDiscIfAvailable()
    {
        string profilePath = @"Z:\DATA\PROFILE._ST";
        if (!File.Exists(profilePath))
            return;

        var profile = MediaStation.Unpack.Metadata.ProfileMetadata.LoadFromFile(profilePath);
        Assert.NotNull(profile);
        Assert.NotEmpty(profile.Assets);
        Assert.NotEmpty(profile.ParamTokens);

        Assert.Equal("image_6c14_Human1", profile.GetAssetName(278));
        Assert.Equal("spritemovie_6c14_Door3", profile.GetAssetName(234));
        Assert.Equal("open", profile.GetClipName(10024));
        Assert.Equal("sound_6c14_Leaves", profile.GetAssetName(2022));
    }

    [Fact]
    public void SaveMovieApng_CompositesTimelineWithoutGhosting()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ms_test_movie_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var palette = new Palette();
            palette[1] = new Rgba32(255, 0, 0, 255); // Red
            palette[2] = new Rgba32(0, 0, 255, 255); // Blue
            palette[3] = new Rgba32(0, 255, 0, 255); // Green

            // Frame 1: 5x5 Red @ (0, 0), [0ms - 100ms]
            byte[] p1 = new byte[5 * 5];
            Array.Fill(p1, (byte)1);
            var pix1 = new PixMapImage(new MediaStation.Unpack.Graphics.ImageInfo(5, 5), p1, transparentColorIndex: 0);

            // Frame 2: 5x5 Blue @ (10, 0), [100ms - 200ms]
            byte[] p2 = new byte[5 * 5];
            Array.Fill(p2, (byte)2);
            var pix2 = new PixMapImage(new MediaStation.Unpack.Graphics.ImageInfo(5, 5), p2, transparentColorIndex: 0);

            var items = new List<MovieFrameItem>
            {
                new() { Pixmap = pix1, Origin = new MediaStation.Unpack.Core.Point(0, 0), StartTimeMs = 0, EndTimeMs = 100, ZIndex = 0, Order = 0 },
                new() { Pixmap = pix2, Origin = new MediaStation.Unpack.Core.Point(10, 0), StartTimeMs = 100, EndTimeMs = 200, ZIndex = 0, Order = 1 }
            };

            string outPath = Path.Combine(tempDir, "seq_movie.png");
            ImageExporter.SaveMovieApng(items, palette, outPath);

            Assert.True(File.Exists(outPath));
            using (var img = Image.Load<Rgba32>(outPath))
            {
                Assert.Equal(2, img.Frames.Count);
                Assert.Equal(15, img.Width); // 0 to 10+5 = 15
                Assert.Equal(5, img.Height);

                // Frame 0: Red at (0, 0), Transparent at (10, 0)
                Assert.Equal(255, img.Frames[0][0, 0].R);
                Assert.Equal(255, img.Frames[0][0, 0].A);
                Assert.Equal(0, img.Frames[0][10, 0].A);

                // Frame 1: Blue at (10, 0), Transparent at (0, 0) -> NO GHOSTING of Frame 1!
                Assert.Equal(255, img.Frames[1][10, 0].B);
                Assert.Equal(255, img.Frames[1][10, 0].A);
                Assert.Equal(0, img.Frames[1][0, 0].A); // Frame 1 MUST be gone!
            }

            // Test 2: Overlapping frames (Background + Animated overlay)
            // Background: 15x15 Green @ (0, 0), [0ms - 200ms]
            byte[] pBg = new byte[15 * 15];
            Array.Fill(pBg, (byte)3);
            var pixBg = new PixMapImage(new MediaStation.Unpack.Graphics.ImageInfo(15, 15), pBg, transparentColorIndex: 0);

            // Overlay: 5x5 Red @ (5, 5), [100ms - 200ms]
            var overlapItems = new List<MovieFrameItem>
            {
                new() { Pixmap = pixBg, Origin = new MediaStation.Unpack.Core.Point(0, 0), StartTimeMs = 0, EndTimeMs = 200, ZIndex = 0, Order = 0 },
                new() { Pixmap = pix1, Origin = new MediaStation.Unpack.Core.Point(5, 5), StartTimeMs = 100, EndTimeMs = 200, ZIndex = 0, Order = 1 }
            };

            string overlapPath = Path.Combine(tempDir, "overlap_movie.png");
            ImageExporter.SaveMovieApng(overlapItems, palette, overlapPath);

            using (var img = Image.Load<Rgba32>(overlapPath))
            {
                Assert.Equal(2, img.Frames.Count);

                // Frame 0 [0-100ms]: Background only, (5, 5) is Green
                Assert.Equal(255, img.Frames[0][5, 5].G);

                // Frame 1 [100-200ms]: Background + Overlay, (5, 5) is Red, (0, 0) is still Green
                Assert.Equal(255, img.Frames[1][0, 0].G);
                Assert.Equal(255, img.Frames[1][5, 5].R);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResolveActivePalette_PrioritizesInitializedContextPaletteOverPaletteActors()
    {
        var context = new MediaStationContext();
        byte[] dummyBytes1 = new byte[Palette.SizeInBytes];
        dummyBytes1[0] = 111; // R of color 0
        var palActor = new PaletteActor();
        palActor.Name = "palette_cutscene";
        typeof(PaletteActor).GetProperty("Palette")!.SetValue(palActor, new Palette(dummyBytes1));
        context.Actors[1] = palActor;

        byte[] dummyBytes2 = new byte[Palette.SizeInBytes];
        dummyBytes2[0] = 222; // R of color 0
        context.Palette = new Palette(dummyBytes2);

        var resolved = context.ResolveActivePalette();
        Assert.Same(context.Palette, resolved);
        Assert.Equal(222, resolved[0].R);
    }

    [Fact]
    public void ResolveActivePalette_PrefersDefaultScreenPaletteActorWhenContextPaletteNotInitialized()
    {
        var context = new MediaStationContext(); // context.Palette is uninitialized

        byte[] cutsceneBytes = new byte[Palette.SizeInBytes];
        cutsceneBytes[0] = 50;
        var cutsceneActor = new PaletteActor { Name = "palette_newborn" };
        typeof(PaletteActor).GetProperty("Palette")!.SetValue(cutsceneActor, new Palette(cutsceneBytes));
        context.Actors[1] = cutsceneActor;

        byte[] defaultBytes = new byte[Palette.SizeInBytes];
        defaultBytes[0] = 100;
        var defaultActor = new PaletteActor { Name = "palette_defaultScreen" };
        typeof(PaletteActor).GetProperty("Palette")!.SetValue(defaultActor, new Palette(defaultBytes));
        context.Actors[2] = defaultActor;

        var resolved = context.ResolveActivePalette();
        Assert.Same(defaultActor.Palette, resolved);
        Assert.Equal(100, resolved[0].R);
    }

    [Fact]
    public void Context174_ResolveActivePalette_MatchesReferenceColors()
    {
        string discPath = @"Z:\";
        if (!Directory.Exists(discPath))
            return;

        var title = new MediaStation.Unpack.Model.MediaStationTitle(discPath);
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

    [Fact]
    public void ContextFile_SpecifiedAsInput_ResolvesTargetContextAndMetadata()
    {
        string cxtPath = @"Z:\DATA\174.CXT";
        if (!File.Exists(cxtPath))
            return;

        var title = new MediaStation.Unpack.Model.MediaStationTitle(cxtPath);
        Assert.NotNull(title.BootStmPath);
        Assert.True(File.Exists(title.BootStmPath));

        title.LoadManifest();
        Assert.Equal((ushort)174, title.TargetContextId);
        Assert.NotNull(title.Profile);
        Assert.Equal("101Dalmatian", title.GameTitle);

        string tempDir = Path.Combine(Path.GetTempPath(), "ms_test_cxt_input_" + Guid.NewGuid().ToString("N"));
        try
        {
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
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BootStm_OrDirectory_SpecifiedAsInput_HasNullTargetContextId()
    {
        string bootPath = @"Z:\DATA\BOOT.STM";
        if (!File.Exists(bootPath))
            return;

        var titleBoot = new MediaStation.Unpack.Model.MediaStationTitle(bootPath);
        titleBoot.LoadManifest();
        Assert.Null(titleBoot.TargetContextId);

        var titleDir = new MediaStation.Unpack.Model.MediaStationTitle(@"Z:\");
        titleDir.LoadManifest();
        Assert.Null(titleDir.TargetContextId);
    }

    [Fact]
    public void Cli_SpecifyingContextFileDirectly_Succeeds()
    {
        string cxtPath = @"Z:\DATA\174.CXT";
        if (!File.Exists(cxtPath))
            return;

        string tempDir = Path.Combine(Path.GetTempPath(), "ms_cli_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            int exitCode = MediaStation.Unpack.Cli.Program.Main([cxtPath, "-o", tempDir, "-e", "images"]);
            Assert.Equal(0, exitCode);

            string contextDir = Path.Combine(tempDir, "Context_174");
            Assert.True(Directory.Exists(contextDir));

            string namedBonePath = Path.Combine(contextDir, "Image_6c06_Bone.png");
            Assert.True(File.Exists(namedBonePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Cli_FormatOptions_ExportsSelectedFormats()
    {
        string cxtPath = @"Z:\DATA\174.CXT";
        if (!File.Exists(cxtPath))
            return;

        string tempDir = Path.Combine(Path.GetTempPath(), "ms_cli_fmt_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            int exitCode = MediaStation.Unpack.Cli.Program.Main([cxtPath, "-o", tempDir, "-e", "images", "-if", "png,webp,gif"]);
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
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CreatePaletteQuantizer_ThrowsWhenColorsExceed256()
    {
        // Default palette has 256 distinct grayscale colors (0..255)
        var palette = new Palette();

        // With transparent: true and transparentIndex: null, adding Color.Transparent results in 257 distinct colors
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            ImageExporter.CreatePaletteQuantizer(palette, transparentIndex: null, transparent: true);
        });

        Assert.Contains("256", ex.Message);
    }

    [Fact]
    public void SaveImage_Png_UsesPaletteColorType()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ms_test_png_pal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var palette = new Palette();
            palette[0] = new Rgba32(0, 0, 0, 0); // Transparent
            palette[1] = new Rgba32(255, 0, 0, 255); // Red

            byte[] pixels = [1, 1, 0, 1];
            var pixmap = new PixMapImage(new MediaStation.Unpack.Graphics.ImageInfo(2, 2), pixels, transparentColorIndex: 0);

            string outPath = Path.Combine(tempDir, "image.png");
            ImageExporter.SaveImage(pixmap, palette, outPath, GraphicFormat.Png);

            Assert.True(File.Exists(outPath));
            var info = Image.Identify(outPath);
            var pngMeta = info.Metadata.GetPngMetadata();
            Assert.Equal(PngColorType.Palette, pngMeta.ColorType);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveSpriteAnimation_Png_UsesPaletteColorType()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ms_test_apng_pal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var palette = new Palette();
            palette[0] = new Rgba32(0, 0, 0, 0);
            palette[1] = new Rgba32(255, 0, 0, 255);
            palette[2] = new Rgba32(0, 255, 0, 255);

            byte[] p1 = [1, 1, 1, 1];
            byte[] p2 = [2, 2, 2, 2];
            var pix1 = new PixMapImage(new MediaStation.Unpack.Graphics.ImageInfo(2, 2), p1, transparentColorIndex: 0);
            var pix2 = new PixMapImage(new MediaStation.Unpack.Graphics.ImageInfo(2, 2), p2, transparentColorIndex: 0);

            var frames = new List<(PixMapImage Pixmap, Core.Point Origin)>
            {
                (pix1, new Core.Point(0, 0)),
                (pix2, new Core.Point(0, 0))
            };

            string outPath = Path.Combine(tempDir, "anim.png");
            ImageExporter.SaveSpriteAnimation(frames, palette, 10, outPath, GraphicFormat.Png);

            Assert.True(File.Exists(outPath));
            var info = Image.Identify(outPath);
            var pngMeta = info.Metadata.GetPngMetadata();
            Assert.Equal(PngColorType.Palette, pngMeta.ColorType);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MovieFrame_FirstGeneration_ReadsCorrectly()
    {
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(BinaryPrimitives.ReverseEndianness(Chunk.MakeTag('a', '0', '0', '1')));
            writer.Write((uint)0); // Placeholder length

            long payloadStart = ms.Position;

            // First generation layout:
            // BlitType (Uint16)
            writer.Write((ushort)DatumType.Uint16);
            writer.Write((ushort)MovieBlitType.Uncompressed);
            // StartInMilliseconds (Uint32)
            writer.Write((ushort)DatumType.Uint32);
            writer.Write((uint)100);
            // EndInMilliseconds (Uint32)
            writer.Write((ushort)DatumType.Uint32);
            writer.Write((uint)600);
            // LeftTop.x (Uint16)
            writer.Write((ushort)DatumType.Uint16);
            writer.Write((ushort)15);
            // LeftTop.y (Uint16)
            writer.Write((ushort)DatumType.Uint16);
            writer.Write((ushort)25);
            // Index (Uint32)
            writer.Write((ushort)DatumType.Uint32);
            writer.Write((uint)42);
            // KeyframeIndex (Uint32)
            writer.Write((ushort)DatumType.Uint32);
            writer.Write((uint)1);
            // KeepAfterEnd (Uint8)
            writer.Write((ushort)DatumType.Uint8);
            writer.Write((byte)1);

            long payloadEnd = ms.Position;
            uint length = (uint)(payloadEnd - payloadStart);
            ms.Seek(4, SeekOrigin.Begin);
            writer.Write(length);
        }

        ms.Seek(0, SeekOrigin.Begin);
        var chunk = new Chunk(ms);
        var frame = new MovieFrame(chunk, isFirstGeneration: true);

        Assert.Equal(MovieBlitType.Uncompressed, frame.BlitType);
        Assert.Equal(100u, frame.StartInMilliseconds);
        Assert.Equal(600u, frame.EndInMilliseconds);
        Assert.Equal(15, frame.LeftTop.X);
        Assert.Equal(25, frame.LeftTop.Y);
        Assert.Equal(42u, frame.Index);
        Assert.Equal(1u, frame.KeyframeIndex);
        Assert.True(frame.KeepAfterEnd);
        Assert.Equal(0u, frame.LayerId);
        Assert.Equal(0, frame.ZIndex);
    }

    [Fact]
    public void StreamMovieActor_ReadFrameData_UsesIsFirstGeneration()
    {
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(BinaryPrimitives.ReverseEndianness(Chunk.MakeTag('a', '0', '0', '1')));
            writer.Write((uint)0);

            long payloadStart = ms.Position;

            // FrameDataSection tag (0x06AA)
            writer.Write((ushort)DatumType.Uint16);
            writer.Write((ushort)0x06AA);

            // Frame count: 1
            writer.Write((ushort)DatumType.Uint16);
            writer.Write((ushort)1);

            // First generation frame:
            writer.Write((ushort)DatumType.Uint16);
            writer.Write((ushort)MovieBlitType.Uncompressed);
            writer.Write((ushort)DatumType.Uint32);
            writer.Write((uint)0);
            writer.Write((ushort)DatumType.Uint32);
            writer.Write((uint)100);
            writer.Write((ushort)DatumType.Uint16);
            writer.Write((ushort)0);
            writer.Write((ushort)DatumType.Uint16);
            writer.Write((ushort)0);
            writer.Write((ushort)DatumType.Uint32);
            writer.Write((uint)10);
            writer.Write((ushort)DatumType.Uint32);
            writer.Write((uint)0);
            writer.Write((ushort)DatumType.Uint8);
            writer.Write((byte)0);

            long payloadEnd = ms.Position;
            uint length = (uint)(payloadEnd - payloadStart);
            ms.Seek(4, SeekOrigin.Begin);
            writer.Write(length);
        }

        ms.Seek(0, SeekOrigin.Begin);
        var chunk = new Chunk(ms);
        var movieActor = new StreamMovieActor { IsFirstGeneration = true };
        movieActor.ReadFramesChunk(chunk);

        Assert.Single(movieActor.Frames);
        Assert.Equal(10u, movieActor.Frames[0].Index);
        Assert.Equal(100u, movieActor.Frames[0].EndInMilliseconds);
    }
}



