namespace NormalBake.Core;

/// <summary>
/// A baked texture: a row-major RGB image of linear (non-color) float samples in <c>[0, 1]</c>, top row
/// first. For a tangent-space normal map a flat normal <c>(0,0,1)</c> encodes to <c>(0.5,0.5,1.0)</c>.
/// Image writers (e.g. the 16-bit PNG writer) consume this directly; the data is <b>linear</b> and must
/// be written with no sRGB/gamma transfer (<c>docs/design/01-normal-mapping-fundamentals.md</c>).
/// </summary>
public sealed class BakedMap
{
    /// <summary>Number of channels per texel (RGB).</summary>
    public const int ChannelCount = 3;

    private readonly float[] _pixels;

    /// <summary>Creates a zero-filled map of the given size.</summary>
    public BakedMap(int width, int height)
        : this(width, height, new float[checked(width * height * ChannelCount)])
    {
    }

    /// <summary>Creates a map backed by an existing row-major RGB buffer (length <c>width*height*3</c>).</summary>
    public BakedMap(int width, int height, float[] pixels)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "height must be positive.");
        }

        ArgumentNullException.ThrowIfNull(pixels);
        int expected = checked(width * height * ChannelCount);
        if (pixels.Length != expected)
        {
            throw new ArgumentException(
                $"pixels length ({pixels.Length}) must equal width*height*{ChannelCount} ({expected}).",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels;
    }

    /// <summary>Image width in texels.</summary>
    public int Width { get; }

    /// <summary>Image height in texels.</summary>
    public int Height { get; }

    /// <summary>Row-major RGB samples in <c>[0, 1]</c> (linear), top row first.</summary>
    public ReadOnlySpan<float> Pixels => _pixels;

    // Direct array access for Core writers/baker hot paths; internal keeps it off the public surface.
    internal float[] PixelArray => _pixels;

    /// <summary>Writes one RGB texel. Values are stored verbatim (clamp at encode time).</summary>
    public void SetPixel(int x, int y, float r, float g, float b)
    {
        int i = Offset(x, y);
        _pixels[i] = r;
        _pixels[i + 1] = g;
        _pixels[i + 2] = b;
    }

    /// <summary>Reads one RGB texel.</summary>
    public (float R, float G, float B) GetPixel(int x, int y)
    {
        int i = Offset(x, y);
        return (_pixels[i], _pixels[i + 1], _pixels[i + 2]);
    }

    private int Offset(int x, int y)
    {
        if ((uint)x >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "x is outside the image.");
        }

        if ((uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "y is outside the image.");
        }

        return ((y * Width) + x) * ChannelCount;
    }
}
