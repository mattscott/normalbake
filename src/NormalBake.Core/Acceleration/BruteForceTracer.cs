using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// A reference ray tracer that tests every triangle. O(n) per ray — far too slow for baking, but it is
/// the ground truth the accelerated <see cref="Bvh"/> is validated against (same triangle and
/// barycentrics within ε, including grazing/shared-edge rays).
/// </summary>
public sealed class BruteForceTracer : IRayTracer
{
    private readonly Vector3[] _v0;
    private readonly Vector3[] _v1;
    private readonly Vector3[] _v2;

    /// <summary>Builds a brute-force tracer over a mesh's triangles.</summary>
    public BruteForceTracer(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        (_v0, _v1, _v2) = TriangleSoup.Extract(mesh);
    }

    /// <inheritdoc/>
    public bool ClosestHit(in Ray ray, out Hit hit)
    {
        var rs = new RayShear(ray);
        float closest = ray.TMax;
        int bestTri = -1;
        float bestU = 0f;
        float bestV = 0f;
        for (int i = 0; i < _v0.Length; i++)
        {
            if (RayTriangle.Intersect(rs, _v0[i], _v1[i], _v2[i], closest, out float t, out float u, out float v))
            {
                closest = t;
                bestTri = i;
                bestU = u;
                bestV = v;
            }
        }

        hit = new Hit(closest, bestTri, bestU, bestV);
        return bestTri >= 0;
    }

    /// <inheritdoc/>
    public bool AnyHit(in Ray ray)
    {
        var rs = new RayShear(ray);
        for (int i = 0; i < _v0.Length; i++)
        {
            if (RayTriangle.Intersect(rs, _v0[i], _v1[i], _v2[i], ray.TMax, out _, out _, out _))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Flattens an indexed mesh into per-triangle vertex arrays (shared by the tracers).</summary>
internal static class TriangleSoup
{
    public static (Vector3[] V0, Vector3[] V1, Vector3[] V2) Extract(Mesh mesh)
    {
        ReadOnlySpan<int> indices = mesh.Indices;
        ReadOnlySpan<Vector3> positions = mesh.Positions;
        int triCount = mesh.TriangleCount;
        var v0 = new Vector3[triCount];
        var v1 = new Vector3[triCount];
        var v2 = new Vector3[triCount];
        for (int t = 0; t < triCount; t++)
        {
            v0[t] = positions[indices[(t * 3) + 0]];
            v1[t] = positions[indices[(t * 3) + 1]];
            v2[t] = positions[indices[(t * 3) + 2]];
        }

        return (v0, v1, v2);
    }
}
