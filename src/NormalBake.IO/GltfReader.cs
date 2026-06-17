using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using NormalBake.Core;

namespace NormalBake.IO;

/// <summary>
/// A minimal reader for self-contained binary glTF (<c>.glb</c>) meshes. It pulls the first primitive
/// that has POSITION + TEXCOORD_0 and returns it as a <see cref="UvMesh"/>, plus the file's shipped
/// <c>TANGENT</c> (typically MikkTSpace-generated) as a reference for validating our tangent solver.
/// Supports float vertex attributes and unsigned byte/short/int indices — enough for the Khronos sample
/// assets (<c>NormalTangentTest</c> / <c>NormalTangentMirrorTest</c>, <c>DamagedHelmet</c>).
/// </summary>
public static class GltfReader
{
    private const uint GlbMagic = 0x46546C67;     // "glTF"
    private const uint ChunkJson = 0x4E4F534A;    // "JSON"
    private const uint ChunkBin = 0x004E4942;     // "BIN\0"

    /// <summary>Reads the first UV-bearing primitive from a <c>.glb</c> file.</summary>
    public static UvMesh ReadUvMesh(string path, out Vector4[]? shippedTangents)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return ReadUvMesh(stream, out shippedTangents);
    }

    /// <summary>Reads the first UV-bearing primitive from a <c>.glb</c> stream.</summary>
    public static UvMesh ReadUvMesh(Stream stream, out Vector4[]? shippedTangents)
    {
        ArgumentNullException.ThrowIfNull(stream);
        (byte[] json, byte[] bin) = ReadGlb(stream);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        JsonElement primitive = FindUvPrimitive(root);
        JsonElement attributes = primitive.GetProperty("attributes");

        Vector3[] positions = ReadVec3(root, bin, attributes.GetProperty("POSITION").GetInt32());
        Vector2[] uvs = ReadVec2(root, bin, attributes.GetProperty("TEXCOORD_0").GetInt32());
        int[] indices = primitive.TryGetProperty("indices", out JsonElement idx)
            ? ReadIndices(root, bin, idx.GetInt32())
            : Sequential(positions.Length);

        Vector3[] normals = attributes.TryGetProperty("NORMAL", out JsonElement nrm)
            ? ReadVec3(root, bin, nrm.GetInt32())
            : MeshNormals.ComputeSmooth(positions, indices);

        shippedTangents = attributes.TryGetProperty("TANGENT", out JsonElement tan)
            ? ReadVec4(root, bin, tan.GetInt32())
            : null;

        return new UvMesh(positions, normals, uvs, indices);
    }

    private static JsonElement FindUvPrimitive(JsonElement root)
    {
        JsonElement? fallback = null;
        foreach (JsonElement mesh in root.GetProperty("meshes").EnumerateArray())
        {
            foreach (JsonElement prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                if (!prim.TryGetProperty("attributes", out JsonElement attr)
                    || !attr.TryGetProperty("POSITION", out _)
                    || !attr.TryGetProperty("TEXCOORD_0", out _))
                {
                    continue;
                }

                if (attr.TryGetProperty("TANGENT", out _))
                {
                    return prim; // prefer a primitive that ships reference tangents
                }

                fallback ??= prim;
            }
        }

        return fallback ?? throw new InvalidDataException("glTF has no primitive with POSITION + TEXCOORD_0.");
    }

    private static (byte[] Json, byte[] Bin) ReadGlb(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        byte[] data = ms.ToArray();
        if (data.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(data) != GlbMagic)
        {
            throw new InvalidDataException("not a binary glTF (.glb) stream.");
        }

        byte[]? json = null;
        byte[]? bin = null;
        int pos = 12; // skip magic + version + length
        while (pos + 8 <= data.Length)
        {
            int len = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            int start = pos + 8;
            if (type == ChunkJson)
            {
                json = data[start..(start + len)];
            }
            else if (type == ChunkBin)
            {
                bin = data[start..(start + len)];
            }

            pos = start + len;
        }

        if (json is null)
        {
            throw new InvalidDataException("glTF JSON chunk missing.");
        }

        return (json, bin ?? Array.Empty<byte>());
    }

    private static Vector2[] ReadVec2(JsonElement root, byte[] bin, int accessor)
    {
        float[] f = ReadFloats(root, bin, accessor, 2, out int count);
        var result = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new Vector2(f[(i * 2) + 0], f[(i * 2) + 1]);
        }

        return result;
    }

    private static Vector3[] ReadVec3(JsonElement root, byte[] bin, int accessor)
    {
        float[] f = ReadFloats(root, bin, accessor, 3, out int count);
        var result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new Vector3(f[(i * 3) + 0], f[(i * 3) + 1], f[(i * 3) + 2]);
        }

        return result;
    }

    private static Vector4[] ReadVec4(JsonElement root, byte[] bin, int accessor)
    {
        float[] f = ReadFloats(root, bin, accessor, 4, out int count);
        var result = new Vector4[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new Vector4(f[(i * 4) + 0], f[(i * 4) + 1], f[(i * 4) + 2], f[(i * 4) + 3]);
        }

        return result;
    }

    private static float[] ReadFloats(JsonElement root, byte[] bin, int accessor, int components, out int count)
    {
        JsonElement acc = root.GetProperty("accessors")[accessor];
        if (acc.GetProperty("componentType").GetInt32() != 5126)
        {
            throw new InvalidDataException("only float (5126) vertex attributes are supported.");
        }

        count = acc.GetProperty("count").GetInt32();
        int accOffset = acc.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;
        (int viewOffset, int stride) = BufferView(root, acc, components * sizeof(float));

        var result = new float[count * components];
        for (int i = 0; i < count; i++)
        {
            int elem = viewOffset + accOffset + (i * stride);
            for (int c = 0; c < components; c++)
            {
                result[(i * components) + c] =
                    BinaryPrimitives.ReadSingleLittleEndian(bin.AsSpan(elem + (c * sizeof(float))));
            }
        }

        return result;
    }

    private static int[] ReadIndices(JsonElement root, byte[] bin, int accessor)
    {
        JsonElement acc = root.GetProperty("accessors")[accessor];
        int componentType = acc.GetProperty("componentType").GetInt32();
        int count = acc.GetProperty("count").GetInt32();
        int accOffset = acc.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;
        int size = componentType switch
        {
            5121 => 1, // unsigned byte
            5123 => 2, // unsigned short
            5125 => 4, // unsigned int
            _ => throw new InvalidDataException($"unsupported index component type {componentType}."),
        };
        (int viewOffset, int stride) = BufferView(root, acc, size);

        var result = new int[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> at = bin.AsSpan(viewOffset + accOffset + (i * stride));
            result[i] = componentType switch
            {
                5121 => at[0],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(at),
                _ => (int)BinaryPrimitives.ReadUInt32LittleEndian(at),
            };
        }

        return result;
    }

    private static (int Offset, int Stride) BufferView(JsonElement root, JsonElement accessor, int tightStride)
    {
        JsonElement view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        int offset = view.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0;
        int stride = view.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : tightStride;
        return (offset, stride);
    }

    private static int[] Sequential(int count)
    {
        var result = new int[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = i;
        }

        return result;
    }
}
