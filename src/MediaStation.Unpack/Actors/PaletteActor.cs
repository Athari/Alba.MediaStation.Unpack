using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;

namespace MediaStation.Unpack.Actors;

public class PaletteActor : Actor
{
    public Palette? Palette { get; private set; }

    public PaletteActor() : base(ActorType.Palette)
    {
    }

    public override void ReadParameter(Chunk chunk, ushort paramType)
    {
        switch (paramType)
        {
            case 0x05AA: // kActorHeaderPalette
            {
                byte[] buffer = chunk.ReadBytes(Palette.SizeInBytes);
                Palette = new Palette(buffer);
                break;
            }

            default:
                base.ReadParameter(chunk, paramType);
                break;
        }
    }
}
