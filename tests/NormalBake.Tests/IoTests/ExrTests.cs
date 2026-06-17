using System.Buffers.Binary;
using NormalBake.Core;
using NormalBake.IO;
using Xunit;

namespace NormalBake.Tests;

/// <summary>P5: the hand-rolled float EXR writer round-trips exactly (uncompressed = lossless).</summary>
public class ExrTests
{
    [Fact]
    public void Exr_RoundTripsFloatRgbExactly()
    {
        var map = new BakedMap(5, 3);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                map.SetPixel(x, y, x / 4f, y / 2f, 0.123456f);
            }
        }

        using var ms = new MemoryStream();
        ExrWriter.Write(ms, map);
        byte[] exr = ms.ToArray();
        Assert.Equal(20000630, BinaryPrimitives.ReadInt32LittleEndian(exr));

        BakedMap decoded = ParseExr(exr);
        Assert.Equal(map.Width, decoded.Width);
        Assert.Equal(map.Height, decoded.Height);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                Assert.Equal(map.GetPixel(x, y), decoded.GetPixel(x, y));
            }
        }
    }

    private static BakedMap ParseExr(byte[] exr)
    {
        int pos = 8; // skip magic + version
        int width = 0;
        int height = 0;
        while (true)
        {
            string name = ReadNullString(exr, ref pos);
            if (name.Length == 0)
            {
                break; // end of header
            }

            ReadNullString(exr, ref pos); // type
            int size = ReadI32(exr, ref pos);
            if (name == "dataWindow")
            {
                int xMin = BinaryPrimitives.ReadInt32LittleEndian(exr.AsSpan(pos));
                int yMin = BinaryPrimitives.ReadInt32LittleEndian(exr.AsSpan(pos + 4));
                int xMax = BinaryPrimitives.ReadInt32LittleEndian(exr.AsSpan(pos + 8));
                int yMax = BinaryPrimitives.ReadInt32LittleEndian(exr.AsSpan(pos + 12));
                width = xMax - xMin + 1;
                height = yMax - yMin + 1;
            }

            pos += size;
        }

        pos += height * 8; // skip the scanline offset table

        var map = new BakedMap(width, height);
        for (int row = 0; row < height; row++)
        {
            int y = ReadI32(exr, ref pos);
            ReadI32(exr, ref pos); // data size
            float[] b = ReadFloats(exr, ref pos, width);
            float[] g = ReadFloats(exr, ref pos, width);
            float[] r = ReadFloats(exr, ref pos, width);
            for (int x = 0; x < width; x++)
            {
                map.SetPixel(x, y, r[x], g[x], b[x]);
            }
        }

        return map;
    }

    private static float[] ReadFloats(byte[] data, ref int pos, int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pos));
            pos += 4;
        }

        return values;
    }

    private static string ReadNullString(byte[] data, ref int pos)
    {
        int start = pos;
        while (data[pos] != 0)
        {
            pos++;
        }

        string s = System.Text.Encoding.ASCII.GetString(data, start, pos - start);
        pos++; // skip null
        return s;
    }

    private static int ReadI32(byte[] data, ref int pos)
    {
        int v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
        pos += 4;
        return v;
    }
}
