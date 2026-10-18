using System.Text;

namespace MediaStation.Unpack.Core;

public class Chunk : ParameterBinaryReader
{
    public uint Id { get; }
    public new uint Length { get; }
    public long DataStartOffset { get; }
    public long DataEndOffset { get; }

    public Chunk(Stream stream, bool leaveOpen = true)
        : base(stream, leaveOpen)
    {
        Id = ReadUint32BE();
        Length = ReadUint32LE();
        DataStartOffset = stream.Position;
        DataEndOffset = DataStartOffset + Length;
    }

    public long ChunkBytesRemaining => Math.Max(0, DataEndOffset - Position);

    public string TagString => FormatTag(Id);

    public static string FormatTag(uint tag)
    {
        byte[] bytes = new byte[4];
        bytes[0] = (byte)(tag >> 24);
        bytes[1] = (byte)(tag >> 16);
        bytes[2] = (byte)(tag >> 8);
        bytes[3] = (byte)tag;

        bool isPrintable = true;
        for (int i = 0; i < 4; i++)
        {
            if (bytes[i] < 32 || bytes[i] > 126)
            {
                isPrintable = false;
                break;
            }
        }

        return isPrintable ? Encoding.ASCII.GetString(bytes) : $"0x{tag:X8}";
    }

    public static uint MakeTag(char c1, char c2, char c3, char c4)
    {
        return ((uint)(byte)c1 << 24) |
               ((uint)(byte)c2 << 16) |
               ((uint)(byte)c3 << 8) |
               (uint)(byte)c4;
    }

    public MemoryStream ReadRemainingToMemoryStream()
    {
        int remaining = (int)ChunkBytesRemaining;
        byte[] buffer = ReadBytes(remaining);
        return new MemoryStream(buffer, writable: false);
    }
}
