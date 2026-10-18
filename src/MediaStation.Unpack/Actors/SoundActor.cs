using MediaStation.Unpack.Audio;
using MediaStation.Unpack.Core;

namespace MediaStation.Unpack.Actors;

public class SoundActor : Actor
{
    public AudioSequence Sequence { get; } = new();

    public SoundActor() : base(ActorType.Sound)
    {
    }

    public override void ReadParameter(Chunk chunk, ushort paramType)
    {
        switch (paramType)
        {
            case 0x001B: // kActorHeaderChannelIdent
                ChannelIdent = chunk.ReadTypedChannelIdent();
                break;

            case 0x0033: // kActorHeaderSoundInfo
                Sequence.ReadParameters(chunk);
                break;

            default:
                base.ReadParameter(chunk, paramType);
                break;
        }
    }

    public override void ReadChunk(Chunk chunk)
    {
        Sequence.ReadChunk(chunk);
    }
}
