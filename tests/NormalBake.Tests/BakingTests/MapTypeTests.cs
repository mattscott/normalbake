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

    [Theory]
    [InlineData(MapType.Normal)]
    [InlineData(MapType.Position)]
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
