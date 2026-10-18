using MediaStation.Unpack.Core;

namespace MediaStation.Unpack.Actors;

public enum ActorType : ushort
{
    Empty = 0x0000,
    Screen = 0x0001,
    Stage = 0x0002,
    Path = 0x0004,
    Sound = 0x0005,
    Timer = 0x0006,
    Image = 0x0007,
    Hotspot = 0x000B,
    Cursor = 0x000C,
    Sprite = 0x000E,
    StalkingZazu = 0x000F,
    DotGame = 0x0010,
    Document = 0x0011,
    Movie = 0x0016,
    Palette = 0x0017,
    StreamMovieProxy = 0x0018,
    Printer = 0x0019,
    Text = 0x001A,
    Font = 0x001B,
    Camera = 0x001C,
    DiskImage = 0x001D,
    Canvas = 0x001E,
    Xsnd = 0x001F,
    XsndMidi = 0x0020,
    Recorder = 0x0021,
    Function = 0x0069,
}

public class Actor
{
    public uint Id { get; set; }
    public uint ContextId { get; set; }
    public ActorType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint ChannelIdent { get; set; }
    public Rect BoundingBox { get; set; }
    public int ZIndex { get; set; }
    public bool IsVisible { get; set; } = true;
    public double DissolveFactor { get; set; }

    public Actor(ActorType type)
    {
        Type = type;
    }

    public virtual void InitFromParameterStream(Chunk chunk)
    {
        while (true)
        {
            ushort paramType = chunk.ReadTypedUint16();
            if (paramType == 0)
                break;

            ReadParameter(chunk, paramType);
        }
    }

