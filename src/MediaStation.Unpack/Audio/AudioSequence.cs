using MediaStation.Unpack.Core;

namespace MediaStation.Unpack.Audio;

public class AudioSequence
{
    public ushort ChunkCount { get; private set; }
    public uint SampleRate { get; private set; }
    public ushort ChannelCount { get; private set; }
    public ushort BitsPerSample { get; private set; }

    private readonly MemoryStream _accumulatedPcm = new();

    public bool HasAudio => _accumulatedPcm.Length > 0;

    public void ReadParameters(Chunk chunk)
    {
        ChunkCount = chunk.ReadTypedUint16();
        SampleRate = chunk.ReadTypedUint32();
        ChannelCount = chunk.ReadTypedUint16();
        BitsPerSample = chunk.ReadTypedUint16();
    }

    public void ReadChunk(Chunk chunk)
    {
        byte[] chunkData = chunk.ReadBytes((int)chunk.ChunkBytesRemaining);
        if (chunkData.Length == 0)
            return;

        if (BitsPerSample == 16)
        {
            // Raw 16-bit little endian PCM
            _accumulatedPcm.Write(chunkData);
        }
        else if (BitsPerSample == 4)
        {
            // 4-bit DVI IMA ADPCM
            byte[] pcm = ImaAdpcmDecoder.DecodeDviIma(chunkData, blockAlign: 8);
            _accumulatedPcm.Write(pcm);
        }
    }

    public byte[] GetPcmBytes() => _accumulatedPcm.ToArray();

    public void SaveAsWav(string filePath)
    {
        if (!HasAudio)
            return;

        int rate = SampleRate > 0 ? (int)SampleRate : 22050;
        int channels = ChannelCount > 0 ? ChannelCount : 1;
        WavWriter.SavePcmWav(filePath, _accumulatedPcm.ToArray(), rate, channels, bitsPerSample: 16);
    }
}
