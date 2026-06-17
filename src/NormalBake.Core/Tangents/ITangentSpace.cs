namespace NormalBake.Core;

/// <summary>
/// Computes a per-vertex tangent basis for a low-poly and attaches it to the mesh. The bake and the
/// consuming renderer must share this basis (the whole point of MikkTSpace); NormalBake emits the
/// tangents it used with the output mesh. See <c>Agents/Documents/03-tangent-space-and-mikktspace.md</c>.
/// </summary>
public interface ITangentSpace
{
    /// <summary>Computes tangents on the mesh's shared triangulation and stores them via the mesh.</summary>
    void Compute(UvMesh mesh);
}
