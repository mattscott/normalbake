using System.Numerics;

namespace NormalBake.Core;

/// <summary>
/// A bounding-volume hierarchy over a high-poly's triangles, built with a <b>binned SAH</b> sweep
/// (12 bins × 3 axes) and queried with ordered front-to-back traversal. Leaves hold a contiguous slice
/// of a reordered triangle-index array; intersection uses the watertight <see cref="RayTriangle"/> test
/// with a cage-bounded <c>tMax</c>. Built once, queried read-only — safe to share across bake threads.
/// (<c>docs/design/06-acceleration-and-performance.md</c>.)
/// </summary>
public sealed class Bvh : IRayTracer
{
    private const int BinCount = 12;
    private const int MaxLeafSize = 4;

    private readonly Vector3[] _v0;
    private readonly Vector3[] _v1;
    private readonly Vector3[] _v2;
    private readonly Vector3[] _centroid;
    private readonly int[] _triIndex;
    private readonly Node[] _nodes;

    private Bvh(Vector3[] v0, Vector3[] v1, Vector3[] v2, Vector3[] centroid, int[] triIndex, Node[] nodes)
    {
        _v0 = v0;
        _v1 = v1;
        _v2 = v2;
        _centroid = centroid;
        _triIndex = triIndex;
        _nodes = nodes;
    }

    /// <summary>Builds a BVH over the mesh's triangles.</summary>
    public static Bvh Build(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        (Vector3[] v0, Vector3[] v1, Vector3[] v2) = TriangleSoup.Extract(mesh);
        int triCount = v0.Length;

        var centroid = new Vector3[triCount];
        var triIndex = new int[triCount];
        for (int t = 0; t < triCount; t++)
        {
            centroid[t] = (v0[t] + v1[t] + v2[t]) * (1f / 3f);
            triIndex[t] = t;
        }

        // A binary BVH over N leaves has at most 2N−1 nodes; allocate 2N to be safe (≥1 for empty).
        var nodes = new Node[Math.Max(1, 2 * triCount)];
        var builder = new Builder(v0, v1, v2, centroid, triIndex, nodes);
        builder.Build(triCount);
        return new Bvh(v0, v1, v2, centroid, triIndex, nodes);
    }

    /// <inheritdoc/>
    public bool ClosestHit(in Ray ray, out Hit hit)
    {
        hit = default;
        var rs = new RayShear(ray);
        Vector3 invDir = InverseDirection(ray.Direction);
        float closest = ray.TMax;
        int bestTri = -1;
        float bestU = 0f;
        float bestV = 0f;

        Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            ref Node node = ref _nodes[stack[--sp]];
            if (!IntersectAabb(node.Min, node.Max, ray.Origin, invDir, closest, out _))
            {
                continue;
            }

            if (node.Count > 0)
            {
                int end = node.LeftFirst + node.Count;
                for (int i = node.LeftFirst; i < end; i++)
                {
                    int tri = _triIndex[i];
                    if (RayTriangle.Intersect(rs, _v0[tri], _v1[tri], _v2[tri], closest, out float t, out float u, out float v))
                    {
                        closest = t;
                        bestTri = tri;
                        bestU = u;
                        bestV = v;
                    }
                }

                continue;
            }

            int left = node.LeftFirst;
            int right = left + 1;
            bool hitL = IntersectAabb(_nodes[left].Min, _nodes[left].Max, ray.Origin, invDir, closest, out float dl);
            bool hitR = IntersectAabb(_nodes[right].Min, _nodes[right].Max, ray.Origin, invDir, closest, out float dr);

            // Descend the nearer child first: push the farther one, then the nearer (popped next).
            if (hitL && hitR)
            {
                if (dl <= dr)
                {
                    stack[sp++] = right;
                    stack[sp++] = left;
                }
                else
                {
                    stack[sp++] = left;
                    stack[sp++] = right;
                }
            }
            else if (hitL)
            {
                stack[sp++] = left;
            }
            else if (hitR)
            {
                stack[sp++] = right;
            }
        }

