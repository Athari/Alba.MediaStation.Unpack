using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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

    public static void SaveImage(PixMapImage pixmap, Palette palette, string filePath)
    {
        if (pixmap.Width <= 0 || pixmap.Height <= 0)
            return;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var image = CreateRgbaImage(pixmap, palette);
        image.SaveAsPng(filePath);
    }

    public static void SaveSpriteApng(
        IReadOnlyList<(PixMapImage Pixmap, MsPoint Origin)> frames,
        Palette palette,
        int frameRate,
        string filePath)
    {
        if (frames.Count == 0)
            return;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (frames.Count == 1)
        {
            SaveImage(frames[0].Pixmap, palette, filePath);
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
        var delay = new Rational(1, (uint)fps);

        using var masterImage = new Image<Rgba32>(canvasWidth, canvasHeight);
        var pngMeta = masterImage.Metadata.GetPngMetadata();
        pngMeta.RepeatCount = 0; // infinite loop
        pngMeta.AnimateRootFrame = true;

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
                var rootFrameMeta = masterImage.Frames.RootFrame.Metadata.GetPngMetadata();
                rootFrameMeta.FrameDelay = delay;
                rootFrameMeta.DisposalMode = FrameDisposalMode.RestoreToBackground;
                rootFrameMeta.BlendMode = FrameBlendMode.Source;
            }
            else
            {
                var addedFrame = masterImage.Frames.AddFrame(canvasFrame.Frames.RootFrame);
                var frameMeta = addedFrame.Metadata.GetPngMetadata();
                frameMeta.FrameDelay = delay;
                frameMeta.DisposalMode = FrameDisposalMode.RestoreToBackground;
                frameMeta.BlendMode = FrameBlendMode.Source;
            }
        }

        masterImage.SaveAsPng(filePath);
    }

    public static void SaveMovieApng(
        IReadOnlyList<MovieFrameItem> frames,
        Palette palette,
        string filePath)
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

        // If only 1 slice, save directly as static PNG
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
            singleImage.SaveAsPng(filePath);
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
            var pngMeta = masterImage.Metadata.GetPngMetadata();
            pngMeta.RepeatCount = 0; // loop infinitely
            pngMeta.AnimateRootFrame = true;

            for (int i = 0; i < mergedSlices.Count; i++)
            {
                var (active, durationMs) = mergedSlices[i];
                uint ms = Math.Max(10, durationMs);
                var delay = new Rational(ms, 1000);

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
                    var rootMeta = masterImage.Frames.RootFrame.Metadata.GetPngMetadata();
                    rootMeta.FrameDelay = delay;
                    rootMeta.DisposalMode = FrameDisposalMode.RestoreToBackground;
                    rootMeta.BlendMode = FrameBlendMode.Source;
                }
                else
                {
                    var addedFrame = masterImage.Frames.AddFrame(canvasFrame.Frames.RootFrame);
                    var frameMeta = addedFrame.Metadata.GetPngMetadata();
                    frameMeta.FrameDelay = delay;
                    frameMeta.DisposalMode = FrameDisposalMode.RestoreToBackground;
                    frameMeta.BlendMode = FrameBlendMode.Source;
                }
            }

            masterImage.SaveAsPng(filePath);
        }
        finally
        {
            foreach (var img in rgbaCache.Values)
            {
                img.Dispose();
            }
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
