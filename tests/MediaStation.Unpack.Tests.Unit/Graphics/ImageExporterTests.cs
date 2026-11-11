using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Model;
using MediaStation.Unpack.Tests.Unit.TestUtilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using ImageInfo = MediaStation.Unpack.Graphics.ImageInfo;
using Point = MediaStation.Unpack.Core.Point;

namespace MediaStation.Unpack.Tests.Unit.Graphics;

public class ImageExporterTests
{
    [Fact]
    public void ImageExporter_ExportsStaticPngAndApngCorrectly()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[4] = 255; // Color 1 = Green (0, 255, 0)
        rgb[6] = 255; rgb[7] = 255; // Color 2 = Yellow (255, 255, 0)
        var palette = new Palette(rgb);

        using var tempDir = new DisposableDirectory("ms_test_png_");

        // 1. Static PNG
        byte[] rawPixels = new byte[8 * 8];
        Array.Fill(rawPixels, (byte)1);
        var info = new ImageInfo(8, 8);
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
        var info2 = new ImageInfo(8, 8);
        var pixmap2 = new PixMapImage(info2, frame2Pixels, transparentColorIndex: 0);

        var frames = new List<(PixMapImage Pixmap, Point Origin)>
        {
            (pixmap, new Point(0, 0)),
            (pixmap2, new Point(2, 2))
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

    [Fact]
    public void ImageExporter_ExportsStaticWebpAndGifCorrectly()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[3] = 0; rgb[4] = 200; rgb[5] = 100; // Color 1
        var palette = new Palette(rgb);

        using var tempDir = new DisposableDirectory("ms_test_webp_gif_");

        byte[] rawPixels = new byte[8 * 8];
        Array.Fill(rawPixels, (byte)1);
        rawPixels[0] = 0; // Transparent pixel
        var info = new ImageInfo(8, 8);
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

    [Fact]
    public void ImageExporter_ExportsSpriteAnimationWebpAndGifCorrectly()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[3] = 10; rgb[4] = 20; rgb[5] = 30; // Color 1
        rgb[6] = 40; rgb[7] = 50; rgb[8] = 60; // Color 2
        var palette = new Palette(rgb);

        using var tempDir = new DisposableDirectory("ms_test_anim_wg_");

        byte[] p1 = new byte[4 * 4];
        Array.Fill(p1, (byte)1);
        var pix1 = new PixMapImage(new ImageInfo(4, 4), p1, transparentColorIndex: 0);

        byte[] p2 = new byte[4 * 4];
        Array.Fill(p2, (byte)2);
        var pix2 = new PixMapImage(new ImageInfo(4, 4), p2, transparentColorIndex: 0);

        var frames = new List<(PixMapImage Pixmap, Point Origin)>
        {
            (pix1, new Point(0, 0)),
            (pix2, new Point(2, 2))
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

    [Fact]
    public void ImageExporter_ExportsMovieAnimationWebpAndGifCorrectly()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[3] = 255; // Color 1 = Red
        rgb[7] = 255; // Color 2 = Green
        var palette = new Palette(rgb);

        using var tempDir = new DisposableDirectory("ms_test_mov_wg_");

        byte[] p1 = new byte[4 * 4];
        Array.Fill(p1, (byte)1);
        var pix1 = new PixMapImage(new ImageInfo(4, 4), p1, transparentColorIndex: 0);

        byte[] p2 = new byte[4 * 4];
        Array.Fill(p2, (byte)2);
        var pix2 = new PixMapImage(new ImageInfo(4, 4), p2, transparentColorIndex: 0);

        var items = new List<MovieFrameItem>
        {
            new() { Pixmap = pix1, Origin = new Point(0, 0), StartTimeMs = 0, EndTimeMs = 50, ZIndex = 0, Order = 0 },
            new() { Pixmap = pix2, Origin = new Point(2, 0), StartTimeMs = 50, EndTimeMs = 100, ZIndex = 0, Order = 1 }
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

    [Fact]
    public void SaveMovieApng_CompositesTimelineWithoutGhosting()
    {
        using var tempDir = new DisposableDirectory("ms_test_movie_");

        var palette = new Palette();
        palette[1] = new Rgba32(255, 0, 0, 255); // Red
        palette[2] = new Rgba32(0, 0, 255, 255); // Blue
        palette[3] = new Rgba32(0, 255, 0, 255); // Green

        // Frame 1: 5x5 Red @ (0, 0), [0ms - 100ms]
        byte[] p1 = new byte[5 * 5];
        Array.Fill(p1, (byte)1);
        var pix1 = new PixMapImage(new ImageInfo(5, 5), p1, transparentColorIndex: 0);

        // Frame 2: 5x5 Blue @ (10, 0), [100ms - 200ms]
        byte[] p2 = new byte[5 * 5];
        Array.Fill(p2, (byte)2);
        var pix2 = new PixMapImage(new ImageInfo(5, 5), p2, transparentColorIndex: 0);

        var items = new List<MovieFrameItem>
        {
            new() { Pixmap = pix1, Origin = new Point(0, 0), StartTimeMs = 0, EndTimeMs = 100, ZIndex = 0, Order = 0 },
            new() { Pixmap = pix2, Origin = new Point(10, 0), StartTimeMs = 100, EndTimeMs = 200, ZIndex = 0, Order = 1 }
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
        var pixBg = new PixMapImage(new ImageInfo(15, 15), pBg, transparentColorIndex: 0);

        // Overlay: 5x5 Red @ (5, 5), [100ms - 200ms]
        var overlapItems = new List<MovieFrameItem>
        {
            new() { Pixmap = pixBg, Origin = new Point(0, 0), StartTimeMs = 0, EndTimeMs = 200, ZIndex = 0, Order = 0 },
            new() { Pixmap = pix1, Origin = new Point(5, 5), StartTimeMs = 100, EndTimeMs = 200, ZIndex = 0, Order = 1 }
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

    [Fact]
    public void SaveImage_Png_UsesPaletteColorType()
    {
        using var tempDir = new DisposableDirectory("ms_test_png_pal_");

        var palette = new Palette();
        palette[0] = new Rgba32(0, 0, 0, 0); // Transparent
        palette[1] = new Rgba32(255, 0, 0, 255); // Red

        byte[] pixels = [1, 1, 0, 1];
        var pixmap = new PixMapImage(new ImageInfo(2, 2), pixels, transparentColorIndex: 0);

        string outPath = Path.Combine(tempDir, "image.png");
        ImageExporter.SaveImage(pixmap, palette, outPath, GraphicFormat.Png);

        Assert.True(File.Exists(outPath));
        var info = Image.Identify(outPath);
        var pngMeta = info.Metadata.GetPngMetadata();
        Assert.Equal(PngColorType.Palette, pngMeta.ColorType);
    }

    [Fact]
    public void SaveSpriteAnimation_Png_UsesPaletteColorType()
    {
        using var tempDir = new DisposableDirectory("ms_test_apng_pal_");

        var palette = new Palette();
        palette[0] = new Rgba32(0, 0, 0, 0);
        palette[1] = new Rgba32(255, 0, 0, 255);
        palette[2] = new Rgba32(0, 255, 0, 255);

        byte[] p1 = [1, 1, 1, 1];
        byte[] p2 = [2, 2, 2, 2];
        var pix1 = new PixMapImage(new ImageInfo(2, 2), p1, transparentColorIndex: 0);
        var pix2 = new PixMapImage(new ImageInfo(2, 2), p2, transparentColorIndex: 0);

        var frames = new List<(PixMapImage Pixmap, Point Origin)>
        {
            (pix1, new Point(0, 0)),
            (pix2, new Point(0, 0))
        };

        string outPath = Path.Combine(tempDir, "anim.png");
        ImageExporter.SaveSpriteAnimation(frames, palette, 10, outPath, GraphicFormat.Png);

        Assert.True(File.Exists(outPath));
        var info = Image.Identify(outPath);
        var pngMeta = info.Metadata.GetPngMetadata();
        Assert.Equal(PngColorType.Palette, pngMeta.ColorType);
    }
}
