using MediaStation.Unpack.Audio;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Audio;

public class ImaAdpcmDecoderTests
{
    [Fact]
    public void ImaAdpcmDecoder_DecodesBlockWithoutErrors()
    {
        // 8 bytes block:
        // Header: 0x0000 (last = 0, stepIndex = 0)
        // 6 data bytes: 12 nibbles of 0 (minimal delta)
        byte[] block = new byte[8];
        byte[] pcm = ImaAdpcmDecoder.DecodeDviIma(block, blockAlign: 8);

        // 12 samples * 2 bytes = 24 bytes PCM
        Assert.Equal(24, pcm.Length);
    }
}
