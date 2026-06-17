using System.Numerics;
using NormalBake.Core;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>P5 match-by-name isolation: each part bakes against only its own high-poly (no bleed).</summary>
public class MultiPartTests
{
    [Fact]
    public void MatchByName_PairsLowAndHighBaseNames()
    {
        var highs = new Dictionary<string, Mesh>
        {
            ["crate_high"] = Primitives.Box().High,
            ["ball_high"] = Primitives.Sphere().High,
        };
        var lows = new Dictionary<string, UvMesh>
        {
            ["crate_low"] = Primitives.Box().Low,
            ["ball_low"] = Primitives.Sphere().Low,
        };

        IReadOnlyList<BakePart> parts = NormalBaker.MatchByName(highs, lows);

        Assert.Equal(2, parts.Count);
        Assert.Contains(parts, p => p.Name == "crate");
        Assert.Contains(parts, p => p.Name == "ball");
    }

    [Fact]
    public void BakeParts_IsolatesEachPart_FromOverlappingGeometry()
    {
        // A sphere and a box, both at the origin (heavily overlapping in 3D — a shared BVH would bleed),
        // packed into non-overlapping UV halves. Each part's baked texels must equal a solo bake of that
        // part, proving the other part's high-poly never contributed.
        var opt = new NormalBakeOptions { Resolution = 128, Samples = 2 };
        UvMesh sphereLow = RemapUvs(Primitives.Sphere().Low, 0.46f, new Vector2(0.02f, 0.02f));
        UvMesh boxLow = RemapUvs(Primitives.Box().Low, 0.46f, new Vector2(0.52f, 0.02f));
        Mesh sphereHigh = Primitives.Sphere().High;
        Mesh boxHigh = Primitives.Box().High;

        var parts = new List<BakePart>
        {
            new("sphere", sphereHigh, sphereLow),
            new("box", boxHigh, boxLow),
        };
        BakedMap composite = NormalBaker.BakeParts(parts, opt);

        BakedMap soloSphere = NormalBaker.Bake(sphereHigh, sphereLow, opt, out bool[] sphereMask);
        BakedMap soloBox = NormalBaker.Bake(boxHigh, boxLow, opt, out bool[] boxMask);

        AssertBakedTexelsMatch(composite, soloSphere, sphereMask);
        AssertBakedTexelsMatch(composite, soloBox, boxMask);
    }

    [Fact]
    public void UdimTileOffset_BakesShellInTile1_LikeTile0()
    {
        MeshPair sp = Primitives.Sphere();
        var opt0 = new NormalBakeOptions { Resolution = 96, Samples = 2 };
        BakedMap map0 = NormalBaker.Bake(sp.High, sp.Low, opt0, out bool[] mask0);

        // The same shell shifted one tile right in U, baked with TileU=1, must reproduce the tile-0 bake.
        UvMesh shifted = RemapUvs(Primitives.Sphere().Low, 1f, new Vector2(1f, 0f));
        var opt1 = new NormalBakeOptions { Resolution = 96, Samples = 2, TileU = 1 };
        BakedMap map1 = NormalBaker.Bake(sp.High, shifted, opt1, out bool[] mask1);

        Assert.Equal(mask0, mask1);

        // Shifting U by a whole tile and back is not bit-exact in float (x+1−1 ≠ x), so a few edge texels
        // shift sub-samples; assert the images are essentially identical (>95% within a tight tolerance).
        int baked = 0;
        int close = 0;
        for (int i = 0; i < mask0.Length; i++)
        {
            if (!mask0[i])
            {
                continue;
            }

            baked++;
            (float r0, float g0, float b0) = map0.GetPixel(i % map0.Width, i / map0.Width);
            (float r1, float g1, float b1) = map1.GetPixel(i % map1.Width, i / map1.Width);
            float d = MathF.Max(MathF.Abs(r0 - r1), MathF.Max(MathF.Abs(g0 - g1), MathF.Abs(b0 - b1)));
            if (d < 0.03f)
            {
                close++;
            }
        }

        Assert.True(close / (float)baked > 0.95f, $"UDIM bake diverged: only {close}/{baked} texels matched");
    }

    private static void AssertBakedTexelsMatch(BakedMap composite, BakedMap solo, bool[] mask)
    {
        int compared = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
            {
                continue;
            }

            (float cr, float cg, float cb) = composite.GetPixel(i % composite.Width, i / composite.Width);
            (float sr, float sg, float sb) = solo.GetPixel(i % solo.Width, i / solo.Width);
            Assert.True(cr == sr && cg == sg && cb == sb, $"part bled at texel {i}");
            compared++;
        }

        Assert.True(compared > 500, $"too few baked texels compared ({compared})");
    }

    private static UvMesh RemapUvs(UvMesh mesh, float scale, Vector2 offset)
    {
        var uvs = mesh.Uvs.ToArray();
        for (int i = 0; i < uvs.Length; i++)
        {
            uvs[i] = (uvs[i] * scale) + offset;
        }

        return new UvMesh(mesh.Positions.ToArray(), mesh.Normals.ToArray(), uvs, mesh.Indices.ToArray());
    }
}
