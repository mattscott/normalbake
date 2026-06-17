using System.Numerics;
using NormalBake.Core;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>
/// P7: the tile-parallel bake stays <b>bit-exact</b> across thread counts (the determinism guarantee),
/// and dirty inputs degrade without crashing.
/// </summary>
public class PerformanceTests
{
    [Theory]
    [InlineData(MapType.Normal)]
    [InlineData(MapType.Position)]
    [InlineData(MapType.AmbientOcclusion)]
    [InlineData(MapType.Curvature)]
    public void Bake_IsByteIdentical_AcrossThreadCounts(MapType map)
    {
        MeshPair sp = Primitives.Sphere();
        BakedMap one = Bake(sp, map, threads: 1);
        BakedMap four = Bake(sp, map, threads: 4);
        BakedMap auto = Bake(sp, map, threads: -1);

        Assert.True(one.Pixels.SequenceEqual(four.Pixels), "threads 1 vs 4 diverged");
        Assert.True(one.Pixels.SequenceEqual(auto.Pixels), "threads 1 vs all-cores diverged");
    }

    [Fact]
    public void Bake_HandlesDegenerateHighPoly_WithoutCrashing()
    {
        MeshPair sp = Primitives.Sphere();
        Mesh dirty = AppendDegenerateTriangle(sp.High);

        BakedMap map = NormalBaker.Bake(dirty, sp.Low, new NormalBakeOptions { Resolution = 64, Samples = 1 });

        Assert.Equal(64, map.Width);
    }

    [Fact]
    public void Bake_HandlesDegenerateLowPolyTriangle_WithoutCrashing()
    {
        // A valid single triangle plus a zero-area (degenerate) triangle in the low-poly.
        var positions = new[]
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0),
        };
        var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        var uvs = new[]
        {
            new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.1f, 0.9f), new Vector2(0.5f, 0.5f),
        };
        var low = new UvMesh(positions, normals, uvs, new[] { 0, 1, 2, 3, 3, 3 }); // 2nd triangle is degenerate
        var high = new Mesh(positions[..3].ToArray(), normals[..3].ToArray(), new[] { 0, 1, 2 });

        BakedMap map = NormalBaker.Bake(high, low, new NormalBakeOptions { Resolution = 64, Samples = 1 });

        Assert.Equal(64, map.Width);
    }

    private static BakedMap Bake(MeshPair pair, MapType map, int threads) =>
        NormalBaker.Bake(pair.High, pair.Low, new NormalBakeOptions
        {
            Resolution = 64,
            Samples = 1,
            Map = map,
            OcclusionSamples = 16,
            Threads = threads,
        });

    private static Mesh AppendDegenerateTriangle(Mesh mesh)
    {
        var positions = new List<Vector3>(mesh.Positions.ToArray());
        var normals = new List<Vector3>(mesh.Normals.ToArray());
        var indices = new List<int>(mesh.Indices.ToArray());
        int b = positions.Count;
        var p = new Vector3(0.123f, 0.456f, 0.789f);
        positions.Add(p);
        positions.Add(p);
        positions.Add(p); // three coincident vertices → zero area
        normals.Add(Vector3.UnitZ);
        normals.Add(Vector3.UnitZ);
        normals.Add(Vector3.UnitZ);
        indices.Add(b);
        indices.Add(b + 1);
        indices.Add(b + 2);
        return new Mesh(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }
}
