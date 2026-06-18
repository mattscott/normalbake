using System.Numerics;
using NormalBake.Core;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>P6 multi-map baker: position, AO, object ID, curvature — analytic substitute oracles.</summary>
public class MapTypeTests
{
    [Fact]
    public void Position_Sphere_ReconstructsRadialPositions()
    {
        MeshPair sp = Primitives.Sphere();
        var opt = new NormalBakeOptions { Resolution = 96, Samples = 2, Map = MapType.Position };
        BakedMap map = NormalBaker.Bake(sp.High, sp.Low, opt, out bool[] baked);

        int onSphere = 0;
        int total = 0;
        float minX = 1f;
        float maxX = 0f;
        for (int i = 0; i < baked.Length; i++)
        {
            if (!baked[i])
            {
                continue;
            }

            (float r, float g, float b) = map.GetPixel(i % map.Width, i / map.Width);
            Vector3 pos = (new Vector3(r, g, b) * 2f) - Vector3.One; // bounds [-1,1]³ → un-normalize
            if (MathF.Abs(pos.Length() - 1f) < 0.1f)
            {
                onSphere++;
            }

            minX = MathF.Min(minX, r);
            maxX = MathF.Max(maxX, r);
            total++;
        }

        Assert.True(onSphere / (float)total > 0.9f, $"only {onSphere}/{total} positions on the sphere");
        Assert.True(maxX - minX > 0.8f, "position channel should span the bounds");
    }

    [Fact]
    public void Height_LevelOnFlatFaces_RecessedAtBeveledEdges()
    {
        MeshPair pair = Primitives.Box();
        var opt = new NormalBakeOptions { Resolution = 128, Samples = 2, Map = MapType.Height };
        BakedMap map = NormalBaker.Bake(pair.High, pair.Low, opt, out bool[] baked);

        int level = 0;
        int recessed = 0;
        for (int i = 0; i < baked.Length; i++)
        {
            if (!baked[i])
            {
                continue;
            }

            float h = map.GetPixel(i % map.Width, i / map.Width).R;
            if (MathF.Abs(h - 0.5f) < 0.02f)
            {
                level++; // flat faces coincide → 0.5
            }
            else if (h < 0.45f)
            {
                recessed++; // rounded-in edges sit below the flat low-poly
            }
        }

        Assert.True(level > 200, $"expected level (0.5) flat faces, got {level}");
        Assert.True(recessed > 20, $"expected recessed beveled edges, got {recessed}");
    }

    [Fact]
    public void AmbientOcclusion_ConvexIsOpen_OccluderDarkens()
    {
        MeshPair sp = Primitives.Sphere();
        var opt = new NormalBakeOptions
        {
            Resolution = 80,
            Samples = 1,
            Map = MapType.AmbientOcclusion,
            OcclusionSamples = 24,
        };

        BakedMap lone = NormalBaker.Bake(sp.High, sp.Low, opt, out bool[] loneMask);
        Mesh occluded = Merge(sp.High, sp.High, new Vector3(1.6f, 0f, 0f));
        BakedMap withOcc = NormalBaker.Bake(occluded, sp.Low, opt, out bool[] occMask);

        Assert.True(MeanGray(lone, loneMask) > 0.85f, "a lone convex sphere should be mostly open");
        Assert.True(MinGray(withOcc, occMask) < 0.75f, "a neighbouring occluder should darken some texels");
        Assert.True(MeanGray(withOcc, occMask) < MeanGray(lone, loneMask), "occlusion should lower mean AO");
    }

