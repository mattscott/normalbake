using System.Numerics;
using NormalBake.Core;

namespace NormalBake.Tests.Corpus;

/// <summary>A high-poly source mesh paired with its UV-unwrapped low-poly target.</summary>
public sealed record MeshPair(string Name, Mesh High, UvMesh Low);

/// <summary>
/// Deterministic, license-free procedural corpus (tier A). Each entry is a mesh pair — a detailed
/// high-poly plus a coarse, UV-unwrapped low-poly — exercising curvature and hard edges without any
/// downloaded asset: a lat-long <b>sphere</b> (one UV island), a flat-low / rounded-high <b>box</b>
/// (six per-face islands, hard edges), and a <b>cylinder</b> (side + two caps, three islands). See
/// <c>Agents/Prompts/01-implementation-kickoff.md</c> §9A.
/// </summary>
public static class Primitives
{
    /// <summary>All procedural mesh pairs.</summary>
    public static IReadOnlyList<MeshPair> All() => new[] { Sphere(), Box(), Cylinder() };

    /// <summary>A unit sphere: fine UV-sphere high-poly, coarse lat-long-UV low-poly.</summary>
    public static MeshPair Sphere()
    {
        Mesh high = BuildUvSphere(1f, stacks: 32, slices: 64);
        UvMesh low = BuildUvSphereWithUvs(1f, stacks: 12, slices: 24);
        return new MeshPair("sphere", high, low);
    }

    /// <summary>A box: rounded (beveled) high-poly, flat low-poly with six per-face UV islands.</summary>
    public static MeshPair Box()
    {
        Mesh high = BuildRoundedBox(0.5f, radius: 0.12f, subdivisions: 16);
        UvMesh low = BuildFlatBox(0.5f);
        return new MeshPair("box", high, low);
    }

    /// <summary>A cylinder: fine high-poly, coarse low-poly unwrapped as a side rectangle + two cap disks.</summary>
    public static MeshPair Cylinder()
    {
        Mesh high = BuildCylinder(0.4f, halfHeight: 0.5f, slices: 72, withUv: false, out _, out _, out _, out _);
        UvMesh low = BuildCylinderLow(0.4f, halfHeight: 0.5f, slices: 24);
        return new MeshPair("cylinder", high, low);
    }

    /// <summary>
    /// Two coplanar quads (normal +Z) in two separate UV islands: island A with standard UVs and
    /// island B with U mirrored. The mirror must flip the tangent handedness <c>w</c> — the canonical
    /// MikkTSpace mirrored-UV test without needing a downloaded glTF asset. Vertices 0..3 are island A,
    /// 4..7 island B.
    /// </summary>
    public static UvMesh MirroredQuads()
    {
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        var uv = new List<Vector2>();
        var idx = new List<int>();
        Vector3 n = Vector3.UnitZ;

        int a = pos.Count; // island A: +U follows +X
        AddVertex(pos, nrm, uv, new Vector3(0, 0, 0), n, new Vector2(0.05f, 0.05f));
        AddVertex(pos, nrm, uv, new Vector3(1, 0, 0), n, new Vector2(0.40f, 0.05f));
        AddVertex(pos, nrm, uv, new Vector3(1, 1, 0), n, new Vector2(0.40f, 0.40f));
        AddVertex(pos, nrm, uv, new Vector3(0, 1, 0), n, new Vector2(0.05f, 0.40f));
        AddQuad(idx, a, a + 1, a + 2, a + 3);

        int b = pos.Count; // island B: +U follows -X (mirrored)
        AddVertex(pos, nrm, uv, new Vector3(2, 0, 0), n, new Vector2(0.95f, 0.05f));
        AddVertex(pos, nrm, uv, new Vector3(3, 0, 0), n, new Vector2(0.60f, 0.05f));
        AddVertex(pos, nrm, uv, new Vector3(3, 1, 0), n, new Vector2(0.60f, 0.40f));
        AddVertex(pos, nrm, uv, new Vector3(2, 1, 0), n, new Vector2(0.95f, 0.40f));
        AddQuad(idx, b, b + 1, b + 2, b + 3);

        return new UvMesh(pos.ToArray(), nrm.ToArray(), uv.ToArray(), idx.ToArray());
    }

