using MediaStation.Unpack.Actors;
using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Metadata;

namespace MediaStation.Unpack.Model;

public class MediaStationTitle
{
    private static readonly uint TagIgod = Chunk.MakeTag('i', 'g', 'o', 'd');

    public string GameDirectory { get; }
    public string BootStmPath { get; }
    public string GameTitle { get; private set; } = string.Empty;
    public VersionInfo Version { get; private set; }
    public string EngineInfo { get; private set; } = string.Empty;
    public string SourceString { get; private set; } = string.Empty;
    public bool IsFirstGeneration => Version.Major == 0;

    public ushort? TargetContextId { get; private set; }

    public Dictionary<ushort, FileInfoRecord> FileMap { get; } = new();
    public Dictionary<ushort, StreamInfoRecord> StreamMap { get; } = new();
    public Dictionary<ushort, ContextReferenceRecord> ContextReferences { get; } = new();
    public Dictionary<ushort, ScreenReferenceRecord> ScreenReferences { get; } = new();
    public Dictionary<ushort, MediaStationContext> Contexts { get; } = new();
    public ProfileMetadata? Profile { get; private set; }

    private readonly string? _inputContextFile;

    public MediaStationTitle(string gameDirOrFile)
    {
        if (File.Exists(gameDirOrFile))
        {
            string fullPath = Path.GetFullPath(gameDirOrFile);
            if (Path.GetFileName(fullPath).Equals("BOOT.STM", StringComparison.OrdinalIgnoreCase))
            {
                BootStmPath = fullPath;
                GameDirectory = Path.GetDirectoryName(BootStmPath) ?? Directory.GetCurrentDirectory();
            }
            else
            {
                _inputContextFile = fullPath;
                string dir = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
                string bootCandidate = Path.Combine(dir, "BOOT.STM");
                BootStmPath = File.Exists(bootCandidate)
                    ? bootCandidate
                    : throw new FileNotFoundException($"Could not find BOOT.STM in '{dir}'.");
                GameDirectory = dir;
            }
        }
        else if (Directory.Exists(gameDirOrFile))
        {
            GameDirectory = Path.GetFullPath(gameDirOrFile);
            string? found = Directory.EnumerateFiles(GameDirectory, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => Path.GetFileName(f).Equals("BOOT.STM", StringComparison.OrdinalIgnoreCase));

            if (found == null)
            {
                // Also check subdirectories (e.g. data/)
                found = Directory.EnumerateFiles(GameDirectory, "BOOT.STM", SearchOption.AllDirectories).FirstOrDefault();
            }

            BootStmPath = found ?? throw new FileNotFoundException($"Could not find BOOT.STM in '{GameDirectory}'.");
            GameDirectory = Path.GetDirectoryName(BootStmPath) ?? GameDirectory;
        }
        else
        {
            throw new FileNotFoundException($"Path '{gameDirOrFile}' does not exist.");
        }

        // Set up initial stream map entry for BOOT_STREAM_ID (0)
        StreamMap[0] = new StreamInfoRecord(0, 0, 0);
        FileMap[0] = new FileInfoRecord(0, 0, Path.GetFileName(BootStmPath));
    }

    public void LoadManifest()
    {
        string? profilePath = FindProfilePath();
        if (profilePath != null)
        {
            Profile = ProfileMetadata.LoadFromFile(profilePath);
        }

        using var container = new ContainerFile(BootStmPath);
        var subfile = container.OpenSubfileAt(0);

        while (!subfile.AtEnd)
        {
            var chunk = subfile.NextChunk();
            if (chunk.Length == 0)
                continue;

            if (chunk.Id == TagIgod && chunk.ChunkBytesRemaining >= 4)
            {
                ushort streamType = chunk.ReadTypedUint16();
                if (streamType == 0x01) // kDocumentDefStream
                {
                    ReadDocumentDef(chunk);
                }
            }
        }

        if (_inputContextFile != null)
        {
            string fileName = Path.GetFileName(_inputContextFile);
            var file = FileMap.Values.FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (file != null && StreamMap.Values.FirstOrDefault(s => s.FileId == file.Id) is { } stream)
            {
                TargetContextId = ContextReferences.Values.FirstOrDefault(c => c.StreamId == stream.ActorId)?.ContextId;
            }
        }

        DetectGameTitle();
    }

