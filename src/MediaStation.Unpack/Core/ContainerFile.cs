namespace MediaStation.Unpack.Core;

public class ContainerFile : IDisposable
{
    private readonly FileStream _stream;
    public string FilePath { get; }

    public ContainerFile(string filePath)
    {
        FilePath = filePath;
        _stream = File.OpenRead(filePath);
    }

    public Subfile OpenSubfileAt(long offset)
    {
        _stream.Seek(offset, SeekOrigin.Begin);
        return new Subfile(_stream);
    }

    public IEnumerable<(long Offset, Subfile Subfile)> EnumerateSubfiles()
    {
        _stream.Seek(0, SeekOrigin.Begin);

        // Check if there is a 16-byte CXT header
        if (_stream.Length >= 16)
        {
            Span<byte> magic = stackalloc byte[4];
            _stream.ReadExactly(magic);
            if (magic[0] == 'I' && magic[1] == 'I' && magic[2] == 0 && magic[3] == 0)
            {
                // CXT header, skip to 0x10
                _stream.Seek(16, SeekOrigin.Begin);
            }
            else
            {
                _stream.Seek(0, SeekOrigin.Begin);
            }
        }

        while (_stream.Position + 8 < _stream.Length)
        {
            if (_stream.Position % 2 != 0)
                _stream.Seek(1, SeekOrigin.Current);

            long pos = _stream.Position;
            if (pos + 8 >= _stream.Length)
                break;

            // Peek at RIFF header
            Span<byte> tagBuf = stackalloc byte[4];
            int read = _stream.Read(tagBuf);
            if (read < 4)
                break;

            uint tag = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(tagBuf);
            if (tag != Chunk.MakeTag('R', 'I', 'F', 'F'))
            {
                // Seek to next even byte
                _stream.Seek(pos + 2, SeekOrigin.Begin);
                continue;
            }

            _stream.Seek(pos, SeekOrigin.Begin);
            Subfile subfile;
            try
            {
                subfile = new Subfile(_stream);
            }
            catch
            {
                _stream.Seek(pos + 2, SeekOrigin.Begin);
                continue;
            }

            yield return (pos, subfile);
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
