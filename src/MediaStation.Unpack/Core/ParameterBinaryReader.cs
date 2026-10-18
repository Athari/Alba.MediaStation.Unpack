using System.Buffers.Binary;
using System.Text;

namespace MediaStation.Unpack.Core;

public readonly record struct Point(int X, int Y);

public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public readonly record struct VersionInfo(ushort Major, ushort Minor, ushort Patch);

public class ParameterBinaryReader : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public ParameterBinaryReader(Stream stream, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _leaveOpen = leaveOpen;
    }

    public Stream BaseStream => _stream;
    public long Position => _stream.Position;
    public long Length => _stream.Length;
    public long BytesRemaining => _stream.Length - _stream.Position;
    public bool EndOfStream => _stream.Position >= _stream.Length;

    public void Seek(long offset, SeekOrigin origin = SeekOrigin.Begin)
    {
        _stream.Seek(offset, origin);
    }

    public void Skip(long count)
    {
        _stream.Seek(count, SeekOrigin.Current);
    }

    public byte ReadByte()
    {
        int b = _stream.ReadByte();
        if (b < 0)
            throw new EndOfStreamException();
        return (byte)b;
    }

    public sbyte ReadSByte() => (sbyte)ReadByte();

    public byte[] ReadBytes(int count)
    {
        byte[] buffer = new byte[count];
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = _stream.Read(buffer, totalRead, count - totalRead);
            if (read == 0)
                throw new EndOfStreamException($"Reached end of stream while reading {count} bytes (read {totalRead}).");
            totalRead += read;
        }
        return buffer;
    }

    public ushort ReadUint16LE()
    {
        Span<byte> buf = stackalloc byte[2];
        ReadExact(buf);
        return BinaryPrimitives.ReadUInt16LittleEndian(buf);
    }

    public short ReadInt16LE()
    {
        Span<byte> buf = stackalloc byte[2];
        ReadExact(buf);
        return BinaryPrimitives.ReadInt16LittleEndian(buf);
    }

    public uint ReadUint32LE()
    {
        Span<byte> buf = stackalloc byte[4];
        ReadExact(buf);
        return BinaryPrimitives.ReadUInt32LittleEndian(buf);
    }

    public int ReadInt32LE()
    {
        Span<byte> buf = stackalloc byte[4];
        ReadExact(buf);
        return BinaryPrimitives.ReadInt32LittleEndian(buf);
    }

    public uint ReadUint32BE()
    {
        Span<byte> buf = stackalloc byte[4];
        ReadExact(buf);
        return BinaryPrimitives.ReadUInt32BigEndian(buf);
    }

    public ushort ReadUint16BE()
    {
        Span<byte> buf = stackalloc byte[2];
        ReadExact(buf);
        return BinaryPrimitives.ReadUInt16BigEndian(buf);
    }

    public float ReadFloatLE()
    {
        Span<byte> buf = stackalloc byte[4];
        ReadExact(buf);
        return BinaryPrimitives.ReadSingleLittleEndian(buf);
    }

    public double ReadDoubleLE()
    {
        Span<byte> buf = stackalloc byte[8];
        ReadExact(buf);
        return BinaryPrimitives.ReadDoubleLittleEndian(buf);
    }

    private void ReadExact(Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = _stream.Read(buffer[total..]);
            if (read == 0)
                throw new EndOfStreamException();
            total += read;
        }
    }

    public void ReadAndVerifyType(DatumType expectedType)
    {
        var actualType = (DatumType)ReadUint16LE();
        if (actualType != expectedType)
        {
            throw new InvalidDataException(
                $"Expected datum type {expectedType} (0x{(ushort)expectedType:X4}), got {actualType} (0x{(ushort)actualType:X4}) at stream position 0x{Position:X}");
        }
    }

    public byte ReadTypedByte()
    {
        ReadAndVerifyType(DatumType.Uint8);
        return ReadByte();
    }

    public sbyte ReadTypedSByte()
    {
        ReadAndVerifyType(DatumType.Int8);
        return ReadSByte();
    }

    public ushort ReadTypedUint16()
    {
        ReadAndVerifyType(DatumType.Uint16);
        return ReadUint16LE();
    }

    public short ReadTypedSint16()
    {
        ReadAndVerifyType(DatumType.Int16);
        return ReadInt16LE();
    }

    public uint ReadTypedUint32()
    {
        ReadAndVerifyType(DatumType.Uint32);
        return ReadUint32LE();
    }

    public int ReadTypedSint32()
    {
        ReadAndVerifyType(DatumType.Int32);
        return ReadInt32LE();
    }

    public float ReadTypedFloat()
    {
        ReadAndVerifyType(DatumType.Float);
        return ReadFloatLE();
    }

    public double ReadTypedDouble()
    {
        ReadAndVerifyType(DatumType.Double);
        return ReadDoubleLE();
    }

    public double ReadTypedTime()
    {
        ReadAndVerifyType(DatumType.Time);
        return ReadDoubleLE();
    }

    public short ReadTypedGraphicUnit()
    {
        ReadAndVerifyType(DatumType.GraphicUnit);
        return ReadInt16LE();
    }

    public Point ReadTypedPoint()
    {
        ReadAndVerifyType(DatumType.Point);
        short x = ReadTypedGraphicUnit();
        short y = ReadTypedGraphicUnit();
        return new Point(x, y);
    }

    public Point ReadTypedGraphicSize()
    {
        ReadAndVerifyType(DatumType.GraphicSize);
        short w = ReadTypedGraphicUnit();
        short h = ReadTypedGraphicUnit();
        return new Point(w, h);
    }

    public Rect ReadTypedRect()
    {
        ReadAndVerifyType(DatumType.Rect);
        Point origin = ReadTypedPoint();
        Point size = ReadTypedGraphicSize();
        return new Rect(origin.X, origin.Y, size.X, size.Y);
    }

    public string ReadTypedFilename()
    {
        ReadAndVerifyType(DatumType.Filename);
        uint size = ReadTypedUint32();
        byte[] bytes = ReadBytes((int)size);
        int nullIndex = Array.IndexOf(bytes, (byte)0);
        int length = nullIndex >= 0 ? nullIndex : bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, length);
    }

    public string ReadTypedString()
    {
        ReadAndVerifyType(DatumType.String);
        uint size = ReadTypedUint32();
        byte[] bytes = ReadBytes((int)size);
        int nullIndex = Array.IndexOf(bytes, (byte)0);
        int length = nullIndex >= 0 ? nullIndex : bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, length);
    }

    public VersionInfo ReadTypedVersion()
    {
        ReadAndVerifyType(DatumType.Version);
        ushort major = ReadTypedUint16();
        ushort minor = ReadTypedUint16();
        ushort patch = ReadTypedUint16();
        return new VersionInfo(major, minor, patch);
    }

    public uint ReadTypedChannelIdent()
    {
        ReadAndVerifyType(DatumType.ChannelIdent);
        return ReadUint32BE();
    }

    public void SkipScriptValue()
    {
        byte type = ReadTypedByte();
        switch (type)
        {
            case 0: // Empty
                break;
            case 1: // Float
                ReadTypedDouble();
                break;
            case 2: // Bool
                ReadTypedByte();
                break;
            case 3: // Time
                ReadTypedTime();
                break;
            case 4: // ParamToken
            case 5: // ActorId
                ReadTypedUint16();
                break;
            case 6: // String
                uint size = ReadTypedUint16();
                ReadBytes((int)size);
                break;
            case 7: // Collection
                uint totalItems = ReadTypedUint16();
                for (uint i = 0; i < totalItems; i++)
                    SkipScriptValue();
                break;
            case 8: // FunctionId
            case 9: // MethodId
                ReadTypedUint16();
                break;
            default:
                break;
        }
    }

    public short ReadTypedGraphicUnitOrUint16()
    {
        var type = (DatumType)ReadUint16LE();
        if (type == DatumType.GraphicUnit || type == DatumType.Int16 || type == DatumType.Uint16)
        {
            return ReadInt16LE();
        }
        throw new InvalidDataException($"Expected GraphicUnit or Uint16, got {type} at stream position 0x{Position:X}");
    }

    public uint ReadTypedByteOrUint16()
    {
        var type = (DatumType)ReadUint16LE();
        if (type == DatumType.Uint8 || type == DatumType.Int8)
            return ReadByte();
        if (type == DatumType.Uint16 || type == DatumType.Int16)
            return ReadUint16LE();
        throw new InvalidDataException($"Expected Uint8 or Uint16, got {type} at stream position 0x{Position:X}");
    }

    public void SkipTypedDatum()
    {
        var type = (DatumType)ReadUint16LE();
        SkipDatumOfType(type);
    }

    public void SkipDatumOfType(DatumType type)
    {
        switch (type)
        {
            case DatumType.Empty:
                break;
            case DatumType.Uint8:
            case DatumType.Int8:
                ReadByte();
                break;
            case DatumType.Uint16:
            case DatumType.Int16:
            case DatumType.GraphicUnit:
                ReadUint16LE();
                break;
            case DatumType.Uint32:
            case DatumType.Int32:
            case DatumType.Float:
                ReadUint32LE();
                break;
            case DatumType.Double:
            case DatumType.Time:
                ReadDoubleLE();
                break;
            case DatumType.Filename:
            case DatumType.String:
            {
                uint size = ReadTypedUint32();
                Skip(size);
                break;
            }
            case DatumType.Point:
            case DatumType.GraphicSize:
            {
                ReadTypedGraphicUnit();
                ReadTypedGraphicUnit();
                break;
            }
            case DatumType.Rect:
            {
                ReadTypedPoint();
                ReadTypedGraphicSize();
                break;
            }
            case DatumType.Version:
            {
                ReadTypedUint16();
                ReadTypedUint16();
                ReadTypedUint16();
                break;
            }
            case DatumType.ChannelIdent:
                ReadUint32BE();
                break;
            case DatumType.Polygon:
            {
                ushort totalPoints = ReadTypedUint16();
                for (int i = 0; i < totalPoints; i++)
                    ReadTypedPoint();
                break;
            }
            default:
                throw new InvalidDataException($"Unknown datum type 0x{(ushort)type:X4} at stream position 0x{Position:X}");
        }
    }

    public void Dispose()
    {
        if (!_leaveOpen)
            _stream.Dispose();
    }
}
