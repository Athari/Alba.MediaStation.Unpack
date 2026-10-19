using MediaStation.Unpack.Audio;
using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Metadata;

namespace MediaStation.Unpack.Actors;

public class StreamMovieProxyRecord
{
    public uint LayerId { get; set; }
    public uint ScriptId { get; set; }
    public short ZIndex { get; set; }
}

public class MovieImageData
{
    public uint Index { get; }
    public uint KeyframeEndInMilliseconds { get; }
    public PixMapImage Image { get; }

    public MovieImageData(uint index, uint keyframeEndMs, PixMapImage image)
    {
        Index = index;
        KeyframeEndInMilliseconds = keyframeEndMs;
        Image = image;
    }
}

public class StreamMovieActor : Actor
{
    public uint FramesChannelIdent { get; set; }
    public uint AudioChannelIdent { get; set; }
    public double FrameRate { get; set; }
    public uint ChunkCount { get; set; }
    public bool IsFirstGeneration { get; set; }

    public AudioSequence Audio { get; } = new();
    public List<MovieImageData> Images { get; } = new();
    public List<MovieFrame> Frames { get; } = new();
    public List<StreamMovieProxyRecord> Proxies { get; } = new();

    public StreamMovieActor() : base(ActorType.Movie)
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

            case 0x0033: // kActorHeaderSoundInfo
                Audio.ReadParameters(chunk);
                break;

            case 0x06A4: // kActorHeaderMovieAnimationChannelIdent
                FramesChannelIdent = chunk.ReadTypedChannelIdent();
                break;

            case 0x06A5: // kActorHeaderMovieAudioChannelIdent
                AudioChannelIdent = chunk.ReadTypedChannelIdent();
                break;

            case 0x06AC: // kStreamMovieProxyInfo
            {
                var proxy = new StreamMovieProxyRecord
                {
                    LayerId = chunk.ReadTypedUint32(),
                    ScriptId = chunk.ReadTypedUint16(),
                    ZIndex = chunk.ReadTypedSint16(),
                };
                Proxies.Add(proxy);
                break;
            }

            default:
                base.ReadParameter(chunk, paramType);
                break;
        }
    }

    public void ReadFramesChunk(Chunk chunk)
    {
        ushort sectionType = chunk.ReadTypedUint16();
        switch (sectionType)
        {
            case 0x06A9: // kMovieImageDataSection
                ReadImageData(chunk);
                break;

            case 0x06AA: // kMovieFrameDataSection
                ReadFrameData(chunk);
                break;
        }
    }

    private void ReadImageData(Chunk chunk)
    {
        var imageInfo = new ImageInfo(chunk);
        uint index = chunk.ReadTypedUint32();
        uint keyframeEndMs = chunk.ReadTypedUint32();
        var image = new PixMapImage(chunk, imageInfo, decompressImmediately: false);
        Images.Add(new MovieImageData(index, keyframeEndMs, image));
    }

    private void ReadFrameData(Chunk chunk)
    {
        uint frameCount = chunk.ReadTypedUint16();
        for (uint i = 0; i < frameCount; i++)
        {
            var frame = new MovieFrame(chunk, IsFirstGeneration);
            Frames.Add(frame);
        }
    }

    public void ReadAudioChunk(Chunk chunk)
    {
        Audio.ReadChunk(chunk);
    }

    public void DecompressAllFrames()
    {
        if (Frames.Count == 0 || Images.Count == 0)
            return;

        var imageMap = new Dictionary<uint, PixMapImage>();
        foreach (var img in Images)
        {
            imageMap.TryAdd(img.Index, img.Image);
        }

        foreach (var frame in Frames)
        {
            if (!imageMap.TryGetValue(frame.Index, out var framePixmap))
                continue;

            if (frame.KeyframeIndex != 0 && imageMap.TryGetValue(frame.KeyframeIndex, out var keyframePixmap))
            {
                if (keyframePixmap.IsCompressed)
                    keyframePixmap.Decompress();

                if (framePixmap.IsCompressed)
                {
                    framePixmap.Decompress(
                        keyframePixmap.Pixels,
                        keyframePixmap.Width,
                        keyframePixmap.Height,
                        frame.DiffBetweenKeyframeAndFrame);
                }
            }
            else
            {
                if (framePixmap.IsCompressed)
                    framePixmap.Decompress();
            }
        }
    }
}
