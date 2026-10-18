using MediaStation.Unpack.Core;

namespace MediaStation.Unpack.Graphics;

public enum BitmapCompressionType : ushort
{
    UncompressedBitmap = 0,
    Rle8BitmapCompression = 1,
    CccBitmapCompression = 5,
    CccTransparentBitmapCompression = 6,
    UncompressedTransparentBitmap = 7,
}

public class ImageInfo
{
    public uint ImageDataStartOffset { get; }
    public Point Dimensions { get; }
    public BitmapCompressionType CompressionType { get; set; }
    public short Stride { get; }

    public int Width => Dimensions.X;
    public int Height => Dimensions.Y;

    public ImageInfo(Chunk chunk)
    {
        ImageDataStartOffset = chunk.ReadTypedUint16();
        Dimensions = chunk.ReadTypedGraphicSize();
        CompressionType = (BitmapCompressionType)chunk.ReadTypedUint16();
        Stride = (short)chunk.ReadTypedUint16();
    }

    public ImageInfo(int width, int height, BitmapCompressionType compressionType = BitmapCompressionType.UncompressedBitmap, short stride = 0)
    {
        Dimensions = new Point(width, height);
        CompressionType = compressionType;
        Stride = stride > 0 ? stride : (short)width;
    }
}
