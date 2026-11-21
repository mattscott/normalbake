using System.Buffers.Binary;
using System.Text;
using NormalBake.IO;
using Xunit;

namespace NormalBake.Tests;

/// <summary>
/// The glTF reader treats input as untrusted. Each hand-built hostile <c>.glb</c> below must be rejected
/// quickly with <see cref="InvalidDataException"/>: no hang and no allocation sized by an untrusted count.
/// </summary>
public class GltfHostileInputTests
{
    private const uint Magic = 0x46546C67;  // "glTF"
    private const uint ChunkJson = 0x4E4F534A;
    private const uint ChunkBin = 0x004E4942;

    [Fact(Timeout = 10_000)]
    public async Task Chunk_with_negative_length_is_rejected_without_hanging()
    {
        // An unknown chunk type with length -8 leaves the read position where it was: an endless loop.
        var glb = Header();
        glb.AddRange(Chunk(ChunkJson, Encoding.UTF8.GetBytes("{}  ")));
        glb.AddRange(Int32(-8));
        glb.AddRange(UInt32(0x12345678));
        await AssertRejectedAsync(glb);
    }

    [Fact(Timeout = 10_000)]
    public async Task Accessor_count_larger_than_the_buffer_is_rejected_before_allocating()
    {
        // POSITION claims 500 million VEC3 floats (6 GB) backed by a 12-byte binary chunk.
        const string json =
            "{\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}]," +
            "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":500000000,\"type\":\"VEC3\"}]," +
            "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":12}]}";
        var glb = Header();
        glb.AddRange(Chunk(ChunkJson, Pad(Encoding.UTF8.GetBytes(json))));
        glb.AddRange(Chunk(ChunkBin, new byte[12]));
        InvalidDataException ex = await AssertRejectedAsync(glb);
        Assert.Contains("binary chunk has 12", ex.Message);
    }

    private static async Task<InvalidDataException> AssertRejectedAsync(List<byte> glb)
    {
        byte[] bytes = glb.ToArray();
        return await Task.Run(() =>
        {
            using var stream = new MemoryStream(bytes);
            return Assert.Throws<InvalidDataException>(() => GltfReader.ReadMesh(stream));
        });
    }

    private static List<byte> Header()
    {
        var b = new List<byte>();
        b.AddRange(UInt32(Magic));
        b.AddRange(UInt32(2));
        b.AddRange(UInt32(0)); // total length (not relied on by the reader)
        return b;
    }

    private static IEnumerable<byte> Chunk(uint type, byte[] payload)
    {
        var b = new List<byte>();
        b.AddRange(Int32(payload.Length));
        b.AddRange(UInt32(type));
        b.AddRange(payload);
        return b;
    }

    private static byte[] Pad(byte[] json)
    {
        int padded = (json.Length + 3) & ~3;
        var b = new byte[padded];
        Array.Fill(b, (byte)' ');
        json.CopyTo(b, 0);
        return b;
    }

    private static byte[] Int32(int v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, v);
        return b;
    }

    private static byte[] UInt32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }
}
