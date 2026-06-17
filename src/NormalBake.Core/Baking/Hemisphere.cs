using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// A fixed, cosine-weighted hemisphere direction set (local, around +Z) for AO / thickness / bent-normal
/// sampling. The directions come from the deterministic Hammersley sequence (no RNG), so bakes stay
/// bit-reproducible (the determinism rule, <c>Agents/Plans/01-architecture.md</c>). The caller builds a
/// surface frame — <b>rotated per texel</b> — and combines it with <see cref="Local"/> to avoid the
/// banding a shared, un-rotated sample set produces.
/// </summary>
internal sealed class Hemisphere
{
    private readonly Vector3[] _local;

    public Hemisphere(int count)
    {
        count = Math.Max(1, count);
        _local = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float u1 = (i + 0.5f) / count;
            float u2 = RadicalInverseBase2(i);
            float r = MathF.Sqrt(u1); // cosine-weighted: density ∝ cosθ
            float phi = 2f * MathF.PI * u2;
            _local[i] = new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), MathF.Sqrt(MathF.Max(0f, 1f - u1)));
        }
    }

    public int Count => _local.Length;

    /// <summary>The i-th local hemisphere direction (around +Z).</summary>
    public Vector3 Local(int i) => _local[i];

    private static float RadicalInverseBase2(int i)
    {
        uint bits = (uint)i;
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return bits * 2.3283064365386963e-10f; // / 2^32
    }
}