    // --- sphere -----------------------------------------------------------------------------------

    private static Mesh BuildUvSphere(float r, int stacks, int slices)
    {
        BuildSphereArrays(r, stacks, slices, out Vector3[] pos, out Vector3[] nrm, out _, out int[] idx);
        return new Mesh(pos, nrm, idx);
    }

    private static UvMesh BuildUvSphereWithUvs(float r, int stacks, int slices)
    {
        BuildSphereArrays(r, stacks, slices, out Vector3[] pos, out Vector3[] nrm, out Vector2[] uv, out int[] idx);
        return new UvMesh(pos, nrm, uv, idx);
    }

    private static void BuildSphereArrays(
        float r, int stacks, int slices, out Vector3[] positions, out Vector3[] normals,
        out Vector2[] uvs, out int[] indices)
    {
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        var uv = new List<Vector2>();
        for (int i = 0; i <= stacks; i++)
        {
            float phi = MathF.PI * i / stacks;          // 0 (north pole) .. PI (south pole)
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);
            for (int j = 0; j <= slices; j++)
            {
                float theta = 2f * MathF.PI * j / slices;
                var p = new Vector3(r * sinPhi * MathF.Cos(theta), r * cosPhi, r * sinPhi * MathF.Sin(theta));
                pos.Add(p);
                nrm.Add(Vector3.Normalize(new Vector3(sinPhi * MathF.Cos(theta), cosPhi, sinPhi * MathF.Sin(theta))));
                uv.Add(new Vector2((float)j / slices, (float)i / stacks));
            }
        }

