using MsPoint = MediaStation.Unpack.Core.Point;

namespace MediaStation.Unpack.Graphics;

public sealed class MovieFrameItem
{
    public required PixMapImage Pixmap { get; init; }
    public required MsPoint Origin { get; init; }
    public required uint StartTimeMs { get; init; }
    public required uint EndTimeMs { get; init; }
    public int ZIndex { get; init; }
    public int Order { get; init; }
}
