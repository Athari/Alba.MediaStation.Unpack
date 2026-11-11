using MediaStation.Unpack.Actors;
using MediaStation.Unpack.Core;
using MediaStation.Unpack.Graphics;
using MediaStation.Unpack.Metadata;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Export;

public class MovieDeoptimizerTests
{
    [Fact]
    public void Deoptimizer_UnsharesFramesAndClearsBaseImage()
    {
        var movie = new StreamMovieActor
        {
            Id = 1,
            Name = "movie_TestUnshare"
        };

        // Shared base image: 20x20, all pixels = 1
        byte[] basePixels = new byte[20 * 20];
        Array.Fill(basePixels, (byte)1);
        var basePixMap = new PixMapImage(new ImageInfo(20, 20), basePixels, transparentColorIndex: 0);

        // Dependent head 1: 10x10, all pixels = 2
        byte[] head1Pixels = new byte[10 * 10];
        Array.Fill(head1Pixels, (byte)2);
        var head1PixMap = new PixMapImage(new ImageInfo(10, 10), head1Pixels, transparentColorIndex: 0);

        // Dependent head 2: 10x10, all pixels = 3
        byte[] head2Pixels = new byte[10 * 10];
        Array.Fill(head2Pixels, (byte)3);
        var head2PixMap = new PixMapImage(new ImageInfo(10, 10), head2Pixels, transparentColorIndex: 0);

        movie.Images.Add(new MovieImageData(1, 200, basePixMap));
        movie.Images.Add(new MovieImageData(2, 100, head1PixMap));
        movie.Images.Add(new MovieImageData(3, 200, head2PixMap));

        // Frame 0: Base image spanning 0..200 ms at (10, 10)
        movie.Frames.Add(new MovieFrame
        {
            Index = 1,
            StartInMilliseconds = 0,
            EndInMilliseconds = 200,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(10, 10)
        });

        // Frame 1: Head 1 spanning 0..100 ms at (15, 5)
        movie.Frames.Add(new MovieFrame
        {
            Index = 2,
            StartInMilliseconds = 0,
            EndInMilliseconds = 100,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(15, 5)
        });

        // Frame 2: Head 2 spanning 100..200 ms at (15, 6)
        movie.Frames.Add(new MovieFrame
        {
            Index = 3,
            StartInMilliseconds = 100,
            EndInMilliseconds = 200,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(15, 6)
        });

        AnimateJsonExporter.DeoptimizeMovieSharedImages(movie);

        // 1. Shared base image must be cleared (fully transparent, original 20x20 size)
        var clearedBase = movie.Images.First(i => i.Index == 1).Image;
        Assert.Equal(20, clearedBase.Width);
        Assert.Equal(20, clearedBase.Height);
        Assert.All(clearedBase.Pixels, p => Assert.Equal(0, p));

        // 2. Dependent image 2 must be merged with base image:
        // Union: min(10, 15)=10, min(10, 5)=5, max(10+20, 15+10)=30, max(10+20, 5+10)=30 => 20x25
        var mergedHead1 = movie.Images.First(i => i.Index == 2).Image;
        Assert.Equal(20, mergedHead1.Width);
        Assert.Equal(25, mergedHead1.Height);
        var frameHead1 = movie.Frames.First(f => f.Index == 2);
        Assert.Equal(new Point(10, 5), frameHead1.LeftTop);

        // Verify head 1 pixels at local offset (5, 0) within merged canvas
        Assert.Equal(2, mergedHead1.Pixels[0 * 20 + 5]); // head pixel
        Assert.Equal(1, mergedHead1.Pixels[5 * 20 + 0]); // base body pixel

        // 3. Dependent image 3 must be merged with base image:
        // Union: min(10, 15)=10, min(10, 6)=6, max(30, 25)=30, max(30, 16)=30 => 20x24
        var mergedHead2 = movie.Images.First(i => i.Index == 3).Image;
        Assert.Equal(20, mergedHead2.Width);
        Assert.Equal(24, mergedHead2.Height);
        var frameHead2 = movie.Frames.First(f => f.Index == 3);
        Assert.Equal(new Point(10, 6), frameHead2.LeftTop);
        Assert.Equal(3, mergedHead2.Pixels[0 * 20 + 5]); // head pixel
    }

    [Fact]
    public void Deoptimizer_PreservesImagesThatAppearInIsolation()
    {
        var movie = new StreamMovieActor
        {
            Id = 2,
            Name = "movie_IsolatedTest"
        };

        byte[] p1 = new byte[10 * 10];
        Array.Fill(p1, (byte)5);
        var pix1 = new PixMapImage(new ImageInfo(10, 10), p1, transparentColorIndex: 0);

        byte[] p2 = new byte[10 * 10];
        Array.Fill(p2, (byte)6);
        var pix2 = new PixMapImage(new ImageInfo(10, 10), p2, transparentColorIndex: 0);

        movie.Images.Add(new MovieImageData(1, 200, pix1));
        movie.Images.Add(new MovieImageData(2, 200, pix2));

        // Image 1 active alone during 0..100 ms
        movie.Frames.Add(new MovieFrame
        {
            Index = 1,
            StartInMilliseconds = 0,
            EndInMilliseconds = 100,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(0, 0)
        });

        // Image 1 and Image 2 active together during 100..200 ms
        movie.Frames.Add(new MovieFrame
        {
            Index = 1,
            StartInMilliseconds = 100,
            EndInMilliseconds = 200,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(0, 0)
        });
        movie.Frames.Add(new MovieFrame
        {
            Index = 2,
            StartInMilliseconds = 100,
            EndInMilliseconds = 200,
            LayerId = 0,
            ZIndex = 0,
            LeftTop = new Point(5, 5)
        });

        AnimateJsonExporter.DeoptimizeMovieSharedImages(movie);

        // Image 1 appeared in isolation during 0..100 ms, so it must NOT be cleared!
        var img1 = movie.Images.First(i => i.Index == 1).Image;
        Assert.Contains(img1.Pixels, p => p == 5);
    }
}
