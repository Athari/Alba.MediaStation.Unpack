using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Compression;

public class DecompressorTests
{
    [Fact]
    public void Rle8_DecompressesLiteralAndRepeatRuns()
    {
        // 4x2 image
        // Line 1: repeat 4 of color 7 (operation=4, color=7), eol (0x00, 0x00)
        // Line 2: literal 4 of [1, 2, 3, 4] (operation=0x00, length=4, 1, 2, 3, 4), eoi (0x00, 0x01)
        var stream = new List<byte>
        {
            // Line 1
            4, 7, // repeat 4 of color 7
            0, 0, // end of line

            // Line 2
            0, 4, 1, 2, 3, 4, // literal run of 4
            0, 1  // end of image
        };

        byte[] result = Decompressor.DecompressRle8(stream.ToArray(), 4, 2);

        byte[] expected =
        [
            7, 7, 7, 7,
            1, 2, 3, 4
        ];

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Ccc_Decompresses4x4BlockCorrectly()
    {
        // 4x4 image with color1=10, color2=20
        // bitmask: 0b1010_0101_1100_0011 (0xA5C3)
        // Row 0: 1 0 1 0 -> 20, 10, 20, 10
        // Row 1: 0 1 0 1 -> 10, 20, 10, 20
        // Row 2: 1 1 0 0 -> 20, 20, 10, 10
        // Row 3: 0 0 1 1 -> 10, 10, 20, 20
        byte[] compressed =
        [
            10, 20,
            0xA5, 0xC3
        ];

        byte[] result = Decompressor.DecompressCcc(compressed, 4, 4);

        byte[] expected =
        [
            20, 10, 20, 10,
            10, 20, 10, 20,
            20, 20, 10, 10,
            10, 10, 20, 20
        ];

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CccTransparent_DecompressesCommandStream()
    {
        // Command stream:
        // 0x00010000: 1 block at (0, 0)
        // Block: color1=5, color2=9, bitmask=0xFFFF (all color2)
        // 0xFF000000: End
        byte[] compressed =
        [
            0x00, 0x01, 0x00, 0x00, // 1 block at (0, 0)
            5, 9, 0xFF, 0xFF,       // Block data
            0xFF, 0x00, 0x00, 0x00  // End command
        ];

        byte[] result = Decompressor.DecompressCccTransparent(compressed, 4, 4);

        Assert.Equal(16, result.Length);
        Assert.All(result, b => Assert.Equal(9, b));
    }
}
