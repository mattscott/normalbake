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

/// <summary>The kind of map to bake. <see cref="Normal"/> is the default; the rest share the ray core.</summary>
public enum MapType
{
    /// <summary>Tangent- or object-space normal map (the default).</summary>
    Normal,

    /// <summary>World/object-space hit position, normalized to the mesh bounds (RGB).</summary>
    Position,

    /// <summary>Height/displacement — signed distance from the low surface to the high along the normal (grayscale, 0.5 = level).</summary>
    Height,

    /// <summary>Ambient occlusion — fraction of a hemisphere that escapes nearby geometry (grayscale).</summary>
    AmbientOcclusion,

    /// <summary>Thickness — how far rays travel into the solid before exiting (grayscale).</summary>
    Thickness,

    /// <summary>Surface curvature from the normal field — convex bright, concave dark (grayscale, 0.5 = flat).</summary>
    Curvature,

    /// <summary>Per-part flat ID colour with hard (un-antialiased) boundaries.</summary>
    ObjectId,

    /// <summary>Binary coverage mask — white where the low-poly surface occupies UV space, black elsewhere (no dilation).</summary>
    Mask,

    /// <summary>Anti-aliased coverage — grayscale = fraction of supersamples on the surface (soft edges, no dilation).</summary>
    Coverage,
}

/// <summary>The frame a baked normal is expressed in.</summary>
public enum NormalSpace
{
    /// <summary>Per-pixel TBN frame — reusable on deforming/tiled/mirrored meshes (the default).</summary>
    Tangent,

    /// <summary>The model's local frame — "rainbow" map; static, unique-UV props; no runtime TBN.</summary>
    Object,
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

    /// <summary>The kind of map to bake (normal by default).</summary>
    public MapType Map { get; init; } = MapType.Normal;

    /// <summary>The frame the baked normal is expressed in (tangent-space by default).</summary>
    public NormalSpace Space { get; init; } = NormalSpace.Tangent;

    /// <summary>Tangent-space normal depth scale (1 = unchanged, 2 = twice the apparent depth, &lt;1 flatter).</summary>
    public float NormalStrength { get; init; } = 1f;

    /// <summary>Hemisphere ray count for AO / thickness / bent-normal maps.</summary>
    public int OcclusionSamples { get; init; } = 32;

    /// <summary>Post-bake coverage-aware Gaussian blur radius in texels (0 = off). Smooths AO/curvature.</summary>
    public int BlurRadius { get; init; }

    /// <summary>For the height map: auto-scale to full contrast around a mid-gray neutral — recesses toward
    /// black, bumps toward white — using a robust per-side percentile instead of the wide cage envelope.</summary>
    public bool NormalizeHeight { get; init; }

    /// <summary>Maximum occlusion ray distance (model units); 0 auto-derives from the bounds.</summary>
    public double OcclusionRadius { get; init; }

    /// <summary>When a cage ray finds no hit, sample the nearest high-poly surface point instead (fills holes).</summary>
    public bool NearestPointFallback { get; init; } = true;

    /// <summary>
    /// When true (default), a UV-contract error (e.g. overlapping islands) aborts the bake. Set false to
    /// bake anyway — needed for intentionally <b>mirrored/stacked UVs</b>, where symmetric parts share UV
    /// space (the handedness <c>w</c> keeps the mirror correct).
    /// </summary>
    public bool StrictUvValidation { get; init; } = true;

    /// <summary>UDIM tile column to bake: UVs are shifted by −<see cref="TileU"/> in U into the 0–1 output.</summary>
    public int TileU { get; init; }

    /// <summary>UDIM tile row to bake: UVs are shifted by −<see cref="TileV"/> in V into the 0–1 output.</summary>
    public int TileV { get; init; }

    /// <summary>Bits per channel of the output image (8 or 16).</summary>
    public int BitDepth { get; init; } = 16;

    /// <summary>Output image format.</summary>
    public OutputFormat Format { get; init; } = OutputFormat.Png;

    /// <summary>Worker thread count; -1 uses all available cores.</summary>
    public int Threads { get; init; } = -1;
}
