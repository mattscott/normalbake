namespace NormalBake.Core;

/// <summary>
/// A separable, coverage-aware Gaussian blur applied to a baked map <b>before</b> dilation. Only covered
/// texels contribute (weights renormalized at island edges), so the gutter/background never bleeds in.
/// It smooths the high-frequency noise that per-texel rotation trades banding for (AO/thickness) and the
/// faceting of the normal-field derivative (curvature). Run before dilation so the gutter is filled from
/// the smoothed result. (Tight UV packing means a large radius can blur across adjacent islands — keep
/// the radius small.)
/// </summary>
internal static class Blur
{
    public static void Apply(BakedMap map, bool[] covered, int radius)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(covered);
        if (radius <= 0)
        {
            return;
        }

        float sigma = (radius * 0.5f) + 0.5f;
        var weights = new float[radius + 1];
        for (int k = 0; k <= radius; k++)
        {
            weights[k] = MathF.Exp(-(k * k) / (2f * sigma * sigma));
        }

        int w = map.Width;
        int h = map.Height;
        float[] pixels = map.PixelArray;
        var temp = new float[pixels.Length];
        Pass(pixels, temp, covered, w, h, radius, weights, horizontal: true);
        Pass(temp, pixels, covered, w, h, radius, weights, horizontal: false);
    }

    private static void Pass(
        float[] src, float[] dst, bool[] covered, int w, int h, int radius, float[] weights, bool horizontal)
    {
        Array.Copy(src, dst, src.Length); // uncovered texels pass through unchanged
        const int channels = BakedMap.ChannelCount;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w) + x;
                if (!covered[idx])
                {
                    continue;
                }

                float r = 0f;
                float g = 0f;
                float b = 0f;
                float wsum = 0f;
                for (int k = -radius; k <= radius; k++)
                {
                    int nx = horizontal ? x + k : x;
                    int ny = horizontal ? y : y + k;
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h)
                    {
                        continue;
                    }

                    int nidx = (ny * w) + nx;
                    if (!covered[nidx])
                    {
                        continue; // mask-aware: never average in the gutter
                    }

                    float wt = weights[Math.Abs(k)];
                    int s = nidx * channels;
                    r += wt * src[s];
                    g += wt * src[s + 1];
                    b += wt * src[s + 2];
                    wsum += wt;
                }

                if (wsum > 0f)
                {
                    int d = idx * channels;
                    dst[d] = r / wsum;
                    dst[d + 1] = g / wsum;
                    dst[d + 2] = b / wsum;
                }
            }
        }
    }
}
