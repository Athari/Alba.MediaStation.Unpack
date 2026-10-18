namespace MediaStation.Unpack.Graphics;

public enum GraphicFormat
{
    Png,
    Webp,
    Gif,
    JsonAnimate
}

public static class GraphicFormatExtensions
{
    public static string GetExtension(this GraphicFormat format) => format switch
    {
        GraphicFormat.Png => "png",
        GraphicFormat.Webp => "webp",
        GraphicFormat.Gif => "gif",
        GraphicFormat.JsonAnimate => "json",
        _ => "png"
    };
}
