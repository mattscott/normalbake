namespace NormalBake.Core;

/// <summary>
/// <b>Extend</b> edge padding: floods each covered texel's colour outward into uncovered texels so GPU
/// bilinear filtering and mipmapping don't sample the empty gutter across UV seams. A multi-source BFS
/// from all covered texels gives every uncovered texel the colour of its nearest covered neighbour —
/// the radial flood-fill that is the <i>only</i> correct margin for tangent-space normals (never the
/// cross-seam "adjacent faces" copy). AA must already be resolved over the coverage mask first.
/// See <c>docs/design/05-uvs-and-dilation.md</c> §4.
/// </summary>
internal static class Dilation
{
    /// <summary>
    /// Floods covered colours outward in place. <paramref name="covered"/> marks written texels; on
    /// return every reachable uncovered texel has been filled (and its flag set).
    /// </summary>
    public static void Extend(BakedMap map, bool[] covered)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(covered);
        int width = map.Width;
        int height = map.Height;
        float[] pixels = map.PixelArray;

        var queue = new Queue<int>();
        for (int i = 0; i < covered.Length; i++)
        {
            if (covered[i])
            {
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int cx = c % width;
            int cy = c / width;
            Spread(c, cx - 1, cy, width, height, pixels, covered, queue);
            Spread(c, cx + 1, cy, width, height, pixels, covered, queue);
            Spread(c, cx, cy - 1, width, height, pixels, covered, queue);
            Spread(c, cx, cy + 1, width, height, pixels, covered, queue);
        }
    }

    private static void Spread(
        int from, int x, int y, int width, int height, float[] pixels, bool[] covered, Queue<int> queue)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return;
        }

        int nb = (y * width) + x;
        if (covered[nb])
        {
            return;
        }

        int s = from * BakedMap.ChannelCount;
        int d = nb * BakedMap.ChannelCount;
        pixels[d] = pixels[s];
        pixels[d + 1] = pixels[s + 1];
        pixels[d + 2] = pixels[s + 2];
        covered[nb] = true;
        queue.Enqueue(nb);
    }
}
