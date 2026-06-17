using System.Numerics;

namespace NormalBake.Core;

/// <summary>Geometry helpers shared by the tracers (closest point on a triangle, Ericson's method).</summary>
internal static class TriangleMath
{
    /// <summary>
    /// Returns the closest point on triangle <c>(a,b,c)</c> to <paramref name="p"/>, with its barycentric
    /// weights (<paramref name="wa"/>/<paramref name="wb"/>/<paramref name="wc"/> for a/b/c). Used by the
    /// nearest-point fallback when a cage ray finds no hit. (Ericson, <i>Real-Time Collision Detection</i>.)
    /// </summary>
    public static Vector3 ClosestPointOnTriangle(
        Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float wa, out float wb, out float wc)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = p - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            return Bary(out wa, out wb, out wc, 1f, 0f, 0f, a);
        }

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            return Bary(out wa, out wb, out wc, 0f, 1f, 0f, b);
        }

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float t = d1 / (d1 - d3);
            return Bary(out wa, out wb, out wc, 1f - t, t, 0f, a + (t * ab));
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            return Bary(out wa, out wb, out wc, 0f, 0f, 1f, c);
        }

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float t = d2 / (d2 - d6);
            return Bary(out wa, out wb, out wc, 1f - t, 0f, t, a + (t * ac));
        }

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        {
            float t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return Bary(out wa, out wb, out wc, 0f, 1f - t, t, b + (t * (c - b)));
        }

        float denom = 1f / (va + vb + vc);
        float v = vb * denom;
        float w = vc * denom;
        return Bary(out wa, out wb, out wc, 1f - v - w, v, w, a + (ab * v) + (ac * w));
    }

    private static Vector3 Bary(out float wa, out float wb, out float wc, float a, float b, float c, Vector3 point)
    {
        wa = a;
        wb = b;
        wc = c;
        return point;
    }
}
