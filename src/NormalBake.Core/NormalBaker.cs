using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// The top-level baker: projects a high-poly's surface detail into a low-poly's UV space as a
/// tangent-space normal map. Validates the UV contract (P1), computes MikkTSpace tangents if absent
/// (P2), builds a BVH (P3), then for every UV texel recovers the surface point and tangent frame, casts
/// a cage-bounded ray to the high-poly, samples its smooth normal, and re-expresses it in the low-poly
/// frame — supersampled over a coverage mask and Extend-dilated. See
/// <c>Agents/Documents/02-baking-algorithm.md</c>.
/// </summary>
public static class NormalBaker
{
    /// <summary>Bakes a tangent-space normal map. The low-poly's tangents are computed if not present.</summary>
    public static BakedMap Bake(Mesh highPoly, UvMesh lowPoly, NormalBakeOptions options) =>
        Bake(highPoly, lowPoly, options, out _);

    /// <summary>Bakes and also returns the pre-dilation coverage mask (true where a texel was baked).</summary>
    internal static BakedMap Bake(
        Mesh highPoly, UvMesh lowPoly, NormalBakeOptions options, out bool[] bakedMask)
    {
        ArgumentNullException.ThrowIfNull(highPoly);
        ArgumentNullException.ThrowIfNull(lowPoly);
        ArgumentNullException.ThrowIfNull(options);

        UvContract.ValidateOrThrow(lowPoly, new UvValidationOptions { Resolution = options.Resolution });

        int res = options.Resolution;
        var accum = new Vector3[res * res];
        var coverage = new int[res * res];
        Accumulate(highPoly, lowPoly, options, accum, coverage);
        return Resolve(accum, coverage, res, options.Convention, out bakedMask);
    }

    /// <summary>
    /// Bakes several named parts into one shared atlas, each low-poly projected against <b>only its own</b>
    /// high-poly (a separate BVH per part). This match-by-name isolation stops a nearby part's geometry
    /// from bleeding into another part's bake. See <c>Agents/Documents/04-cage-and-projection.md</c>.
    /// </summary>
    public static BakedMap BakeParts(IReadOnlyList<BakePart> parts, NormalBakeOptions options)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(options);
        if (parts.Count == 0)
        {
            throw new ArgumentException("at least one part is required.", nameof(parts));
        }

        int res = options.Resolution;
        var accum = new Vector3[res * res];
        var coverage = new int[res * res];
        foreach (BakePart part in parts)
        {
            UvContract.ValidateOrThrow(part.Low, new UvValidationOptions { Resolution = res });
            Accumulate(part.High, part.Low, options, accum, coverage);
        }

