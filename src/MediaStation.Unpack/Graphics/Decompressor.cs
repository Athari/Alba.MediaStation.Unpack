using System.Buffers.Binary;
using MediaStation.Unpack.Core;

namespace MediaStation.Unpack.Graphics;

public static class Decompressor
{
    private const int CccBlockDimension = 4;

    public static byte[] DecompressRle8(
        ReadOnlySpan<byte> compressedData,
        int width,
        int height,
        byte[]? keyFramePixels = null,
        int keyFrameWidth = 0,
        int keyFrameHeight = 0,
        Point? keyFrameOffset = null)
    {
        byte[] dest = new byte[width * height];
        int destSize = dest.Length;
        int readOffset = 0;
        int streamLength = compressedData.Length;

        int currentX = 0;
        int currentY = 0;
        bool imageFullyRead = false;

        while (currentY < height && readOffset < streamLength)
        {
            currentX = 0;
            while (readOffset < streamLength)
            {
                if (currentY >= height)
                    break;

                byte operation = compressedData[readOffset++];
                if (operation == 0x00)
                {
                    if (readOffset >= streamLength)
                    {
                        imageFullyRead = true;
                        break;
                    }

                    operation = compressedData[readOffset++];
                    if (operation == 0x00)
                    {
                        // End of line
                        if (readOffset >= streamLength)
                            imageFullyRead = true;
                        break;
                    }
                    else if (operation == 0x01)
                    {
                        // End of image
                        imageFullyRead = true;
                        break;
                    }
                    else if (operation == 0x02)
                    {
                        // Copy from keyframe region
                        if (readOffset + 2 > streamLength)
                            break;

                        byte xToCopy = compressedData[readOffset++];
                        byte yToCopy = compressedData[readOffset++];

                        if (keyFramePixels != null && keyFrameOffset.HasValue)
                        {
                            Point kfOffset = keyFrameOffset.Value;

                            // If multiple lines requested, copy preceding lines
                            for (int lineOffset = 0; lineOffset < yToCopy; lineOffset++)
                            {
                                int kfX = currentX - kfOffset.X;
                                int kfY = currentY - kfOffset.Y + lineOffset;
                                int dstX = currentX;
                                int dstY = currentY + lineOffset;

                                if (kfX >= 0 && kfX + xToCopy <= keyFrameWidth &&
                                    kfY >= 0 && kfY < keyFrameHeight &&
                                    dstY * width + dstX + xToCopy <= destSize)
                                {
                                    int srcIndex = kfY * keyFrameWidth + kfX;
                                    int dstIndex = dstY * width + dstX;
                                    Array.Copy(keyFramePixels, srcIndex, dest, dstIndex, xToCopy);
                                }
                            }

                            // Same line copy
                            int kfXSame = currentX - kfOffset.X;
                            int kfYSame = currentY - kfOffset.Y;
                            if (kfXSame >= 0 && kfXSame + xToCopy <= keyFrameWidth &&
                                kfYSame >= 0 && kfYSame < keyFrameHeight &&
                                currentY * width + currentX + xToCopy <= destSize)
                            {
                                int srcIndex = kfYSame * keyFrameWidth + kfXSame;
                                int dstIndex = currentY * width + currentX;
                                Array.Copy(keyFramePixels, srcIndex, dest, dstIndex, xToCopy);
                            }
                        }

                        currentX += xToCopy;
                        currentY += yToCopy;
                    }
                    else if (operation == 0x03)
                    {
                        // Adjust pixel position
                        if (readOffset + 2 <= streamLength)
                        {
                            currentX += compressedData[readOffset++];
                            currentY += compressedData[readOffset++];
                        }
                    }
                    else if (operation >= 0x04)
                    {
                        // Literal uncompressed run
                        byte runLength = operation;
                        int maxRun = destSize - (currentY * width + currentX);
                        if (runLength > maxRun)
                            runLength = (byte)Math.Max(0, maxRun);

                        int dstIndex = currentY * width + currentX;
                        int toRead = Math.Min((int)runLength, streamLength - readOffset);
                        compressedData.Slice(readOffset, toRead).CopyTo(dest.AsSpan(dstIndex, toRead));
                        readOffset += toRead;

                        // Align to 2-byte boundary
                        if (readOffset % 2 == 1 && readOffset < streamLength)
                            readOffset++;

                        currentX += runLength;
                    }
                }
                else
                {
                    // Repeat run
                    if (readOffset >= streamLength)
                        break;

                    byte colorToRepeat = compressedData[readOffset++];
                    byte count = operation;
                    int maxCount = destSize - (currentY * width + currentX);
                    if (count > maxCount)
                        count = (byte)Math.Max(0, maxCount);

                    int dstIndex = currentY * width + currentX;
                    dest.AsSpan(dstIndex, count).Fill(colorToRepeat);
                    currentX += count;
                }
            }

            currentY++;
            if (imageFullyRead)
                break;
        }

        return dest;
    }

