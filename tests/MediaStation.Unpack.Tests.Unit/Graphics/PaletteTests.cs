using MediaStation.Unpack.Actors;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Model;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Graphics;

public class PaletteTests
{
    [Fact]
    public void Palette_ResolvesColorsAndTransparency()
    {
        byte[] rgb = new byte[256 * 3];
        rgb[0] = 255; rgb[1] = 0; rgb[2] = 0; // index 0: Red
        rgb[3] = 0; rgb[4] = 255; rgb[5] = 0; // index 1: Green

        var palette = new Palette(rgb);
        var color0 = palette.GetColor(0);
        Assert.Equal(255, color0.R);
        Assert.Equal(0, color0.G);
        Assert.Equal(0, color0.B);
        Assert.Equal(255, color0.A);

        var transColor = palette.GetColor(0, transparentIndex: 0);
        Assert.Equal(0, transColor.A); // Transparent
    }

    [Fact]
    public void ResolveActivePalette_PrioritizesInitializedContextPaletteOverPaletteActors()
    {
        var context = new MediaStationContext();
        byte[] dummyBytes1 = new byte[Palette.SizeInBytes];
        dummyBytes1[0] = 111; // R of color 0
        var palActor = new PaletteActor();
        palActor.Name = "palette_cutscene";
        typeof(PaletteActor).GetProperty("Palette")!.SetValue(palActor, new Palette(dummyBytes1));
        context.Actors[1] = palActor;

        byte[] dummyBytes2 = new byte[Palette.SizeInBytes];
        dummyBytes2[0] = 222; // R of color 0
        context.Palette = new Palette(dummyBytes2);

        var resolved = context.ResolveActivePalette();
        Assert.Same(context.Palette, resolved);
        Assert.Equal(222, resolved[0].R);
    }

    [Fact]
    public void ResolveActivePalette_PrefersDefaultScreenPaletteActorWhenContextPaletteNotInitialized()
    {
        var context = new MediaStationContext(); // context.Palette is uninitialized

        byte[] cutsceneBytes = new byte[Palette.SizeInBytes];
        cutsceneBytes[0] = 50;
        var cutsceneActor = new PaletteActor { Name = "palette_newborn" };
        typeof(PaletteActor).GetProperty("Palette")!.SetValue(cutsceneActor, new Palette(cutsceneBytes));
        context.Actors[1] = cutsceneActor;

        byte[] defaultBytes = new byte[Palette.SizeInBytes];
        defaultBytes[0] = 100;
        var defaultActor = new PaletteActor { Name = "palette_defaultScreen" };
        typeof(PaletteActor).GetProperty("Palette")!.SetValue(defaultActor, new Palette(defaultBytes));
        context.Actors[2] = defaultActor;

        var resolved = context.ResolveActivePalette();
        Assert.Same(defaultActor.Palette, resolved);
        Assert.Equal(100, resolved[0].R);
    }

    [Fact]
    public void CreatePaletteQuantizer_ThrowsWhenColorsExceed256()
    {
        // Default palette has 256 distinct grayscale colors (0..255)
        var palette = new Palette();

        // With transparent: true and transparentIndex: null, adding Color.Transparent results in 257 distinct colors
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            ImageExporter.CreatePaletteQuantizer(palette, transparentIndex: null, transparent: true);
        });

        Assert.Contains("256", ex.Message);
    }
}
