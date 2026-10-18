using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;

namespace MediaStation.Unpack.Actors;

public class StripInfoNode
{
    public uint Index { get; set; }
    public uint StreamId { get; set; }
    public uint LengthInBytes { get; set; }
    public Rect Rect { get; set; }
}

public class DiskImageActor : Actor
{
    public ushort MaxStripsInMemory { get; set; }
    public int StripThickness { get; set; } = 640;
    public bool UseVerticalStrips { get; set; }
    public ushort MaxImagesInMemory { get; set; }
    public bool DecompressInPlace { get; set; }

    public Dictionary<uint, StripInfoNode> StripInfos { get; } = new();
    public Dictionary<uint, PixMapImage> StripImages { get; } = new();

    public DiskImageActor() : base(ActorType.DiskImage)
    {
    }

    public override void ReadParameter(Chunk chunk, ushort paramType)
    {
        switch (paramType)
        {
            case 0x001B: // kActorHeaderChannelIdent
                ChannelIdent = chunk.ReadTypedChannelIdent();
                break;

            case 0x001F: // kActorHeaderStartup
                IsVisible = chunk.ReadTypedByte() != 0;
                break;

            case 0x0021: // kActorHeaderDiscardAfterUse
                chunk.ReadTypedByteOrUint16();
                break;

            case 0x0032: // kActorHeaderLoadType
                DecompressInPlace = chunk.ReadTypedByte() != 0;
                break;

            case 0x0037: // kActorHeaderInstallType
                chunk.ReadTypedByte();
                break;

            case 0x0774: // kActorHeaderDiskImageMaxStrips
                MaxStripsInMemory = chunk.ReadTypedUint16();
                break;

            case 0x0775: // kActorHeaderDiskImageStripWidth
                StripThickness = chunk.ReadTypedUint16();
                if (StripThickness == 0)
                    StripThickness = 640;
                break;

            case 0x0776: // kActorHeaderDiskImageUnk1
                UseVerticalStrips = chunk.ReadTypedByte() != 0;
                break;

            case 0x0777: // kActorHeaderDiskImageMaxImages
                MaxImagesInMemory = chunk.ReadTypedUint16();
                break;

            case 0x0778: // kActorHeaderDiskImageStripInfo
            {
                uint index = chunk.ReadTypedUint16();
                uint streamId = chunk.ReadTypedUint16();
                uint length = chunk.ReadTypedUint32();

                Point origin = UseVerticalStrips
                    ? new Point(BoundingBox.Left + (StripThickness * (int)index), BoundingBox.Top)
                    : new Point(BoundingBox.Left, BoundingBox.Top + (StripThickness * (int)index));

                int width = UseVerticalStrips ? StripThickness : BoundingBox.Width;
                int height = UseVerticalStrips ? BoundingBox.Height : StripThickness;

                StripInfos[index] = new StripInfoNode
                {
                    Index = index,
                    StreamId = streamId,
                    LengthInBytes = length,
                    Rect = new Rect(origin.X, origin.Y, width, height)
                };
                break;
            }

            case 0x0779: // kActorHeaderDiskImageUnkRect
                chunk.ReadTypedRect();
                break;

            default:
                base.ReadParameter(chunk, paramType);
                break;
        }
    }

    public override void ReadChunk(Chunk chunk)
    {
        uint index = chunk.ReadTypedUint16();
        var imageInfo = new ImageInfo(chunk);
        var stripImage = new PixMapImage(chunk, imageInfo, decompressImmediately: true);
        StripImages[index] = stripImage;

        if (StripInfos.TryGetValue(index, out var info))
        {
            info.Rect = new Rect(info.Rect.X, info.Rect.Y, imageInfo.Width, imageInfo.Height);
        }
    }

    public PixMapImage? CompositeImage()
    {
        if (StripImages.Count == 0)
            return null;

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        foreach (var (index, strip) in StripImages)
        {
            int x = UseVerticalStrips ? (StripThickness * (int)index) : 0;
            int y = UseVerticalStrips ? 0 : (StripThickness * (int)index);
            if (StripInfos.TryGetValue(index, out var info))
            {
                x = info.Rect.X;
                y = info.Rect.Y;
            }

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + strip.Width);
            maxY = Math.Max(maxY, y + strip.Height);
        }

        if (BoundingBox.Width > 0 && BoundingBox.Height > 0)
        {
            minX = Math.Min(minX, BoundingBox.Left);
            minY = Math.Min(minY, BoundingBox.Top);
            maxX = Math.Max(maxX, BoundingBox.Right);
            maxY = Math.Max(maxY, BoundingBox.Bottom);
        }

        int totalWidth = Math.Max(1, maxX - minX);
        int totalHeight = Math.Max(1, maxY - minY);

        byte[] canvas = new byte[totalWidth * totalHeight];

        foreach (var (index, strip) in StripImages)
        {
            int originX = UseVerticalStrips ? (StripThickness * (int)index) : 0;
            int originY = UseVerticalStrips ? 0 : (StripThickness * (int)index);
            if (StripInfos.TryGetValue(index, out var info))
            {
                originX = info.Rect.X;
                originY = info.Rect.Y;
            }

            int destOffsetX = originX - minX;
            int destOffsetY = originY - minY;

            for (int row = 0; row < strip.Height; row++)
            {
                int destY = destOffsetY + row;
                if (destY < 0 || destY >= totalHeight)
                    continue;

                int srcOffset = row * strip.Stride;
                int destOffset = destY * totalWidth + destOffsetX;
                int copyWidth = Math.Min(strip.Width, totalWidth - destOffsetX);
                if (copyWidth > 0 && destOffsetX >= 0)
                {
                    Array.Copy(strip.Pixels, srcOffset, canvas, destOffset, copyWidth);
                }
            }
        }

        var compositedInfo = new ImageInfo(totalWidth, totalHeight, BitmapCompressionType.UncompressedBitmap, (short)totalWidth);
        return new PixMapImage(compositedInfo, canvas);
    }
}
