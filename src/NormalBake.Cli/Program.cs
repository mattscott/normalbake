using NormalBake.Core;

namespace NormalBake.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        Console.Error.WriteLine(
            "normalbake: the baker is not implemented yet (P0 scaffold). See Agents/Plans for the roadmap.");
        return 1;
    }

    private static void PrintUsage()
    {
        var d = new NormalBakeOptions();
        Console.WriteLine("normalbake — bake a normal map from a high-poly mesh into a low-poly's UV space");
        Console.WriteLine();
        Console.WriteLine("usage: normalbake <high> <low> -o <out> [options]");
        Console.WriteLine();
        Console.WriteLine("The low-poly must already have UVs (produced by the sister UV-unwrap tool).");
        Console.WriteLine();
        Console.WriteLine("defaults:");
        Console.WriteLine($"  --res {d.Resolution}   --samples {d.Samples}   --convention {d.Convention}");
        Console.WriteLine($"  --bitdepth {d.BitDepth}   --format {d.Format}");
    }
}
