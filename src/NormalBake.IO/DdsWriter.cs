using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using CommunityToolkit.HighPerformance;
using NormalBake.Core;

namespace NormalBake.IO;

/// <summary>Block-compressed DDS variants for GPU delivery. BC5 is the standard for tangent-space normals.</summary>
public enum BlockCompression
{
    /// <summary>Two-channel (R,G) — the standard tangent-space normal format (B reconstructed in-shader).</summary>
    Bc5,

    /// <summary>High-quality RGBA block compression.</summary>
    Bc7,
}

/// <summary>
/// Writes a baked map as a block-compressed DDS via BCnEncoder.NET (pure-managed, MIT/Unlicense — the
/// vetted BC encoder, <c>Agents/Documents/08</c> §3.1). BC5 stores the normal's X/Y; the engine
/// reconstructs Z. Round-trip with <see cref="DdsReader"/> to compare against the uncompressed map.
/// </summary>
public static class DdsWriter
{
    /// <summary>Writes the map as a BCn DDS file (BC5 by default).</summary>
    public static void Write(string path, BakedMap map, BlockCompression format = BlockCompression.Bc5)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);
        using var stream = File.Create(path);
        Write(stream, map, format);
    }

    /// <summary>Writes the map as a BCn DDS stream (BC5 by default).</summary>
    public static void Write(Stream stream, BakedMap map, BlockCompression format = BlockCompression.Bc5)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(map);

        var encoder = new BcEncoder();
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = CompressionQuality.BestQuality;
        encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;
        encoder.OutputOptions.Format =
            format == BlockCompression.Bc7 ? CompressionFormat.Bc7 : CompressionFormat.Bc5;

        ColorRgba32[] pixels = ToColors(map);
        ReadOnlyMemory2D<ColorRgba32> image = pixels.AsMemory().AsMemory2D(map.Height, map.Width);
        encoder.EncodeToStream(image, stream);
    }

    internal static ColorRgba32[] ToColors(BakedMap map)
    {
        ReadOnlySpan<float> p = map.Pixels;
        var pixels = new ColorRgba32[map.Width * map.Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            int s = i * BakedMap.ChannelCount;
            pixels[i] = new ColorRgba32(ToByte(p[s]), ToByte(p[s + 1]), ToByte(p[s + 2]), 255);
        }

        return pixels;
    }

    private static byte ToByte(float v) => (byte)(int)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);
}
