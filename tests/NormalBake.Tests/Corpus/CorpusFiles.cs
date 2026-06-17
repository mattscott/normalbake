using NormalBake.IO;

namespace NormalBake.Tests.Corpus;

/// <summary>Locates the repository layout and writes procedural corpus mesh pairs to disk as OBJ.</summary>
public static class CorpusFiles
{
    /// <summary>Walks up from the test assembly until it finds the directory holding <c>NormalBake.sln</c>.</summary>
    public static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NormalBake.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root (NormalBake.sln).");
    }

    /// <summary>The committed corpus directory (<c>tests/corpus</c>).</summary>
    public static string CorpusDirectory => Path.Combine(FindRepoRoot(), "tests", "corpus");

    /// <summary>Writes a mesh pair as <c>{name}_high.obj</c> / <c>{name}_low.obj</c> under a directory.</summary>
    public static void WritePair(string directory, MeshPair pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        Directory.CreateDirectory(directory);
        ObjWriter.Write(Path.Combine(directory, $"{pair.Name}_high.obj"), pair.High);
        ObjWriter.Write(Path.Combine(directory, $"{pair.Name}_low.obj"), pair.Low);
    }
}
