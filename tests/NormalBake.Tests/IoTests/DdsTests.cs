using System.Text;
using NormalBake.Core;
using NormalBake.IO;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>P5: BC5/BC7 DDS encode → decode round-trips a baked normal map within tolerance.</summary>
public class DdsTests
{
    private static BakedMap BakeBox() =>
        NormalBaker.Bake(Primitives.Box().High, Primitives.Box().Low, new NormalBakeOptions
        {
            Resolution = 128,
            Samples = 2,
        });

    [Fact]
    public void Bc5_RoundTripsXYWithinTolerance()
    {
        BakedMap baked = BakeBox();

        using var ms = new MemoryStream();
        DdsWriter.Write(ms, baked, BlockCompression.Bc5);
        byte[] dds = ms.ToArray();
        Assert.Equal("DDS ", Encoding.ASCII.GetString(dds, 0, 4));

        ms.Position = 0;
        BakedMap decoded = DdsReader.Read(ms);
        Assert.Equal(baked.Width, decoded.Width);
        Assert.Equal(baked.Height, decoded.Height);

        (double meanR, double meanG, _) = MeanAbsDiff(baked, decoded);
        Assert.True(meanR < 0.02 && meanG < 0.02, $"BC5 error too high: R={meanR:0.0000} G={meanG:0.0000}");
    }

    [Fact]
    public void Bc7_RoundTripsRgbWithinTolerance()
    {
        BakedMap baked = BakeBox();

        using var ms = new MemoryStream();
        DdsWriter.Write(ms, baked, BlockCompression.Bc7);
        ms.Position = 0;
        BakedMap decoded = DdsReader.Read(ms);

        (double meanR, double meanG, double meanB) = MeanAbsDiff(baked, decoded);
        Assert.True(
            meanR < 0.02 && meanG < 0.02 && meanB < 0.02,
            $"BC7 error too high: R={meanR:0.0000} G={meanG:0.0000} B={meanB:0.0000}");
    }

    private static (double R, double G, double B) MeanAbsDiff(BakedMap a, BakedMap b)
    {
        double sr = 0;
        double sg = 0;
        double sb = 0;
        int n = a.Width * a.Height;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                (float ar, float ag, float ab) = a.GetPixel(x, y);
                (float br, float bg, float bb) = b.GetPixel(x, y);
                sr += Math.Abs(ar - br);
                sg += Math.Abs(ag - bg);
                sb += Math.Abs(ab - bb);
            }
        }

        return (sr / n, sg / n, sb / n);
    }
}
