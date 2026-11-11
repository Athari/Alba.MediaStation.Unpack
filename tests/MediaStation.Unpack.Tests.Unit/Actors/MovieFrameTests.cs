using System.Buffers.Binary;
using System.Text;
using MediaStation.Unpack.Actors;
using MediaStation.Unpack.Core;
using MediaStation.Unpack.Metadata;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Actors;

public class MovieFrameTests
{
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
}
