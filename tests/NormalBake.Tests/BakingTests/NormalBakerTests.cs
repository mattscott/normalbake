using System.Numerics;
using NormalBake.Core;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>
/// P4 validation via the analytic oracle: a low/high pair that share a surface bakes to the identity
/// tangent-space normal ≈ (0,0,1) → RGB ≈ (0.5,0.5,1.0). Flat regions stay flat; the box's beveled
/// edges deviate (the bevel is captured). Plus determinism and no-holes. (Blender golden deferred.)
/// </summary>
public class NormalBakerTests
{
    private static NormalBakeOptions Options() => new() { Resolution = 128, Samples = 2 };

    [Fact]
    public void Sphere_BakesFlatBlue()
    {
        MeshPair pair = Primitives.Sphere();
        BakedMap map = NormalBaker.Bake(pair.High, pair.Low, Options(), out bool[] baked);

        int covered = 0;
        int flat = 0;
        for (int i = 0; i < baked.Length; i++)
        {
            if (!baked[i])
            {
                continue;
            }

            covered++;
            Vector3 nT = DecodedNormal(map, i);
            if (nT.Z > 0.9f && MathF.Sqrt((nT.X * nT.X) + (nT.Y * nT.Y)) < 0.2f)
            {
                flat++;
            }
        }

        Assert.True(covered > 1000, $"too few baked texels ({covered})");
        Assert.True(flat / (float)covered > 0.9f, $"only {flat}/{covered} texels were flat-blue");
    }

    [Fact]
    public void Box_FlatFacesBlue_BeveledEdgesDeviate()
    {
        MeshPair pair = Primitives.Box();
        BakedMap map = NormalBaker.Bake(pair.High, pair.Low, Options(), out bool[] baked);

        int flat = 0;
        int deviating = 0;
        for (int i = 0; i < baked.Length; i++)
        {
            if (!baked[i])
            {
                continue;
            }

            Vector3 nT = DecodedNormal(map, i);
            if (nT.Z > 0.97f)
            {
                flat++;
            }
            else if (nT.Z < 0.9f)
            {
                deviating++;
            }
        }

        Assert.True(flat > 200, $"expected flat-blue face interiors, got {flat}");
        Assert.True(deviating > 50, $"expected beveled edges to deviate, got {deviating}");
    }

    [Fact]
    public void Cylinder_BakesWithoutCrashing_AndHasFlatSide()
    {
        MeshPair pair = Primitives.Cylinder();
        BakedMap map = NormalBaker.Bake(pair.High, pair.Low, Options(), out bool[] baked);

        int flat = 0;
        for (int i = 0; i < baked.Length; i++)
        {
            if (baked[i] && DecodedNormal(map, i).Z > 0.95f)
            {
                flat++;
            }
        }

        Assert.True(flat > 200, $"expected a flat-blue cylinder side, got {flat}");
    }

    [Fact]
    public void Bake_IsDeterministic()
    {
        MeshPair pair = Primitives.Box();
        BakedMap a = NormalBaker.Bake(pair.High, pair.Low, Options());
        BakedMap b = NormalBaker.Bake(pair.High, pair.Low, Options());

        Assert.True(a.Pixels.SequenceEqual(b.Pixels));
    }

    [Fact]
    public void Bake_DilatesBackground_NoHoles()
    {
        MeshPair pair = Primitives.Box();
        BakedMap map = NormalBaker.Bake(pair.High, pair.Low, Options());

        // After Extend dilation every texel is filled; an unfilled background texel would be exactly zero
        // (no unit normal encodes to (0,0,0)).
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                (float r, float g, float b) = map.GetPixel(x, y);
                Assert.False(r == 0f && g == 0f && b == 0f, $"hole at ({x},{y})");
            }
        }
    }

    [Fact]
    public void DirectXConvention_FlipsGreenRelativeToOpenGl()
    {
        MeshPair pair = Primitives.Box();
        BakedMap gl = NormalBaker.Bake(pair.High, pair.Low, new NormalBakeOptions { Resolution = 64, Samples = 1 });
        BakedMap dx = NormalBaker.Bake(
            pair.High,
            pair.Low,
            new NormalBakeOptions { Resolution = 64, Samples = 1, Convention = NormalMapConvention.DirectX });

        // Green channel is mirrored about 0.5; red/blue identical.
        bool anyGreenDiff = false;
        for (int y = 0; y < gl.Height; y++)
        {
            for (int x = 0; x < gl.Width; x++)
            {
                (float gr, float gg, float gb) = gl.GetPixel(x, y);
                (float dr, float dg, float db) = dx.GetPixel(x, y);
                Assert.True(MathF.Abs(gr - dr) < 1e-6f);
                Assert.True(MathF.Abs(gb - db) < 1e-6f);
                Assert.True(MathF.Abs((gg - 0.5f) + (dg - 0.5f)) < 1e-6f); // gg-0.5 == -(dg-0.5)
                if (MathF.Abs(gg - dg) > 1e-3f)
                {
                    anyGreenDiff = true;
                }
            }
        }

        Assert.True(anyGreenDiff, "expected some texels where green actually differs");
    }

    private static Vector3 DecodedNormal(BakedMap map, int index)
    {
        (float r, float g, float b) = map.GetPixel(index % map.Width, index / map.Width);
        return NormalEncoder.Decode(new Vector3(r, g, b), NormalMapConvention.OpenGl);
    }
}
