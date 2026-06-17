using System.Numerics;
using NormalBake.Core;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>
/// P2 validation of the MikkTSpace-compatible tangent solver: orthonormality, analytic agreement,
/// order-independence, mirrored-UV handedness, and degenerate tolerance. (Bit-parity with mikktspace.c
/// is deferred — no C toolchain; glTF-shipped tangents are compared separately once the glTF reader lands.)
/// </summary>
public class MikkTSpaceTests
{
    [Theory]
    [InlineData("sphere")]
    [InlineData("box")]
    [InlineData("cylinder")]
    public void Tangents_AreOrthonormal_WithUnitHandedness(string name)
    {
        UvMesh mesh = GetLow(name);
        MikkTSpaceTangents.Generate(mesh);

        Assert.True(mesh.HasTangents);
        ReadOnlySpan<Vector4> tangents = mesh.Tangents;
        ReadOnlySpan<Vector3> normals = mesh.Normals;
        for (int v = 0; v < tangents.Length; v++)
        {
            var t = new Vector3(tangents[v].X, tangents[v].Y, tangents[v].Z);
            Assert.True(MathF.Abs(t.Length() - 1f) < 1e-3f, $"{name} vtx {v}: |T|={t.Length()}");
            Assert.True(MathF.Abs(Vector3.Dot(t, normals[v])) < 1e-3f, $"{name} vtx {v}: T·N not 0");
            Assert.True(tangents[v].W is 1f or -1f, $"{name} vtx {v}: w={tangents[v].W}");
        }
    }

    [Fact]
    public void Sphere_TangentsFollowAnalyticLongitude()
    {
        UvMesh mesh = Primitives.Sphere().Low;
        MikkTSpaceTangents.Generate(mesh);

        ReadOnlySpan<Vector4> tangents = mesh.Tangents;
        ReadOnlySpan<Vector2> uvs = mesh.Uvs;
        int inspected = 0;
        for (int v = 0; v < tangents.Length; v++)
        {
            if (uvs[v].Y is < 0.08f or > 0.92f)
            {
                continue; // skip pole neighbourhood where the tangent is ill-defined
            }

            float theta = 2f * MathF.PI * uvs[v].X;
            var analytic = new Vector3(-MathF.Sin(theta), 0f, MathF.Cos(theta));
            var t = new Vector3(tangents[v].X, tangents[v].Y, tangents[v].Z);
            Assert.True(MathF.Abs(Vector3.Dot(t, analytic)) > 0.96f, $"vtx {v}: dot={Vector3.Dot(t, analytic)}");
            inspected++;
        }

        Assert.True(inspected > 100, $"expected many interior vertices, checked {inspected}");
    }

    [Fact]
    public void Cylinder_SideTangentsFollowAnalyticLongitude()
    {
        UvMesh mesh = Primitives.Cylinder().Low;
        MikkTSpaceTangents.Generate(mesh);

        ReadOnlySpan<Vector4> tangents = mesh.Tangents;
        ReadOnlySpan<Vector3> positions = mesh.Positions;
        ReadOnlySpan<Vector3> normals = mesh.Normals;
        int inspected = 0;
        for (int v = 0; v < tangents.Length; v++)
        {
            if (MathF.Abs(normals[v].Y) > 0.5f)
            {
                continue; // cap vertices, not the side
            }

            float thetaPos = MathF.Atan2(positions[v].Z, positions[v].X);
            var analytic = new Vector3(-MathF.Sin(thetaPos), 0f, MathF.Cos(thetaPos));
            var t = new Vector3(tangents[v].X, tangents[v].Y, tangents[v].Z);
            Assert.True(MathF.Abs(Vector3.Dot(t, analytic)) > 0.96f, $"vtx {v}: dot={Vector3.Dot(t, analytic)}");
            inspected++;
        }

        Assert.True(inspected > 10);
    }

