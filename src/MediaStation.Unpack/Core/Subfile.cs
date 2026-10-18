namespace MediaStation.Unpack.Core;

public class Subfile
{
    private static readonly uint TagRiff = Chunk.MakeTag('R', 'I', 'F', 'F');
    private static readonly uint TagRate = Chunk.MakeTag('r', 'a', 't', 'e');
    private static readonly uint TagList = Chunk.MakeTag('L', 'I', 'S', 'T');
    private static readonly uint TagData = Chunk.MakeTag('d', 'a', 't', 'a');

    private readonly Stream _stream;
    private readonly Chunk _rootChunk;
    public uint Rate { get; }

    public Subfile(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));

        // Align if odd
        if (_stream.Position % 2 != 0)
            _stream.Seek(1, SeekOrigin.Current);

        _rootChunk = new Chunk(_stream);
        if (_rootChunk.Id != TagRiff)
            throw new InvalidDataException($"Expected RIFF chunk at 0x{_rootChunk.DataStartOffset:X}, got {_rootChunk.TagString}");

        Span<byte> imts = stackalloc byte[4];
        _stream.ReadExactly(imts); // "IMTS"

        // Rate chunk
        var rateChunk = new Chunk(_stream);
        if (rateChunk.Id != TagRate)
            throw new InvalidDataException($"Expected 'rate' chunk, got {rateChunk.TagString}");

        Span<byte> rateBytes = stackalloc byte[4];
        _stream.ReadExactly(rateBytes);
        Rate = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(rateBytes);

        // LIST data chunk
        var listChunk = new Chunk(_stream);
        if (listChunk.Id != TagList)
            throw new InvalidDataException($"Expected 'LIST' chunk, got {listChunk.TagString}");

        Span<byte> dataTag = stackalloc byte[4];
        _stream.ReadExactly(dataTag);
        uint listType = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(dataTag);
        if (listType != TagData)
            throw new InvalidDataException($"Expected 'data' LIST type, got {Chunk.FormatTag(listType)}");

        _dataEndOffset = listChunk.DataEndOffset;
    }

    private Chunk? _currentChunk;
    private readonly long _dataEndOffset;

    public Chunk NextChunk()
    {
        if (_currentChunk != null)
        {
            if (_stream.Position < _currentChunk.DataEndOffset)
            {
                _stream.Seek(_currentChunk.DataEndOffset, SeekOrigin.Begin);
            }
        }

        if (_stream.Position % 2 != 0)
            _stream.Seek(1, SeekOrigin.Current);

        _currentChunk = new Chunk(_stream);
        return _currentChunk;
    }

    public bool AtEnd
    {
        get
        {
            long pos = _currentChunk != null ? Math.Max(_stream.Position, _currentChunk.DataEndOffset) : _stream.Position;
            return (_dataEndOffset - pos) <= 1;
        }
    }
}
