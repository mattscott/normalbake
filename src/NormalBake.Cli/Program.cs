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

        if (Array.IndexOf(args, "--list-parts") >= 0)
        {
            return ListParts(args);
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

    private static int ListParts(string[] args)
    {
        string? path = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (path is null)
        {
            Console.Error.WriteLine("normalbake: --list-parts needs an .fbx path.");
            return 2;
        }

        if (Ext(path) != ".fbx")
        {
            Console.Error.WriteLine("normalbake: --list-parts only supports .fbx files.");
            return 2;
        }

        try
        {
            IReadOnlyList<FbxPart> parts = FbxReader.ReadParts(path);
            Console.WriteLine($"{parts.Count} part(s) in {Path.GetFileName(path)}:");
            for (int i = 0; i < parts.Count; i++)
            {
                Console.WriteLine($"  [{i}] {parts[i].Name}  ({parts[i].TriangleCount} tris)");
            }

            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"normalbake: {ex.Message}");
            return 1;
        }
    }

    private static void Run(Config config)
    {
        Mesh high = ReadHigh(config.High, config.HighFilter);
        UvMesh low = ReadLow(config.Low, config.Part, config.LowFilter);
        BakedMap map = NormalBaker.Bake(high, low, config.Options);

        string ext = Path.GetExtension(config.Output).ToLowerInvariant();
        string format = ext switch
        {
            ".dds" => WriteDds(config, map),
            ".exr" => WriteExr(config, map),
            _ => WritePng(config, map),
        };

        Console.WriteLine(
            $"baked {config.Options.Resolution}x{config.Options.Resolution} {config.Options.Map} map "
            + $"({format}) → {config.Output}");
    }

    private static Mesh ReadHigh(string path, string? filter) => Ext(path) switch
    {
        ".fbx" => FbxReader.ReadMesh(path, filter),
        ".glb" or ".gltf" => GltfReader.ReadMesh(path),
        _ => ObjReader.ReadMesh(path),
    };

    private static UvMesh ReadLow(string path, int part, string? filter)
    {
        if (!string.IsNullOrEmpty(filter) && Ext(path) == ".fbx")
        {
            return FbxReader.ReadUvMesh(path, filter);
        }

        if (part >= 0 && Ext(path) == ".fbx")
        {
            IReadOnlyList<FbxPart> parts = FbxReader.ReadParts(path);
            if (part >= parts.Count)
            {
                throw new ArgumentException($"--part {part} out of range (the file has {parts.Count} parts).");
            }

            return parts[part].ToUvMesh();
        }

        return Ext(path) switch
        {
            ".fbx" => FbxReader.ReadUvMesh(path),
            ".glb" or ".gltf" => GltfReader.ReadUvMesh(path, out _),
            _ => ObjReader.ReadUvMesh(path),
        };
    }

    private static string Ext(string path) => Path.GetExtension(path).ToLowerInvariant();

    private static string WritePng(Config config, BakedMap map)
    {
        PngWriter.Write(config.Output, map, config.Options.BitDepth);
        return $"PNG {config.Options.BitDepth}-bit";
    }

    private static string WriteDds(Config config, BakedMap map)
    {
        BlockCompression bc = config.Bc7 ? BlockCompression.Bc7 : BlockCompression.Bc5;
        DdsWriter.Write(config.Output, map, bc);
        return $"DDS {bc}";
    }

    private static string WriteExr(Config config, BakedMap map)
    {
        ExrWriter.Write(config.Output, map);
        return "EXR float";
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
        float strength = 1f;
        var convention = NormalMapConvention.OpenGl;
        var space = NormalSpace.Tangent;
        var map = MapType.Normal;
        int occlusionSamples = 32;
        int blurRadius = 0;
        int threads = -1;
        int tileU = 0;
        int tileV = 0;
        int part = -1;
        string? highFilter = null;
        string? lowFilter = null;
        bool fallback = true;
        bool lenient = false;
        bool normalizeHeight = false;
        bool bc7 = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-o" or "--output":
                    output = Next(args, ref i);
                    break;
                case "--map":
                    map = ParseMap(Next(args, ref i));
                    break;
                case "--occ-samples":
                    occlusionSamples = ParseInt(Next(args, ref i), "--occ-samples");
                    break;
                case "--blur":
                    blurRadius = ParseInt(Next(args, ref i), "--blur");
                    break;
                case "--threads":
                    threads = ParseInt(Next(args, ref i), "--threads");
                    break;
                case "--tile-u":
                    tileU = ParseInt(Next(args, ref i), "--tile-u");
                    break;
                case "--tile-v":
                    tileV = ParseInt(Next(args, ref i), "--tile-v");
                    break;
                case "--part":
                    part = ParseInt(Next(args, ref i), "--part");
                    break;
                case "--high-filter":
                    highFilter = Next(args, ref i);
                    break;
                case "--low-filter":
                    lowFilter = Next(args, ref i);
                    break;
                case "--space":
                    space = ParseSpace(Next(args, ref i));
                    break;
                case "--no-fallback":
                    fallback = false;
                    break;
                case "--lenient":
                    lenient = true;
                    break;
                case "--normalize":
                    normalizeHeight = true;
                    break;
                case "--bc7":
                    bc7 = true;
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
                case "--strength" or "--depth":
                    strength = (float)ParseFloat(Next(args, ref i), "--strength");
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
            Space = space,
            NormalStrength = strength,
            Map = map,
            OcclusionSamples = occlusionSamples,
            BlurRadius = blurRadius,
            NormalizeHeight = normalizeHeight,
            Threads = threads,
            TileU = tileU,
            TileV = tileV,
            NearestPointFallback = fallback,
            StrictUvValidation = !lenient,
        };
        return new Config(high, low, output, options, bc7, part, highFilter, lowFilter);
    }

    private static MapType ParseMap(string value) => value.ToLowerInvariant() switch
    {
        "normal" or "n" => MapType.Normal,
        "position" or "pos" => MapType.Position,
        "height" or "displacement" or "disp" => MapType.Height,
        "ao" or "occlusion" => MapType.AmbientOcclusion,
        "thickness" => MapType.Thickness,
        "curvature" or "curve" => MapType.Curvature,
        "id" or "objectid" => MapType.ObjectId,
        "mask" => MapType.Mask,
        "coverage" => MapType.Coverage,
        _ => throw new ArgumentException(
            $"--map must be normal|height|position|ao|thickness|curvature|id|mask|coverage, got '{value}'."),
    };

    private static NormalSpace ParseSpace(string value) => value.ToLowerInvariant() switch
    {
        "tangent" or "ts" => NormalSpace.Tangent,
        "object" or "os" => NormalSpace.Object,
        _ => throw new ArgumentException($"--space must be 'tangent' or 'object', got '{value}'."),
    };

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
        // Unreal (UE3/UDK through UE5), 3ds Max and Source all use the DirectX green-down (Y−) convention.
        "directx" or "dx" or "unreal" or "ue" or "ue3" or "ue4" or "ue5" or "udk" => NormalMapConvention.DirectX,
        _ => throw new ArgumentException(
            $"--convention must be opengl|directx|unreal, got '{value}'."),
    };

    private static void PrintUsage()
    {
        var d = new NormalBakeOptions();
        Console.WriteLine("normalbake — bake a normal map from a high-poly mesh into a low-poly's UV space");
        Console.WriteLine();
        Console.WriteLine("usage: normalbake <high.obj|.glb|.fbx> <low.obj|.glb|.fbx> -o <out.png> [options]");
        Console.WriteLine();
        Console.WriteLine("The low-poly must already have UVs (produced by a separate UV-unwrap tool).");
        Console.WriteLine();
        Console.WriteLine("options:");
        Console.WriteLine($"  -o, --output <path>     output image (required)");
        Console.WriteLine($"  --res <n>               texture resolution (default {d.Resolution})");
        Console.WriteLine($"  --samples <n>           supersample grid N (NxN rays/texel, default {d.Samples})");
        Console.WriteLine($"  --convention <gl|dx|unreal>  green-channel convention (default {d.Convention}; unreal=dx, green-down)");
        Console.WriteLine($"  --bitdepth <8|16>       bits per channel for PNG (default {d.BitDepth})");
        Console.WriteLine($"  --map <type>            normal|height|position|ao|thickness|curvature|id|mask|coverage (default {d.Map})");
        Console.WriteLine($"  --space <tangent|object> normal space (default {d.Space})");
        Console.WriteLine($"  --strength <f>          tangent normal depth scale (1=as-baked, 2=2x depth; aka --depth)");
        Console.WriteLine($"  --occ-samples <n>       hemisphere rays for ao/thickness (default {d.OcclusionSamples})");
        Console.WriteLine($"  --blur <px>             coverage-aware smoothing radius for ao/curvature (default 0)");
        Console.WriteLine($"  --normalize             height map: stretch lowest->black, highest->white (full contrast)");
        Console.WriteLine($"  --threads <n>           worker threads (-1 = all cores, default {d.Threads})");
        Console.WriteLine($"  --tile-u/--tile-v <n>   UDIM tile to bake (shift UVs into 0-1; default 0)");
        Console.WriteLine($"  --part <i>              bake only FBX low-poly part i against the full high (default all)");
        Console.WriteLine($"  --low-filter <substr>  merge only low-poly FBX parts whose name contains substr (e.g. LOD0)");
        Console.WriteLine($"  --high-filter <substr> merge only high-poly FBX parts whose name contains substr");
        Console.WriteLine($"  --list-parts           print the FBX's part indices and mesh names, then exit");
        Console.WriteLine($"  --frontal <d>           outer cage distance (0 = auto from bounds)");
        Console.WriteLine($"  --rear <d>              inner cage distance (0 = auto from bounds)");
        Console.WriteLine($"  --no-fallback           don't fill cage misses from the nearest surface point");
        Console.WriteLine($"  --lenient               bake despite UV-contract errors (e.g. mirrored/stacked UVs)");
        Console.WriteLine($"  --bc7                   use BC7 instead of BC5 for .dds output");
        Console.WriteLine();
        Console.WriteLine("output format is chosen by extension: .png (default), .dds (BC5/BC7), .exr (float).");
    }

    private sealed record Config(
        string High, string Low, string Output, NormalBakeOptions Options, bool Bc7, int Part,
        string? HighFilter, string? LowFilter);
}
