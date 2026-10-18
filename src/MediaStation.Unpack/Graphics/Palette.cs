using SixLabors.ImageSharp.PixelFormats;

namespace MediaStation.Unpack.Graphics;

public class Palette
{
    public const int ColorCount = 256;
    public const int SizeInBytes = ColorCount * 3;

    public bool IsInitialized { get; }

    private readonly Rgba32[] _colors = new Rgba32[ColorCount];

    public Palette()
    {
        IsInitialized = false;
        // Default grayscale palette
        for (int i = 0; i < ColorCount; i++)
        {
            byte val = (byte)i;
            _colors[i] = new Rgba32(val, val, val, 255);
        }
    }

    public Palette(ReadOnlySpan<byte> rgbBytes)
    {
        IsInitialized = true;
        int count = Math.Min(ColorCount, rgbBytes.Length / 3);
        for (int i = 0; i < count; i++)
        {
            byte r = rgbBytes[i * 3 + 0];
            byte g = rgbBytes[i * 3 + 1];
            byte b = rgbBytes[i * 3 + 2];
            _colors[i] = new Rgba32(r, g, b, 255);
        }

        // Fill remainder if any
        for (int i = count; i < ColorCount; i++)
        {
            _colors[i] = new Rgba32(0, 0, 0, 255);
        }
    }

    public Rgba32 this[int index]
    {
        get
        {
            if (index < 0 || index >= ColorCount)
                return new Rgba32(0, 0, 0, 0);
            return _colors[index];
        }
        set
        {
            if (index >= 0 && index < ColorCount)
                _colors[index] = value;
        }
    }

    public Rgba32 GetColor(byte index, byte? transparentIndex = null)
    {
        if (transparentIndex.HasValue && index == transparentIndex.Value)
            return new Rgba32(0, 0, 0, 0);

        return _colors[index];
    }
}
