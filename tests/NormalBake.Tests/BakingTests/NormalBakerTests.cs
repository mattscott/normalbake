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
    public void ObjectSpace_Sphere_ProducesRainbowNormals()
    {
        MeshPair pair = Primitives.Sphere();
        var opt = new NormalBakeOptions { Resolution = 128, Samples = 2, Space = NormalSpace.Object };
        BakedMap map = NormalBaker.Bake(pair.High, pair.Low, opt, out bool[] baked);

        Vector3 sum = Vector3.Zero;
        int n = 0;
        float minX = 1f;
        float maxX = -1f;
        float minZ = 1f;
        float maxZ = -1f;
        for (int i = 0; i < baked.Length; i++)
        {
            if (!baked[i])
            {
                continue;
            }

            Vector3 nrm = DecodedNormal(map, i);
            Assert.True(MathF.Abs(nrm.Length() - 1f) < 0.05f, $"object-space normal not unit: {nrm.Length()}");
            sum += nrm;
            n++;
            minX = MathF.Min(minX, nrm.X);
            maxX = MathF.Max(maxX, nrm.X);
            minZ = MathF.Min(minZ, nrm.Z);
            maxZ = MathF.Max(maxZ, nrm.Z);
        }

        // Radial object-space normals span the full sphere of directions and average to ~0 (not flat-blue).
        Assert.True((sum / n).Length() < 0.2f, $"object-space normals not symmetric: mean {(sum / n).Length()}");
        Assert.True(maxX - minX > 1.5f && maxZ - minZ > 1.5f, "object-space normals should span both signs");
    }

    [Fact]
    public void NearestPointFallback_FillsMissesWhereCageMisses()
    {
        // A high sphere shrunk to 0.8 sits 0.2 inside the low sphere; a tiny 0.05 cage can't reach it,
        // so every ray misses. The fallback must fill those texels from the nearest surface point.
        MeshPair pair = Primitives.Sphere();
        Mesh shrunk = ScalePositions(pair.High, 0.8f);

        BakedMap onMap = NormalBaker.Bake(shrunk, pair.Low, Cage(fallback: true), out bool[] bakedOn);
        BakedMap offMap = NormalBaker.Bake(shrunk, pair.Low, Cage(fallback: false), out bool[] bakedOff);
        _ = onMap;
        _ = offMap;

        int on = bakedOn.Count(b => b);
        int off = bakedOff.Count(b => b);
        Assert.True(off < 50, $"expected the tiny cage to miss almost everything, baked {off}");
        Assert.True(on > 1000, $"expected the nearest-point fallback to fill misses, baked {on}");
    }

    private static NormalBakeOptions Cage(bool fallback) => new()
    {
        Resolution = 128,
        Samples = 2,
        FrontalDistance = 0.05,
        RearDistance = 0.05,
        NearestPointFallback = fallback,
    };

    private static Mesh ScalePositions(Mesh mesh, float scale)
    {
        var positions = mesh.Positions.ToArray();
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] *= scale;
        }

        return new Mesh(positions, mesh.Normals.ToArray(), mesh.Indices.ToArray());
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

    [Fact]
    public void ApplyStrength_ScalesTangentTilt_AndStaysUnit()
    {
        Assert.Equal(new Vector3(0f, 0f, 1f), NormalEncoder.ApplyStrength(new Vector3(0f, 0f, 1f), 2f));

        Vector3 n = Vector3.Normalize(new Vector3(0.3f, 0f, 0.9f));
        Vector3 s = NormalEncoder.ApplyStrength(n, 2f);
        Assert.True(MathF.Abs(s.Length() - 1f) < 1e-4f);
        Assert.True(MathF.Abs(s.X - (n.X * 2f)) < 1e-4f); // X/Y doubled
        Assert.True(s.Z < n.Z);                           // steeper (smaller Z)

        Vector3 maxed = NormalEncoder.ApplyStrength(new Vector3(0.8f, 0f, 0.6f), 2f);
        Assert.True(maxed.Z < 1e-3f); // tilt clamps at the horizon
    }

    [Fact]
    public void NormalStrength_DeepensBakedNormals()
    {
        MeshPair pair = Primitives.Box();
        BakedMap weak = NormalBaker.Bake(pair.High, pair.Low, StrengthOptions(1f), out bool[] mask);
        BakedMap strong = NormalBaker.Bake(pair.High, pair.Low, StrengthOptions(2f), out _);

        Assert.True(MeanTilt(strong, mask) > MeanTilt(weak, mask) * 1.2f, "strength 2 should deepen the normals");
    }

    private static NormalBakeOptions StrengthOptions(float strength) =>
        new() { Resolution = 128, Samples = 2, NormalStrength = strength };

    private static float MeanTilt(BakedMap map, bool[] mask)
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i])
            {
                sum += 1f - DecodedNormal(map, i).Z; // 0 = flat, larger = more tilted
                n++;
            }
        }

        return n > 0 ? (float)(sum / n) : 0f;
    }

    private static Vector3 DecodedNormal(BakedMap map, int index)
    {
        (float r, float g, float b) = map.GetPixel(index % map.Width, index / map.Width);
        return NormalEncoder.Decode(new Vector3(r, g, b), NormalMapConvention.OpenGl);
    }
}
