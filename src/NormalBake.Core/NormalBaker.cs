using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// The top-level baker. Its default product is a tangent-space normal map (projection ray-cast + the
/// MikkTSpace frame), but the same single-ray core drives a family of maps via
/// <see cref="NormalBakeOptions.Map"/>: object/world position, ambient occlusion and thickness
/// (hemisphere sampling), curvature (normal-field derivative), and per-part object ID. Pipeline: validate
/// UVs (P1) → tangents (P2) → BVH (P3) → per-texel rasterize + cage ray / hemisphere → resolve over a
/// coverage mask → Extend dilate. See <c>Agents/Documents/02-baking-algorithm.md</c>.
/// </summary>
public static class NormalBaker
{
    /// <summary>Bakes a map for one high/low pair. The low-poly's tangents are computed if not present.</summary>
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
        Aabb bounds = BoundsOf(highPoly.Positions);
        Accumulate(highPoly, lowPoly, options, accum, coverage, IdColor(0), bounds);
        return Resolve(accum, coverage, options, bounds, out bakedMask);
    }

    /// <summary>
    /// Bakes several named parts into one shared atlas, each low-poly projected against <b>only its own</b>
    /// high-poly (a separate BVH per part). This match-by-name isolation stops a nearby part's geometry
    /// from bleeding into another part's bake, and gives each part a distinct ID colour.
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

        var bounds = Aabb.Empty;
        foreach (BakePart part in parts)
        {
            bounds = bounds.Union(BoundsOf(part.High.Positions));
        }

        for (int p = 0; p < parts.Count; p++)
        {
            BakePart part = parts[p];
            UvContract.ValidateOrThrow(part.Low, new UvValidationOptions { Resolution = res });
            Accumulate(part.High, part.Low, options, accum, coverage, IdColor(p), bounds);
        }

        return Resolve(accum, coverage, options, bounds, out _);
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
        Mesh high, UvMesh low, NormalBakeOptions options, Vector3[] accum, int[] coverage, Vector3 idColor,
        Aabb bounds)
    {
        if (!low.HasTangents)
        {
            MikkTSpaceTangents.Generate(low);
        }

        var bvh = Bvh.Build(high);
        (float frontal, float rear) = ResolveCage(low, options);
        float occRadius = options.OcclusionRadius > 0
            ? (float)options.OcclusionRadius
            : bounds.Extent.Length() * 0.5f;
        float occBias = MathF.Max(1e-4f, bounds.Extent.Length() * 1e-2f); // clear local facets on grazing rays
        var hemi = new Hemisphere(options.OcclusionSamples);

        var ctx = new BakeContext(high, low, bvh, options, frontal, rear, idColor, hemi, occRadius, occBias);
        ctx.Run(accum, coverage, options.Threads);
    }

    private static BakedMap Resolve(
        Vector3[] accum, int[] coverage, NormalBakeOptions options, Aabb bounds, out bool[] bakedMask)
    {
        int res = options.Resolution;
        var map = new BakedMap(res, res);
        var covered = new bool[res * res];
        var averaged = new Vector3[res * res];
        for (int i = 0; i < coverage.Length; i++)
        {
            if (coverage[i] == 0)
            {
                continue;
            }

            averaged[i] = accum[i] / coverage[i];
            covered[i] = true;
        }

        bakedMask = (bool[])covered.Clone();

        if (options.Map == MapType.Curvature)
        {
            ResolveCurvature(averaged, covered, res, map);
        }
        else
        {
            for (int i = 0; i < covered.Length; i++)
            {
                if (covered[i])
                {
                    Vector3 rgb = Encode(averaged[i], options, bounds);
                    map.SetPixel(i % res, i / res, rgb.X, rgb.Y, rgb.Z);
                }
            }
        }

        Dilation.Extend(map, covered);
        return map;
    }

    private static Vector3 Encode(Vector3 value, NormalBakeOptions options, Aabb bounds)
    {
        switch (options.Map)
        {
            case MapType.Normal:
                float len = value.Length();
                Vector3 nT = len > 1e-12f ? value / len : new Vector3(0f, 0f, 1f);
                return NormalEncoder.Encode(nT, options.Convention);
            case MapType.Position:
                Vector3 extent = bounds.Extent;
                return new Vector3(
                    SafeNormalize(value.X - bounds.Min.X, extent.X),
                    SafeNormalize(value.Y - bounds.Min.Y, extent.Y),
                    SafeNormalize(value.Z - bounds.Min.Z, extent.Z));
            default: // AO, Thickness, ObjectId already carry [0,1] RGB/grayscale values
                return Vector3.Clamp(value, Vector3.Zero, Vector3.One);
        }
    }

    private static void ResolveCurvature(Vector3[] normals, bool[] covered, int res, BakedMap map)
    {
        const float scale = 0.06f;
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int i = (y * res) + x;
                if (!covered[i])
                {
                    continue;
                }

                float dNxdx = Gradient(normals, covered, res, x, y, 1, 0, n => n.X);
                float dNydy = Gradient(normals, covered, res, x, y, 0, 1, n => n.Y);
                float curvature = (dNxdx + dNydy) * res * scale; // convex → positive
                float g = Math.Clamp(0.5f + curvature, 0f, 1f);
                map.SetPixel(x, y, g, g, g);
            }
        }
    }

    private static float Gradient(
        Vector3[] normals, bool[] covered, int res, int x, int y, int dx, int dy, Func<Vector3, float> comp)
    {
        int prev = Index(res, x - dx, y - dy);
        int next = Index(res, x + dx, y + dy);
        bool hasPrev = prev >= 0 && covered[prev];
        bool hasNext = next >= 0 && covered[next];
        int center = (y * res) + x;
        if (hasPrev && hasNext)
        {
            return (comp(Normalized(normals[next])) - comp(Normalized(normals[prev]))) * 0.5f;
        }

        if (hasNext)
        {
            return comp(Normalized(normals[next])) - comp(Normalized(normals[center]));
        }

        if (hasPrev)
        {
            return comp(Normalized(normals[center])) - comp(Normalized(normals[prev]));
        }

        return 0f;
    }

    private static Vector3 Normalized(Vector3 v)
    {
        float len = v.Length();
        return len > 1e-12f ? v / len : v;
    }

    private static int Index(int res, int x, int y) =>
        (uint)x >= (uint)res || (uint)y >= (uint)res ? -1 : (y * res) + x;

    private static float SafeNormalize(float v, float extent) =>
        extent > 1e-12f ? Math.Clamp(v / extent, 0f, 1f) : 0.5f;

    private static (float Frontal, float Rear) ResolveCage(UvMesh lowPoly, NormalBakeOptions options)
    {
        float frontal = (float)options.FrontalDistance;
        float rear = (float)options.RearDistance;
        if (frontal > 0f && rear > 0f)
        {
            return (frontal, rear);
        }

        float auto = BoundsOf(lowPoly.Positions).Extent.Length() * 0.1f; // ~10% of the diagonal
        return (frontal > 0f ? frontal : auto, rear > 0f ? rear : auto);
    }

    private static Aabb BoundsOf(ReadOnlySpan<Vector3> positions)
    {
        var box = Aabb.Empty;
        foreach (Vector3 p in positions)
        {
            box = box.Include(p);
        }

        return box;
    }

    private static Vector3 IdColor(int index)
    {
        float h = Frac(0.5f + (index * 0.618033989f));
        return HsvToRgb(h, 0.65f, 0.95f);
    }

    private static float Frac(float x) => x - MathF.Floor(x);

    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        float i = MathF.Floor(h * 6f);
        float f = (h * 6f) - i;
        float p = v * (1f - s);
        float q = v * (1f - (f * s));
        float t = v * (1f - ((1f - f) * s));
        return ((int)i % 6) switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }

    private sealed class BakeContext
    {
        private readonly Vector3[] _hiPos;
        private readonly Vector3[] _hiNormals;
        private readonly int[] _hiIndices;
        private readonly Vector3[] _loPos;
        private readonly Vector3[] _loNormals;
        private readonly Vector2[] _loUvs;
        private readonly Vector4[] _loTangents;
        private readonly int[] _loIndices;
        private readonly Bvh _bvh;
        private readonly MapType _map;
        private readonly NormalSpace _space;
        private readonly bool _fallback;
        private readonly float _frontal;
        private readonly float _rear;
        private readonly int _res;
        private readonly int _samples;
        private readonly float _tileU;
        private readonly float _tileV;
        private readonly Vector3 _idColor;
        private readonly Hemisphere _hemi;
        private readonly float _occRadius;
        private readonly float _occBias;

        public BakeContext(
            Mesh high, UvMesh low, Bvh bvh, NormalBakeOptions options, float frontal, float rear,
            Vector3 idColor, Hemisphere hemi, float occRadius, float occBias)
        {
            _hiPos = high.PositionArray;
            _hiNormals = high.NormalArray;
            _hiIndices = high.IndexArray;
            _loPos = low.PositionArray;
            _loNormals = low.NormalArray;
            _loUvs = low.UvArray;
            _loTangents = low.TangentArray;
            _loIndices = low.IndexArray;
            _bvh = bvh;
            _map = options.Map;
            _space = options.Space;
            _fallback = options.NearestPointFallback;
            _frontal = frontal;
            _rear = rear;
            _res = options.Resolution;
            _samples = Math.Max(1, options.Samples);
            _tileU = options.TileU;
            _tileV = options.TileV;
            _idColor = idColor;
            _hemi = hemi;
            _occRadius = occRadius;
            _occBias = occBias;
        }

        private const int TileSize = 32;

        /// <summary>
        /// Bakes all triangles into <paramref name="accum"/>/<paramref name="coverage"/>, parallelized over
        /// output tiles. Each texel belongs to exactly one tile (no write races), and within a tile its
        /// samples are summed in <b>triangle-index order</b> — identical to the serial bake, so the result
        /// is bit-exact regardless of thread count (the determinism rule).
        /// </summary>
        public void Run(Vector3[] accum, int[] coverage, int threads)
        {
            int tilesPerSide = (_res + TileSize - 1) / TileSize;
            var tileTriangles = new List<int>[tilesPerSide * tilesPerSide];
            int triCount = _loIndices.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                if (!TexelBounds(t, out int x0, out int y0, out int x1, out int y1))
                {
                    continue;
                }

                for (int ty = y0 / TileSize; ty <= y1 / TileSize; ty++)
                {
                    for (int tx = x0 / TileSize; tx <= x1 / TileSize; tx++)
                    {
                        int tile = (ty * tilesPerSide) + tx;
                        (tileTriangles[tile] ??= new List<int>()).Add(t);
                    }
                }
            }

            var options = new ParallelOptions { MaxDegreeOfParallelism = threads <= 0 ? -1 : threads };
            Parallel.For(0, tileTriangles.Length, options, tile =>
            {
                List<int>? list = tileTriangles[tile];
                if (list is null)
                {
                    return;
                }

                int tx = tile % tilesPerSide;
                int ty = tile / tilesPerSide;
                int clipX1 = Math.Min(_res - 1, ((tx + 1) * TileSize) - 1);
                int clipY1 = Math.Min(_res - 1, ((ty + 1) * TileSize) - 1);
                foreach (int t in list)
                {
                    RasterizeTriangle(t, accum, coverage, tx * TileSize, ty * TileSize, clipX1, clipY1);
                }
            });
        }

        private bool TexelBounds(int t, out int x0, out int y0, out int x1, out int y1)
        {
            Vector2 t0 = ToTexel(_loUvs[_loIndices[t * 3]]);
            Vector2 t1 = ToTexel(_loUvs[_loIndices[(t * 3) + 1]]);
            Vector2 t2 = ToTexel(_loUvs[_loIndices[(t * 3) + 2]]);
            x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(t0.X, MathF.Min(t1.X, t2.X))));
            x1 = Math.Min(_res - 1, (int)MathF.Ceiling(MathF.Max(t0.X, MathF.Max(t1.X, t2.X))));
            y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(t0.Y, MathF.Min(t1.Y, t2.Y))));
            y1 = Math.Min(_res - 1, (int)MathF.Ceiling(MathF.Max(t0.Y, MathF.Max(t1.Y, t2.Y))));
            return x0 <= x1 && y0 <= y1;
        }

        private void RasterizeTriangle(
            int t, Vector3[] accum, int[] coverage, int clipX0, int clipY0, int clipX1, int clipY1)
        {
            int i0 = _loIndices[t * 3];
            int i1 = _loIndices[(t * 3) + 1];
            int i2 = _loIndices[(t * 3) + 2];
            Vector2 t0 = ToTexel(_loUvs[i0]);
            Vector2 t1 = ToTexel(_loUvs[i1]);
            Vector2 t2 = ToTexel(_loUvs[i2]);

            float minX = MathF.Min(t0.X, MathF.Min(t1.X, t2.X));
            float maxX = MathF.Max(t0.X, MathF.Max(t1.X, t2.X));
            float minY = MathF.Min(t0.Y, MathF.Min(t1.Y, t2.Y));
            float maxY = MathF.Max(t0.Y, MathF.Max(t1.Y, t2.Y));
            int x0 = Math.Max(clipX0, (int)MathF.Floor(minX));
            int x1 = Math.Min(clipX1, (int)MathF.Ceiling(maxX));
            int y0 = Math.Max(clipY0, (int)MathF.Floor(minY));
            int y1 = Math.Min(clipY1, (int)MathF.Ceiling(maxY));

            float area = EdgeFunction(t0, t1, t2);
            if (MathF.Abs(area) < 1e-12f)
            {
                return;
            }

            float invArea = 1f / area;
            float w = _loTangents[i0].W;

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
                                continue;
                            }

                            if (TrySampleValue(i0, i1, i2, a, b, c, w, out Vector3 value))
                            {
                                int idx = (py * _res) + px;
                                accum[idx] += value;
                                coverage[idx]++;
                            }
                        }
                    }
                }
            }
        }

        private bool TrySampleValue(int i0, int i1, int i2, float a, float b, float c, float w, out Vector3 value)
        {
            value = default;
            Vector3 p = (a * _loPos[i0]) + (b * _loPos[i1]) + (c * _loPos[i2]);
            Vector3 n = Vector3.Normalize((a * _loNormals[i0]) + (b * _loNormals[i1]) + (c * _loNormals[i2]));

            switch (_map)
            {
                case MapType.ObjectId:
                    value = _idColor;
                    return true;
                case MapType.AmbientOcclusion:
                    value = Broadcast(AmbientOcclusion(p, n));
                    return true;
                case MapType.Thickness:
                    value = Broadcast(Thickness(p, n));
                    return true;
                default:
                    break;
            }

            var ray = new Ray(p + (n * _frontal), -n, _frontal + _rear);
            if (!_bvh.ClosestHit(ray, out Hit hit) && !(_fallback && _bvh.ClosestPoint(p, out hit)))
            {
                return false;
            }

            int hi = hit.Triangle * 3;
            if (_map == MapType.Position)
            {
                value = (hit.W * _hiPos[_hiIndices[hi]])
                    + (hit.U * _hiPos[_hiIndices[hi + 1]])
                    + (hit.V * _hiPos[_hiIndices[hi + 2]]);
                return true;
            }

            Vector3 nhi = Vector3.Normalize(
                (hit.W * _hiNormals[_hiIndices[hi]])
                + (hit.U * _hiNormals[_hiIndices[hi + 1]])
                + (hit.V * _hiNormals[_hiIndices[hi + 2]]));

            if (_map == MapType.Curvature || _space == NormalSpace.Object)
            {
                value = nhi; // object frame (curvature post-processes this normal field)
                return true;
            }

            Vector3 tan =
                (a * Xyz(_loTangents[i0])) + (b * Xyz(_loTangents[i1])) + (c * Xyz(_loTangents[i2]));
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
            value = ll > 1e-12f ? local / ll : new Vector3(0f, 0f, 1f);
            return true;
        }

        private float AmbientOcclusion(Vector3 p, Vector3 n)
        {
            Vector3 origin = p + (n * _occBias);
            int occluded = 0;
            for (int k = 0; k < _hemi.Count; k++)
            {
                if (_bvh.AnyHit(new Ray(origin, _hemi.ToFrame(k, n), _occRadius)))
                {
                    occluded++;
                }
            }

            return 1f - (occluded / (float)_hemi.Count);
        }

        private float Thickness(Vector3 p, Vector3 n)
        {
            Vector3 origin = p - (n * _occBias);
            float sum = 0f;
            for (int k = 0; k < _hemi.Count; k++)
            {
                Vector3 dir = _hemi.ToFrame(k, -n); // into the solid
                sum += _bvh.ClosestHit(new Ray(origin, dir, _occRadius), out Hit hit) ? hit.T : _occRadius;
            }

            return (sum / _hemi.Count) / _occRadius;
        }

        private static Vector3 Broadcast(float s) => new(s, s, s);

        private Vector2 ToTexel(Vector2 uv) =>
            new((uv.X - _tileU) * _res, (1f - (uv.Y - _tileV)) * _res);

        private static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);

        private static float EdgeFunction(Vector2 a, Vector2 b, Vector2 p) =>
            ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));
    }
}
