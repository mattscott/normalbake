using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// Woop et al. "Watertight Ray/Triangle Intersection" (2013): the ray is transformed to a space where
/// it is the +Z axis (a max-dimension permutation + shear), and the three edge functions are evaluated
/// there. Because adjacent triangles share the exact same edge computation, there are no cracks or
/// double hits at shared edges. Two-sided (no back-face cull). A double-precision fallback on any
/// exactly-zero edge function preserves watertightness for grazing rays.
/// (<c>docs/design/06-acceleration-and-performance.md</c>.)
/// </summary>
internal readonly struct RayShear
{
    public readonly Vector3 Org;
    public readonly int Kx;
    public readonly int Ky;
    public readonly int Kz;
    public readonly float Sx;
    public readonly float Sy;
    public readonly float Sz;

    public RayShear(in Ray ray)
    {
        Org = ray.Origin;
        Vector3 d = ray.Direction;

        // Pick the dimension of greatest magnitude as the ray's "z".
        int kz = 0;
        float maxAbs = MathF.Abs(d.X);
        if (MathF.Abs(d.Y) > maxAbs)
        {
            kz = 1;
            maxAbs = MathF.Abs(d.Y);
        }

        if (MathF.Abs(d.Z) > maxAbs)
        {
            kz = 2;
        }

        int kx = kz + 1 == 3 ? 0 : kz + 1;
        int ky = kx + 1 == 3 ? 0 : kx + 1;
        if (Component(d, kz) < 0f)
        {
            (kx, ky) = (ky, kx); // keep winding consistent
        }

        Kx = kx;
        Ky = ky;
        Kz = kz;
        float dz = Component(d, kz);
        Sx = Component(d, kx) / dz;
        Sy = Component(d, ky) / dz;
        Sz = 1f / dz;
    }

    internal static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}

internal static class RayTriangle
{
    /// <summary>Intersects a sheared ray with a triangle. On hit, returns distance and barycentrics.</summary>
    public static bool Intersect(
        in RayShear rs, Vector3 a, Vector3 b, Vector3 c, float tMax, out float t, out float u, out float v)
    {
        t = 0f;
        u = 0f;
        v = 0f;

        Vector3 va = a - rs.Org;
        Vector3 vb = b - rs.Org;
        Vector3 vc = c - rs.Org;
        int kx = rs.Kx;
        int ky = rs.Ky;
        int kz = rs.Kz;

        float ax = RayShear.Component(va, kx) - (rs.Sx * RayShear.Component(va, kz));
        float ay = RayShear.Component(va, ky) - (rs.Sy * RayShear.Component(va, kz));
        float bx = RayShear.Component(vb, kx) - (rs.Sx * RayShear.Component(vb, kz));
        float by = RayShear.Component(vb, ky) - (rs.Sy * RayShear.Component(vb, kz));
        float cx = RayShear.Component(vc, kx) - (rs.Sx * RayShear.Component(vc, kz));
        float cy = RayShear.Component(vc, ky) - (rs.Sy * RayShear.Component(vc, kz));

        float u0 = (cx * by) - (cy * bx); // weight of vertex a
        float v0 = (ax * cy) - (ay * cx); // weight of vertex b
        float w0 = (bx * ay) - (by * ax); // weight of vertex c
        if (u0 == 0f || v0 == 0f || w0 == 0f)
        {
            u0 = (float)(((double)cx * by) - ((double)cy * bx));
            v0 = (float)(((double)ax * cy) - ((double)ay * cx));
            w0 = (float)(((double)bx * ay) - ((double)by * ax));
        }

        if ((u0 < 0f || v0 < 0f || w0 < 0f) && (u0 > 0f || v0 > 0f || w0 > 0f))
        {
            return false; // edge functions disagree in sign → outside the triangle
        }

        float det = u0 + v0 + w0;
        if (det == 0f)
        {
            return false;
        }

        float az = rs.Sz * RayShear.Component(va, kz);
        float bz = rs.Sz * RayShear.Component(vb, kz);
        float cz = rs.Sz * RayShear.Component(vc, kz);
        float tScaled = (u0 * az) + (v0 * bz) + (w0 * cz);
        float rcp = 1f / det;
        float tt = tScaled * rcp;
        if (tt < 0f || tt > tMax)
        {
            return false;
        }

        t = tt;
        u = v0 * rcp; // barycentric weight of the second vertex
        v = w0 * rcp; // barycentric weight of the third vertex
        return true;
    }
}
