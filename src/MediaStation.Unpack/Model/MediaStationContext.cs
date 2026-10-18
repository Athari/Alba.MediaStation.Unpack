using MediaStation.Unpack.Actors;
using MediaStation.Unpack.Graphics;

namespace MediaStation.Unpack.Model;

public class MediaStationContext
{
    public ushort ContextId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Dictionary<uint, Actor> Actors { get; } = new();
    public Palette Palette { get; set; } = new();

    public IEnumerable<ImageActor> Images => Actors.Values.OfType<ImageActor>();
    public IEnumerable<DiskImageActor> DiskImages => Actors.Values.OfType<DiskImageActor>();
    public IEnumerable<SpriteMovieActor> Sprites => Actors.Values.OfType<SpriteMovieActor>();
    public IEnumerable<StreamMovieActor> Movies => Actors.Values.OfType<StreamMovieActor>();
    public IEnumerable<SoundActor> Sounds => Actors.Values.OfType<SoundActor>();
    public IEnumerable<PaletteActor> Palettes => Actors.Values.OfType<PaletteActor>();

    public Palette ResolveActivePalette()
    {
        // 1. If context has an explicit screen palette loaded via 0x05AA (kVideoDisplayManagerLoadPalette),
        // that is the primary screen palette for all standard image rendering in the context.
        if (Palette.IsInitialized)
            return Palette;

        // 2. If a PaletteActor has "default" or "screen" in its name, prefer that.
        var defaultPal = Palettes.FirstOrDefault(p => p.Palette != null &&
            (p.Name.Contains("default", StringComparison.OrdinalIgnoreCase) ||
             p.Name.Contains("screen", StringComparison.OrdinalIgnoreCase)));
        if (defaultPal?.Palette != null)
            return defaultPal.Palette;

        // 3. Fallback to any PaletteActor with a palette.
        var anyPal = Palettes.FirstOrDefault(p => p.Palette != null);
        if (anyPal?.Palette != null)
            return anyPal.Palette;

        // 4. Return context's registered palette (or default).
        return Palette;
    }
}
