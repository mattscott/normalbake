using System.Numerics;
using NormalBake.Core;
using NormalBake.IO;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>
/// FBX reader validation against the local hand-authored car asset (Assets/03*_apart.fbx). Gated on the
/// files being present (they are kept local / git-ignored), so CI without them simply skips.
/// </summary>
public class FbxReaderTests
{
    private static string? AssetDir
    {
        get
        {
            string dir = Path.Combine(CorpusFiles.FindRepoRoot(), "Assets");
            return Directory.Exists(dir) ? dir : null;
        }
    }

    [Fact]
    public void ReadsCarParts_WithDenseHigh_AndValidLowUvs()
    {
        if (AssetDir is null)
        {
            return;
        }

        IReadOnlyList<FbxPart> lowParts = FbxReader.ReadParts(Path.Combine(AssetDir, "03low_apart.fbx"));
        Mesh high = FbxReader.ReadMesh(Path.Combine(AssetDir, "03high_apart.fbx"));
        UvMesh low = FbxReader.ReadUvMesh(Path.Combine(AssetDir, "03low_apart.fbx"));

        Assert.Equal(9, lowParts.Count);
        Assert.True(high.TriangleCount > low.TriangleCount * 20, "high should be far denser than low");

        // UVs are finite and (mostly) within the 0-1 tile.
        foreach (Vector2 uv in low.Uvs)
        {
            Assert.True(float.IsFinite(uv.X) && float.IsFinite(uv.Y));
        }
    }

    [Fact]
    public void HighAndLow_AlignInWorldSpace_AfterTransforms()
    {
        if (AssetDir is null)
        {
            return;
        }

        Mesh high = FbxReader.ReadMesh(Path.Combine(AssetDir, "03high_apart.fbx"));
        UvMesh low = FbxReader.ReadUvMesh(Path.Combine(AssetDir, "03low_apart.fbx"));
        (Vector3 hMin, Vector3 hMax) = Bounds(high.Positions);
        (Vector3 lMin, Vector3 lMax) = Bounds(low.Positions);

        // After applying the Model transforms, the high and low must occupy the same world space — the
        // bounds should match within a small fraction of the model size (only the surface gap differs).
        float tolerance = (hMax - hMin).Length() * 0.02f;
        Assert.True((hMin - lMin).Length() < tolerance, $"min misaligned: high {hMin} vs low {lMin}");
        Assert.True((hMax - lMax).Length() < tolerance, $"max misaligned: high {hMax} vs low {lMax}");
    }

    private static (Vector3 Min, Vector3 Max) Bounds(ReadOnlySpan<Vector3> points)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (Vector3 p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return (min, max);
    }
}
