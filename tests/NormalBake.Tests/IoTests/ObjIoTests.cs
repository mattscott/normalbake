using System.Numerics;
using System.Text;
using NormalBake.Core;
using NormalBake.IO;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>OBJ reader/writer round-trips and parsing-corner coverage.</summary>
public class ObjIoTests
{
    [Fact]
    public void RoundTrip_Box_PreservesTrianglesAndAttributes()
    {
        UvMesh original = Primitives.Box().Low;

        using var stream = new MemoryStream();
        ObjWriter.Write(stream, original);
        stream.Position = 0;
        UvMesh reloaded = ObjReader.ReadUvMesh(stream);

        AssertSameTriangles(original, reloaded);
    }

    [Fact]
    public void RoundTrip_Sphere_PreservesTrianglesAndAttributes()
    {
        UvMesh original = Primitives.Sphere().Low;

        using var stream = new MemoryStream();
        ObjWriter.Write(stream, original);
        stream.Position = 0;
        UvMesh reloaded = ObjReader.ReadUvMesh(stream);

        AssertSameTriangles(original, reloaded);
    }

    [Fact]
    public void ReadUvMesh_WithoutTexcoords_Throws()
    {
        const string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";
        using var stream = Utf8(obj);
        Assert.Throws<InvalidDataException>(() => ObjReader.ReadUvMesh(stream));
    }

    [Fact]
    public void ReadMesh_WithoutNormals_ComputesUnitNormals()
    {
        const string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";
        using var stream = Utf8(obj);

        Mesh mesh = ObjReader.ReadMesh(stream);

        Assert.Equal(1, mesh.TriangleCount);
        foreach (Vector3 n in mesh.Normals)
        {
            Assert.Equal(1f, n.Length(), 5);
        }
    }

    [Fact]
    public void ReadMesh_ParsesAllCornerForms_AndNegativeIndices()
    {
        // Same triangle expressed via v/vt/vn, then again via v//vn with negative (relative) indices.
        var sb = new StringBuilder();
        sb.Append("v 0 0 0\nv 1 0 0\nv 0 1 0\n");
        sb.Append("vt 0 0\nvt 1 0\nvt 0 1\n");
        sb.Append("vn 0 0 1\n");
        sb.Append("f 1/1/1 2/2/1 3/3/1\n");
        sb.Append("f -3//-1 -2//-1 -1//-1\n");
        using var stream = Utf8(sb.ToString());

        Mesh mesh = ObjReader.ReadMesh(stream);

        Assert.Equal(2, mesh.TriangleCount);
    }

    [Fact]
    public void ReadMesh_Quad_SplitsAlongShorterDiagonal()
    {
        // Diagonal 0-2 (len²=8) is longer than 1-3 (len²=2), so the split must be 1-3 → [1,2,3, 1,3,0].
        var sb = new StringBuilder();
        sb.Append("v 0 0 0\nv 1 0 0\nv 2 2 0\nv 0 1 0\n");
        sb.Append("f 1 2 3 4\n");
        using var stream = Utf8(sb.ToString());

        Mesh mesh = ObjReader.ReadMesh(stream);

        Assert.Equal(new[] { 1, 2, 3, 1, 3, 0 }, mesh.Indices.ToArray());
    }

    private static void AssertSameTriangles(UvMesh a, UvMesh b)
    {
        Assert.Equal(a.TriangleCount, b.TriangleCount);
        ReadOnlySpan<int> ia = a.Indices;
        ReadOnlySpan<int> ib = b.Indices;
        for (int k = 0; k < ia.Length; k++)
        {
            Assert.Equal(a.Positions[ia[k]], b.Positions[ib[k]]);
            Assert.Equal(a.Uvs[ia[k]], b.Uvs[ib[k]]);
            Assert.Equal(a.Normals[ia[k]], b.Normals[ib[k]]);
        }
    }

    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));
}
