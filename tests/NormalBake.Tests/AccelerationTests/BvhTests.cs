using System.Numerics;
using NormalBake.Core;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>
/// P3 validation: the binned-SAH BVH must agree with the brute-force reference tracer (the oracle) on
/// the high-poly corpus — same hit/miss, same distance and hit point within ε (including grazing rays
/// aimed at shared edges/vertices, which stress watertightness).
/// </summary>
public class BvhTests
{
    private const float Eps = 1e-3f;

    [Theory]
    [InlineData("sphere")]
    [InlineData("box")]
    [InlineData("cylinder")]
    public void ClosestHit_MatchesBruteForce(string name)
    {
        Mesh mesh = GetHigh(name);
        var bvh = Bvh.Build(mesh);
        var brute = new BruteForceTracer(mesh);
        (Vector3 center, float radius) = BoundsOf(mesh);

        var rng = new Random(20260617);
        int hits = 0;
        for (int k = 0; k < 3000; k++)
        {
            Ray ray = ThroughInterior(rng, center, radius);
            bool bvhHit = bvh.ClosestHit(ray, out Hit hb);
            bool bruteHit = brute.ClosestHit(ray, out Hit hr);

            Assert.Equal(bruteHit, bvhHit);
            if (!bvhHit)
            {
                continue;
            }

            hits++;
            Assert.True(MathF.Abs(hb.T - hr.T) < Eps, $"{name}: t {hb.T} vs {hr.T}");
            Vector3 pb = ray.Origin + (hb.T * ray.Direction);
            Vector3 pr = ray.Origin + (hr.T * ray.Direction);
            Assert.True((pb - pr).Length() < Eps, $"{name}: hit point divergence {(pb - pr).Length()}");
        }

        Assert.True(hits > 500, $"{name}: too few hits ({hits}) to be a meaningful test");
    }

    [Theory]
    [InlineData("sphere")]
    [InlineData("box")]
    [InlineData("cylinder")]
    public void AnyHit_MatchesBruteForce(string name)
    {
        Mesh mesh = GetHigh(name);
        var bvh = Bvh.Build(mesh);
        var brute = new BruteForceTracer(mesh);
        (Vector3 center, float radius) = BoundsOf(mesh);

        var rng = new Random(99);
        for (int k = 0; k < 2000; k++)
        {
            Vector3 origin = center + (RandomUnit(rng) * radius * 2.5f);
            Vector3 target = center + (RandomInBox(rng) * radius * 0.9f);
            Vector3 dir = Vector3.Normalize(target - origin);
            float tMax = (target - origin).Length() * (0.3f + (rng.NextSingle() * 1.2f));
            var ray = new Ray(origin, dir, tMax);

            bool any = bvh.AnyHit(ray);
            bool expected = brute.ClosestHit(ray, out Hit h) && h.T <= tMax;
            Assert.Equal(expected, any);
        }
    }

    [Fact]
    public void GrazingRays_AimedAtVertices_AgreeWithBruteForce()
    {
        Mesh mesh = Primitives.Sphere().High;
        var bvh = Bvh.Build(mesh);
        var brute = new BruteForceTracer(mesh);
        (Vector3 center, float radius) = BoundsOf(mesh);

        ReadOnlySpan<Vector3> positions = mesh.Positions;
        int step = Math.Max(1, positions.Length / 500);
        for (int v = 0; v < positions.Length; v += step)
        {
            Vector3 vertex = positions[v];
            Vector3 outward = Vector3.Normalize(vertex - center);
            Vector3 origin = vertex + (outward * radius * 2f);
            var ray = new Ray(origin, Vector3.Normalize(vertex - origin), radius * 5f);

            bool bvhHit = bvh.ClosestHit(ray, out Hit hb);
            bool bruteHit = brute.ClosestHit(ray, out Hit hr);
            Assert.Equal(bruteHit, bvhHit);
            if (bvhHit)
            {
                Assert.True(MathF.Abs(hb.T - hr.T) < Eps, $"edge-aimed t {hb.T} vs {hr.T}");
            }
        }
    }

