using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// Encodes a tangent-space normal (a unit vector in <c>[-1,1]</c>) into linear RGB in <c>[0,1]</c>, and
/// back. A flat normal <c>(0,0,1)</c> maps to <c>(0.5,0.5,1.0)</c> — the familiar bluish look. The
/// <see cref="NormalMapConvention"/> flips only the green (Y) channel: OpenGL is +Y (green up), DirectX
/// is −Y (green down). The data is <b>linear</b> (no sRGB) — see
/// <c>Agents/Documents/01-normal-mapping-fundamentals.md</c>.
/// </summary>
public static class NormalEncoder
{
    /// <summary>Encodes a tangent-space normal to linear RGB, applying the green-channel convention.</summary>
    public static Vector3 Encode(Vector3 tangentSpaceNormal, NormalMapConvention convention)
    {
        Vector3 n = tangentSpaceNormal;
        if (convention == NormalMapConvention.DirectX)
        {
            n.Y = -n.Y;
        }

        return (n * 0.5f) + new Vector3(0.5f);
    }

    /// <summary>Decodes linear RGB back to a tangent-space normal (inverse of <see cref="Encode"/>).</summary>
    public static Vector3 Decode(Vector3 rgb, NormalMapConvention convention)
    {
        Vector3 n = (rgb * 2f) - new Vector3(1f);
        if (convention == NormalMapConvention.DirectX)
        {
            n.Y = -n.Y;
        }

        return n;
    }
}
