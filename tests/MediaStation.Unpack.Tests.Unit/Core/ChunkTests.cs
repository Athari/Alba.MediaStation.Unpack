using System.Buffers.Binary;
using System.Text;
using MediaStation.Unpack.Core;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Core;

public class ChunkTests
{
    [Fact]
    public void Chunk_ParsesHeaderAndRemainingCorrectly()
    {
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            Span<byte> tag = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(tag, Chunk.MakeTag('a', '0', '0', '1'));
            writer.Write(tag);
            writer.Write((uint)4); // length
            writer.Write(new byte[] { 1, 2, 3, 4 });
        }

        ms.Seek(0, SeekOrigin.Begin);
        var chunk = new Chunk(ms);

        Assert.Equal(Chunk.MakeTag('a', '0', '0', '1'), chunk.Id);
        Assert.Equal("a001", chunk.TagString);
        Assert.Equal(4u, chunk.Length);
        Assert.Equal(4, chunk.ChunkBytesRemaining);

        byte[] data = chunk.ReadBytes(4);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, data);
        Assert.Equal(0, chunk.ChunkBytesRemaining);
    }
}