        if (bestTri < 0)
        {
            return false;
        }

        hit = new Hit(closest, bestTri, bestU, bestV);
        return true;
    }

    /// <inheritdoc/>
    public bool AnyHit(in Ray ray)
    {
        var rs = new RayShear(ray);
        Vector3 invDir = InverseDirection(ray.Direction);
        Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            ref Node node = ref _nodes[stack[--sp]];
            if (!IntersectAabb(node.Min, node.Max, ray.Origin, invDir, ray.TMax, out _))
            {
                continue;
            }

            if (node.Count > 0)
            {
                int end = node.LeftFirst + node.Count;
                for (int i = node.LeftFirst; i < end; i++)
                {
                    int tri = _triIndex[i];
                    if (RayTriangle.Intersect(rs, _v0[tri], _v1[tri], _v2[tri], ray.TMax, out _, out _, out _))
                    {
                        return true;
                    }
                }

                continue;
            }

            stack[sp++] = node.LeftFirst;
            stack[sp++] = node.LeftFirst + 1;
        }

        return false;
    }

    /// <summary>
    /// Finds the closest point on any triangle to <paramref name="query"/> (the nearest-point fallback
    /// when a cage ray misses). <c>hit.T</c> is the distance; <c>hit.U/V</c> the barycentric weights.
    /// </summary>
    public bool ClosestPoint(Vector3 query, out Hit hit)
    {
        hit = default;
        float bestSq = float.MaxValue;
        int bestTri = -1;
        float bestU = 0f;
        float bestV = 0f;

        Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            ref Node node = ref _nodes[stack[--sp]];
            if (DistanceSqToBox(query, node.Min, node.Max) >= bestSq)
            {
                continue;
            }

            if (node.Count > 0)
            {
                int end = node.LeftFirst + node.Count;
                for (int i = node.LeftFirst; i < end; i++)
                {
                    int tri = _triIndex[i];
                    Vector3 cp = TriangleMath.ClosestPointOnTriangle(
                        query, _v0[tri], _v1[tri], _v2[tri], out _, out float wb, out float wc);
                    float dsq = (cp - query).LengthSquared();
                    if (dsq < bestSq)
                    {
                        bestSq = dsq;
                        bestTri = tri;
                        bestU = wb; // barycentric weight of the second vertex
                        bestV = wc; // barycentric weight of the third vertex
                    }
                }

                continue;
            }

            int left = node.LeftFirst;
            int right = left + 1;
            float dl = DistanceSqToBox(query, _nodes[left].Min, _nodes[left].Max);
            float dr = DistanceSqToBox(query, _nodes[right].Min, _nodes[right].Max);
            if (dl <= dr)
            {
                if (dr < bestSq)
                {
                    stack[sp++] = right;
                }

                if (dl < bestSq)
                {
                    stack[sp++] = left;
                }
            }
            else
            {
                if (dl < bestSq)
                {
                    stack[sp++] = left;
                }

                if (dr < bestSq)
                {
                    stack[sp++] = right;
                }
            }
        }

        if (bestTri < 0)
        {
            return false;
        }

        hit = new Hit(MathF.Sqrt(bestSq), bestTri, bestU, bestV);
        return true;
    }

    private static float DistanceSqToBox(Vector3 p, Vector3 min, Vector3 max)
    {
        Vector3 clamped = Vector3.Clamp(p, min, max);
        return (p - clamped).LengthSquared();
    }

    private static Vector3 InverseDirection(Vector3 d) => new(1f / d.X, 1f / d.Y, 1f / d.Z);

    private static bool IntersectAabb(
        Vector3 min, Vector3 max, Vector3 origin, Vector3 invDir, float tMax, out float tNear)
    {
        float tx1 = (min.X - origin.X) * invDir.X;
        float tx2 = (max.X - origin.X) * invDir.X;
        float tmin = MathF.Min(tx1, tx2);
        float tmax = MathF.Max(tx1, tx2);

        float ty1 = (min.Y - origin.Y) * invDir.Y;
        float ty2 = (max.Y - origin.Y) * invDir.Y;
        tmin = MathF.Max(tmin, MathF.Min(ty1, ty2));
        tmax = MathF.Min(tmax, MathF.Max(ty1, ty2));

        float tz1 = (min.Z - origin.Z) * invDir.Z;
        float tz2 = (max.Z - origin.Z) * invDir.Z;
        tmin = MathF.Max(tmin, MathF.Min(tz1, tz2));
        tmax = MathF.Min(tmax, MathF.Max(tz1, tz2));

        tNear = MathF.Max(tmin, 0f);

        // Conservative far bound (robust BVH traversal): nudge tFar up so a ray grazing a box corner or
        // a shared edge is never wrongly rejected. The watertight RayTriangle test still decides hits
        // exactly, so this only ever visits a few extra boxes — it can't introduce a false hit.
        float tFar = tmax >= 0f ? tmax * 1.00001f : tmax;
        return tFar >= tNear && tNear <= tMax;
    }

    internal struct Node
    {
        public Vector3 Min;
        public Vector3 Max;
        public int LeftFirst; // internal: left child node index; leaf: first triangle slot
        public int Count;     // 0 = internal node, >0 = leaf triangle count
    }

    private sealed class Builder
    {
        private readonly Vector3[] _v0;
        private readonly Vector3[] _v1;
        private readonly Vector3[] _v2;
        private readonly Vector3[] _centroid;
        private readonly int[] _triIndex;
        private readonly Node[] _nodes;
        private int _nodeCount;

        public Builder(
            Vector3[] v0, Vector3[] v1, Vector3[] v2, Vector3[] centroid, int[] triIndex, Node[] nodes)
        {
            _v0 = v0;
            _v1 = v1;
            _v2 = v2;
            _centroid = centroid;
            _triIndex = triIndex;
            _nodes = nodes;
        }

        public void Build(int triCount)
        {
            _nodes[0].LeftFirst = 0;
            _nodes[0].Count = triCount;
            _nodeCount = 1;
            if (triCount == 0)
            {
                _nodes[0].Min = Vector3.Zero;
                _nodes[0].Max = Vector3.Zero;
                return;
            }

            UpdateBounds(0);
            Subdivide(0);
        }

        private void UpdateBounds(int nodeIndex)
        {
            ref Node node = ref _nodes[nodeIndex];
            var box = Aabb.Empty;
            int end = node.LeftFirst + node.Count;
            for (int i = node.LeftFirst; i < end; i++)
            {
                int tri = _triIndex[i];
                box = box.Include(_v0[tri]).Include(_v1[tri]).Include(_v2[tri]);
            }

            // Pad the box by a small relative+absolute epsilon so a ray grazing a triangle exactly at a
            // box corner/edge (e.g. aimed through a shared vertex) is never wrongly culled. The exact
            // RayTriangle test still decides every hit, so padding can only add a few visited boxes.
            Vector3 pad = ((box.Max - box.Min) * 1e-5f) + new Vector3(1e-6f);
            node.Min = box.Min - pad;
            node.Max = box.Max + pad;
        }

        private void Subdivide(int nodeIndex)
        {
            ref Node node = ref _nodes[nodeIndex];
            if (node.Count <= MaxLeafSize)
            {
                return;
            }

            if (!TryFindSplit(node, out int axis, out float position, out float splitCost))
            {
                return;
            }

            float leafCost = node.Count * new Aabb(node.Min, node.Max).SurfaceArea;
            if (splitCost >= leafCost)
            {
                return; // splitting does not pay off — keep as a leaf
            }

            int first = node.LeftFirst;
            int mid = Partition(first, node.Count, axis, position);
            int leftCount = mid - first;
            if (leftCount == 0 || leftCount == node.Count)
            {
                return; // degenerate split — keep as a leaf
            }

            int leftChild = _nodeCount++;
            int rightChild = _nodeCount++;
            _nodes[leftChild].LeftFirst = first;
            _nodes[leftChild].Count = leftCount;
            _nodes[rightChild].LeftFirst = mid;
            _nodes[rightChild].Count = node.Count - leftCount;

            node.LeftFirst = leftChild;
            node.Count = 0;

            UpdateBounds(leftChild);
            UpdateBounds(rightChild);
            Subdivide(leftChild);
            Subdivide(rightChild);
        }

        private bool TryFindSplit(in Node node, out int bestAxis, out float bestPos, out float bestCost)
        {
            bestAxis = -1;
            bestPos = 0f;
            bestCost = float.MaxValue;

            int first = node.LeftFirst;
            int count = node.Count;
            Span<int> binCount = stackalloc int[BinCount];
            Span<Aabb> binBox = stackalloc Aabb[BinCount];
            for (int axis = 0; axis < 3; axis++)
            {
                float boundsMin = float.MaxValue;
                float boundsMax = float.MinValue;
                for (int i = first; i < first + count; i++)
                {
                    float c = RayShear.Component(_centroid[_triIndex[i]], axis);
                    boundsMin = MathF.Min(boundsMin, c);
                    boundsMax = MathF.Max(boundsMax, c);
                }

                if (boundsMax - boundsMin < 1e-12f)
                {
                    continue;
                }

                binCount.Clear();
                for (int b = 0; b < BinCount; b++)
                {
                    binBox[b] = Aabb.Empty;
                }

                float scale = BinCount / (boundsMax - boundsMin);
                for (int i = first; i < first + count; i++)
                {
                    int tri = _triIndex[i];
                    int b = Math.Min(BinCount - 1, (int)((RayShear.Component(_centroid[tri], axis) - boundsMin) * scale));
                    binCount[b]++;
                    binBox[b] = binBox[b].Include(_v0[tri]).Include(_v1[tri]).Include(_v2[tri]);
                }

                EvaluateBins(binCount, binBox, boundsMin, scale, axis, ref bestAxis, ref bestPos, ref bestCost);
            }

            return bestAxis >= 0;
        }

        private static void EvaluateBins(
            ReadOnlySpan<int> binCount, ReadOnlySpan<Aabb> binBox, float boundsMin, float scale, int axis,
            ref int bestAxis, ref float bestPos, ref float bestCost)
        {
            Span<float> leftArea = stackalloc float[BinCount - 1];
            Span<float> rightArea = stackalloc float[BinCount - 1];
            Span<int> leftCount = stackalloc int[BinCount - 1];
            Span<int> rightCount = stackalloc int[BinCount - 1];

            var leftBox = Aabb.Empty;
            var rightBox = Aabb.Empty;
            int leftSum = 0;
            int rightSum = 0;
            for (int i = 0; i < BinCount - 1; i++)
            {
                leftSum += binCount[i];
                leftCount[i] = leftSum;
                leftBox = leftBox.Union(binBox[i]);
                leftArea[i] = leftBox.SurfaceArea;

                int r = BinCount - 1 - i;
                rightSum += binCount[r];
                rightCount[BinCount - 2 - i] = rightSum;
                rightBox = rightBox.Union(binBox[r]);
                rightArea[BinCount - 2 - i] = rightBox.SurfaceArea;
            }

            float inverseScale = 1f / scale;
            for (int i = 0; i < BinCount - 1; i++)
            {
                float cost = (leftCount[i] * leftArea[i]) + (rightCount[i] * rightArea[i]);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestAxis = axis;
                    bestPos = boundsMin + ((i + 1) * inverseScale);
                }
            }
        }

        private int Partition(int first, int count, int axis, float position)
        {
            int i = first;
            int j = first + count - 1;
            while (i <= j)
            {
                if (RayShear.Component(_centroid[_triIndex[i]], axis) < position)
                {
                    i++;
                }
                else
                {
                    (_triIndex[i], _triIndex[j]) = (_triIndex[j], _triIndex[i]);
                    j--;
                }
            }

            return i;
        }
    }
}