    public virtual void ReadParameter(Chunk chunk, ushort paramType)
    {
        switch (paramType)
        {
            case 0x0017: // kActorHeaderScriptResponse
                chunk.ReadTypedUint16(); // eventType
                chunk.SkipScriptValue(); // argumentValue
                uint bytecodeSize = chunk.ReadTypedUint32();
                chunk.ReadBytes((int)bytecodeSize);
                break;

            case 0x0019: // kActorHeaderChildActorId
            case 0x001A: // kActorHeaderActorId
            case 0x0022: // kActorHeaderCursorResourceId
            case 0x0258: // kActorHeaderFontActorId
            case 0x025A: // kActorHeaderTextMaxLength
            case 0x025B: // kActorHeaderTextJustification
            case 0x025F: // kActorHeaderTextPosition
            case 0x0262: // kActorHeaderTextCursorIsVisible
            case 0x03E8: // kActorHeaderSpriteChunkCount
            case 0x03EA: // kActorHeaderDefaultSpriteClip
            case 0x03EC: // kActorHeaderStalkingZazuSpriteId
            case 0x03EF: // kActorHeaderStalkingZazuObstacleActorId
            case 0x03F2: // kActorHeaderStalkingZazuUnkSound1
            case 0x03F3: // kActorHeaderStalkingZazuUnkSound2
            case 0x0514: // kActorHeaderDotGameHelperSprite1
            case 0x0515: // kActorHeaderDotGameHelperSprite2
            case 0x0516: // kActorHeaderDotGameState
            case 0x0517: // kActorHeaderDotGameSpeed
            case 0x0610: // kActorHeaderPathTotalSteps
            case 0x0774: // kActorHeaderDiskImageMaxStrips
            case 0x0775: // kActorHeaderDiskImageStripWidth
            case 0x0777: // kActorHeaderDiskImageMaxImages
            case 0x077B: // kActorHeaderActorReference / kActorHeaderCameraImageActor
                chunk.ReadTypedUint16();
                break;

            case 0x0021: // kActorHeaderDiscardAfterUse
                chunk.ReadTypedByteOrUint16();
                break;

            case 0x0033: // kActorHeaderSoundInfo
                chunk.ReadTypedUint16(); // chunkCount
                chunk.ReadTypedUint32(); // rate
                chunk.ReadTypedUint16(); // channelCount
                chunk.ReadTypedUint16(); // bitsPerSample
                break;

            case 0x03E9: // kActorHeaderSpriteClip
                chunk.ReadTypedUint16();
                chunk.ReadTypedUint16();
                chunk.ReadTypedUint16();
                break;

            case 0x001B: // kActorHeaderChannelIdent
            case 0x06A4: // kActorHeaderMovieAnimationChannelIdent
            case 0x06A5: // kActorHeaderMovieAudioChannelIdent
                ChannelIdent = chunk.ReadTypedChannelIdent();
                break;

            case 0x001C: // kActorHeaderBoundingBox
                BoundingBox = chunk.ReadTypedRect();
                break;

            case 0x001D: // kActorHeaderMouseActiveArea / kActorHeaderDotGameMaxDots
            {
                uint totalPoints = chunk.ReadTypedUint16();
                for (uint i = 0; i < totalPoints; i++)
                    chunk.ReadTypedPoint();
                break;
            }

            case 0x001E: // kActorHeaderZIndex
            case 0x03F0: // kActorHeaderStalkingZazuUnkX
            case 0x03F1: // kActorHeaderStalkingZazuUnkY
            case 0x05DE: // kActorHeaderX
            case 0x05DF: // kActorHeaderY
                ZIndex = chunk.ReadTypedGraphicUnitOrUint16();
                break;

            case 0x03F5: // kActorHeaderStalkingZazuObstaclesToShow
                chunk.ReadTypedSint16();
                break;

            case 0x001F: // kActorHeaderStartup
                IsVisible = chunk.ReadTypedByte() != 0;
                break;

            case 0x0020: // kActorHeaderTransparency
            case 0x0032: // kActorHeaderLoadType
            case 0x0034: // kActorHeaderCachingEnabled
            case 0x0037: // kActorHeaderInstallType
            case 0x0263: // kActorHeaderTextConstrainToWidth
            case 0x0264: // kActorHeaderTextOverwriteMode
            case 0x03EB: // kActorHeaderEditable
            case 0x03F4: // kActorHeaderStalkingZazuAudioEnabled
            case 0x0518: // kActorHeaderDotGameLineThickness
            case 0x05DD: // kActorHeaderGetOffstageEvents
            case 0x0770: // kActorHeaderCameraLensOpen
            case 0x0772: // kActorHeaderCylindricalX
            case 0x0773: // kActorHeaderCylindricalY
            case 0x0776: // kActorHeaderDiskImageUnk1
            case 0x07D0: // kActorHeaderCanvasTransparency
                chunk.ReadTypedByte();
                break;

            case 0x0519: // kActorHeaderDotGameColor
                chunk.ReadTypedByte();
                chunk.ReadTypedByte();
                chunk.ReadTypedByte();
                break;

            case 0x0024: // kActorHeaderFrameRate
            case 0x0493: // kActorHeaderCanvasDissolveFactor
            case 0x05DC: // kActorHeaderDissolveFactor
            case 0x0611: // kActorHeaderStepRate
            case 0x077A: // kActorHeaderScaleXAndY
            case 0x077C: // kActorHeaderScaleX
            case 0x077D: // kActorHeaderScaleY
                DissolveFactor = chunk.ReadTypedDouble();
                break;

            case 0x0259: // kActorHeaderInitialText
            case 0x0BB8: // kActorHeaderActorName
                Name = chunk.ReadTypedString();
                break;

            case 0x03F6: // kActorHeaderStalkingZazuUnkPoint
            case 0x060E: // kActorHeaderStartPoint
            case 0x060F: // kActorHeaderEndPoint
            case 0x076F: // kActorHeaderCameraViewportOrigin
                chunk.ReadTypedPoint();
                break;

            case 0x0265: // kActorHeaderTextAcceptedCharRangeWithOffset
                chunk.ReadTypedUint16();
                chunk.ReadTypedUint16();
                chunk.ReadTypedUint16();
                break;

            case 0x0266: // kActorHeaderTextAcceptedCharRange
                chunk.ReadTypedUint16();
                chunk.ReadTypedUint16();
                break;

            case 0x03ED: // kActorHeaderStalkingZazuDirections
                for (int i = 0; i < 15; i++)
                    chunk.ReadTypedUint16();
                break;

            case 0x03EE: // kActorHeaderStalkingZazuField
                chunk.ReadTypedUint16();
                chunk.ReadTypedUint16();
                break;

            case 0x05AA: // kActorHeaderPalette
                chunk.ReadBytes(768);
                break;

            case 0x0612: // kActorHeaderDuration
                chunk.ReadTypedTime();
                break;

            case 0x06AC: // kStreamMovieProxyInfo
                chunk.ReadTypedUint32();
                chunk.ReadTypedUint16();
                chunk.ReadTypedSint16();
                break;

            case 0x0771: // kActorHeaderStageExtent
                chunk.ReadTypedGraphicSize();
                break;

            case 0x0778: // kActorHeaderDiskImageStripInfo
                chunk.ReadTypedUint16(); // index
                chunk.ReadTypedUint16(); // streamId
                chunk.ReadTypedUint32(); // lengthInBytes
                break;

            case 0x0779: // kActorHeaderDiskImageUnkRect
                chunk.ReadTypedRect();
                break;

            default:
                chunk.SkipTypedDatum();
                break;
        }
    }

    public virtual void ReadChunk(Chunk chunk)
    {
    }
}
