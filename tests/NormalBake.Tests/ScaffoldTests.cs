using NormalBake.Core;
using NormalBake.IO;
using Xunit;

namespace NormalBake.Tests;

/// <summary>
/// P0 smoke tests: the solution builds, the public scaffold surface is reachable, and the documented
/// defaults hold. Real bake tests (golden-image diffs vs the reference oracle) arrive in Phase 1.
/// </summary>
public class ScaffoldTests
{
    [Fact]
    public void DefaultOptions_MatchDocumentedDefaults()
    {
        var opt = new NormalBakeOptions();

        Assert.Equal(2048, opt.Resolution);
        Assert.Equal(4, opt.Samples);
        Assert.Equal(NormalMapConvention.OpenGl, opt.Convention);
        Assert.Equal(16, opt.BitDepth);
        Assert.Equal(OutputFormat.Png, opt.Format);
        Assert.Equal(-1, opt.Threads);
    }

    [Fact]
    public void Io_StartsWithObjAndPng()
    {
        Assert.Equal(".obj", MeshIo.PrimaryMeshExtension);
        Assert.Equal(".png", MeshIo.PrimaryImageExtension);
    }
}
