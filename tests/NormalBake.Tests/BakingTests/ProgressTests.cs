using NormalBake.Core;
using NormalBake.Tests.Corpus;
using Xunit;

namespace NormalBake.Tests;

/// <summary>P8: the progress/cancellation overload reports 0→1 and honours a cancellation token.</summary>
public class ProgressTests
{
    [Fact]
    public void Bake_ReportsProgress_EndingAtOne()
    {
        MeshPair sp = Primitives.Sphere();
        var progress = new SyncProgress();

        NormalBaker.Bake(sp.High, sp.Low, new NormalBakeOptions { Resolution = 96, Samples = 2 }, progress);

        Assert.NotEmpty(progress.Values);
        Assert.All(progress.Values, v => Assert.InRange(v, 0f, 1f));
        Assert.Equal(1f, progress.Values[^1]);
    }

    [Fact]
    public void Bake_CancelledToken_Throws()
    {
        MeshPair sp = Primitives.Sphere();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            NormalBaker.Bake(sp.High, sp.Low, new NormalBakeOptions { Resolution = 96 }, null, cts.Token));
    }

    private sealed class SyncProgress : IProgress<float>
    {
        private readonly List<float> _values = new();

        public IReadOnlyList<float> Values => _values;

        public void Report(float value)
        {
            lock (_values)
            {
                _values.Add(value);
            }
        }
    }
}