    private void ReadDocumentDef(Chunk chunk)
    {
        while (chunk.ChunkBytesRemaining >= 2)
        {
            var sectionType = (BootSectionType)chunk.ReadTypedUint16();
            if (sectionType == BootSectionType.LastSection)
                break;

            switch (sectionType)
            {
                case BootSectionType.VersionInformation:
                    GameTitle = chunk.ReadTypedString();
                    Version = chunk.ReadTypedVersion();
                    EngineInfo = chunk.ReadTypedString();
                    SourceString = chunk.ReadTypedString();
                    break;

                case BootSectionType.ContextReference:
                    ReadContextReferences(chunk);
                    break;

                case BootSectionType.ScreenReference:
                    ReadScreenReferences(chunk);
                    break;

                case BootSectionType.FileInfo:
                    ReadFileMap(chunk);
                    break;

                case BootSectionType.StreamInfo:
                    ReadStreamMap(chunk);
                    break;

                case BootSectionType.Unk1:
                    chunk.ReadTypedUint16();
                    break;

                case BootSectionType.FunctionTableSize:
                    chunk.ReadTypedUint16();
                    break;

                case BootSectionType.Unk3:
                    chunk.ReadTypedUint16();
                    break;

                case BootSectionType.ParamTokenDeclaration:
                    chunk.ReadTypedString();
                    var valType = (BootSectionType)chunk.ReadTypedUint16();
                    if (valType == BootSectionType.ParamTokenValue)
                    {
                        chunk.ReadTypedString();
                    }
                    break;

                case BootSectionType.CursorManagerInit:
                    chunk.ReadTypedUint16(); // baseCursorId
                    chunk.ReadTypedUint16(); // maxCursorId
                    break;

                case BootSectionType.CursorManagerNewCursor:
                    ushort cursorType = chunk.ReadTypedUint16();
                    chunk.ReadTypedUint16(); // cursorId
                    if (cursorType == 0) // kPlatformCursor
                    {
                        chunk.ReadTypedUint16();
                    }
                    else if (cursorType == 1) // kResourceCursor
                    {
                        chunk.ReadTypedUint16();
                        chunk.ReadTypedFilename();
                    }
                    break;

                case BootSectionType.CursorManagerDisposeCursor:
                    chunk.ReadTypedUint16();
                    break;

                case BootSectionType.DocumentStartupInformation:
                    ushort docSection = chunk.ReadTypedUint16();
                    if (docSection == 0x002F) // kDocumentEntryScreen
                    {
                        chunk.ReadTypedUint16();
                    }
                    break;

                case BootSectionType.DeviceOwnerAllowMultipleSounds:
                    chunk.ReadTypedByte();
                    break;

                case BootSectionType.DeviceOwnerAllowMultipleStreams:
                    chunk.ReadTypedByte();
                    break;

                case BootSectionType.VideoDisplayManagerSetTime:
                    chunk.ReadTypedTime();
                    break;

                default:
                    // Skip unrecognized
                    break;
            }
        }
    }

    private void ReadContextReferences(Chunk chunk)
    {
        uint flag = chunk.ReadTypedUint16();
        while (flag != 0)
        {
            var contextRef = new ContextReferenceRecord(chunk);
            ContextReferences[contextRef.ContextId] = contextRef;
            flag = chunk.ReadTypedUint16();
        }
    }

    private void ReadScreenReferences(Chunk chunk)
    {
        uint flag = chunk.ReadTypedUint16();
        while (flag != 0)
        {
            var screenRef = new ScreenReferenceRecord(chunk);
            ScreenReferences[screenRef.ScreenActorId] = screenRef;
            flag = chunk.ReadTypedUint16();
        }
    }

    private void ReadFileMap(Chunk chunk)
    {
        uint flag = chunk.ReadTypedUint16();
        while (flag != 0)
        {
            var fileInfo = new FileInfoRecord(chunk);
            FileMap[fileInfo.Id] = fileInfo;
            flag = chunk.ReadTypedUint16();
        }
    }

    private void ReadStreamMap(Chunk chunk)
    {
        uint flag = chunk.ReadTypedUint16();
        while (flag != 0)
        {
            var streamInfo = new StreamInfoRecord(chunk);
            StreamMap[streamInfo.ActorId] = streamInfo;
            flag = chunk.ReadTypedUint16();
        }
    }

    private readonly Dictionary<string, string?> _resolvedFileCache = new(StringComparer.OrdinalIgnoreCase);

