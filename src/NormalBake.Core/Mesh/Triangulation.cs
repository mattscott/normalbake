using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// The single, deterministic polygon-to-triangle rule shared across the pipeline. Quads are split along
/// the <b>shorter 3D diagonal</b> (order-independent and mirror-safe, matching MikkTSpace); larger
/// polygons fan from their first corner. Triangulating exactly once and feeding the same triangles to
/// the OBJ reader, the tangent solver, and the UV rasterizer is what prevents X-shaped shading seams
/// (<c>docs/design/02-baking-algorithm.md</c> §4, <c>03-tangent-space-and-mikktspace.md</c> §4).
/// </summary>
internal static class Triangulation
{
    /// <summary>
    /// Appends the triangles of one polygon face (given as combined-vertex corner indices, wound CCW) to
    /// <paramref name="output"/>. Faces with fewer than three corners are skipped.
    /// </summary>
    /// <param name="faceCorners">The face's vertex indices into <paramref name="positions"/>.</param>
    /// <param name="positions">All vertex positions (used to pick the shorter quad diagonal).</param>
    /// <param name="output">Destination triangle index list; three indices are appended per triangle.</param>
    public static void TriangulateFace(
        ReadOnlySpan<int> faceCorners,
        ReadOnlySpan<Vector3> positions,
        List<int> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        int n = faceCorners.Length;
        if (n < 3)
        {
            return;
        }

        if (n == 3)
        {
            output.Add(faceCorners[0]);
            output.Add(faceCorners[1]);
            output.Add(faceCorners[2]);
            return;
        }

        if (n == 4)
        {
            // Split along the shorter diagonal: 0-2 vs 1-3. Tie goes to 0-2 for determinism.
            float d02 = Vector3.DistanceSquared(positions[faceCorners[0]], positions[faceCorners[2]]);
            float d13 = Vector3.DistanceSquared(positions[faceCorners[1]], positions[faceCorners[3]]);
            if (d02 <= d13)
            {
                output.Add(faceCorners[0]);
                output.Add(faceCorners[1]);
                output.Add(faceCorners[2]);
                output.Add(faceCorners[0]);
                output.Add(faceCorners[2]);
                output.Add(faceCorners[3]);
            }
            else
            {
                output.Add(faceCorners[1]);
                output.Add(faceCorners[2]);
                output.Add(faceCorners[3]);
                output.Add(faceCorners[1]);
                output.Add(faceCorners[3]);
                output.Add(faceCorners[0]);
            }

            return;
        }

        // n > 4: deterministic fan from the first corner. Rare for quad-remesher output; documented fallback.
        for (int i = 1; i + 1 < n; i++)
        {
            output.Add(faceCorners[0]);
            output.Add(faceCorners[i]);
            output.Add(faceCorners[i + 1]);
        }
    }
}
