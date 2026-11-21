using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// Validates that a low-poly's incoming UVs satisfy the bake contract and <b>fails fast</b> with
/// actionable messages. NormalBake consumes UVs (a separate tool produces them) and checks: structural
/// sanity (indices, finiteness), non-overlap in the active 0–1 tile, and inter-island gutters ≥ 2× the
/// edge padding. UV seams at hard edges are enforced implicitly — a hard edge with shared UVs shows up
/// as a tangent discontinuity downstream — and a light texel-density check flags near-empty layouts.
/// See <c>docs/design/05-uvs-and-dilation.md</c>.
/// </summary>
public static class UvContract
{
    private const int MaxValidationResolution = 2048;

    /// <summary>Validates a low-poly's UV layout, returning every finding (errors and warnings).</summary>
    public static UvValidationResult Validate(UvMesh mesh, UvValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        options ??= new UvValidationOptions();
        var issues = new List<UvValidationIssue>();

        if (!CheckStructure(mesh, issues))
        {
            return new UvValidationResult(issues);
        }

        CheckTrianglesAndTile(mesh, options, issues);
        RasterChecks(mesh, options, issues);
        return new UvValidationResult(issues);
    }

    /// <summary>Validates and throws <see cref="UvContractException"/> if any error was found.</summary>
    public static void ValidateOrThrow(UvMesh mesh, UvValidationOptions? options = null)
    {
        UvValidationResult result = Validate(mesh, options);
        if (result.IsValid)
        {
            return;
        }

        var sb = new System.Text.StringBuilder("UV contract violated:");
        foreach (UvValidationIssue issue in result.Issues)
        {
            if (issue.Severity == UvValidationSeverity.Error)
            {
                sb.Append("\n  - ").Append(issue.Message);
            }
        }

        throw new UvContractException(sb.ToString());
    }

    private static bool CheckStructure(UvMesh mesh, List<UvValidationIssue> issues)
    {
        if (mesh.VertexCount == 0 || mesh.TriangleCount == 0)
        {
            issues.Add(new UvValidationIssue(
                UvValidationSeverity.Error, "UV.Empty", "mesh has no vertices or no triangles."));
            return false;
        }

        ReadOnlySpan<int> indices = mesh.Indices;
        int vertexCount = mesh.VertexCount;
        for (int i = 0; i < indices.Length; i++)
        {
            if ((uint)indices[i] >= (uint)vertexCount)
            {
                issues.Add(new UvValidationIssue(
                    UvValidationSeverity.Error,
                    "UV.BadIndex",
                    $"triangle index {indices[i]} at position {i} is out of range (vertex count {vertexCount})."));
                return false;
            }
        }

        ReadOnlySpan<Vector2> uvs = mesh.Uvs;
        for (int v = 0; v < uvs.Length; v++)
        {
            if (!float.IsFinite(uvs[v].X) || !float.IsFinite(uvs[v].Y))
            {
                issues.Add(new UvValidationIssue(
                    UvValidationSeverity.Error, "UV.NonFinite", $"UV at vertex {v} is NaN or infinite."));
                return false;
            }
        }

        return true;
    }

