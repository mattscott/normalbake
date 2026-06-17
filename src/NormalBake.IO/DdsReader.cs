using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;
using NormalBake.Core;

namespace NormalBake.IO;

/// <summary>Decodes a BCn DDS back to an (8-bit-per-channel) <see cref="BakedMap"/> via BCnEncoder.NET.</summary>
public static class DdsReader
{
    /// <summary>Reads and decodes a DDS file.</summary>
    public static BakedMap Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads and decodes a DDS stream (first mip level).</summary>
    public static BakedMap Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var decoder = new BcDecoder();
        Memory2D<ColorRgba32> image = decoder.Decode2D(stream);
        int height = image.Height;
        int width = image.Width;

        var map = new BakedMap(width, height);
        ReadOnlySpan2D<ColorRgba32> span = image.Span;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ColorRgba32 c = span[y, x];
                map.SetPixel(x, y, c.r / 255f, c.g / 255f, c.b / 255f);
            }
        }

        return map;
    }
}
