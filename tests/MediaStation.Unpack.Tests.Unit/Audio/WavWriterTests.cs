using MediaStation.Unpack.Audio;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Audio;

public class WavWriterTests
{
    [Fact]
    public void WavWriter_ProducesValidRiffHeader()
    {
        byte[] pcm = new byte[200];
        var ms = new MemoryStream();
        WavWriter.WritePcmWav(ms, pcm, sampleRate: 22050, channels: 1, bitsPerSample: 16);

        byte[] wav = ms.ToArray();
        Assert.Equal((int)'R', wav[0]);
        Assert.Equal((int)'I', wav[1]);
        Assert.Equal((int)'F', wav[2]);
        Assert.Equal((int)'F', wav[3]);

        Assert.Equal((int)'W', wav[8]);
        Assert.Equal((int)'A', wav[9]);
        Assert.Equal((int)'V', wav[10]);
        Assert.Equal((int)'E', wav[11]);

        // Total file size: 44 header + 200 data = 244
        Assert.Equal(244, wav.Length);
    }
}
