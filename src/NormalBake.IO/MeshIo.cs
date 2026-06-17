namespace NormalBake.IO;

/// <summary>
/// Entry point for mesh and image I/O. The readers/writers (OBJ meshes, PNG/EXR/DDS images) are
/// implemented in Phase 1+; see <c>Agents/Plans/02-phase-roadmap.md</c>. This placeholder anchors the
/// namespace and records the formats the I/O layer starts with.
/// </summary>
public static class MeshIo
{
    /// <summary>The mesh format read and written first (Wavefront OBJ, with UVs).</summary>
    public const string PrimaryMeshExtension = ".obj";

    /// <summary>The image format written first for baked maps (16-bit PNG, linear/non-color).</summary>
    public const string PrimaryImageExtension = ".png";
}
