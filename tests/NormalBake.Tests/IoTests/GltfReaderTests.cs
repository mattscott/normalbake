using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using NormalBake.Core;
using NormalBake.IO;
using Xunit;

namespace NormalBake.Tests;

/// <summary>Validates the minimal GLB reader against a synthetic, in-memory glTF (no network needed).</summary>
public class GltfReaderTests
{
    private static readonly Vector3[] Positions =
    {
        new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 1f, 0f),
    };

    private static readonly Vector3[] Normals =
    {
        Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
    };

    private static readonly Vector2[] Uvs =
    {
        new(0f, 0f), new(1f, 0f), new(0f, 1f),
    };

    private static readonly Vector4[] Tangents =
    {
        new(1f, 0f, 0f, 1f), new(1f, 0f, 0f, 1f), new(1f, 0f, 0f, -1f),
    };

    private static readonly ushort[] Indices = { 0, 1, 2 };

    [Fact]
    public void ReadUvMesh_RecoversAttributesAndShippedTangents()
    {
        byte[] glb = BuildGlb(Positions, Normals, Uvs, Tangents, Indices);

        using var stream = new MemoryStream(glb);
        UvMesh mesh = GltfReader.ReadUvMesh(stream, out Vector4[]? shipped);

        Assert.Equal(Positions, mesh.Positions.ToArray());
        Assert.Equal(Normals, mesh.Normals.ToArray());
        Assert.Equal(Uvs, mesh.Uvs.ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, mesh.Indices.ToArray());
        Assert.NotNull(shipped);
        Assert.Equal(Tangents, shipped);
    }

    [Fact]
    public void ReadMesh_RecoversGeometry_IgnoringUvs()
    {
        byte[] glb = BuildGlb(Positions, Normals, Uvs, Tangents, Indices);

        using var stream = new MemoryStream(glb);
        Mesh mesh = GltfReader.ReadMesh(stream);

        Assert.Equal(Positions, mesh.Positions.ToArray());
        Assert.Equal(Normals, mesh.Normals.ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, mesh.Indices.ToArray());
    }

    [Fact]
    public void ReadUvMesh_WithoutTangent_ReturnsNullShipped_AndComputesNormals()
    {
        byte[] glb = BuildGlb(Positions, normals: null, Uvs, tangents: null, Indices);

        using var stream = new MemoryStream(glb);
        UvMesh mesh = GltfReader.ReadUvMesh(stream, out Vector4[]? shipped);

        Assert.Null(shipped);
        Assert.Equal(3, mesh.VertexCount);
        foreach (Vector3 n in mesh.Normals)
        {
            Assert.True(float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z));
        }
    }

    private static byte[] BuildGlb(
        Vector3[] pos, Vector3[]? normals, Vector2[] uv, Vector4[]? tangents, ushort[] indices)
    {
        using var bin = new MemoryStream();
        var views = new List<(int Offset, int Length)>();
        var attributes = new List<(string Name, int Accessor)>();
        var accessors = new List<(int View, int ComponentType, int Count, string Type)>();

        int AddView(byte[] data)
        {
            while (bin.Length % 4 != 0)
            {
                bin.WriteByte(0);
            }

            int off = (int)bin.Length;
            bin.Write(data, 0, data.Length);
            views.Add((off, data.Length));
            return views.Count - 1;
        }

        int AddAccessor(int view, int componentType, int count, string type)
        {
            accessors.Add((view, componentType, count, type));
            return accessors.Count - 1;
        }

        attributes.Add(("POSITION", AddAccessor(AddView(Vec3Bytes(pos)), 5126, pos.Length, "VEC3")));
        if (normals is not null)
        {
            attributes.Add(("NORMAL", AddAccessor(AddView(Vec3Bytes(normals)), 5126, normals.Length, "VEC3")));
        }

        attributes.Add(("TEXCOORD_0", AddAccessor(AddView(Vec2Bytes(uv)), 5126, uv.Length, "VEC2")));
        if (tangents is not null)
        {
            attributes.Add(("TANGENT", AddAccessor(AddView(Vec4Bytes(tangents)), 5126, tangents.Length, "VEC4")));
        }

        int indicesAccessor = AddAccessor(AddView(UShortBytes(indices)), 5123, indices.Length, "SCALAR");
        while (bin.Length % 4 != 0)
        {
            bin.WriteByte(0);
        }

        byte[] binBytes = bin.ToArray();
        byte[] jsonBytes = BuildJson(binBytes.Length, views, accessors, attributes, indicesAccessor);
        return Frame(jsonBytes, binBytes);
    }

    private static byte[] BuildJson(
        int bufferLength,
        List<(int Offset, int Length)> views,
        List<(int View, int ComponentType, int Count, string Type)> accessors,
        List<(string Name, int Accessor)> attributes,
        int indicesAccessor)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteStartObject("asset");
            w.WriteString("version", "2.0");
            w.WriteEndObject();

            w.WriteStartArray("buffers");
            w.WriteStartObject();
            w.WriteNumber("byteLength", bufferLength);
            w.WriteEndObject();
            w.WriteEndArray();

            w.WriteStartArray("bufferViews");
            foreach ((int offset, int length) in views)
            {
                w.WriteStartObject();
                w.WriteNumber("buffer", 0);
                w.WriteNumber("byteOffset", offset);
                w.WriteNumber("byteLength", length);
                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartArray("accessors");
            foreach ((int view, int componentType, int count, string type) in accessors)
            {
                w.WriteStartObject();
                w.WriteNumber("bufferView", view);
                w.WriteNumber("componentType", componentType);
                w.WriteNumber("count", count);
                w.WriteString("type", type);
                w.WriteEndObject();
            }

            w.WriteEndArray();

            w.WriteStartArray("meshes");
            w.WriteStartObject();
            w.WriteStartArray("primitives");
            w.WriteStartObject();
            w.WriteStartObject("attributes");
            foreach ((string name, int accessor) in attributes)
            {
                w.WriteNumber(name, accessor);
            }

            w.WriteEndObject();
            w.WriteNumber("indices", indicesAccessor);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }

        return ms.ToArray();
    }

    private static byte[] Frame(byte[] json, byte[] bin)
    {
        byte[] jsonPadded = PadTo4(json, 0x20);
        byte[] binPadded = PadTo4(bin, 0x00);
        int total = 12 + 8 + jsonPadded.Length + 8 + binPadded.Length;

        using var ms = new MemoryStream(total);
        U32(ms, 0x46546C67); // "glTF"
        U32(ms, 2);
        U32(ms, (uint)total);
        U32(ms, (uint)jsonPadded.Length);
        U32(ms, 0x4E4F534A); // "JSON"
        ms.Write(jsonPadded);
        U32(ms, (uint)binPadded.Length);
        U32(ms, 0x004E4942); // "BIN\0"
        ms.Write(binPadded);
        return ms.ToArray();
    }

    private static void U32(Stream stream, uint value)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(word, value);
        stream.Write(word);
    }

    private static byte[] PadTo4(byte[] data, byte pad)
    {
        int padded = (data.Length + 3) & ~3;
        if (padded == data.Length)
        {
            return data;
        }

        var result = new byte[padded];
        Array.Copy(data, result, data.Length);
        for (int i = data.Length; i < padded; i++)
        {
            result[i] = pad;
        }

        return result;
    }

    private static byte[] Vec3Bytes(Vector3[] v)
    {
        var b = new byte[v.Length * 12];
        for (int i = 0; i < v.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan((i * 12) + 0), v[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan((i * 12) + 4), v[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan((i * 12) + 8), v[i].Z);
        }

        return b;
    }

    private static byte[] Vec2Bytes(Vector2[] v)
    {
        var b = new byte[v.Length * 8];
        for (int i = 0; i < v.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan((i * 8) + 0), v[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan((i * 8) + 4), v[i].Y);
        }

        return b;
    }

    private static byte[] Vec4Bytes(Vector4[] v)
    {
        var b = new byte[v.Length * 16];
        for (int i = 0; i < v.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan((i * 16) + 0), v[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan((i * 16) + 4), v[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan((i * 16) + 8), v[i].Z);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan((i * 16) + 12), v[i].W);
        }

        return b;
    }

    private static byte[] UShortBytes(ushort[] v)
    {
        var b = new byte[v.Length * 2];
        for (int i = 0; i < v.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i * 2), v[i]);
        }

        return b;
    }
}
