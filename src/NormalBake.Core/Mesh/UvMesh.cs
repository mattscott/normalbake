using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// A low-poly target mesh that <b>already carries UVs</b> (produced upstream by a separate UV-unwrap
/// tool — NormalBake validates them, it does not unwrap). Struct-of-arrays: positions, smooth normals,
/// UVs, an optional per-vertex tangent basis (<c>xyz</c> direction + <c>w</c> handedness), and one
/// shared triangle index buffer. That single triangulation feeds both the tangent solver and the UV
/// rasterizer (a mismatch causes X-shaped seams). See <c>Agents/Documents/03-tangent-space-and-mikktspace.md</c>.
/// </summary>
public sealed class UvMesh
{
    private readonly Vector3[] _positions;
    private readonly Vector3[] _normals;
    private readonly Vector2[] _uvs;
    private readonly int[] _indices;
    private Vector4[] _tangents;

    /// <summary>Creates a low-poly mesh from parallel attribute arrays and a triangle index buffer.</summary>
    /// <param name="positions">Vertex positions (model space).</param>
    /// <param name="normals">Smooth per-vertex normals; same length as <paramref name="positions"/>.</param>
    /// <param name="uvs">Per-vertex UV coordinates; same length as <paramref name="positions"/>.</param>
    /// <param name="indices">Triangle index buffer; length must be a multiple of 3.</param>
    /// <param name="tangents">
    /// Optional per-vertex tangents (<c>xyz</c> + handedness <c>w</c>); when supplied, same length as
    /// <paramref name="positions"/>. Left empty until the MikkTSpace solver fills them in (P2).
    /// </param>
    public UvMesh(
        Vector3[] positions,
        Vector3[] normals,
        Vector2[] uvs,
        int[] indices,
        Vector4[]? tangents = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(uvs);
        ArgumentNullException.ThrowIfNull(indices);
        if (normals.Length != positions.Length)
        {
            throw new ArgumentException(
                $"normals length ({normals.Length}) must equal positions length ({positions.Length}).",
                nameof(normals));
        }

        if (uvs.Length != positions.Length)
        {
            throw new ArgumentException(
                $"uvs length ({uvs.Length}) must equal positions length ({positions.Length}).", nameof(uvs));
        }

        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException(
                $"index count ({indices.Length}) must be a multiple of 3 (triangle list).", nameof(indices));
        }

        if (tangents is not null && tangents.Length != positions.Length)
        {
            throw new ArgumentException(
                $"tangents length ({tangents.Length}) must equal positions length ({positions.Length}).",
                nameof(tangents));
        }

        _positions = positions;
        _normals = normals;
        _uvs = uvs;
        _indices = indices;
        _tangents = tangents ?? Array.Empty<Vector4>();
    }

    /// <summary>Number of vertices.</summary>
    public int VertexCount => _positions.Length;

    /// <summary>Number of triangles (<c>Indices.Length / 3</c>).</summary>
    public int TriangleCount => _indices.Length / 3;

    /// <summary>True once a full per-vertex tangent basis has been computed and attached.</summary>
    public bool HasTangents => _tangents.Length == _positions.Length;

    /// <summary>Vertex positions (model space).</summary>
    public ReadOnlySpan<Vector3> Positions => _positions;

    /// <summary>Smooth per-vertex normals.</summary>
    public ReadOnlySpan<Vector3> Normals => _normals;

    /// <summary>Per-vertex UV coordinates.</summary>
    public ReadOnlySpan<Vector2> Uvs => _uvs;

    /// <summary>Per-vertex tangents (<c>xyz</c> direction + handedness <c>w</c>); empty until computed.</summary>
    public ReadOnlySpan<Vector4> Tangents => _tangents;

    /// <summary>Triangle index buffer (three indices per triangle).</summary>
    public ReadOnlySpan<int> Indices => _indices;

    // Direct array access for Core hot paths; internal keeps them off the public surface (no CA1819).
    internal Vector3[] PositionArray => _positions;

    internal Vector3[] NormalArray => _normals;

    internal Vector2[] UvArray => _uvs;

    internal Vector4[] TangentArray => _tangents;

    internal int[] IndexArray => _indices;

    /// <summary>
    /// Attaches the per-vertex tangent basis computed by the MikkTSpace solver (P2). Internal so only the
    /// Core algorithm writes tangents; the public surface stays read-only.
    /// </summary>
    internal void SetTangents(Vector4[] tangents)
    {
        ArgumentNullException.ThrowIfNull(tangents);
        if (tangents.Length != _positions.Length)
        {
            throw new ArgumentException(
                $"tangents length ({tangents.Length}) must equal positions length ({_positions.Length}).",
                nameof(tangents));
        }

        _tangents = tangents;
    }
}
