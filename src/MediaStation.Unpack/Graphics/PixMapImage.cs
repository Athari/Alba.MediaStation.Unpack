using MediaStation.Unpack.Core;

namespace MediaStation.Unpack.Graphics;

public class PixMapImage
{
    public ImageInfo Info { get; }
    public byte[]? CompressedBytes { get; }
    public byte[] Pixels { get; private set; }
    public byte? TransparentColorIndex { get; set; }

    public int Width => Info.Width;
    public int Height => Info.Height;
    public int Stride => Width;
    public BitmapCompressionType CompressionType => Info.CompressionType;

    public bool IsCompressed =>
        CompressionType != BitmapCompressionType.UncompressedBitmap &&
        CompressionType != BitmapCompressionType.UncompressedTransparentBitmap;

    public PixMapImage(Chunk chunk, ImageInfo info, bool decompressImmediately = true)
    {
        Info = info;
        Pixels = new byte[Width * Height];

        if (info.CompressionType == BitmapCompressionType.UncompressedTransparentBitmap)
            TransparentColorIndex = 0;

        long targetPos = chunk.DataStartOffset + info.ImageDataStartOffset;
        if (targetPos < chunk.DataEndOffset)
        {
            chunk.Seek(targetPos);
            int bytesRemaining = (int)chunk.ChunkBytesRemaining;

            if (bytesRemaining > 0)
            {
                if (IsCompressed)
                {
                    CompressedBytes = chunk.ReadBytes(bytesRemaining);
                    if (decompressImmediately)
                    {
                        Decompress();
                    }
                }
                else
                {
                    int rowStride = Info.Stride > Width ? Info.Stride : Width;
                    for (int y = 0; y < Height; y++)
                    {
                        int toRead = Math.Min(rowStride, (int)chunk.ChunkBytesRemaining);
                        if (toRead <= 0) break;
                        byte[] rowBytes = chunk.ReadBytes(toRead);
                        int copyLen = Math.Min(Width, rowBytes.Length);
                        Array.Copy(rowBytes, 0, Pixels, y * Width, copyLen);
                    }
                }
            }
        }
    }

    public PixMapImage(ImageInfo info, byte[] pixels, byte? transparentColorIndex = null)
    {
        Info = info;
        Pixels = pixels;
        TransparentColorIndex = transparentColorIndex;
    }

    public void Decompress(byte[]? keyFramePixels = null, int keyFrameWidth = 0, int keyFrameHeight = 0, Point? keyFrameOffset = null)
    {
        if (CompressedBytes == null || CompressedBytes.Length == 0)
            return;

        switch (CompressionType)
        {
            case BitmapCompressionType.Rle8BitmapCompression:
                Pixels = Decompressor.DecompressRle8(CompressedBytes, Width, Height, keyFramePixels, keyFrameWidth, keyFrameHeight, keyFrameOffset);
                TransparentColorIndex = 0;
                break;

            case BitmapCompressionType.CccBitmapCompression:
                Pixels = Decompressor.DecompressCcc(CompressedBytes, Width, Height);
                break;

            case BitmapCompressionType.CccTransparentBitmapCompression:
                Pixels = Decompressor.DecompressCccTransparent(CompressedBytes, Width, Height);
                TransparentColorIndex = 0;
                break;
        }

        Info.CompressionType = BitmapCompressionType.UncompressedBitmap;
    }
}