    public static byte[] DecompressCcc(ReadOnlySpan<byte> compressedData, int width, int height)
    {
        byte[] dest = new byte[width * height];
        int readOffset = 0;
        int streamLength = compressedData.Length;

        for (int blockY = 0; blockY < height; blockY += CccBlockDimension)
        {
            for (int blockX = 0; blockX < width; blockX += CccBlockDimension)
            {
                if (readOffset + 4 > streamLength)
                    return dest;

                byte color1 = compressedData[readOffset++];
                byte color2 = compressedData[readOffset++];
                ushort bitmask = BinaryPrimitives.ReadUInt16BigEndian(compressedData.Slice(readOffset, 2));
                readOffset += 2;

                int blockHeight = Math.Min(height - blockY, CccBlockDimension);
                int blockWidth = Math.Min(width - blockX, CccBlockDimension);

                DecompressCccBlock(dest, width, blockX, blockY, color1, color2, bitmask, blockWidth, blockHeight, null);
            }
        }

        return dest;
    }

    public static byte[] DecompressCccTransparent(ReadOnlySpan<byte> compressedData, int width, int height)
    {
        byte[] dest = new byte[width * height];
        int readOffset = 0;
        int streamLength = compressedData.Length;

        byte transparencyColor = 0;
        Point blockPos = new Point(0, 0);

        while (readOffset + 4 <= streamLength)
        {
            uint command = BinaryPrimitives.ReadUInt32BigEndian(compressedData.Slice(readOffset, 4));
            readOffset += 4;

            if (command == 0xFF000000)
                break;

            if ((command & 0xFF000000) == 0xFE000000)
            {
                transparencyColor = (byte)((command >> 16) & 0xFF);
                continue;
            }

            ushort blockCount = (ushort)((command >> 16) & 0xFFFF);
            byte yDelta = (byte)((command >> 8) & 0xFF);
            byte xDelta = (byte)(command & 0xFF);

            if (yDelta == 0)
            {
                blockPos = new Point(blockPos.X + xDelta * CccBlockDimension, blockPos.Y);
            }
            else
            {
                blockPos = new Point(xDelta * CccBlockDimension, blockPos.Y + yDelta * CccBlockDimension);
            }

            for (int i = 0; i < blockCount; i++)
            {
                if (readOffset + 4 > streamLength)
                    return dest;

                byte color1 = compressedData[readOffset++];
                byte color2 = compressedData[readOffset++];
                ushort bitmask = BinaryPrimitives.ReadUInt16BigEndian(compressedData.Slice(readOffset, 2));
                readOffset += 2;

                int blockHeight = Math.Min(height - blockPos.Y, CccBlockDimension);
                int blockWidth = Math.Min(width - blockPos.X, CccBlockDimension);

                if (blockWidth > 0 && blockHeight > 0)
                {
                    DecompressCccBlock(dest, width, blockPos.X, blockPos.Y, color1, color2, bitmask, blockWidth, blockHeight, transparencyColor);
                }

                blockPos = new Point(blockPos.X + CccBlockDimension, blockPos.Y);
            }
        }

        return dest;
    }

    private static void DecompressCccBlock(
        byte[] dest,
        int destStride,
        int blockX,
        int blockY,
        byte color1,
        byte color2,
        ushort bitMask,
        int blockWidth,
        int blockHeight,
        byte? transparencyColor)
    {
        bool backgroundIsTransparent = transparencyColor.HasValue && color1 == transparencyColor.Value;

        for (int y = 0; y < blockHeight; y++)
        {
            int rowOffset = (blockY + y) * destStride + blockX;

            for (int x = 0; x < blockWidth; x++)
            {
                if ((bitMask & 0x8000) != 0)
                {
                    dest[rowOffset + x] = color2;
                }
                else if (!backgroundIsTransparent)
                {
                    dest[rowOffset + x] = color1;
                }

                bitMask <<= 1;
            }

            int unusedBits = 4 - blockWidth;
            bitMask <<= unusedBits;
        }
    }
}
