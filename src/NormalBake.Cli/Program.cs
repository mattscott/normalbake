using System.Globalization;
using NormalBake.Core;
using NormalBake.IO;

namespace NormalBake.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            Run(Parse(args));
            return 0;
        }
        catch (UvContractException ex)
        {
            Console.Error.WriteLine("normalbake: the low-poly's UVs are not bakeable:");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"normalbake: {ex.Message}");
            Console.Error.WriteLine("try 'normalbake --help'.");
            return 2;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException)
        {
            Console.Error.WriteLine($"normalbake: {ex.Message}");
            return 1;
        }
    }

    private static void Run(Config config)
    {
        Mesh high = ObjReader.ReadMesh(config.High);
        UvMesh low = ObjReader.ReadUvMesh(config.Low);
        BakedMap map = NormalBaker.Bake(high, low, config.Options);
        PngWriter.Write(config.Output, map, config.Options.BitDepth);
        Console.WriteLine(
            $"baked {config.Options.Resolution}x{config.Options.Resolution} {config.Options.BitDepth}-bit "
            + $"{config.Options.Convention} normal map → {config.Output}");
    }

    private static Config Parse(string[] args)
    {
        string? high = null;
        string? low = null;
        string? output = null;
        int res = 2048;
        int samples = 4;
        int bitDepth = 16;
        double frontal = 0;
        double rear = 0;
        var convention = NormalMapConvention.OpenGl;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-o" or "--output":
                    output = Next(args, ref i);
                    break;
                case "--res" or "--resolution":
                    res = ParseInt(Next(args, ref i), "--res");
                    break;
                case "--samples":
                    samples = ParseInt(Next(args, ref i), "--samples");
                    break;
                case "--bitdepth":
                    bitDepth = ParseInt(Next(args, ref i), "--bitdepth");
                    break;
                case "--frontal":
                    frontal = ParseFloat(Next(args, ref i), "--frontal");
                    break;
                case "--rear":
                    rear = ParseFloat(Next(args, ref i), "--rear");
                    break;
                case "--convention":
                    convention = ParseConvention(Next(args, ref i));
                    break;
                default:
                    AssignPositional(a, ref high, ref low);
                    break;
            }
        }

        if (high is null || low is null)
        {
            throw new ArgumentException("expected <high> and <low> mesh paths.");
        }

        if (output is null)
        {
            throw new ArgumentException("missing required -o <output> path.");
        }

        if (bitDepth is not (8 or 16))
        {
            throw new ArgumentException("--bitdepth must be 8 or 16.");
        }

        var options = new NormalBakeOptions
        {
            Resolution = res,
            Samples = samples,
            BitDepth = bitDepth,
            FrontalDistance = frontal,
            RearDistance = rear,
            Convention = convention,
        };
        return new Config(high, low, output, options);
    }

    private static void AssignPositional(string value, ref string? high, ref string? low)
    {
        if (value.StartsWith('-'))
        {
            throw new ArgumentException($"unknown option '{value}'.");
        }

        if (high is null)
        {
            high = value;
        }
        else if (low is null)
        {
            low = value;
        }
        else
        {
            throw new ArgumentException($"unexpected extra argument '{value}'.");
        }
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"option '{args[i]}' expects a value.");
        }

        return args[++i];
    }

    private static int ParseInt(string value, string option) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : throw new ArgumentException($"{option} expects an integer, got '{value}'.");

    private static double ParseFloat(string value, string option) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v
            : throw new ArgumentException($"{option} expects a number, got '{value}'.");

    private static NormalMapConvention ParseConvention(string value) => value.ToLowerInvariant() switch
    {
        "opengl" or "gl" => NormalMapConvention.OpenGl,
        "directx" or "dx" => NormalMapConvention.DirectX,
        _ => throw new ArgumentException($"--convention must be 'opengl' or 'directx', got '{value}'."),
    };

    private static void PrintUsage()
    {
        var d = new NormalBakeOptions();
        Console.WriteLine("normalbake — bake a normal map from a high-poly mesh into a low-poly's UV space");
        Console.WriteLine();
        Console.WriteLine("usage: normalbake <high.obj> <low.obj> -o <out.png> [options]");
        Console.WriteLine();
        Console.WriteLine("The low-poly must already have UVs (produced by a separate UV-unwrap tool).");
        Console.WriteLine();
        Console.WriteLine("options:");
        Console.WriteLine($"  -o, --output <path>     output image (required)");
        Console.WriteLine($"  --res <n>               texture resolution (default {d.Resolution})");
        Console.WriteLine($"  --samples <n>           supersample grid N (NxN rays/texel, default {d.Samples})");
        Console.WriteLine($"  --convention <gl|dx>    green-channel convention (default {d.Convention})");
        Console.WriteLine($"  --bitdepth <8|16>       bits per channel (default {d.BitDepth})");
        Console.WriteLine($"  --frontal <d>           outer cage distance (0 = auto from bounds)");
        Console.WriteLine($"  --rear <d>              inner cage distance (0 = auto from bounds)");
    }

    private sealed record Config(string High, string Low, string Output, NormalBakeOptions Options);
}
