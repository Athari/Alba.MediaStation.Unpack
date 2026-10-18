using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using MsPoint = MediaStation.Unpack.Core.Point;

namespace MediaStation.Unpack.Graphics;

public static class ImageExporter
{
    public static Image<Rgba32> CreateRgbaImage(PixMapImage pixmap, Palette palette)
    {
        int width = Math.Max(1, pixmap.Width);
        int height = Math.Max(1, pixmap.Height);
        var image = new Image<Rgba32>(width, height);
        if (pixmap.Width <= 0 || pixmap.Height <= 0)
            return image;

        byte[] pixels = pixmap.Pixels;
        int stride = pixmap.Stride;
        byte? trans = pixmap.TransparentColorIndex;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var rowSpan = accessor.GetRowSpan(y);
                int srcRowOffset = y * stride;
                for (int x = 0; x < width; x++)
                {
                    byte colorIdx = srcRowOffset + x < pixels.Length ? pixels[srcRowOffset + x] : (byte)0;
                    rowSpan[x] = palette.GetColor(colorIdx, trans);
                }
            }
        });

        return image;
    }

    public static void SaveImage(PixMapImage pixmap, Palette palette, string filePath, GraphicFormat format = GraphicFormat.Png)
    {
        if (pixmap.Width <= 0 || pixmap.Height <= 0)
            return;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var image = CreateRgbaImage(pixmap, palette);
        SaveImageFile(image, palette, pixmap.TransparentColorIndex, filePath, format);
    }

    public static void SaveSpriteApng(
        IReadOnlyList<(PixMapImage Pixmap, MsPoint Origin)> frames,
        Palette palette,
        int frameRate,
        string filePath)
    {
        SaveSpriteAnimation(frames, palette, frameRate, filePath, GraphicFormat.Png);
    }

    public static void SaveSpriteAnimation(
        IReadOnlyList<(PixMapImage Pixmap, MsPoint Origin)> frames,
        Palette palette,
        int frameRate,
        string filePath,
        GraphicFormat format = GraphicFormat.Png)
    {
        if (frames.Count == 0)
            return;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (frames.Count == 1)
        {
            SaveImage(frames[0].Pixmap, palette, filePath, format);
            return;
        }

        // Calculate common canvas bounds
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        foreach (var (pixmap, origin) in frames)
        {
            if (pixmap.Width <= 0 || pixmap.Height <= 0)
                continue;
            minX = Math.Min(minX, origin.X);
            minY = Math.Min(minY, origin.Y);
            maxX = Math.Max(maxX, origin.X + pixmap.Width);
            maxY = Math.Max(maxY, origin.Y + pixmap.Height);
        }

        int canvasWidth = minX == int.MaxValue ? 1 : Math.Max(1, maxX - minX);
        int canvasHeight = minY == int.MaxValue ? 1 : Math.Max(1, maxY - minY);
        int fps = frameRate > 0 ? frameRate : 12;

        using var masterImage = new Image<Rgba32>(canvasWidth, canvasHeight);

        for (int i = 0; i < frames.Count; i++)
        {
            var (pixmap, origin) = frames[i];
            int destX = origin.X - (minX == int.MaxValue ? 0 : minX);
            int destY = origin.Y - (minY == int.MaxValue ? 0 : minY);

            using var frameImage = CreateRgbaImage(pixmap, palette);
            using var canvasFrame = new Image<Rgba32>(canvasWidth, canvasHeight);

            if (pixmap.Width > 0 && pixmap.Height > 0)
            {
                canvasFrame.Mutate(ctx => ctx.DrawImage(frameImage, new SixLabors.ImageSharp.Point(destX, destY), 1f));
            }

            if (i == 0)
            {
                // Root frame
                masterImage.Mutate(ctx => ctx.DrawImage(canvasFrame, 1f));
                ConfigureFrameMetadata(masterImage.Frames.RootFrame, 0, fps, format);
            }
            else
            {
                var addedFrame = masterImage.Frames.AddFrame(canvasFrame.Frames.RootFrame);
                ConfigureFrameMetadata(addedFrame, 0, fps, format);
            }
        }

        SaveAnimationMaster(masterImage, palette, filePath, format);
    }

    public static void SaveMovieApng(
        IReadOnlyList<MovieFrameItem> frames,
        Palette palette,
        string filePath)
    {
        SaveMovieAnimation(frames, palette, filePath, GraphicFormat.Png);
    }

    public static void SaveMovieAnimation(
        IReadOnlyList<MovieFrameItem> frames,
        Palette palette,
        string filePath,
        GraphicFormat format = GraphicFormat.Png)
    {
        if (frames.Count == 0)
            return;

        var visibleFrames = frames
            .Where(f => f.Pixmap.Width > 0 && f.Pixmap.Height > 0 && f.EndTimeMs > f.StartTimeMs)
            .ToList();

        if (visibleFrames.Count == 0)
            return;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        foreach (var f in visibleFrames)
        {
            minX = Math.Min(minX, f.Origin.X);
            minY = Math.Min(minY, f.Origin.Y);
            maxX = Math.Max(maxX, f.Origin.X + f.Pixmap.Width);
            maxY = Math.Max(maxY, f.Origin.Y + f.Pixmap.Height);
        }

        int canvasWidth = Math.Max(1, maxX - minX);
        int canvasHeight = Math.Max(1, maxY - minY);

        // Collect distinct boundary timestamps
        var timePoints = new SortedSet<uint>();
        foreach (var f in visibleFrames)
        {
            timePoints.Add(f.StartTimeMs);
            timePoints.Add(f.EndTimeMs);
        }

        var timeList = timePoints.ToList();
        var rawSlices = new List<(IReadOnlyList<MovieFrameItem> ActiveFrames, uint DurationMs)>();

        for (int i = 0; i < timeList.Count - 1; i++)
        {
            uint tStart = timeList[i];
            uint tEnd = timeList[i + 1];
            uint duration = tEnd - tStart;
            if (duration == 0)
                continue;

            // Frames active in interval [tStart, tEnd):
            // Order: Higher ZIndex drawn first (background), then by original Order ascending (foreground)
            var active = visibleFrames
                .Where(f => f.StartTimeMs <= tStart && f.EndTimeMs >= tEnd)
                .OrderByDescending(f => f.ZIndex)
                .ThenBy(f => f.Order)
                .ToList();

            rawSlices.Add((active, duration));
        }

        // Merge consecutive identical slices
        var mergedSlices = new List<(IReadOnlyList<MovieFrameItem> ActiveFrames, uint DurationMs)>();
        foreach (var (active, duration) in rawSlices)
        {
            if (mergedSlices.Count > 0 && AreActiveFramesEqual(mergedSlices[^1].ActiveFrames, active))
            {
                mergedSlices[^1] = (mergedSlices[^1].ActiveFrames, mergedSlices[^1].DurationMs + duration);
            }
            else
            {
                mergedSlices.Add((active, duration));
            }
        }

        if (mergedSlices.Count == 0)
            return;

        // If only 1 slice, save directly as static image
        if (mergedSlices.Count == 1)
        {
            using var singleImage = new Image<Rgba32>(canvasWidth, canvasHeight);
            foreach (var frameItem in mergedSlices[0].ActiveFrames)
            {
                using var frameRgba = CreateRgbaImage(frameItem.Pixmap, palette);
                int destX = frameItem.Origin.X - minX;
                int destY = frameItem.Origin.Y - minY;
                singleImage.Mutate(ctx => ctx.DrawImage(frameRgba, new SixLabors.ImageSharp.Point(destX, destY), 1f));
            }
            SaveImageFile(singleImage, palette, transparentIndex: null, filePath, format);
            return;
        }

        // Pre-create RGBA images for all unique PixMaps used in visibleFrames
        var rgbaCache = new Dictionary<PixMapImage, Image<Rgba32>>();
        try
        {
            foreach (var f in visibleFrames)
            {
                if (!rgbaCache.ContainsKey(f.Pixmap))
                {
                    rgbaCache[f.Pixmap] = CreateRgbaImage(f.Pixmap, palette);
                }
            }

            using var masterImage = new Image<Rgba32>(canvasWidth, canvasHeight);

            for (int i = 0; i < mergedSlices.Count; i++)
            {
                var (active, durationMs) = mergedSlices[i];

                using var canvasFrame = new Image<Rgba32>(canvasWidth, canvasHeight);
                foreach (var frameItem in active)
                {
                    var frameRgba = rgbaCache[frameItem.Pixmap];
                    int destX = frameItem.Origin.X - minX;
                    int destY = frameItem.Origin.Y - minY;
                    canvasFrame.Mutate(ctx => ctx.DrawImage(frameRgba, new SixLabors.ImageSharp.Point(destX, destY), 1f));
                }

                if (i == 0)
                {
                    masterImage.Mutate(ctx => ctx.DrawImage(canvasFrame, 1f));
                    ConfigureFrameMetadata(masterImage.Frames.RootFrame, durationMs, 0, format);
                }
                else
                {
                    var addedFrame = masterImage.Frames.AddFrame(canvasFrame.Frames.RootFrame);
                    ConfigureFrameMetadata(addedFrame, durationMs, 0, format);
                }
            }

            SaveAnimationMaster(masterImage, palette, filePath, format);
        }
        finally
        {
            foreach (var img in rgbaCache.Values)
            {
                img.Dispose();
            }
        }
    }

    public static PaletteQuantizer CreatePaletteQuantizer(Palette palette, byte? transparentIndex = null, bool transparent = true)
    {
        var distinctColors = new HashSet<Color>();

        if (transparent)
        {
            distinctColors.Add(Color.Transparent);
        }

        for (int i = 0; i < Palette.ColorCount; i++)
        {
            if (transparentIndex.HasValue && i == transparentIndex.Value)
            {
                distinctColors.Add(Color.Transparent);
            }
            else
            {
                distinctColors.Add(Color.FromPixel(palette[i]));
            }
        }

        var colorArray = distinctColors.Take(256).ToArray();
        return new PaletteQuantizer(
            colorArray,
            new QuantizerOptions
            {
                Dither = null,
                ColorMatchingMode = ColorMatchingMode.Exact
            });
    }

    private static void SaveImageFile(Image<Rgba32> image, Palette palette, byte? transparentIndex, string filePath, GraphicFormat format)
    {
        switch (format)
        {
            case GraphicFormat.Png:
                image.SaveAsPng(filePath);
                break;

            case GraphicFormat.Webp:
                image.SaveAsWebp(filePath, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
                break;

            case GraphicFormat.Gif:
                var quantizer = CreatePaletteQuantizer(palette, transparentIndex, transparent: transparentIndex.HasValue);
                image.SaveAsGif(filePath, new GifEncoder { Quantizer = quantizer });
                break;
        }
    }

    private static void ConfigureFrameMetadata(ImageFrame<Rgba32> frame, uint durationMs, int fps, GraphicFormat format)
    {
        switch (format)
        {
            case GraphicFormat.Png:
                var pngMeta = frame.Metadata.GetPngMetadata();
                pngMeta.FrameDelay = fps > 0 ? new Rational(1, (uint)fps) : new Rational(Math.Max(10, durationMs), 1000);
                pngMeta.DisposalMode = FrameDisposalMode.RestoreToBackground;
                pngMeta.BlendMode = FrameBlendMode.Source;
                break;

            case GraphicFormat.Webp:
                var webpMeta = frame.Metadata.GetWebpMetadata();
                webpMeta.FrameDelay = fps > 0 ? (uint)Math.Max(1, 1000 / fps) : Math.Max(10, durationMs);
                webpMeta.DisposalMode = FrameDisposalMode.RestoreToBackground;
                webpMeta.BlendMode = FrameBlendMode.Source;
                break;

            case GraphicFormat.Gif:
                var gifMeta = frame.Metadata.GetGifMetadata();
                gifMeta.FrameDelay = fps > 0 ? (int)Math.Max(2, Math.Round(100.0 / fps)) : (int)Math.Max(2, Math.Round(durationMs / 10.0));
                gifMeta.DisposalMode = FrameDisposalMode.RestoreToBackground;
                break;
        }
    }

    private static void SaveAnimationMaster(Image<Rgba32> masterImage, Palette palette, string filePath, GraphicFormat format)
    {
        switch (format)
        {
            case GraphicFormat.Png:
                var pngMeta = masterImage.Metadata.GetPngMetadata();
                pngMeta.RepeatCount = 0;
                pngMeta.AnimateRootFrame = true;
                masterImage.SaveAsPng(filePath);
                break;

            case GraphicFormat.Webp:
                var webpMeta = masterImage.Metadata.GetWebpMetadata();
                webpMeta.RepeatCount = 0;
                masterImage.SaveAsWebp(filePath, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
                break;

            case GraphicFormat.Gif:
                var gifMeta = masterImage.Metadata.GetGifMetadata();
                gifMeta.RepeatCount = 0;
                var quantizer = CreatePaletteQuantizer(palette, transparentIndex: 0, transparent: true);
                masterImage.SaveAsGif(filePath, new GifEncoder { Quantizer = quantizer });
                break;
        }
    }

    private static bool AreActiveFramesEqual(IReadOnlyList<MovieFrameItem> a, IReadOnlyList<MovieFrameItem> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
                return false;
        }
        return true;
    }
}