    [Fact]
    public void Solver_IsOrderIndependent_WithinTolerance()
    {
        UvMesh original = Primitives.Sphere().Low;
        MikkTSpaceTangents.Generate(original);

        (UvMesh shuffled, int[] perm) = Shuffle(Primitives.Sphere().Low);
        MikkTSpaceTangents.Generate(shuffled);

        ReadOnlySpan<Vector4> a = original.Tangents;
        ReadOnlySpan<Vector4> b = shuffled.Tangents;
        for (int v = 0; v < a.Length; v++)
        {
            Vector4 sb = b[perm[v]];
            var ta = new Vector3(a[v].X, a[v].Y, a[v].Z);
            var tb = new Vector3(sb.X, sb.Y, sb.Z);
            Assert.True(Vector3.Dot(ta, tb) > 0.999f, $"vtx {v}: dot={Vector3.Dot(ta, tb)}");
            Assert.Equal(a[v].W, sb.W);
        }
    }

    [Fact]
    public void MirroredUv_FlipsHandedness()
    {
        UvMesh mesh = Primitives.MirroredQuads();
        MikkTSpaceTangents.Generate(mesh);

        ReadOnlySpan<Vector4> t = mesh.Tangents;
        // Island A = vertices 0..3, island B = vertices 4..7.
        for (int v = 0; v < 4; v++)
        {
            Assert.Equal(t[0].W, t[v].W); // consistent within island A
            Assert.Equal(t[4].W, t[v + 4].W); // consistent within island B
        }

        Assert.NotEqual(t[0].W, t[4].W); // mirror flips the sign
    }

    [Fact]
    public void DegenerateTriangle_ProducesNoNaN()
    {
        var positions = new[]
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 0),
        };
        var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        var uvs = new[] { Vector2.Zero, new Vector2(1, 0), new Vector2(0, 1), Vector2.Zero };
        var mesh = new UvMesh(positions, normals, uvs, new[] { 0, 1, 2, 0, 0, 3 }); // 2nd triangle is degenerate
        MikkTSpaceTangents.Generate(mesh);

        foreach (Vector4 tan in mesh.Tangents)
        {
            Assert.True(float.IsFinite(tan.X) && float.IsFinite(tan.Y) && float.IsFinite(tan.Z));
            Assert.True(tan.W is 1f or -1f);
        }
    }

    private static (UvMesh Mesh, int[] Perm) Shuffle(UvMesh src)
    {
        int n = src.VertexCount;
        var perm = new int[n]; // perm[old] = new
        for (int v = 0; v < n; v++)
        {
            perm[v] = (7 * v) % n; // bijection (7 coprime with 325)
        }

        var pos = new Vector3[n];
        var nrm = new Vector3[n];
        var uv = new Vector2[n];
        ReadOnlySpan<Vector3> sp = src.Positions;
        ReadOnlySpan<Vector3> sn = src.Normals;
        ReadOnlySpan<Vector2> su = src.Uvs;
        for (int v = 0; v < n; v++)
        {
            pos[perm[v]] = sp[v];
            nrm[perm[v]] = sn[v];
            uv[perm[v]] = su[v];
        }

        ReadOnlySpan<int> si = src.Indices;
        int triCount = si.Length / 3;
        var idx = new int[si.Length];
        for (int t = 0; t < triCount; t++)
        {
            int dst = (triCount - 1 - t) * 3; // reverse triangle order too
            idx[dst] = perm[si[(t * 3) + 0]];
            idx[dst + 1] = perm[si[(t * 3) + 1]];
            idx[dst + 2] = perm[si[(t * 3) + 2]];
        }

        return (new UvMesh(pos, nrm, uv, idx), perm);
    }

    private static UvMesh GetLow(string name) => name switch
    {
        "sphere" => Primitives.Sphere().Low,
        "box" => Primitives.Box().Low,
        "cylinder" => Primitives.Cylinder().Low,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown primitive."),
    };
}
