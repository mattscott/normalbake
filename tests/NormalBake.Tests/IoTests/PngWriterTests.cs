using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using NormalBake.Core;
using NormalBake.IO;
using Xunit;

namespace NormalBake.Tests;

/// <summary>Verifies the hand-rolled PNG writer: structure, linear (no color chunk), and exact samples.</summary>
public class PngWriterTests
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    [Fact]
    public void Write16Bit_ProducesValidStructure_AndExactSamples()
    {
        var map = new BakedMap(2, 1);
        map.SetPixel(0, 0, 0.5f, 0.5f, 1.0f); // → 32768, 32768, 65535
        map.SetPixel(1, 0, 0.0f, 0.0f, 0.0f); // → 0, 0, 0

        byte[] png = WriteToBytes(map, 16);
        DecodedPng decoded = Decode(png);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(1, decoded.Height);
        Assert.Equal(16, decoded.BitDepth);
        Assert.Equal(2, decoded.ColorType); // truecolor RGB
        Assert.Equal(new ushort[] { 32768, 32768, 65535, 0, 0, 0 }, decoded.Samples16);
    }

    [Fact]
    public void Write_OmitsColorManagementChunks_SoDataIsLinear()
    {
        var map = new BakedMap(1, 1);
        map.SetPixel(0, 0, 0.5f, 0.5f, 1.0f);

        DecodedPng decoded = Decode(WriteToBytes(map, 16));

        Assert.DoesNotContain("gAMA", decoded.ChunkTypes);
        Assert.DoesNotContain("sRGB", decoded.ChunkTypes);
        Assert.DoesNotContain("iCCP", decoded.ChunkTypes);
        Assert.Contains("IHDR", decoded.ChunkTypes);
        Assert.Contains("IDAT", decoded.ChunkTypes);
        Assert.Contains("IEND", decoded.ChunkTypes);
    }

    [Fact]
    public void Write_IsByteStable_ForTheSameInput()
    {
        var map = new BakedMap(4, 4);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                map.SetPixel(x, y, x / 3f, y / 3f, 0.5f);
            }
        }

        Assert.Equal(WriteToBytes(map, 16), WriteToBytes(map, 16));
    }

    [Fact]
    public void Write8Bit_RoundsAndClamps()
    {
        var map = new BakedMap(2, 1);
        map.SetPixel(0, 0, 0.5f, 2.0f, -1.0f); // → 128, 255 (clamped), 0 (clamped)
        map.SetPixel(1, 0, 1.0f, 0.0f, 0.0f);  // → 255, 0, 0

        DecodedPng decoded = Decode(WriteToBytes(map, 8));

        Assert.Equal(8, decoded.BitDepth);
        Assert.Equal(new byte[] { 128, 255, 0, 255, 0, 0 }, decoded.Samples8);
    }

    private static byte[] WriteToBytes(BakedMap map, int bitDepth)
    {
        using var stream = new MemoryStream();
        PngWriter.Write(stream, map, bitDepth);
        return stream.ToArray();
    }

    private static DecodedPng Decode(byte[] png)
    {
        Assert.Equal(Signature, png[..8]);
        var chunkTypes = new List<string>();
        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = 0;
        var idat = new MemoryStream();

        int pos = 8;
        while (pos < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos, 4));
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            int dataStart = pos + 8;
            chunkTypes.Add(type);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(dataStart, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(dataStart + 4, 4));
                bitDepth = png[dataStart + 8];
                colorType = png[dataStart + 9];
            }
            else if (type == "IDAT")
            {
                idat.Write(png, dataStart, length);
            }

            pos = dataStart + length + 4; // skip data + CRC
        }

        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);
        byte[] rawBytes = raw.ToArray();

        int bytesPerSample = bitDepth == 16 ? 2 : 1;
        int stride = width * 3 * bytesPerSample;
        var samples16 = new List<ushort>();
        var samples8 = new List<byte>();
        for (int y = 0; y < height; y++)
        {
            int rowStart = (y * (stride + 1)) + 1; // skip filter byte (0)
            for (int i = 0; i < width * 3; i++)
            {
                if (bitDepth == 16)
                {
                    samples16.Add(BinaryPrimitives.ReadUInt16BigEndian(rawBytes.AsSpan(rowStart + (i * 2), 2)));
                }
                else
                {
                    samples8.Add(rawBytes[rowStart + i]);
                }
            }
        }

        return new DecodedPng(
            width, height, bitDepth, colorType, chunkTypes, samples16.ToArray(), samples8.ToArray());
    }

    private sealed record DecodedPng(
        int Width,
        int Height,
        int BitDepth,
        int ColorType,
        IReadOnlyList<string> ChunkTypes,
        ushort[] Samples16,
        byte[] Samples8);
}
