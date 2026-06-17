using System.IO.Compression;
using NormalBake.Core;

namespace NormalBake.IO;

/// <summary>
/// A small, dependency-free PNG encoder for baked maps. Writes truecolor RGB at 8 or 16 bits per
/// channel; samples are big-endian per the PNG spec, scanlines use filter type 0 (None), and the image
/// data is a single zlib stream (<see cref="ZLibStream"/> — deterministic for a given input). No
/// <c>gAMA</c>/<c>sRGB</c>/<c>iCCP</c> chunk is written: a normal map is <b>linear, non-color</b> data
/// and must not carry a color transform (<c>Agents/Documents/01-normal-mapping-fundamentals.md</c>).
/// 16-bit is the default because it avoids visible banding on smooth normals.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>Writes a baked map to a PNG file (16-bit by default).</summary>
    public static void Write(string path, BakedMap map, int bitDepth = 16)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);
        using var stream = File.Create(path);
        Write(stream, map, bitDepth);
    }

    /// <summary>Writes a baked map to a stream as PNG. <paramref name="bitDepth"/> must be 8 or 16.</summary>
    public static void Write(Stream stream, BakedMap map, int bitDepth = 16)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(map);
        if (bitDepth is not (8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "bit depth must be 8 or 16.");
        }

        byte[] idat = DeflateScanlines(map, bitDepth);

        stream.Write(Signature, 0, Signature.Length);

        Span<byte> ihdr = stackalloc byte[13];
        WriteBe32(ihdr[..4], (uint)map.Width);
        WriteBe32(ihdr.Slice(4, 4), (uint)map.Height);
        ihdr[8] = (byte)bitDepth;
        ihdr[9] = 2;  // color type 2 = truecolor RGB
        ihdr[10] = 0; // compression: deflate
        ihdr[11] = 0; // filter method 0
        ihdr[12] = 0; // no interlace
        WriteChunk(stream, "IHDR", ihdr);

        WriteChunk(stream, "IDAT", idat);
        WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
    }

    /// <summary>Builds the filtered raw scanlines (filter 0 per row) and zlib-compresses them.</summary>
    private static byte[] DeflateScanlines(BakedMap map, int bitDepth)
    {
        ReadOnlySpan<float> pixels = map.Pixels;
        int width = map.Width;
        int height = map.Height;
        int bytesPerSample = bitDepth == 16 ? 2 : 1;
        int stride = width * BakedMap.ChannelCount * bytesPerSample;
        var raw = new byte[height * (1 + stride)];

        int dst = 0;
        for (int y = 0; y < height; y++)
        {
            raw[dst++] = 0; // filter type: None
            int rowBase = y * width * BakedMap.ChannelCount;
            if (bitDepth == 16)
            {
                for (int i = 0; i < width * BakedMap.ChannelCount; i++)
                {
                    ushort s = ToUInt16(pixels[rowBase + i]);
                    raw[dst++] = (byte)(s >> 8);
                    raw[dst++] = (byte)s;
                }
            }
            else
            {
                for (int i = 0; i < width * BakedMap.ChannelCount; i++)
                {
                    raw[dst++] = ToByte(pixels[rowBase + i]);
                }
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }

        return compressed.ToArray();
    }

    private static ushort ToUInt16(float v) => (ushort)(int)(Math.Clamp(v, 0f, 1f) * 65535f + 0.5f);

    private static byte ToByte(float v) => (byte)(int)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        WriteBe32(header[..4], (uint)data.Length);
        header[4] = (byte)type[0];
        header[5] = (byte)type[1];
        header[6] = (byte)type[2];
        header[7] = (byte)type[3];
        stream.Write(header);
        stream.Write(data);

        uint crc = Crc32.Seed;
        crc = Crc32.Update(crc, header.Slice(4, 4)); // CRC covers type + data
        crc = Crc32.Update(crc, data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBe32(crcBytes, Crc32.Finalize(crc));
        stream.Write(crcBytes);
    }

    private static void WriteBe32(Span<byte> dst, uint value)
    {
        dst[0] = (byte)(value >> 24);
        dst[1] = (byte)(value >> 16);
        dst[2] = (byte)(value >> 8);
        dst[3] = (byte)value;
    }

    /// <summary>PNG CRC-32 (ISO-HDLC: reflected poly 0xEDB88320, pre/post-inverted).</summary>
    private static class Crc32
    {
        public const uint Seed = 0xFFFFFFFFu;

        private static readonly uint[] Table = BuildTable();

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte b in data)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        public static uint Finalize(uint crc) => crc ^ 0xFFFFFFFFu;

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