    [Fact]
    public void ObjectId_TwoParts_AreTwoFlatColors()
    {
        var opt = new NormalBakeOptions { Resolution = 96, Samples = 2, Map = MapType.ObjectId };
        var parts = new List<BakePart>
        {
            new("sphere", Primitives.Sphere().High, RemapUvs(Primitives.Sphere().Low, 0.46f, new Vector2(0.02f, 0.02f))),
            new("box", Primitives.Box().High, RemapUvs(Primitives.Box().Low, 0.46f, new Vector2(0.52f, 0.02f))),
        };
        BakedMap map = NormalBaker.BakeParts(parts, opt);

        var colors = new HashSet<int>();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                (float r, float g, float b) = map.GetPixel(x, y);
                colors.Add((Q(r) << 16) | (Q(g) << 8) | Q(b));
            }
        }

        Assert.InRange(colors.Count, 2, 3); // two flat IDs (no antialiasing), maybe one boundary stray
    }

    [Fact]
    public void Curvature_FlatFacesNeutral_EdgesDeviate()
    {
        var opt = new NormalBakeOptions { Resolution = 128, Samples = 2, Map = MapType.Curvature };
        BakedMap box = NormalBaker.Bake(Primitives.Box().High, Primitives.Box().Low, opt, out bool[] boxMask);

        int flat = 0;
        int edge = 0;
        for (int i = 0; i < boxMask.Length; i++)
        {
            if (!boxMask[i])
            {
                continue;
            }

            float c = box.GetPixel(i % box.Width, i / box.Width).R;
            if (MathF.Abs(c - 0.5f) < 0.04f)
            {
                flat++;
            }
            else if (MathF.Abs(c - 0.5f) > 0.12f)
            {
                edge++;
            }
        }

        Assert.True(flat > 200, $"box flat faces should be ≈0.5, got {flat}");
        Assert.True(edge > 20, $"box edges should spike, got {edge}");
    }

    [Fact]
    public void Mask_IsWhiteOnSurface_WithBlackUndilatedBackground()
    {
        // The box has per-face islands with gutters, so there is real background between them.
        MeshPair pair = Primitives.Box();
        var opt = new NormalBakeOptions { Resolution = 128, Samples = 2, Map = MapType.Mask };
        BakedMap map = NormalBaker.Bake(pair.High, pair.Low, opt, out bool[] baked);

        int white = 0;
        int black = 0;
        for (int i = 0; i < baked.Length; i++)
        {
            (float r, float g, float b) = map.GetPixel(i % map.Width, i / map.Width);
            if (baked[i])
            {
                Assert.True(r == 1f && g == 1f && b == 1f, $"baked texel {i} not white");
                white++;
            }
            else if (r == 0f && g == 0f && b == 0f)
            {
                black++;
            }
        }

        Assert.True(white > 200, $"expected a white surface, got {white}");
        Assert.True(black > 200, "expected a black, un-dilated background");
        Assert.Equal(baked.Count(b => b), white); // white exactly matches the coverage mask
    }

    [Fact]
    public void Blur_ReducesNeighbourVariance_WithinCoverage()
    {
        const int res = 32;
        var map = new BakedMap(res, res);
        var covered = new bool[res * res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                covered[(y * res) + x] = true;
                float v = (x + y) % 2 == 0 ? 1f : 0f; // worst-case high-frequency checkerboard
                map.SetPixel(x, y, v, v, v);
            }
        }

        float before = NeighbourRoughness(map, covered);
        Blur.Apply(map, covered, radius: 2);
        float after = NeighbourRoughness(map, covered);

        Assert.True(after < before * 0.5f, $"blur should smooth ({before:0.###} -> {after:0.###})");
    }

    private static float NeighbourRoughness(BakedMap map, bool[] mask)
    {
        double sum = 0;
        int n = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x + 1 < map.Width; x++)
            {
                int i = (y * map.Width) + x;
                if (mask[i] && mask[i + 1])
                {
                    sum += Math.Abs(map.GetPixel(x, y).R - map.GetPixel(x + 1, y).R);
                    n++;
                }
            }
        }

        return n > 0 ? (float)(sum / n) : 0f;
    }

    [Fact]
    public void Coverage_HasAntiAliasedEdges()
    {
        MeshPair pair = Primitives.Box();
        var opt = new NormalBakeOptions { Resolution = 128, Samples = 4, Map = MapType.Coverage };
        BakedMap map = NormalBaker.Bake(pair.High, pair.Low, opt);

        int full = 0;
        int partial = 0;
        int black = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                float g = map.GetPixel(x, y).R;
                if (g >= 0.999f)
                {
                    full++;
                }
                else if (g is > 0.01f and < 0.99f)
                {
                    partial++; // anti-aliased edge — impossible for the binary Mask
                }
                else if (g == 0f)
                {
                    black++;
                }
            }
        }

        Assert.True(full > 200, $"expected solid interior, got {full}");
        Assert.True(partial > 50, $"expected anti-aliased gray edges, got {partial}");
        Assert.True(black > 200, "expected a black background");
    }

    [Theory]
    [InlineData(MapType.Normal)]
    [InlineData(MapType.Position)]
    [InlineData(MapType.Height)]
    [InlineData(MapType.AmbientOcclusion)]
    [InlineData(MapType.Thickness)]
    [InlineData(MapType.Curvature)]
    public void Bake_IsDeterministic_PerMap(MapType mapType)
    {
        MeshPair sp = Primitives.Sphere();
        var opt = new NormalBakeOptions { Resolution = 64, Samples = 1, Map = mapType, OcclusionSamples = 16 };
        BakedMap a = NormalBaker.Bake(sp.High, sp.Low, opt);
        BakedMap b = NormalBaker.Bake(sp.High, sp.Low, opt);
        Assert.True(a.Pixels.SequenceEqual(b.Pixels));
    }

    private static int Q(float v) => (int)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

    private static float MeanGray(BakedMap map, bool[] mask)
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i])
            {
                sum += map.GetPixel(i % map.Width, i / map.Width).R;
                n++;
            }
        }

        return (float)(sum / n);
    }

    private static float MinGray(BakedMap map, bool[] mask)
    {
        float min = 1f;
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i])
            {
                min = MathF.Min(min, map.GetPixel(i % map.Width, i / map.Width).R);
            }
        }

        return min;
    }

    private static Mesh Merge(Mesh a, Mesh b, Vector3 offset)
    {
        var positions = new List<Vector3>(a.Positions.ToArray());
        var normals = new List<Vector3>(a.Normals.ToArray());
        var indices = new List<int>(a.Indices.ToArray());
        int baseIndex = a.VertexCount;
        foreach (Vector3 p in b.Positions)
        {
            positions.Add(p + offset);
        }

        foreach (Vector3 nb in b.Normals)
        {
            normals.Add(nb);
        }

        foreach (int id in b.Indices)
        {
            indices.Add(id + baseIndex);
        }

        return new Mesh(positions.ToArray(), normals.ToArray(), indices.ToArray());
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
