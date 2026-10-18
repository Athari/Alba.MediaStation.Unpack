using System.Buffers.Binary;

namespace MediaStation.Unpack.Audio;

public static class ImaAdpcmDecoder
{
    private static readonly short[] StepAdjustTable =
    [
        -1, -1, -1, -1, 2, 4, 6, 8,
        -1, -1, -1, -1, 2, 4, 6, 8
    ];

    private static readonly short[] ImaTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14,
        16, 17, 19, 21, 23, 25, 28, 31,
        34, 37, 41, 45, 50, 55, 60, 66,
        73, 80, 88, 97, 107, 118, 130, 143,
        157, 173, 190, 209, 230, 253, 279, 307,
        337, 371, 408, 449, 494, 544, 598, 658,
        724, 796, 876, 963, 1060, 1166, 1282, 1411,
        1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
        3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
        7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
        15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
        32767
    ];

    public static byte[] DecodeDviIma(ReadOnlySpan<byte> input, int blockAlign = 8)
    {
        // Each block of 8 bytes produces 12 samples of 16-bit PCM = 24 bytes.
        // We write directly into a MemoryStream.
        using var output = new MemoryStream();
        int offset = 0;
        int inputLength = input.Length;

        int last = 0;
        int stepIndex = 0;
        Span<byte> sampleBytes = stackalloc byte[2];

        while (offset < inputLength)
        {
            int blockSize = Math.Min(blockAlign, inputLength - offset);
            if (blockSize < 2)
                break;

            // 2-byte header
            ushort header = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(offset, 2));
            last = (short)(header & 0xFF80);
            stepIndex = Math.Clamp(header & 0x007F, 0, ImaTable.Length - 1);
            offset += 2;

            int remainingInBlock = blockSize - 2;
            for (int b = 0; b < remainingInBlock; b++)
            {
                byte data = input[offset++];
                // Low nibble first
                short s1 = DecodeSample((byte)(data & 0x0F), ref last, ref stepIndex);
                BinaryPrimitives.WriteInt16LittleEndian(sampleBytes, s1);
                output.Write(sampleBytes);

                // High nibble second
                short s2 = DecodeSample((byte)(data >> 4), ref last, ref stepIndex);
                BinaryPrimitives.WriteInt16LittleEndian(sampleBytes, s2);
                output.Write(sampleBytes);
            }
        }

        return output.ToArray();
    }

    private static short DecodeSample(byte code, ref int last, ref int stepIndex)
    {
        int step = ImaTable[stepIndex];
        int e = ((2 * (code & 0x07) + 1) * step) >> 3;
        int diff = (code & 0x08) != 0 ? -e : e;
        int sample = Math.Clamp(last + diff, -32768, 32767);

        last = sample;
        stepIndex += StepAdjustTable[code & 0x0F];
        stepIndex = Math.Clamp(stepIndex, 0, ImaTable.Length - 1);

        return (short)sample;
    }
}
