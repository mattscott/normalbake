using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using NormalBake.Core;

namespace NormalBake.IO;

/// <summary>
/// Reads Wavefront OBJ meshes. Supports <c>v</c>/<c>vt</c>/<c>vn</c> and every face-corner form
/// (<c>v</c>, <c>v/vt</c>, <c>v//vn</c>, <c>v/vt/vn</c>), 1-based and negative (relative) indices, and
/// polygon faces, which are triangulated with the pipeline's shared deterministic rule
/// (<see cref="Triangulation"/>). Distinct <c>v/vt/vn</c> corners become distinct vertices so the
/// struct-of-arrays attributes line up. Missing normals are computed area-weighted.
/// </summary>
public static class ObjReader
{
    /// <summary>Reads a high-poly <see cref="Mesh"/> (positions + normals + indices) from a file.</summary>
    public static Mesh ReadMesh(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return ReadMesh(stream);
    }

    /// <summary>Reads a high-poly <see cref="Mesh"/> from a stream. UVs, if present, are ignored.</summary>
    public static Mesh ReadMesh(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjData data = Parse(stream);
        Vector3[] normals = data.AllCornersHaveNormal
            ? data.Normals
            : MeshNormals.ComputeSmooth(data.Positions, data.Indices);
        return new Mesh(data.Positions, normals, data.Indices);
    }

    /// <summary>Reads a low-poly <see cref="UvMesh"/> (positions + normals + UVs + indices) from a file.</summary>
    public static UvMesh ReadUvMesh(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return ReadUvMesh(stream);
    }

    /// <summary>
    /// Reads a low-poly <see cref="UvMesh"/> from a stream. Throws if the mesh has no usable texture
    /// coordinates — the low-poly must be UV-unwrapped upstream before baking.
    /// </summary>
    public static UvMesh ReadUvMesh(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjData data = Parse(stream);
        if (!data.AllCornersHaveUv)
        {
            throw new InvalidDataException(
                "OBJ has no (complete) texture coordinates: the low-poly must be UV-unwrapped by a separate " +
                "tool before baking (NormalBake validates UVs, it does not create them).");
        }

        Vector3[] normals = data.AllCornersHaveNormal
            ? data.Normals
            : MeshNormals.ComputeSmooth(data.Positions, data.Indices);
        return new UvMesh(data.Positions, normals, data.Uvs, data.Indices);
    }

    private static ObjData Parse(Stream stream)
    {
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();

        var vPool = new List<Vector3>();
        var vtPool = new List<Vector2>();
        var vnPool = new List<Vector3>();

        var combined = new Dictionary<(int V, int T, int N), int>();
        var faceCorners = new List<int>();
        var faceSizes = new List<int>();

        bool allCornersHaveUv = true;
        bool allCornersHaveNormal = true;
        bool anyFace = false;

        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        int lineNo = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            string[] tok = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0 || tok[0].StartsWith('#'))
            {
                continue;
            }

            switch (tok[0])
            {
                case "v":
                    vPool.Add(new Vector3(
                        ParseFloat(tok, 1, lineNo), ParseFloat(tok, 2, lineNo), ParseFloat(tok, 3, lineNo)));
                    break;

                case "vt":
                    vtPool.Add(new Vector2(
                        ParseFloat(tok, 1, lineNo), tok.Length > 2 ? ParseFloat(tok, 2, lineNo) : 0f));
                    break;

                case "vn":
                    vnPool.Add(new Vector3(
                        ParseFloat(tok, 1, lineNo), ParseFloat(tok, 2, lineNo), ParseFloat(tok, 3, lineNo)));
                    break;

                case "f":
                    anyFace = true;
                    int corners = 0;
                    for (int i = 1; i < tok.Length; i++)
                    {
                        (int vi, int ti, int ni) = ParseCorner(
                            tok[i], vPool.Count, vtPool.Count, vnPool.Count, lineNo);
                        if (ti < 0)
                        {
                            allCornersHaveUv = false;
                        }

                        if (ni < 0)
                        {
                            allCornersHaveNormal = false;
                        }

                        var key = (vi, ti, ni);
                        if (!combined.TryGetValue(key, out int index))
                        {
                            index = positions.Count;
                            combined[key] = index;
                            positions.Add(vPool[vi]);
                            uvs.Add(ti >= 0 ? vtPool[ti] : default);
                            normals.Add(ni >= 0 ? vnPool[ni] : default);
                        }

                        faceCorners.Add(index);
                        corners++;
                    }

                    faceSizes.Add(corners);
                    break;

                default:
                    // Ignore o/g/s/usemtl/mtllib and anything else for P1 (match-by-name groups arrive in P5).
                    break;
            }
        }

        if (!anyFace)
        {
            throw new InvalidDataException("OBJ contains no faces.");
        }

        var indices = new List<int>(faceCorners.Count);
        ReadOnlySpan<Vector3> posSpan = CollectionsMarshal.AsSpan(positions);
        Span<int> cornerSpan = CollectionsMarshal.AsSpan(faceCorners);
        int cursor = 0;
        foreach (int size in faceSizes)
        {
            Triangulation.TriangulateFace(cornerSpan.Slice(cursor, size), posSpan, indices);
            cursor += size;
        }

        return new ObjData(
            positions.ToArray(),
            uvs.ToArray(),
            normals.ToArray(),
            indices.ToArray(),
            allCornersHaveUv,
            allCornersHaveNormal);
    }

    private static float ParseFloat(string[] tokens, int index, int lineNo)
    {
        if (index >= tokens.Length
            || !float.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            throw new InvalidDataException(
                $"OBJ line {lineNo}: expected a number at field {index} ('{tokens[0]}' record).");
        }

        return value;
    }

    private static (int V, int T, int N) ParseCorner(
        string corner, int vCount, int vtCount, int vnCount, int lineNo)
    {
        string[] parts = corner.Split('/');
        int v = ResolveIndex(parts[0], vCount, lineNo);
        int t = parts.Length > 1 && parts[1].Length > 0 ? ResolveIndex(parts[1], vtCount, lineNo) : -1;
        int n = parts.Length > 2 && parts[2].Length > 0 ? ResolveIndex(parts[2], vnCount, lineNo) : -1;
        return (v, t, n);
    }

    private static int ResolveIndex(string token, int count, int lineNo)
    {
        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw) || raw == 0)
        {
            throw new InvalidDataException($"OBJ line {lineNo}: invalid index '{token}'.");
        }

        int resolved = raw > 0 ? raw - 1 : count + raw; // 1-based, or negative = relative to end
        if ((uint)resolved >= (uint)count)
        {
            throw new InvalidDataException(
                $"OBJ line {lineNo}: index {raw} is out of range (have {count}).");
        }

        return resolved;
    }

    private sealed class ObjData
    {
        public ObjData(
            Vector3[] positions,
            Vector2[] uvs,
            Vector3[] normals,
            int[] indices,
            bool allCornersHaveUv,
            bool allCornersHaveNormal)
        {
            Positions = positions;
            Uvs = uvs;
            Normals = normals;
            Indices = indices;
            AllCornersHaveUv = allCornersHaveUv;
            AllCornersHaveNormal = allCornersHaveNormal;
        }

        public Vector3[] Positions { get; }

        public Vector2[] Uvs { get; }

        public Vector3[] Normals { get; }

        public int[] Indices { get; }

        public bool AllCornersHaveUv { get; }

        public bool AllCornersHaveNormal { get; }
    }
}
