using System.Buffers.Binary;
using System.Text;
using MediaStation.Unpack.Core;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Core;

public class ParameterBinaryReaderTests
{
    [Fact]
    public void ParameterBinaryReader_ReadsTypedDatumsCorrectly()
    {
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            // Uint8
            writer.Write((ushort)DatumType.Uint8);
            writer.Write((byte)42);

            // Uint16
            writer.Write((ushort)DatumType.Uint16);
            writer.Write((ushort)12345);

            // Uint32
            writer.Write((ushort)DatumType.Uint32);
            writer.Write((uint)0xDEADBEEF);

            // Point (GraphicUnit, GraphicUnit)
            writer.Write((ushort)DatumType.Point);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)100);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)200);

            // Rect
            writer.Write((ushort)DatumType.Rect);
            writer.Write((ushort)DatumType.Point);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)10);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)20);
            writer.Write((ushort)DatumType.GraphicSize);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)300);
            writer.Write((ushort)DatumType.GraphicUnit);
            writer.Write((short)400);

            // String
            writer.Write((ushort)DatumType.String);
            writer.Write((ushort)DatumType.Uint32);
            string testStr = "Hello MediaStation";
            writer.Write((uint)(testStr.Length + 1));
            writer.Write(Encoding.ASCII.GetBytes(testStr));
            writer.Write((byte)0);

            // ChannelIdent (always Big-Endian)
            writer.Write((ushort)DatumType.ChannelIdent);
            Span<byte> ch = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(ch, Chunk.MakeTag('i', 'g', 'o', 'd'));
            writer.Write(ch);
        }

        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new ParameterBinaryReader(ms);

        Assert.Equal(42, reader.ReadTypedByte());
        Assert.Equal(12345, reader.ReadTypedUint16());
        Assert.Equal(0xDEADBEEF, reader.ReadTypedUint32());

        var pt = reader.ReadTypedPoint();
        Assert.Equal(100, pt.X);
        Assert.Equal(200, pt.Y);

        var rc = reader.ReadTypedRect();
        Assert.Equal(10, rc.X);
        Assert.Equal(20, rc.Y);
        Assert.Equal(300, rc.Width);
        Assert.Equal(400, rc.Height);

        Assert.Equal("Hello MediaStation", reader.ReadTypedString());
        Assert.Equal(Chunk.MakeTag('i', 'g', 'o', 'd'), reader.ReadTypedChannelIdent());
    }
}