    public string? ResolveFilePath(string filename)
    {
        if (_resolvedFileCache.TryGetValue(filename, out var cached))
            return cached;

        string path = Path.Combine(GameDirectory, filename);
        if (File.Exists(path))
        {
            _resolvedFileCache[filename] = path;
            return path;
        }

        string bootDir = Path.GetDirectoryName(BootStmPath) ?? GameDirectory;
        path = Path.Combine(bootDir, filename);
        if (File.Exists(path))
        {
            _resolvedFileCache[filename] = path;
            return path;
        }

        // Fast search top directory
        string? found = Directory.EnumerateFiles(GameDirectory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(filename, StringComparison.OrdinalIgnoreCase));

        if (found == null)
        {
            string? parent = Directory.GetParent(GameDirectory)?.FullName;
            if (parent != null)
            {
                found = Directory.EnumerateFiles(parent, filename, SearchOption.AllDirectories).FirstOrDefault();
            }
        }

        _resolvedFileCache[filename] = found;
        return found;
    }

    public MediaStationContext LoadContext(ushort contextId)
    {
        if (Contexts.TryGetValue(contextId, out var existing))
            return existing;

        var context = new MediaStationContext { ContextId = contextId };
        Contexts[contextId] = context;

        if (!ContextReferences.TryGetValue(contextId, out var contextRef))
            return context;

        if (!string.IsNullOrEmpty(contextRef.Name))
            context.Name = contextRef.Name;

        if (!StreamMap.TryGetValue(contextRef.StreamId, out var streamInfo))
            return context;

        if (!FileMap.TryGetValue(streamInfo.FileId, out var fileInfo))
            return context;

        string? filePath = ResolveFilePath(fileInfo.Name);
        if (filePath == null)
            return context;

        var channelActors = new Dictionary<uint, Actor>();
        var movieFramesActors = new Dictionary<uint, StreamMovieActor>();
        var movieAudioActors = new Dictionary<uint, StreamMovieActor>();

        using var container = new ContainerFile(filePath);
        var subfile = container.OpenSubfileAt(streamInfo.StartOffsetInFile);

        while (!subfile.AtEnd)
        {
            var chunk = subfile.NextChunk();
            if (chunk.Length == 0)
                continue;

            if (chunk.Id == TagIgod)
            {
                ReadContextIgodChunk(chunk, context, channelActors, movieFramesActors, movieAudioActors);
            }
            else
            {
                // Media data chunk
                if (channelActors.TryGetValue(chunk.Id, out var actor))
                {
                    actor.ReadChunk(chunk);
                }
                else if (movieFramesActors.TryGetValue(chunk.Id, out var movieFramesActor))
                {
                    movieFramesActor.ReadFramesChunk(chunk);
                }
                else if (movieAudioActors.TryGetValue(chunk.Id, out var movieAudioActor))
                {
                    movieAudioActor.ReadAudioChunk(chunk);
                }
            }
        }

        // Check if any actors in this context have external streamed feeds
        foreach (var actor in context.Actors.Values)
        {
            if (StreamMap.TryGetValue((ushort)actor.Id, out var actorStreamInfo) && actorStreamInfo.ActorId != 0)
            {
                if (FileMap.TryGetValue(actorStreamInfo.FileId, out var actorFileInfo))
                {
                    string? actorFilePath = ResolveFilePath(actorFileInfo.Name);
                    if (actorFilePath != null)
                    {
                        using var actorContainer = new ContainerFile(actorFilePath);
                        var actorSubfile = actorContainer.OpenSubfileAt(actorStreamInfo.StartOffsetInFile);
                        while (!actorSubfile.AtEnd)
                        {
                            var chunk = actorSubfile.NextChunk();
                            if (chunk.Length == 0)
                                continue;
                            if (actor is StreamMovieActor movie)
                            {
                                if (chunk.Id == movie.FramesChannelIdent)
                                    movie.ReadFramesChunk(chunk);
                                else if (chunk.Id == movie.AudioChannelIdent)
                                    movie.ReadAudioChunk(chunk);
                                else if (chunk.Id == movie.ChannelIdent)
                                    movie.ReadChunk(chunk);
                            }
                            else if (actor.ChannelIdent == chunk.Id)
                            {
                                actor.ReadChunk(chunk);
                            }
                        }
                    }
                }
            }

            if (actor is DiskImageActor diskImg)
            {
                foreach (var stripInfo in diskImg.StripInfos.Values)
                {
                    if (diskImg.StripImages.ContainsKey(stripInfo.Index))
                        continue;

                    if (StreamMap.TryGetValue((ushort)stripInfo.StreamId, out var stripStreamInfo))
                    {
                        if (FileMap.TryGetValue(stripStreamInfo.FileId, out var stripFileInfo))
                        {
                            string? stripFilePath = ResolveFilePath(stripFileInfo.Name);
                            if (stripFilePath != null)
                            {
                                using var stripContainer = new ContainerFile(stripFilePath);
                                var stripSubfile = stripContainer.OpenSubfileAt(stripStreamInfo.StartOffsetInFile);
                                while (!stripSubfile.AtEnd)
                                {
                                    var chunk = stripSubfile.NextChunk();
                                    if (chunk.Length == 0)
                                        continue;
                                    if (chunk.Id == diskImg.ChannelIdent)
                                    {
                                        diskImg.ReadChunk(chunk);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return context;
    }

    private void ReadContextIgodChunk(
        Chunk chunk,
        MediaStationContext context,
        Dictionary<uint, Actor> channelActors,
        Dictionary<uint, StreamMovieActor> movieFramesActors,
        Dictionary<uint, StreamMovieActor> movieAudioActors)
    {
        while (chunk.ChunkBytesRemaining >= 2)
        {
            ushort sectionType = chunk.ReadTypedUint16();
            if (sectionType == 0x00) // kContextEndOfSection
                break;

            switch (sectionType)
            {
                case 0x000C: // kCursorManagerInit
                    chunk.ReadTypedUint16();
                    chunk.ReadTypedUint16();
                    break;

                case 0x000E: // kContextCreateData
                case 0x000F: // kContextDestroyData
                case 0x0010: // kContextLoadCompleteSection
                case 0x0012: // kContextDestroyActorData
                case 0x0013: // kContextActorLoadComplete
                    chunk.ReadTypedUint16();
                    break;

                case 0x0011: // kContextCreateActorData
                {
                    uint ctxId = chunk.ReadTypedUint16();
                    var actorType = (ActorType)chunk.ReadTypedUint16();
                    uint actorId = chunk.ReadTypedUint16();

                    var actor = CreateActor(actorType);
                    actor.Id = actorId;
                    actor.ContextId = ctxId;
                    actor.InitFromParameterStream(chunk);

                    if (Profile != null && Profile.Assets.TryGetValue(actorId, out var actorName))
                        actor.Name = actorName;

                    context.Actors[actorId] = actor;

                    if (actor.ChannelIdent != 0)
                        channelActors[actor.ChannelIdent] = actor;

                    if (actor is StreamMovieActor movie)
                    {
                        if (movie.FramesChannelIdent != 0)
                            movieFramesActors[movie.FramesChannelIdent] = movie;
                        if (movie.AudioChannelIdent != 0)
                            movieAudioActors[movie.AudioChannelIdent] = movie;
                    }

                    break;
                }

                case 0x0014: // kContextCreateVariableData
                    chunk.ReadTypedUint16(); // contextId
                    chunk.ReadTypedUint16(); // id
                    chunk.SkipScriptValue();
                    break;

                case 0x0015: // kCursorManagerNewCursor
                {
                    ushort cursorType = chunk.ReadTypedUint16();
                    chunk.ReadTypedUint16();
                    if (cursorType == 0)
                        chunk.ReadTypedUint16();
                    else if (cursorType == 1)
                    {
                        chunk.ReadTypedUint16();
                        chunk.ReadTypedFilename();
                    }
                    break;
                }

                case 0x0016: // kCursorManagerDisposeCursor
                    chunk.ReadTypedUint16();
                    break;

                case 0x0031: // kContextFunctionSection
                {
                    chunk.ReadTypedUint16(); // contextId
                    chunk.ReadTypedUint16(); // functionId
                    uint bytecodeSize = chunk.ReadTypedUint32();
                    chunk.ReadBytes((int)bytecodeSize);
                    break;
                }

                case 0x0035: // kDeviceOwnerAllowMultipleSounds
                case 0x0036: // kDeviceOwnerAllowMultipleStreams
                    chunk.ReadTypedByte();
                    break;

                case 0x0578: // kVideoDisplayManagerUpdateDirty
                case 0x0579: // kVideoDisplayManagerUpdateAll
                    break;

                case 0x057A: // kVideoDisplayManagerEffectTransition
                {
                    uint argCount = chunk.ReadTypedUint16();
                    for (uint i = 0; i < argCount; i++)
                        chunk.SkipScriptValue();
                    break;
                }

                case 0x057B: // kVideoDisplayManagerSetTime
                    chunk.ReadTypedTime();
                    break;

                case 0x05AA: // kVideoDisplayManagerLoadPalette
                {
                    byte[] paletteBytes = chunk.ReadBytes(Palette.SizeInBytes);
                    context.Palette = new Palette(paletteBytes);
                    break;
                }

                case 0x0BB9: // kContextNameData
                {
                    uint ctxId = chunk.ReadTypedUint16();
                    string ctxName = chunk.ReadTypedString();
                    if (context.ContextId == ctxId)
                        context.Name = ctxName;
                    break;
                }

                default:
                    // Unknown context section
                    break;
            }
        }
    }

    private Actor CreateActor(ActorType type)
    {
        return type switch
        {
            ActorType.Image => new ImageActor(),
            ActorType.DiskImage => new DiskImageActor(),
            ActorType.Sprite => new SpriteMovieActor(),
            ActorType.Movie => new StreamMovieActor { IsFirstGeneration = IsFirstGeneration },
            ActorType.Sound => new SoundActor(),
            ActorType.Palette => new PaletteActor(),
            _ => new Actor(type),
        };
    }

    public string? GetParamTokenName(uint tokenId) => Profile?.GetClipName(tokenId);

    private string? FindProfilePath()
    {
        string dir = Path.GetDirectoryName(BootStmPath) ?? GameDirectory;
        string candidate = Path.Combine(dir, "PROFILE._ST");
        if (File.Exists(candidate))
            return candidate;

        candidate = Path.Combine(GameDirectory, "PROFILE._ST");
        if (File.Exists(candidate))
            return candidate;

        try
        {
            return Directory.EnumerateFiles(GameDirectory, "*", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileName(f).Equals("PROFILE._ST", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static readonly Dictionary<string, string> KnownGameExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LIONKING.EXE"] = "The Lion King",
        ["MOUSECKE.EXE"] = "If You Give a Mouse a Cookie",
        ["HAUNTED.EXE"] = "Jan Pieńkowski Haunted House",
        ["LCMUSIC.EXE"] = "Lamb Chop Loves Music",
        ["FPRINCE.EXE"] = "Fractured Fairy Tales: The Frog Prince",
        ["WPHTASB.EXE"] = "Winnie the Pooh and the Honey Tree",
        ["HB_ASB.EXE"] = "The Hunchback of Notre Dame",
        ["PZCASTLE.EXE"] = "Jan Pieńkowski Haunted House: Puzzle Castle",
        ["101_ASB.EXE"] = "101 Dalmatians",
        ["HERC_ASB.EXE"] = "Hercules",
        ["POCA_ASB.EXE"] = "Pocahontas",
        ["RAPUNZEL.EXE"] = "Fractured Fairy Tales: Rapunzel",
        ["TONKA_SR.EXE"] = "Tonka Search & Rescue",
        ["ARIEL_SS.EXE"] = "The Little Mermaid",
        ["MERMAID.EXE"] = "The Little Mermaid",
        ["TONKA_GR.EXE"] = "Tonka Garage",
        ["TONKA_W.EXE"] = "Tonka Workshop",
        ["TONKA_RACEWAY.EXE"] = "Tonka Raceway",
        ["STUARTCD.EXE"] = "Stuart Little: His Adventures in Numberland",
    };

    private void DetectGameTitle()
    {
        if (!string.IsNullOrEmpty(GameTitle))
            return;

        var candidateDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(GameDirectory) && Directory.Exists(GameDirectory))
        {
            candidateDirs.Add(GameDirectory);
            string? parent = Path.GetDirectoryName(GameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                candidateDirs.Add(parent);
        }

        foreach (var dir in candidateDirs)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.EXE"))
                {
                    string fileName = Path.GetFileName(file);
                    if (KnownGameExecutables.TryGetValue(fileName, out var knownTitle))
                    {
                        GameTitle = knownTitle;
                        return;
                    }

                    try
                    {
                        var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(file);
                        if (!string.IsNullOrWhiteSpace(fileVersionInfo.ProductName) &&
                            !fileVersionInfo.ProductName.Contains("Media Station", StringComparison.OrdinalIgnoreCase) &&
                            !fileVersionInfo.ProductName.Contains("Install", StringComparison.OrdinalIgnoreCase) &&
                            !fileVersionInfo.ProductName.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                        {
                            GameTitle = fileVersionInfo.ProductName.Trim();
                            return;
                        }
                    }
                    catch
                    {
                        // Ignore PE parsing errors
                    }
                }
            }
            catch
            {
                // Ignore directory enumeration errors
            }
        }

        string dirName = Path.GetFileName(GameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(dirName) && !dirName.Equals("DATA", StringComparison.OrdinalIgnoreCase))
        {
            GameTitle = dirName;
        }
    }
}
