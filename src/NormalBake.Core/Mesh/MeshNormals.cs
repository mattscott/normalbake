using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// Computes smooth per-vertex normals when a source mesh omits them. Uses area-weighted face normals
/// (the un-normalized triangle cross product is proportional to twice the face area), accumulated in a
/// fixed triangle order for determinism, then normalized. Vertices touched by no usable face fall back
/// to <c>(0,0,1)</c>.
/// </summary>
internal static class MeshNormals
{
    /// <summary>Returns area-weighted smooth normals for an indexed triangle mesh.</summary>
    public static Vector3[] ComputeSmooth(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices)
    {
        var normals = new Vector3[positions.Length];
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int i0 = indices[t];
            int i1 = indices[t + 1];
            int i2 = indices[t + 2];
            Vector3 p0 = positions[i0];
            Vector3 weighted = Vector3.Cross(positions[i1] - p0, positions[i2] - p0); // ‖·‖ = 2·area
            normals[i0] += weighted;
            normals[i1] += weighted;
            normals[i2] += weighted;
        }

        for (int v = 0; v < normals.Length; v++)
        {
            float lenSq = normals[v].LengthSquared();
            normals[v] = lenSq > 1e-24f ? normals[v] / MathF.Sqrt(lenSq) : new Vector3(0f, 0f, 1f);
        }

        return normals;
    }
}