    [Fact]
    public void RayThroughClosedMesh_AlwaysHits()
    {
        Mesh mesh = Primitives.Box().High;
        var bvh = Bvh.Build(mesh);
        (Vector3 center, float radius) = BoundsOf(mesh);

        var rng = new Random(7);
        for (int k = 0; k < 1000; k++)
        {
            Vector3 dir = RandomUnit(rng);
            var ray = new Ray(center - (dir * radius * 2f), dir, radius * 4f);
            Assert.True(bvh.ClosestHit(ray, out _), "a ray through a closed mesh must hit (no watertight leak)");
        }
    }

    [Theory]
    [InlineData("sphere")]
    [InlineData("box")]
    [InlineData("cylinder")]
    public void ClosestPoint_MatchesBruteForce(string name)
    {
        Mesh mesh = GetHigh(name);
        var bvh = Bvh.Build(mesh);
        (Vector3 center, float radius) = BoundsOf(mesh);
        ReadOnlySpan<Vector3> positions = mesh.Positions;
        ReadOnlySpan<int> indices = mesh.Indices;

        var rng = new Random(5);
        for (int k = 0; k < 500; k++)
        {
            Vector3 q = center + (RandomUnit(rng) * radius * (0.3f + (rng.NextSingle() * 1.7f)));
            Assert.True(bvh.ClosestPoint(q, out Hit hb));

            float bestSq = float.MaxValue;
            for (int t = 0; t < indices.Length; t += 3)
            {
                Vector3 cp = TriangleMath.ClosestPointOnTriangle(
                    q, positions[indices[t]], positions[indices[t + 1]], positions[indices[t + 2]],
                    out _, out _, out _);
                bestSq = MathF.Min(bestSq, (cp - q).LengthSquared());
            }

            Assert.True(MathF.Abs(hb.T - MathF.Sqrt(bestSq)) < Eps, $"{name}: {hb.T} vs {MathF.Sqrt(bestSq)}");
        }
    }

    [Fact]
    public void HandlesLargeRayBatchAtScale()
    {
        Mesh mesh = Primitives.Sphere().High;
        var bvh = Bvh.Build(mesh);
        (Vector3 center, float radius) = BoundsOf(mesh);

        var rng = new Random(42);
        int hits = 0;
        for (int k = 0; k < 50000; k++)
        {
            if (bvh.ClosestHit(ThroughInterior(rng, center, radius), out _))
            {
                hits++;
            }
        }

        Assert.True(hits > 10000, $"expected many interior-aimed rays to hit, got {hits}");
    }

    private static Ray ThroughInterior(Random rng, Vector3 center, float radius)
    {
        Vector3 origin = center + (RandomUnit(rng) * radius * 2.5f);
        Vector3 target = center + (RandomInBox(rng) * radius * 0.9f);
        return new Ray(origin, Vector3.Normalize(target - origin), radius * 10f);
    }

    private static Vector3 RandomUnit(Random rng)
    {
        float z = (rng.NextSingle() * 2f) - 1f;
        float phi = rng.NextSingle() * 2f * MathF.PI;
        float r = MathF.Sqrt(MathF.Max(0f, 1f - (z * z)));
        return new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), z);
    }

    private static Vector3 RandomInBox(Random rng) => new(
        (rng.NextSingle() * 2f) - 1f, (rng.NextSingle() * 2f) - 1f, (rng.NextSingle() * 2f) - 1f);

    private static (Vector3 Center, float Radius) BoundsOf(Mesh mesh)
    {
        var box = Aabb.Empty;
        foreach (Vector3 p in mesh.Positions)
        {
            box = box.Include(p);
        }

        return (box.Centroid, box.Extent.Length() * 0.5f);
    }

    private static Mesh GetHigh(string name) => name switch
    {
        "sphere" => Primitives.Sphere().High,
        "box" => Primitives.Box().High,
        "cylinder" => Primitives.Cylinder().High,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown primitive."),
    };
}