        int cols = slices + 1;
        var idx = new List<int>();
        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                int v00 = (i * cols) + j;
                int v01 = v00 + 1;
                int v10 = v00 + cols;
                int v11 = v10 + 1;
                AddQuad(idx, v00, v10, v11, v01);
            }
        }

        positions = pos.ToArray();
        normals = nrm.ToArray();
        uvs = uv.ToArray();
        indices = idx.ToArray();
    }

    // --- box --------------------------------------------------------------------------------------

    private static readonly (Vector3 N, Vector3 A, Vector3 B, Vector3 C, Vector3 D)[] CubeFaces =
    {
        (new(1, 0, 0), new(1, -1, 1), new(1, -1, -1), new(1, 1, -1), new(1, 1, 1)),     // +X
        (new(-1, 0, 0), new(-1, -1, -1), new(-1, -1, 1), new(-1, 1, 1), new(-1, 1, -1)), // -X
        (new(0, 1, 0), new(-1, 1, 1), new(1, 1, 1), new(1, 1, -1), new(-1, 1, -1)),      // +Y
        (new(0, -1, 0), new(-1, -1, -1), new(1, -1, -1), new(1, -1, 1), new(-1, -1, 1)), // -Y
        (new(0, 0, 1), new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)),      // +Z
        (new(0, 0, -1), new(1, -1, -1), new(-1, -1, -1), new(-1, 1, -1), new(1, 1, -1)), // -Z
    };

    private static UvMesh BuildFlatBox(float s)
    {
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        var uv = new List<Vector2>();
        var idx = new List<int>();
        const float margin = 0.04f; // inset inside each 3x2 cell → inter-island gutter ≈ 0.08 UV
        for (int f = 0; f < CubeFaces.Length; f++)
        {
            (Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d) = CubeFaces[f];
            int col = f % 3;
            int row = f / 3;
            float u0 = (col / 3f) + margin;
            float u1 = ((col + 1) / 3f) - margin;
            float v0 = (row / 2f) + margin;
            float v1 = ((row + 1) / 2f) - margin;

            int baseIndex = pos.Count;
            AddVertex(pos, nrm, uv, a * s, n, new Vector2(u0, v0));
            AddVertex(pos, nrm, uv, b * s, n, new Vector2(u1, v0));
            AddVertex(pos, nrm, uv, c * s, n, new Vector2(u1, v1));
            AddVertex(pos, nrm, uv, d * s, n, new Vector2(u0, v1));
            AddQuad(idx, baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 3);
        }

        return new UvMesh(pos.ToArray(), nrm.ToArray(), uv.ToArray(), idx.ToArray());
    }

    private static Mesh BuildRoundedBox(float s, float radius, int subdivisions)
    {
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        var idx = new List<int>();
        int n = subdivisions;
        float inner = s - radius;
        foreach ((Vector3 _, Vector3 a, Vector3 b, Vector3 d, Vector3 _) in CubeFaces)
        {
            Vector3 origin = a * s;
            Vector3 du = (b - a) * s; // edge A→B
            Vector3 dv = (d - a) * s; // edge A→D
            int baseIndex = pos.Count;
            for (int j = 0; j <= n; j++)
            {
                for (int i = 0; i <= n; i++)
                {
                    Vector3 cube = origin + (du * (i / (float)n)) + (dv * (j / (float)n));
                    Vector3 core = new(
                        Math.Clamp(cube.X, -inner, inner),
                        Math.Clamp(cube.Y, -inner, inner),
                        Math.Clamp(cube.Z, -inner, inner));
                    Vector3 delta = cube - core;
                    float len = delta.Length();
                    Vector3 normal = len > 1e-6f ? delta / len : Vector3.Normalize(cube);
                    pos.Add(core + (normal * radius));
                    nrm.Add(normal);
                }
            }

            int cols = n + 1;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int v00 = baseIndex + (j * cols) + i;
                    AddQuad(idx, v00, v00 + 1, v00 + 1 + cols, v00 + cols);
                }
            }
        }

        return new Mesh(pos.ToArray(), nrm.ToArray(), idx.ToArray());
    }

    // --- cylinder ---------------------------------------------------------------------------------

    private static Mesh BuildCylinder(
        float r, float halfHeight, int slices, bool withUv,
        out Vector3[] pos, out Vector3[] nrm, out Vector2[] uv, out int[] idx)
    {
        _ = withUv;
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();

        // Side: ring of (slices+1) columns × 2 rows (bottom, top), radial normals.
        int sideBase = positions.Count;
        for (int row = 0; row < 2; row++)
        {
            float y = row == 0 ? -halfHeight : halfHeight;
            for (int j = 0; j <= slices; j++)
            {
                float theta = 2f * MathF.PI * j / slices;
                var dir = new Vector3(MathF.Cos(theta), 0f, MathF.Sin(theta));
                positions.Add(new Vector3(dir.X * r, y, dir.Z * r));
                normals.Add(dir);
                uvs.Add(new Vector2((float)j / slices, row));
            }
        }

        int cols = slices + 1;
        for (int j = 0; j < slices; j++)
        {
            int b0 = sideBase + j;
            int t0 = sideBase + cols + j;
            AddQuad(indices, b0, b0 + 1, t0 + 1, t0);
        }

        AddCap(positions, normals, uvs, indices, r, halfHeight, slices, top: true);
        AddCap(positions, normals, uvs, indices, r, -halfHeight, slices, top: false);

        pos = positions.ToArray();
        nrm = normals.ToArray();
        uv = uvs.ToArray();
        idx = indices.ToArray();
        return new Mesh(pos, nrm, idx);
    }

    private static void AddCap(
        List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv, List<int> idx,
        float r, float y, int slices, bool top)
    {
        var normal = new Vector3(0f, top ? 1f : -1f, 0f);
        int center = pos.Count;
        pos.Add(new Vector3(0f, y, 0f));
        nrm.Add(normal);
        uv.Add(new Vector2(0.5f, 0.5f));
        int ringBase = pos.Count;
        for (int j = 0; j <= slices; j++)
        {
            float theta = 2f * MathF.PI * j / slices;
            pos.Add(new Vector3(MathF.Cos(theta) * r, y, MathF.Sin(theta) * r));
            nrm.Add(normal);
            uv.Add(new Vector2((MathF.Cos(theta) * 0.5f) + 0.5f, (MathF.Sin(theta) * 0.5f) + 0.5f));
        }

        for (int j = 0; j < slices; j++)
        {
            int a = ringBase + j;
            int b = ringBase + j + 1;
            if (top)
            {
                idx.Add(center);
                idx.Add(a);
                idx.Add(b);
            }
            else
            {
                idx.Add(center);
                idx.Add(b);
                idx.Add(a);
            }
        }
    }

    private static UvMesh BuildCylinderLow(float r, float halfHeight, int slices)
    {
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        var uv = new List<Vector2>();
        var idx = new List<int>();

        // Side island: a rectangle in the lower-left of the tile.
        int sideBase = pos.Count;
        for (int row = 0; row < 2; row++)
        {
            float y = row == 0 ? -halfHeight : halfHeight;
            for (int j = 0; j <= slices; j++)
            {
                float theta = 2f * MathF.PI * j / slices;
                var dir = new Vector3(MathF.Cos(theta), 0f, MathF.Sin(theta));
                pos.Add(new Vector3(dir.X * r, y, dir.Z * r));
                nrm.Add(dir);
                uv.Add(new Vector2(0.05f + (0.55f * j / slices), 0.1f + (0.8f * row)));
            }
        }

        int cols = slices + 1;
        for (int j = 0; j < slices; j++)
        {
            int b0 = sideBase + j;
            int t0 = sideBase + cols + j;
            AddQuad(idx, b0, b0 + 1, t0 + 1, t0);
        }

        AddCapLow(pos, nrm, uv, idx, r, halfHeight, slices, top: true, new Vector2(0.8f, 0.75f), 0.12f);
        AddCapLow(pos, nrm, uv, idx, r, -halfHeight, slices, top: false, new Vector2(0.8f, 0.25f), 0.12f);

        return new UvMesh(pos.ToArray(), nrm.ToArray(), uv.ToArray(), idx.ToArray());
    }

    private static void AddCapLow(
        List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv, List<int> idx,
        float r, float y, int slices, bool top, Vector2 uvCenter, float uvRadius)
    {
        var normal = new Vector3(0f, top ? 1f : -1f, 0f);
        int center = pos.Count;
        pos.Add(new Vector3(0f, y, 0f));
        nrm.Add(normal);
        uv.Add(uvCenter);
        int ringBase = pos.Count;
        for (int j = 0; j <= slices; j++)
        {
            float theta = 2f * MathF.PI * j / slices;
            pos.Add(new Vector3(MathF.Cos(theta) * r, y, MathF.Sin(theta) * r));
            nrm.Add(normal);
            uv.Add(uvCenter + new Vector2(MathF.Cos(theta) * uvRadius, MathF.Sin(theta) * uvRadius));
        }

        for (int j = 0; j < slices; j++)
        {
            int a = ringBase + j;
            int b = ringBase + j + 1;
            if (top)
            {
                idx.Add(center);
                idx.Add(a);
                idx.Add(b);
            }
            else
            {
                idx.Add(center);
                idx.Add(b);
                idx.Add(a);
            }
        }
    }

    // --- shared -----------------------------------------------------------------------------------

    private static void AddVertex(
        List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv, Vector3 p, Vector3 n, Vector2 t)
    {
        pos.Add(p);
        nrm.Add(n);
        uv.Add(t);
    }

    private static void AddQuad(List<int> idx, int a, int b, int c, int d)
    {
        idx.Add(a);
        idx.Add(b);
        idx.Add(c);
        idx.Add(a);
        idx.Add(c);
        idx.Add(d);
    }
}
