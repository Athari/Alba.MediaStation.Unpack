using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Metadata;

namespace MediaStation.Unpack.Actors;

public class SpriteMovieActor : Actor
{
    public const uint DefaultForwardClipId = 0x4B0;
    public const uint DefaultBackwardClipId = 0x4B1;

    public uint FrameRate { get; set; } = 12;
    public bool DecompressInPlace { get; set; }
    public ushort FrameCount { get; set; }
    public uint DefaultClipId { get; set; } = DefaultForwardClipId;
    public uint ActorReference { get; set; }

    public Dictionary<uint, SpriteMovieClip> Clips { get; } = new();
    public List<(PixMapImage Pixmap, Point Origin, uint Index)> Frames { get; } = new();

    public SpriteMovieActor() : base(ActorType.Sprite)
    {
    }

    public override void ReadParameter(Chunk chunk, ushort paramType)
    {
        switch (paramType)
        {
            case 0x001B: // kActorHeaderChannelIdent
                ChannelIdent = chunk.ReadTypedChannelIdent();
                break;

            case 0x001F: // kActorHeaderStartup
                IsVisible = chunk.ReadTypedByte() != 0;
                break;

            case 0x0024: // kActorHeaderFrameRate
                FrameRate = (uint)Math.Round(chunk.ReadTypedDouble());
                break;

            case 0x0032: // kActorHeaderLoadType
                DecompressInPlace = chunk.ReadTypedByte() != 0;
                break;

            case 0x03E8: // kActorHeaderSpriteChunkCount
                FrameCount = chunk.ReadTypedUint16();
                break;

            case 0x03E9: // kActorHeaderSpriteClip
            {
                uint id = chunk.ReadTypedUint16();
                int first = chunk.ReadTypedUint16();
                int last = chunk.ReadTypedUint16();
                Clips[id] = new SpriteMovieClip(id, first, last);
                break;
            }

            case 0x03EA: // kActorHeaderDefaultSpriteClip
                DefaultClipId = chunk.ReadTypedUint16();
                break;

            case 0x077B: // kActorHeaderActorReference
                ActorReference = chunk.ReadTypedUint16();
                break;

            default:
                base.ReadParameter(chunk, paramType);
                break;
        }
    }

    public override void ReadChunk(Chunk chunk)
    {
        var imageInfo = new ImageInfo(chunk);
        uint index = chunk.ReadTypedUint16();
        Point offset = chunk.ReadTypedPoint();
        var pixmap = new PixMapImage(chunk, imageInfo, decompressImmediately: true);

        Frames.Add((pixmap, offset, index));
        Frames.Sort((a, b) => a.Index.CompareTo(b.Index));
    }
}
