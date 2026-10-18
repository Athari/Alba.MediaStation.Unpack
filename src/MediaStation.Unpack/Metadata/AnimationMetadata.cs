using MediaStation.Unpack.Core;

namespace MediaStation.Unpack.Metadata;

public enum MovieBlitType : ushort
{
    Invalid = 0,
    Uncompressed = 1,
    UncompressedDelta = 2,
    CompressedDelta = 3,
}

public class SpriteMovieClip
{
    public uint Id { get; set; }
    public int FirstFrameIndex { get; set; }
    public int LastFrameIndex { get; set; }
    public string Name { get; set; } = string.Empty;

    public SpriteMovieClip(uint id, int first, int last, string name = "")
    {
        Id = id;
        FirstFrameIndex = first;
        LastFrameIndex = last;
        Name = name;
    }
}

public class MovieFrame
{
    public uint LayerId { get; set; }
    public MovieBlitType BlitType { get; set; }
    public uint StartInMilliseconds { get; set; }
    public uint EndInMilliseconds { get; set; }
    public Point LeftTop { get; set; }
    public Point DiffBetweenKeyframeAndFrame { get; set; }
    public short ZIndex { get; set; }
    public uint KeyframeIndex { get; set; }
    public bool KeepAfterEnd { get; set; }
    public uint Index { get; set; }

    public MovieFrame(Chunk chunk, bool isFirstGeneration = false)
    {
        if (isFirstGeneration)
        {
            BlitType = (MovieBlitType)chunk.ReadTypedUint16();
            StartInMilliseconds = chunk.ReadTypedUint32();
            EndInMilliseconds = chunk.ReadTypedUint32();
            short x = (short)chunk.ReadTypedUint16();
            short y = (short)chunk.ReadTypedUint16();
            LeftTop = new Point(x, y);
            Index = chunk.ReadTypedUint32();
            KeyframeIndex = chunk.ReadTypedUint32();
            KeepAfterEnd = chunk.ReadTypedByte() != 0;
        }
        else
        {
            LayerId = chunk.ReadTypedUint32();
            BlitType = (MovieBlitType)chunk.ReadTypedUint16();
            StartInMilliseconds = chunk.ReadTypedUint32();
            EndInMilliseconds = chunk.ReadTypedUint32();
            short x = (short)chunk.ReadTypedUint16();
            short y = (short)chunk.ReadTypedUint16();
            LeftTop = new Point(x, y);
            ZIndex = chunk.ReadTypedSint16();
            short diffX = chunk.ReadTypedSint16();
            short diffY = chunk.ReadTypedSint16();
            DiffBetweenKeyframeAndFrame = new Point(diffX, diffY);
            Index = chunk.ReadTypedUint32();
            KeyframeIndex = chunk.ReadTypedUint32();
            KeepAfterEnd = chunk.ReadTypedByte() != 0;
        }
    }
}
