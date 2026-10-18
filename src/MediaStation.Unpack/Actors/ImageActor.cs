using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;

namespace MediaStation.Unpack.Actors;

public class ImageActor : Actor
{
    public PixMapImage? Bitmap { get; set; }
    public int XOffset { get; set; }
    public int YOffset { get; set; }
    public uint ActorReference { get; set; }
    public bool DecompressInPlace { get; set; }

    public ImageActor() : base(ActorType.Image)
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

            case 0x0032: // kActorHeaderLoadType
                DecompressInPlace = chunk.ReadTypedByte() != 0;
                break;

            case 0x05DE: // kActorHeaderX
                XOffset = chunk.ReadTypedUint16();
                break;

            case 0x05DF: // kActorHeaderY
                YOffset = chunk.ReadTypedUint16();
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
        Bitmap = new PixMapImage(chunk, imageInfo, decompressImmediately: true);
    }
}
