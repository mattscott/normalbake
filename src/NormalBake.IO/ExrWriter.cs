using System.Buffers.Binary;
using System.Text;
using NormalBake.Core;

namespace NormalBake.IO;

/// <summary>
/// A small, dependency-free OpenEXR writer: 32-bit float, uncompressed scanline, RGB. EXR archives the
/// baked values at full float precision (no 8/16-bit quantization). Channels are stored alphabetically
/// (B, G, R) per the spec; data is little-endian. (Jither.OpenEXR is not on NuGet, so this is hand-rolled
/// in the same spirit as the PNG writer.)
/// </summary>
public static class ExrWriter
{
    private const int PixelTypeFloat = 2;

    /// <summary>Writes the baked map as a float EXR file.</summary>
    public static void Write(string path, BakedMap map)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);
        using var stream = File.Create(path);
        Write(stream, map);
    }

    /// <summary>Writes the baked map as a float EXR stream.</summary>
    public static void Write(Stream stream, BakedMap map)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(map);
        int width = map.Width;
        int height = map.Height;

        using var header = new MemoryStream();
        WriteHeader(header, width, height);

        long blocksStart = header.Length + ((long)height * 8);
        long blockSize = 8 + ((long)width * 3 * sizeof(float));

        header.Position = 0;
        header.CopyTo(stream);

        Span<byte> word8 = stackalloc byte[8];
        for (int y = 0; y < height; y++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(word8, (ulong)(blocksStart + (y * blockSize)));
            stream.Write(word8);
        }

        ReadOnlySpan<float> pixels = map.Pixels;
        var row = new byte[width * sizeof(float)];
        for (int y = 0; y < height; y++)
        {
            WriteI32(stream, y);
            WriteI32(stream, width * 3 * sizeof(float));
            WriteChannel(stream, pixels, y, width, channel: 2, row); // B
            WriteChannel(stream, pixels, y, width, channel: 1, row); // G
            WriteChannel(stream, pixels, y, width, channel: 0, row); // R
        }
    }

    private static void WriteChannel(Stream stream, ReadOnlySpan<float> pixels, int y, int width, int channel, byte[] row)
    {
        int rowBase = y * width * BakedMap.ChannelCount;
        for (int x = 0; x < width; x++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                row.AsSpan(x * sizeof(float)), pixels[rowBase + (x * BakedMap.ChannelCount) + channel]);
        }

        stream.Write(row, 0, row.Length);
    }

    private static void WriteHeader(Stream stream, int width, int height)
    {
        Span<byte> magic = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(magic, 20000630); // 0x01312f76 EXR magic
        BinaryPrimitives.WriteInt32LittleEndian(magic[4..], 2);   // version 2, no flags
        stream.Write(magic);

        WriteAttribute(stream, "channels", "chlist", ChannelListValue());
        WriteAttribute(stream, "compression", "compression", new byte[] { 0 }); // NO_COMPRESSION
        WriteAttribute(stream, "dataWindow", "box2i", Box2I(0, 0, width - 1, height - 1));
        WriteAttribute(stream, "displayWindow", "box2i", Box2I(0, 0, width - 1, height - 1));
        WriteAttribute(stream, "lineOrder", "lineOrder", new byte[] { 0 }); // INCREASING_Y
        WriteAttribute(stream, "pixelAspectRatio", "float", FloatBytes(1f));
        WriteAttribute(stream, "screenWindowCenter", "v2f", V2F(0f, 0f));
        WriteAttribute(stream, "screenWindowWidth", "float", FloatBytes(1f));
        stream.WriteByte(0); // end of header
    }

    private static byte[] ChannelListValue()
    {
        using var ms = new MemoryStream();
        foreach (string name in new[] { "B", "G", "R" }) // alphabetical order required
        {
            WriteNullString(ms, name);
            WriteI32(ms, PixelTypeFloat);
            ms.WriteByte(1); // pLinear
            ms.Write(new byte[3]); // reserved
            WriteI32(ms, 1); // xSampling
            WriteI32(ms, 1); // ySampling
        }

        ms.WriteByte(0); // end of channel list
        return ms.ToArray();
    }

    private static void WriteAttribute(Stream stream, string name, string type, byte[] value)
    {
        WriteNullString(stream, name);
        WriteNullString(stream, type);
        WriteI32(stream, value.Length);
        stream.Write(value, 0, value.Length);
    }

    private static byte[] Box2I(int xMin, int yMin, int xMax, int yMax)
    {
        var b = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), xMin);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), yMin);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), xMax);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), yMax);
        return b;
    }

    private static byte[] V2F(float a, float b)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(0), a);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), b);
        return bytes;
    }

    private static byte[] FloatBytes(float v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, v);
        return b;
    }

    private static void WriteNullString(Stream stream, string s)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    private static void WriteI32(Stream stream, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        stream.Write(b);
    }
}