        return Resolve(accum, coverage, res, options.Convention, out _);
    }

    /// <summary>
    /// Pairs low/high meshes by a shared base name (stripping <paramref name="lowSuffix"/>/
    /// <paramref name="highSuffix"/>), e.g. <c>crate_low</c> ↔ <c>crate_high</c>, into bakeable parts.
    /// </summary>
    public static IReadOnlyList<BakePart> MatchByName(
        IReadOnlyDictionary<string, Mesh> highs,
        IReadOnlyDictionary<string, UvMesh> lows,
        string highSuffix = "_high",
        string lowSuffix = "_low")
    {
        ArgumentNullException.ThrowIfNull(highs);
        ArgumentNullException.ThrowIfNull(lows);
        ArgumentNullException.ThrowIfNull(highSuffix);
        ArgumentNullException.ThrowIfNull(lowSuffix);

        var parts = new List<BakePart>();
        foreach (KeyValuePair<string, UvMesh> low in lows)
        {
            if (!low.Key.EndsWith(lowSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            string baseName = low.Key[..^lowSuffix.Length];
            if (highs.TryGetValue(baseName + highSuffix, out Mesh? high))
            {
                parts.Add(new BakePart(baseName, high, low.Value));
            }
        }

        if (parts.Count == 0)
        {
            throw new ArgumentException("no matching low/high name pairs were found.", nameof(lows));
        }

        return parts;
    }

    private static void Accumulate(
        Mesh high, UvMesh low, NormalBakeOptions options, Vector3[] accum, int[] coverage)
    {
        if (!low.HasTangents)
        {
            MikkTSpaceTangents.Generate(low);
        }

        var bvh = Bvh.Build(high);
        (float frontal, float rear) = ResolveCage(low, options);
        var ctx = new BakeContext(
            high, low, bvh, frontal, rear, options.Resolution, Math.Max(1, options.Samples),
            options.Space, options.NearestPointFallback, options.TileU, options.TileV);

        ReadOnlySpan<int> indices = low.Indices;
        for (int t = 0; t < indices.Length; t += 3)
        {
            ctx.RasterizeTriangle(indices[t], indices[t + 1], indices[t + 2], accum, coverage);
        }
    }

    private static BakedMap Resolve(
        Vector3[] accum, int[] coverage, int res, NormalMapConvention convention, out bool[] bakedMask)
    {
        var map = new BakedMap(res, res);
        var covered = new bool[res * res];
        for (int i = 0; i < coverage.Length; i++)
        {
            if (coverage[i] == 0)
            {
                continue;
            }

            Vector3 avg = accum[i] / coverage[i];
            float len = avg.Length();
            Vector3 nT = len > 1e-12f ? avg / len : new Vector3(0f, 0f, 1f);
            Vector3 rgb = NormalEncoder.Encode(nT, convention);
            map.SetPixel(i % res, i / res, rgb.X, rgb.Y, rgb.Z);
            covered[i] = true;
        }

        bakedMask = (bool[])covered.Clone();
        Dilation.Extend(map, covered);
        return map;
    }

    private static (float Frontal, float Rear) ResolveCage(UvMesh lowPoly, NormalBakeOptions options)
    {
        float frontal = (float)options.FrontalDistance;
        float rear = (float)options.RearDistance;
        if (frontal > 0f && rear > 0f)
        {
            return (frontal, rear);
        }

        var box = Aabb.Empty;
        foreach (Vector3 p in lowPoly.Positions)
        {
            box = box.Include(p);
        }

        float auto = box.Extent.Length() * 0.1f; // ~10% of the bounding-box diagonal
        return (frontal > 0f ? frontal : auto, rear > 0f ? rear : auto);
    }

    private sealed class BakeContext
    {
        private readonly Vector3[] _hiNormals;
        private readonly int[] _hiIndices;
        private readonly Vector3[] _loPos;
        private readonly Vector3[] _loNormals;
        private readonly Vector2[] _loUvs;
        private readonly Vector4[] _loTangents;
        private readonly Bvh _bvh;
        private readonly float _frontal;
        private readonly float _rear;
        private readonly int _res;
        private readonly int _samples;
        private readonly NormalSpace _space;
        private readonly bool _fallback;
        private readonly float _tileU;
        private readonly float _tileV;

        public BakeContext(
            Mesh high, UvMesh low, Bvh bvh, float frontal, float rear, int res, int samples, NormalSpace space,
            bool fallback, int tileU, int tileV)
        {
            _hiNormals = high.NormalArray;
            _hiIndices = high.IndexArray;
            _loPos = low.PositionArray;
            _loNormals = low.NormalArray;
            _loUvs = low.UvArray;
            _loTangents = low.TangentArray;
            _bvh = bvh;
            _frontal = frontal;
            _rear = rear;
            _res = res;
            _samples = samples;
            _space = space;
            _fallback = fallback;
            _tileU = tileU;
            _tileV = tileV;
        }

        public void RasterizeTriangle(int i0, int i1, int i2, Vector3[] accum, int[] coverage)
        {
            // Texel-space triangle (V flipped so row 0 is the top of the 0–1 tile).
            Vector2 t0 = ToTexel(_loUvs[i0]);
            Vector2 t1 = ToTexel(_loUvs[i1]);
            Vector2 t2 = ToTexel(_loUvs[i2]);

            float minX = MathF.Min(t0.X, MathF.Min(t1.X, t2.X));
            float maxX = MathF.Max(t0.X, MathF.Max(t1.X, t2.X));
            float minY = MathF.Min(t0.Y, MathF.Min(t1.Y, t2.Y));
            float maxY = MathF.Max(t0.Y, MathF.Max(t1.Y, t2.Y));
            int x0 = Math.Max(0, (int)MathF.Floor(minX));
            int x1 = Math.Min(_res - 1, (int)MathF.Ceiling(maxX));
            int y0 = Math.Max(0, (int)MathF.Floor(minY));
            int y1 = Math.Min(_res - 1, (int)MathF.Ceiling(maxY));

            float area = EdgeFunction(t0, t1, t2);
            if (MathF.Abs(area) < 1e-12f)
            {
                return; // degenerate in UV — covers no texels
            }

            float invArea = 1f / area;
            float w = _loTangents[i0].W; // handedness is constant within a UV island

            for (int py = y0; py <= y1; py++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    for (int sy = 0; sy < _samples; sy++)
                    {
                        for (int sx = 0; sx < _samples; sx++)
                        {
                            var sample = new Vector2(
                                px + ((sx + 0.5f) / _samples), py + ((sy + 0.5f) / _samples));
                            float a = EdgeFunction(t1, t2, sample) * invArea;
                            float b = EdgeFunction(t2, t0, sample) * invArea;
                            float c = EdgeFunction(t0, t1, sample) * invArea;
                            if (a < -1e-6f || b < -1e-6f || c < -1e-6f)
                            {
                                continue; // outside the triangle
                            }

                            if (TrySample(i0, i1, i2, a, b, c, w, out Vector3 nT))
                            {
                                int idx = (py * _res) + px;
                                accum[idx] += nT;
                                coverage[idx]++;
                            }
                        }
                    }
                }
            }
        }

        private bool TrySample(int i0, int i1, int i2, float a, float b, float c, float w, out Vector3 nT)
        {
            nT = default;
            Vector3 p = (a * _loPos[i0]) + (b * _loPos[i1]) + (c * _loPos[i2]);
            Vector3 n = Vector3.Normalize((a * _loNormals[i0]) + (b * _loNormals[i1]) + (c * _loNormals[i2]));
            Vector3 tan =
                (a * Xyz(_loTangents[i0])) + (b * Xyz(_loTangents[i1])) + (c * Xyz(_loTangents[i2]));

            var ray = new Ray(p + (n * _frontal), -n, _frontal + _rear);
            if (!_bvh.ClosestHit(ray, out Hit hit) && !(_fallback && _bvh.ClosestPoint(p, out hit)))
            {
                return false; // cage miss and no fallback → filled later by dilation
            }

            int hi = hit.Triangle * 3;
            Vector3 nhi = Vector3.Normalize(
                (hit.W * _hiNormals[_hiIndices[hi]])
                + (hit.U * _hiNormals[_hiIndices[hi + 1]])
                + (hit.V * _hiNormals[_hiIndices[hi + 2]]));

            if (_space == NormalSpace.Object)
            {
                nT = nhi; // object/model frame — encode the sampled normal directly
                return true;
            }

            Vector3 bitangent = w * Vector3.Cross(n, tan);
            float bl = bitangent.Length();
            float tl = tan.Length();
            if (tl < 1e-12f || bl < 1e-12f)
            {
                return false;
            }

            var local = new Vector3(
                Vector3.Dot(nhi, tan / tl), Vector3.Dot(nhi, bitangent / bl), Vector3.Dot(nhi, n));
            float ll = local.Length();
            nT = ll > 1e-12f ? local / ll : new Vector3(0f, 0f, 1f);
            return true;
        }

        private Vector2 ToTexel(Vector2 uv) =>
            new((uv.X - _tileU) * _res, (1f - (uv.Y - _tileV)) * _res);

        private static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);

        private static float EdgeFunction(Vector2 a, Vector2 b, Vector2 p) =>
            ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));
    }
}
