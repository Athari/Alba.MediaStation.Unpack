using System.Buffers.Binary;
using System.Text;
using MediaStation.Unpack.Actors;
using MediaStation.Unpack.Core;
using MediaStation.Unpack.Metadata;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Actors;

public class StreamMovieActorTests
{
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
