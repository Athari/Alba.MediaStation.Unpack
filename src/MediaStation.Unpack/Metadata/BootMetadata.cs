using MediaStation.Unpack.Core;

namespace MediaStation.Unpack.Metadata;

public enum BootSectionType : ushort
{
    LastSection = 0x0000,
    ContextReference = 0x0002,
    VersionInformation = 0x0190,
    Unk1 = 0x0191,
    FunctionTableSize = 0x0192,
    Unk3 = 0x0193,
    ParamTokenDeclaration = 0x0BBA,
    ParamTokenValue = 0x0BBB,
    ScreenReference = 0x0007,
    FileInfo = 0x000A,
    StreamInfo = 0x000B,
    CursorManagerInit = 0x000C,
    CursorManagerNewCursor = 0x0015,
    CursorManagerDisposeCursor = 0x0016,
    DocumentStartupInformation = 0x002E,
    DeviceOwnerAllowMultipleSounds = 0x0035,
    DeviceOwnerAllowMultipleStreams = 0x0036,
    VideoDisplayManagerSetTime = 0x057B,
}

public class FileInfoRecord
{
    public ushort Id { get; }
    public ushort IntendedLocation { get; }
    public string Name { get; }

    public FileInfoRecord(ushort id, ushort intendedLocation, string name)
    {
        Id = id;
        IntendedLocation = intendedLocation;
        Name = name;
    }

    public FileInfoRecord(Chunk chunk)
    {
        // 0x002B: FileId
        ushort section = chunk.ReadTypedUint16();
        if (section != 0x002B)
            throw new InvalidDataException($"Expected FileInfo section 0x2B (FileId), got 0x{section:X}");
        Id = chunk.ReadTypedUint16();

        // 0x002D: IntendedLocation & Name
        section = chunk.ReadTypedUint16();
        if (section != 0x002D)
            throw new InvalidDataException($"Expected FileInfo section 0x2D (FileNameAndType), got 0x{section:X}");
        IntendedLocation = chunk.ReadTypedUint16();

        Name = chunk.ReadTypedFilename();
    }
}

public class StreamInfoRecord
{
    public ushort ActorId { get; }
    public ushort FileId { get; }
    public uint StartOffsetInFile { get; }

    public StreamInfoRecord(ushort actorId, ushort fileId, uint startOffsetInFile)
    {
        ActorId = actorId;
        FileId = fileId;
        StartOffsetInFile = startOffsetInFile;
    }

    public StreamInfoRecord(Chunk chunk)
    {
        // 0x002A: ActorId
        ushort section = chunk.ReadTypedUint16();
        if (section != 0x002A)
            throw new InvalidDataException($"Expected StreamInfo section 0x2A (ActorId), got 0x{section:X}");
        ActorId = chunk.ReadTypedUint16();

        // 0x002B: FileId
        section = chunk.ReadTypedUint16();
        if (section != 0x002B)
            throw new InvalidDataException($"Expected StreamInfo section 0x2B (FileId), got 0x{section:X}");
        FileId = chunk.ReadTypedUint16();

        // 0x002C: StartOffset
        section = chunk.ReadTypedUint16();
        if (section != 0x002C)
            throw new InvalidDataException($"Expected StreamInfo section 0x2C (StartOffset), got 0x{section:X}");
        StartOffsetInFile = chunk.ReadTypedUint32();
    }
}

public class ContextReferenceRecord
{
    public ushort ContextId { get; }
    public ushort StreamId { get; }
    public string? Name { get; }
    public List<ushort> ParentContextIds { get; } = new();

    public ContextReferenceRecord(Chunk chunk)
    {
        // Section: ContextId (0x0004)
        ushort section = chunk.ReadTypedUint16();
        if (section != 0x0004)
            throw new InvalidDataException($"Expected ContextReference section 4 (ContextId), got 0x{section:X}");
        ContextId = chunk.ReadTypedUint16();

        // Section: StreamId (0x0005)
        section = chunk.ReadTypedUint16();
        if (section != 0x0005)
            throw new InvalidDataException($"Expected ContextReference section 5 (StreamId), got 0x{section:X}");
        StreamId = chunk.ReadTypedUint16();

        // Optional Name (0x0BB8)
        long rewindOffset = chunk.Position;
        if (chunk.ChunkBytesRemaining >= 4)
        {
            section = chunk.ReadTypedUint16();
            if (section == 0x0BB8)
            {
                Name = chunk.ReadTypedString();
                rewindOffset = chunk.Position;
                if (chunk.ChunkBytesRemaining >= 4)
                    section = chunk.ReadTypedUint16();
                else
                    section = 0;
            }

            // ParentContextIds (0x0006)
            while (section == 0x0006)
            {
                ParentContextIds.Add(chunk.ReadTypedUint16());
                rewindOffset = chunk.Position;
                if (chunk.ChunkBytesRemaining >= 4)
                    section = chunk.ReadTypedUint16();
                else
                    break;
            }
        }
        chunk.Seek(rewindOffset);
    }
}

public class ScreenReferenceRecord
{
    public ushort ScreenActorId { get; }
    public ushort ContextId { get; }

    public ScreenReferenceRecord(Chunk chunk)
    {
        // 0x0009: ScreenId
        ushort section = chunk.ReadTypedUint16();
        if (section != 0x0009)
            throw new InvalidDataException($"Expected ScreenReference section 9 (ScreenId), got 0x{section:X}");
        ScreenActorId = chunk.ReadTypedUint16();

        // 0x0004: ContextId
        section = chunk.ReadTypedUint16();
        if (section != 0x0004)
            throw new InvalidDataException($"Expected ScreenReference section 4 (ContextId), got 0x{section:X}");
        ContextId = chunk.ReadTypedUint16();
    }
}