    private static void CheckTrianglesAndTile(
        UvMesh mesh, UvValidationOptions options, List<UvValidationIssue> issues)
    {
        ReadOnlySpan<int> indices = mesh.Indices;
        ReadOnlySpan<Vector2> uvs = mesh.Uvs;
        int degenerate = 0;
        int outsideTile = 0;
        double totalArea = 0;

        for (int t = 0; t < indices.Length; t += 3)
        {
            Vector2 a = uvs[indices[t]];
            Vector2 b = uvs[indices[t + 1]];
            Vector2 c = uvs[indices[t + 2]];

            double area2 = Math.Abs(SignedDoubleArea(a, b, c));
            if (area2 <= 1e-14)
            {
                degenerate++;
                continue;
            }

            totalArea += area2 * 0.5;

            float minU = MathF.Min(a.X, MathF.Min(b.X, c.X));
            float maxU = MathF.Max(a.X, MathF.Max(b.X, c.X));
            float minV = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
            float maxV = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
            if (maxU <= 0f || minU >= 1f || maxV <= 0f || minV >= 1f)
            {
                outsideTile++;
            }
        }

        if (degenerate > 0)
        {
            issues.Add(new UvValidationIssue(
                UvValidationSeverity.Warning,
                "UV.Degenerate",
                $"{degenerate} triangle(s) have zero UV area; they cover no texels and cannot be baked."));
        }

        if (outsideTile > 0)
        {
            issues.Add(new UvValidationIssue(
                options.RequireInsideTile ? UvValidationSeverity.Error : UvValidationSeverity.Warning,
                "UV.OutsideTile",
                $"{outsideTile} triangle(s) lie entirely outside the 0–1 tile and will not be baked; "
                + "use integer UDIM offsets for multi-tile layouts."));
        }

        if (totalArea < 0.01)
        {
            issues.Add(new UvValidationIssue(
                UvValidationSeverity.Warning,
                "UV.TexelDensity",
                $"UVs cover only {totalArea * 100:0.##}% of the 0–1 tile; texel density may be too low — "
                + "pack the islands to use more of the tile."));
        }
    }

    private static void RasterChecks(
        UvMesh mesh, UvValidationOptions options, List<UvValidationIssue> issues)
    {
        int outRes = Math.Max(1, options.Resolution);
        int padOut = options.PaddingTexels ?? Math.Max(1, outRes / 128);
        int vRes = Math.Min(outRes, MaxValidationResolution);
        double scale = (double)vRes / outRes;
        int requiredGutterV = Math.Max(1, (int)Math.Round(2 * padOut * scale));

        int[] islandOfVertex = ComputeIslands(mesh, out int islandCount);

        var label = new int[vRes * vRes];
        Array.Fill(label, -1);

        ReadOnlySpan<int> indices = mesh.Indices;
        ReadOnlySpan<Vector2> uvs = mesh.Uvs;
        int overlapCount = 0;
        for (int t = 0; t < indices.Length; t += 3)
        {
            int island = islandOfVertex[indices[t]];
            RasterizeTriangle(
                uvs[indices[t]], uvs[indices[t + 1]], uvs[indices[t + 2]], island, vRes, label, ref overlapCount);
        }

        if (overlapCount > 0)
        {
            issues.Add(new UvValidationIssue(
                UvValidationSeverity.Error,
                "UV.Overlap",
                $"{overlapCount} texel(s) are covered by more than one triangle (stacked or folded UVs); "
                + "islands must not overlap in the 0–1 tile."));
        }

        if (islandCount >= 2)
        {
            int minGapV = MinInterIslandGap(label, vRes);
            if (minGapV < requiredGutterV)
            {
                double measuredOut = minGapV / scale;
                double requiredOut = requiredGutterV / scale;
                issues.Add(new UvValidationIssue(
                    UvValidationSeverity.Warning,
                    "UV.Gutter",
                    $"the narrowest gap between UV islands is ≈{measuredOut:0.#} texels, below the recommended "
                    + $"{requiredOut:0.#} (2× the {padOut}px padding at {outRes}px); dilation mitigates it, but "
                    + "wider gutters reduce mip-bleed. Spread the islands apart for best quality."));
            }
        }
    }

    private static int[] ComputeIslands(UvMesh mesh, out int islandCount)
    {
        int v = mesh.VertexCount;
        var parent = new int[v];
        for (int i = 0; i < v; i++)
        {
            parent[i] = i;
        }

        ReadOnlySpan<int> indices = mesh.Indices;
        for (int t = 0; t < indices.Length; t += 3)
        {
            Union(parent, indices[t], indices[t + 1]);
            Union(parent, indices[t + 1], indices[t + 2]);
        }

        var rootToIsland = new Dictionary<int, int>();
        var islandOfVertex = new int[v];
        islandCount = 0;
        for (int t = 0; t < indices.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int root = Find(parent, indices[t + k]);
                if (!rootToIsland.TryGetValue(root, out int id))
                {
                    id = islandCount++;
                    rootToIsland[root] = id;
                }

                islandOfVertex[indices[t + k]] = id;
            }
        }

