using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// A high-poly source mesh: indexed, struct-of-arrays triangle soup with positions and
/// <b>smooth (interpolated vertex) normals</b>. The baker samples the high-poly's smooth normal at the
/// ray hit, so usable normals are required; if a source file omits them they are computed area-weighted
/// (<see cref="MeshNormals"/>). See <c>docs/design/02-baking-algorithm.md</c>.
/// </summary>
public sealed class Mesh
{
    private readonly Vector3[] _positions;
    private readonly Vector3[] _normals;
    private readonly int[] _indices;

    /// <summary>Creates a mesh from parallel attribute arrays and a triangle index buffer.</summary>
    /// <param name="positions">Vertex positions (model space).</param>
    /// <param name="normals">Smooth per-vertex normals; must be the same length as <paramref name="positions"/>.</param>
    /// <param name="indices">Triangle index buffer; length must be a multiple of 3.</param>
    public Mesh(Vector3[] positions, Vector3[] normals, int[] indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(indices);
        if (normals.Length != positions.Length)
        {
            throw new ArgumentException(
                $"normals length ({normals.Length}) must equal positions length ({positions.Length}).",
                nameof(normals));
        }

        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException(
                $"index count ({indices.Length}) must be a multiple of 3 (triangle list).", nameof(indices));
        }

        _positions = positions;
        _normals = normals;
        _indices = indices;
    }

    /// <summary>Number of vertices.</summary>
    public int VertexCount => _positions.Length;

    /// <summary>Number of triangles (<c>Indices.Length / 3</c>).</summary>
    public int TriangleCount => _indices.Length / 3;

    /// <summary>Vertex positions (model space).</summary>
    public ReadOnlySpan<Vector3> Positions => _positions;

    /// <summary>Smooth per-vertex normals.</summary>
    public ReadOnlySpan<Vector3> Normals => _normals;

    /// <summary>Triangle index buffer (three indices per triangle).</summary>
    public ReadOnlySpan<int> Indices => _indices;

    // Direct array access for Core hot paths (BVH build, hit sampling) that need to retain a reference;
    // a ReadOnlySpan cannot be stored in a field. Internal keeps these off the public surface (no CA1819).
    internal Vector3[] PositionArray => _positions;

    internal Vector3[] NormalArray => _normals;

    internal int[] IndexArray => _indices;
}
