using System.Buffers.Binary;
using System.Text;

namespace MediaStation.Unpack.Audio;

public static class WavWriter
{
    public static void WritePcmWav(Stream output, ReadOnlySpan<byte> pcmData, int sampleRate, int channels = 1, int bitsPerSample = 16)
    {
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));
        int dataSize = pcmData.Length;
        int riffSize = 36 + dataSize;

        Span<byte> header = stackalloc byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header[0..4]);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], riffSize);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header[8..12]);

        Encoding.ASCII.GetBytes("fmt ").CopyTo(header[12..16]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], 16); // subchunk1 size
        BinaryPrimitives.WriteInt16LittleEndian(header[20..22], 1);  // PCM format = 1
        BinaryPrimitives.WriteInt16LittleEndian(header[22..24], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..28], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..32], byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..34], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..36], (short)bitsPerSample);

        Encoding.ASCII.GetBytes("data").CopyTo(header[36..40]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..44], dataSize);

        output.Write(header);
        output.Write(pcmData);
    }

    public static void SavePcmWav(string filePath, ReadOnlySpan<byte> pcmData, int sampleRate, int channels = 1, int bitsPerSample = 16)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var fs = File.Create(filePath);
        WritePcmWav(fs, pcmData, sampleRate, channels, bitsPerSample);
    }
}
