using NormalBake.Core;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>The procedural corpus is well-formed, and (on request) is written to <c>tests/corpus</c>.</summary>
public class CorpusTests
{
    [Fact]
    public void EveryPair_HasNonEmptyHighAndLowPoly()
    {
        foreach (MeshPair pair in Primitives.All())
        {
            Assert.True(pair.High.TriangleCount > 0, $"{pair.Name} high-poly is empty");
            Assert.True(pair.Low.TriangleCount > 0, $"{pair.Name} low-poly is empty");
            Assert.True(pair.Low.HasTangents is false); // tangents arrive in P2
        }
    }

    [Fact]
    public void HighPoly_IsDenserThanLowPoly()
    {
        foreach (MeshPair pair in Primitives.All())
        {
            Assert.True(
                pair.High.TriangleCount > pair.Low.TriangleCount,
                $"{pair.Name}: high {pair.High.TriangleCount} should exceed low {pair.Low.TriangleCount}");
        }
    }

    // Generates the committed corpus OBJs. Off by default; run with NORMALBAKE_WRITE_CORPUS=1 to emit.
    [Fact]
    public void WriteCorpusFiles_WhenRequested()
    {
        if (Environment.GetEnvironmentVariable("NORMALBAKE_WRITE_CORPUS") != "1")
        {
            return;
        }

        foreach (MeshPair pair in Primitives.All())
        {
            CorpusFiles.WritePair(Path.Combine(CorpusFiles.CorpusDirectory, pair.Name), pair);
        }
    }
}
