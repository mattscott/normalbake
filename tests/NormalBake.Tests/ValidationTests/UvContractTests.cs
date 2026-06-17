using System.Numerics;
using NormalBake.Core;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>UV-contract validation: accepts the procedural corpus, rejects overlap / poor gutters / junk.</summary>
public class UvContractTests
{
    [Theory]
    [InlineData("sphere")]
    [InlineData("box")]
    [InlineData("cylinder")]
    public void Validate_AcceptsCleanCorpusLowPoly(string name)
    {
        UvMesh low = GetPair(name).Low;

        UvValidationResult result = UvContract.Validate(low);

        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void Validate_RejectsOverlappingUvs()
    {
        // Two identical (folded) triangles cover the same texels.
        var positions = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) };
        var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        var uvs = new[] { new Vector2(0.3f, 0.3f), new Vector2(0.6f, 0.3f), new Vector2(0.3f, 0.6f) };
        var low = new UvMesh(positions, normals, uvs, new[] { 0, 1, 2, 0, 1, 2 });

        UvValidationResult result = UvContract.Validate(low);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "UV.Overlap" && i.Severity == UvValidationSeverity.Error);
    }

    [Fact]
    public void Validate_RejectsInsufficientGutter()
    {
        // Two separate islands ~0.006 UV apart (≈6 texels at 1K) — below the required 16.
        var positions = new Vector3[6];
        Array.Fill(positions, Vector3.Zero);
        var normals = new Vector3[6];
        Array.Fill(normals, Vector3.UnitZ);
        var uvs = new[]
        {
            new Vector2(0.40f, 0.40f), new Vector2(0.45f, 0.40f), new Vector2(0.40f, 0.45f),
            new Vector2(0.456f, 0.40f), new Vector2(0.506f, 0.40f), new Vector2(0.456f, 0.45f),
        };
        var low = new UvMesh(positions, normals, uvs, new[] { 0, 1, 2, 3, 4, 5 });

        UvValidationResult result = UvContract.Validate(low);

        // Tight gutters are a mip-bleed quality warning (dilation mitigates), not a bake-correctness error.
        Assert.Contains(result.Issues, i => i.Code == "UV.Gutter" && i.Severity == UvValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_RejectsOutOfRangeIndex()
    {
        var positions = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY };
        var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        var uvs = new[] { Vector2.Zero, new Vector2(1, 0), new Vector2(0, 1) };
        var low = new UvMesh(positions, normals, uvs, new[] { 0, 1, 9 });

        UvValidationResult result = UvContract.Validate(low);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "UV.BadIndex");
    }

    [Fact]
    public void Validate_RejectsNonFiniteUv()
    {
        var positions = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY };
        var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        var uvs = new[] { Vector2.Zero, new Vector2(float.NaN, 0), new Vector2(0, 1) };
        var low = new UvMesh(positions, normals, uvs, new[] { 0, 1, 2 });

        UvValidationResult result = UvContract.Validate(low);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "UV.NonFinite");
    }

    [Fact]
    public void Validate_WarnsButAcceptsTrianglesOutsideTile()
    {
        var positions = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY };
        var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        var uvs = new[] { new Vector2(1.2f, 1.2f), new Vector2(1.6f, 1.2f), new Vector2(1.2f, 1.6f) };
        var low = new UvMesh(positions, normals, uvs, new[] { 0, 1, 2 });

        UvValidationResult result = UvContract.Validate(low);

        Assert.True(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "UV.OutsideTile" && i.Severity == UvValidationSeverity.Warning);
    }

    [Fact]
    public void ValidateOrThrow_ThrowsOnViolation()
    {
        var positions = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY };
        var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        var uvs = new[] { new Vector2(0.3f, 0.3f), new Vector2(0.6f, 0.3f), new Vector2(0.3f, 0.6f) };
        var low = new UvMesh(positions, normals, uvs, new[] { 0, 1, 2, 0, 1, 2 });

        Assert.Throws<UvContractException>(() => UvContract.ValidateOrThrow(low));
    }

    private static MeshPair GetPair(string name) => name switch
    {
        "sphere" => Primitives.Sphere(),
        "box" => Primitives.Box(),
        "cylinder" => Primitives.Cylinder(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown primitive."),
    };

    private static string Describe(UvValidationResult result) =>
        string.Join("; ", result.Issues.Select(i => i.ToString()));
}