        return islandOfVertex;
    }

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }

        return x;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a);
        int rb = Find(parent, b);
        if (ra != rb)
        {
            parent[ra] = rb;
        }
    }

    private static void RasterizeTriangle(
        Vector2 a, Vector2 b, Vector2 c, int island, int res, int[] label, ref int overlapCount)
    {
        double ax = a.X, ay = a.Y, bx = b.X, by = b.Y, cx = c.X, cy = c.Y;
        double den = ((bx - ax) * (cy - ay)) - ((cx - ax) * (by - ay));
        if (Math.Abs(den) <= 1e-14)
        {
            return;
        }

        double invDen = 1.0 / den;

        int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)) * res));
        int maxX = Math.Min(res - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx)) * res));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)) * res));
        int maxY = Math.Min(res - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy)) * res));

        for (int py = minY; py <= maxY; py++)
        {
            double v = (py + 0.5) / res;
            for (int px = minX; px <= maxX; px++)
            {
                double u = (px + 0.5) / res;
                double beta = (((cy - ay) * (u - ax)) - ((cx - ax) * (v - ay))) * invDen;
                double gamma = (((bx - ax) * (v - ay)) - ((by - ay) * (u - ax))) * invDen;
                double alpha = 1.0 - beta - gamma;
                if (alpha > 1e-9 && beta > 1e-9 && gamma > 1e-9)
                {
                    int idx = (py * res) + px;
                    if (label[idx] == -1)
                    {
                        label[idx] = island;
                    }
                    else
                    {
                        overlapCount++;
                    }
                }
            }
        }
    }

    private static int MinInterIslandGap(int[] label, int res)
    {
        var dist = new int[label.Length];
        var near = new int[label.Length];
        Array.Fill(dist, int.MaxValue);
        Array.Fill(near, -1);
        var queue = new Queue<int>();
        for (int i = 0; i < label.Length; i++)
        {
            if (label[i] >= 0)
            {
                dist[i] = 0;
                near[i] = label[i];
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int cx = c % res;
            int cy = c / res;
            int d = dist[c] + 1;
            Relax(cx - 1, cy, c, d, res, dist, near, queue);
            Relax(cx + 1, cy, c, d, res, dist, near, queue);
            Relax(cx, cy - 1, c, d, res, dist, near, queue);
            Relax(cx, cy + 1, c, d, res, dist, near, queue);
        }

        int minGap = int.MaxValue;
        for (int c = 0; c < label.Length; c++)
        {
            if (near[c] < 0)
            {
                continue;
            }

            int cx = c % res;
            int cy = c / res;
            if (cx + 1 < res)
            {
                minGap = ConsiderGap(c, c + 1, dist, near, minGap);
            }

            if (cy + 1 < res)
            {
                minGap = ConsiderGap(c, c + res, dist, near, minGap);
            }
        }

        return minGap;
    }

    private static void Relax(
        int x, int y, int from, int d, int res, int[] dist, int[] near, Queue<int> queue)
    {
        if ((uint)x >= (uint)res || (uint)y >= (uint)res)
        {
            return;
        }

        int nb = (y * res) + x;
        if (d < dist[nb])
        {
            dist[nb] = d;
            near[nb] = near[from];
            queue.Enqueue(nb);
        }
    }

    private static int ConsiderGap(int a, int b, int[] dist, int[] near, int minGap)
    {
        if (near[b] >= 0 && near[a] != near[b])
        {
            int gap = dist[a] + dist[b];
            if (gap < minGap)
            {
                minGap = gap;
            }
        }

        return minGap;
    }

    private static double SignedDoubleArea(Vector2 a, Vector2 b, Vector2 c) =>
        (((double)b.X - a.X) * ((double)c.Y - a.Y)) - (((double)c.X - a.X) * ((double)b.Y - a.Y));
}
