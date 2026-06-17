namespace NormalBake.Core;

/// <summary>
/// Green-channel convention of an encoded tangent-space normal map. The two conventions differ only
/// in the sign of the Y (green) channel. See <c>Agents/Documents/01-normal-mapping-fundamentals.md</c>.
/// </summary>
public enum NormalMapConvention
{
    /// <summary>+Y / green-up (Blender, Unity, Godot, glTF).</summary>
    OpenGl,

    /// <summary>-Y / green-down (Unreal, 3ds Max, Source).</summary>
    DirectX,
}

/// <summary>Output image format for a baked map.</summary>
public enum OutputFormat
{
    /// <summary>PNG (8- or 16-bit). The default lossless output; written as linear/non-color data.</summary>
    Png,

    /// <summary>OpenEXR (float) — archival / high precision.</summary>
    Exr,

    /// <summary>DDS with BCn block compression — GPU-ready engine delivery (BC5 for tangent-space).</summary>
    Dds,
}

/// <summary>
/// Options controlling a normal-map bake. The defaults follow the recommendations in
/// <c>Agents/Documents</c> (tangent-space output, OpenGL convention, 16-bit, 4x supersampling).
/// </summary>
public sealed class NormalBakeOptions
{
    /// <summary>Output texture width and height in texels. Must be a multiple of 4 for BCn output.</summary>
    public int Resolution { get; init; } = 2048;

    /// <summary>Supersampling grid size N (N x N rays per texel) used for anti-aliasing.</summary>
    public int Samples { get; init; } = 4;

    /// <summary>Outer (frontal) ray-cast distance of the projection envelope, in model units.</summary>
    public double FrontalDistance { get; init; }

    /// <summary>Inner (rear) ray-cast distance of the projection envelope, in model units.</summary>
    public double RearDistance { get; init; }

    /// <summary>Green-channel convention of the written normal map.</summary>
    public NormalMapConvention Convention { get; init; } = NormalMapConvention.OpenGl;

    /// <summary>Bits per channel of the output image (8 or 16).</summary>
    public int BitDepth { get; init; } = 16;

    /// <summary>Output image format.</summary>
    public OutputFormat Format { get; init; } = OutputFormat.Png;

    /// <summary>Worker thread count; -1 uses all available cores.</summary>
    public int Threads { get; init; } = -1;
}
