namespace MediaStation.Unpack.Core;

public enum DatumType : ushort
{
    Empty = 0x00,
    Uint8 = 0x02,
    Uint16 = 0x03,
    Uint32 = 0x04,
    Int8 = 0x05,
    Int16 = 0x06,
    Int32 = 0x07,
    Float = 0x08,
    Double = 0x09,
    Filename = 0x0A,
    Rect = 0x0D,
    Point = 0x0E,
    GraphicSize = 0x0F,
    GraphicUnit = 0x10,
    Time = 0x11,
    String = 0x12,
    Version = 0x13,
    ChannelIdent = 0x1B,
    Polygon = 0x1D,
}
