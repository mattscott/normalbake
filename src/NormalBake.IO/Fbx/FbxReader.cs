using System.Numerics;
using NormalBake.Core;

namespace NormalBake.IO;

/// <summary>One named mesh part extracted from an FBX file (geometry already merged-ready).</summary>
public sealed class FbxPart
{
    internal FbxPart(string name, Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        Name = name;
        Positions = positions;
        Normals = normals;
        Uvs = uvs;
        Indices = indices;
    }

    /// <summary>The part's name (the FBX Geometry/Model name).</summary>
    public string Name { get; }

    /// <summary>Number of triangles in this part.</summary>
    public int TriangleCount => Indices.Length / 3;

    internal Vector3[] Positions { get; }

    internal Vector3[] Normals { get; }

    internal Vector2[] Uvs { get; }

    internal int[] Indices { get; }

    /// <summary>This part as a high-poly <see cref="Mesh"/> (positions + normals + indices).</summary>
    public Mesh ToMesh() => new((Vector3[])Positions.Clone(), (Vector3[])Normals.Clone(), (int[])Indices.Clone());

    /// <summary>This part as a low-poly <see cref="UvMesh"/> (positions + normals + UVs + indices).</summary>
    public UvMesh ToUvMesh() => new(
        (Vector3[])Positions.Clone(),
        (Vector3[])Normals.Clone(),
        (Vector2[])Uvs.Clone(),
        (int[])Indices.Clone());
}

/// <summary>
/// Reads binary FBX meshes. The parser is clean-room (<see cref="FbxBinaryParser"/>); this layer turns
/// FBX's layered geometry — control points, polygon-vertex indices (with the bitwise-NOT polygon-end
/// marker), and per-vertex normal/UV layers in the common Direct/IndexToDirect × ByPolygonVertex/
/// ByControlPoint modes — into indexed triangle meshes, and merges all parts into one mesh.
/// </summary>
public static class FbxReader
{
    /// <summary>Reads all mesh parts from an FBX file.</summary>
    public static IReadOnlyList<FbxPart> ReadParts(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return ReadParts(stream);
    }

    /// <summary>Reads all mesh parts from an FBX stream.</summary>
    public static IReadOnlyList<FbxPart> ReadParts(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        FbxNode root = FbxBinaryParser.Parse(stream);
        var scene = new FbxScene(root);
        var parts = new List<FbxPart>();
        foreach (FbxNode objects in root.ChildrenNamed("Objects"))
        {
            foreach (FbxNode geo in objects.ChildrenNamed("Geometry"))
            {
                FbxPart? part = ExtractGeometry(geo, scene.MeshToWorld(geo.AsLong(0)));
                if (part is not null)
                {
                    parts.Add(part);
                }
            }
        }

        if (parts.Count == 0)
        {
            throw new InvalidDataException("FBX has no mesh geometry.");
        }

        return parts;
    }

    /// <summary>Reads and merges all parts into one high-poly <see cref="Mesh"/> (UVs ignored).</summary>
    public static Mesh ReadMesh(string path)
    {
        IReadOnlyList<FbxPart> parts = ReadParts(path);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        foreach (FbxPart p in parts)
        {
            int baseIndex = positions.Count;
            positions.AddRange(p.Positions);
            normals.AddRange(p.Normals);
            foreach (int i in p.Indices)
            {
                indices.Add(i + baseIndex);
            }
        }

        return new Mesh(positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    /// <summary>Reads and merges all parts into one low-poly <see cref="UvMesh"/>.</summary>
    public static UvMesh ReadUvMesh(string path)
    {
        IReadOnlyList<FbxPart> parts = ReadParts(path);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        foreach (FbxPart p in parts)
        {
            int baseIndex = positions.Count;
            positions.AddRange(p.Positions);
            normals.AddRange(p.Normals);
            uvs.AddRange(p.Uvs);
            foreach (int i in p.Indices)
            {
                indices.Add(i + baseIndex);
            }
        }

        return new UvMesh(positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray());
    }

    private static FbxPart? ExtractGeometry(FbxNode geo, Matrix4x4 meshToWorld)
    {
        double[]? vertices = geo.Child("Vertices")?.AsDoubleArray(0);
        int[]? polygonVertexIndex = geo.Child("PolygonVertexIndex")?.AsIntArray(0);
        if (vertices is null || polygonVertexIndex is null)
        {
            return null;
        }

        var controlPoints = new Vector3[vertices.Length / 3];
        for (int i = 0; i < controlPoints.Length; i++)
        {
            var raw = new Vector3((float)vertices[i * 3], (float)vertices[(i * 3) + 1], (float)vertices[(i * 3) + 2]);
            controlPoints[i] = Vector3.Transform(raw, meshToWorld);
        }

        FbxLayer? normalLayer = FbxLayer.From(geo, "LayerElementNormal", "Normals", "NormalsIndex", 3);
        FbxLayer? uvLayer = FbxLayer.From(geo, "LayerElementUV", "UV", "UVIndex", 2);

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        var dedup = new Dictionary<(int Cp, int N, int Uv), int>();

        int Combined(int pv)
        {
            int raw = polygonVertexIndex[pv];
            int cp = raw < 0 ? ~raw : raw;
            int nIndex = normalLayer?.RawIndex(pv, cp) ?? -1;
            int uvIndex = uvLayer?.RawIndex(pv, cp) ?? -1;
            var key = (cp, nIndex, uvIndex);
            if (dedup.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int index = positions.Count;
            dedup[key] = index;
            positions.Add(controlPoints[cp]);
            Vector3 normal = normalLayer is not null ? normalLayer.Get3(pv, cp) : new Vector3(0f, 0f, 1f);
            normal = Vector3.TransformNormal(normal, meshToWorld);
            normals.Add(normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : new Vector3(0f, 0f, 1f));
            uvs.Add(uvLayer is not null ? uvLayer.Get2(pv, cp) : Vector2.Zero);
            return index;
        }

        int polygonStart = 0;
        for (int pv = 0; pv < polygonVertexIndex.Length; pv++)
        {
            if (polygonVertexIndex[pv] >= 0)
            {
                continue; // not the last vertex of a polygon yet
            }

            // Fan-triangulate the polygon spanning [polygonStart .. pv].
            for (int t = polygonStart + 1; t + 1 <= pv; t++)
            {
                indices.Add(Combined(polygonStart));
                indices.Add(Combined(t));
                indices.Add(Combined(t + 1));
            }

            polygonStart = pv + 1;
        }

        string name = ReadName(geo);
        return new FbxPart(name, positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray());
    }

    private static string ReadName(FbxNode geo)
    {
        string raw = geo.AsString(1) ?? "mesh";
        int sep = raw.IndexOf('\0', StringComparison.Ordinal);
        return sep >= 0 ? raw[..sep] : raw;
    }
}
