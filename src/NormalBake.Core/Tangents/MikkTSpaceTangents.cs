using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// A clean-room, MikkTSpace-compatible per-vertex tangent solver. Vertices are welded by
/// (position, normal, UV) so that UV seams and hard edges — which carry different UVs/normals — keep
/// independent tangents (the MikkTSpace seam behaviour), while smooth shared vertices average together.
/// Per triangle it forms the Lengyel tangent/bitangent, accumulates them into the welded vertices
/// weighted by the corner angle (tessellation-independent and order-independent), then per vertex
/// Gram-Schmidt-orthogonalizes the tangent against the normal and records the handedness sign
/// <c>w</c> so the bitangent reconstructs as <c>B = w·(N×T)</c>.
/// </summary>
/// <remarks>
/// Degenerate triangles (zero UV or geometric area) contribute nothing, so a degenerate vertex inherits
/// its neighbours' frame. Bit-exact parity with the C reference <c>mikktspace.c</c> is deferred (it
/// needs the C toolchain to generate reference data); correctness here is validated by analytic tangents,
/// order-independence, orthonormality, mirrored-UV handedness, and glTF-shipped MikkTSpace tangents.
/// </remarks>
public sealed class MikkTSpaceTangents : ITangentSpace
{
    private const float WeldPositionScale = 1e4f;
    private const float WeldNormalScale = 1e3f;
    private const float WeldUvScale = 1e4f;

    /// <summary>Computes and attaches a MikkTSpace-compatible tangent basis (convenience static form).</summary>
    public static void Generate(UvMesh mesh) => new MikkTSpaceTangents().Compute(mesh);

    /// <inheritdoc/>
    public void Compute(UvMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        int vertexCount = mesh.VertexCount;
        ReadOnlySpan<Vector3> positions = mesh.Positions;
        ReadOnlySpan<Vector3> normals = mesh.Normals;
        ReadOnlySpan<Vector2> uvs = mesh.Uvs;
        ReadOnlySpan<int> indices = mesh.Indices;

        int[] weld = WeldVertices(positions, normals, uvs);
        var tan1 = new Vector3[vertexCount]; // accumulated tangent direction per welded representative
        var tan2 = new Vector3[vertexCount]; // accumulated bitangent direction per welded representative

        for (int t = 0; t < indices.Length; t += 3)
        {
            int i0 = indices[t];
            int i1 = indices[t + 1];
            int i2 = indices[t + 2];
            Vector3 p0 = positions[i0];
            Vector3 e1 = positions[i1] - p0;
            Vector3 e2 = positions[i2] - p0;
            Vector2 uv0 = uvs[i0];
            Vector2 d1 = uvs[i1] - uv0;
            Vector2 d2 = uvs[i2] - uv0;

            float denom = (d1.X * d2.Y) - (d2.X * d1.Y);
            if (MathF.Abs(denom) < 1e-12f || Vector3.Cross(e1, e2).LengthSquared() < 1e-20f)
            {
                continue; // degenerate UV or geometric triangle — inherit neighbours
            }

            float r = 1f / denom;
            Vector3 sdir = ((e1 * d2.Y) - (e2 * d1.Y)) * r; // tangent (+U) direction
            Vector3 tdir = ((e2 * d1.X) - (e1 * d2.X)) * r; // bitangent (+V) direction

            Accumulate(weld, tan1, tan2, i0, sdir, tdir, CornerAngle(positions[i0], positions[i1], positions[i2]));
            Accumulate(weld, tan1, tan2, i1, sdir, tdir, CornerAngle(positions[i1], positions[i2], positions[i0]));
            Accumulate(weld, tan1, tan2, i2, sdir, tdir, CornerAngle(positions[i2], positions[i0], positions[i1]));
        }

        var tangents = new Vector4[vertexCount];
        for (int v = 0; v < vertexCount; v++)
        {
            int rep = weld[v];
            Vector3 n = normals[v];
            Vector3 t = tan1[rep];
            t -= n * Vector3.Dot(n, t); // Gram-Schmidt against the normal
            float len = t.Length();
            Vector3 tangent = len > 1e-8f ? t / len : PerpendicularTo(n);
            float w = Vector3.Dot(Vector3.Cross(n, tangent), tan2[rep]) < 0f ? -1f : 1f;
            tangents[v] = new Vector4(tangent, w);
        }

        mesh.SetTangents(tangents);
    }

    private static void Accumulate(
        int[] weld, Vector3[] tan1, Vector3[] tan2, int vertex, Vector3 sdir, Vector3 tdir, float weight)
    {
        int rep = weld[vertex];
        tan1[rep] += sdir * weight;
        tan2[rep] += tdir * weight;
    }

    private static int[] WeldVertices(
        ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector3> normals, ReadOnlySpan<Vector2> uvs)
    {
        var map = new Dictionary<WeldKey, int>(positions.Length);
        var weld = new int[positions.Length];
        for (int v = 0; v < positions.Length; v++)
        {
            var key = new WeldKey(positions[v], normals[v], uvs[v]);
            if (map.TryGetValue(key, out int rep))
            {
                weld[v] = rep;
            }
            else
            {
                map[key] = v;
                weld[v] = v;
            }
        }

        return weld;
    }

    private static float CornerAngle(Vector3 at, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - at;
        Vector3 e2 = c - at;
        float l1 = e1.Length();
        float l2 = e2.Length();
        if (l1 < 1e-12f || l2 < 1e-12f)
        {
            return 0f;
        }

        float cos = Math.Clamp(Vector3.Dot(e1, e2) / (l1 * l2), -1f, 1f);
        return MathF.Acos(cos);
    }

    private static Vector3 PerpendicularTo(Vector3 n)
    {
        Vector3 axis = MathF.Abs(n.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        return Vector3.Normalize(Vector3.Cross(axis, n));
    }

    private readonly record struct WeldKey
    {
        private readonly long _px;
        private readonly long _py;
        private readonly long _pz;
        private readonly int _nx;
        private readonly int _ny;
        private readonly int _nz;
        private readonly long _u;
        private readonly long _v;

        public WeldKey(Vector3 position, Vector3 normal, Vector2 uv)
        {
            _px = Quantize(position.X, WeldPositionScale);
            _py = Quantize(position.Y, WeldPositionScale);
            _pz = Quantize(position.Z, WeldPositionScale);
            _nx = (int)Quantize(normal.X, WeldNormalScale);
            _ny = (int)Quantize(normal.Y, WeldNormalScale);
            _nz = (int)Quantize(normal.Z, WeldNormalScale);
            _u = Quantize(uv.X, WeldUvScale);
            _v = Quantize(uv.Y, WeldUvScale);
        }

        private static long Quantize(float value, float scale) => (long)MathF.Round(value * scale);
    }
}
