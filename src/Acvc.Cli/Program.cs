using System.CommandLine;
using Acvc.Core.Acd;
using Tomlyn;

namespace Acvc.Cli;

/// <summary>Shape of ./acvc.config.toml.</summary>
public sealed class AcvcConfig
{
    [Tomlyn.Serialization.TomlPropertyName("ac_path")]
    public string? AcPath { get; set; }
}

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 1;      // bad arguments, missing paths, config problems
    private const int ExitFormat = 2;     // data.acd container is structurally broken
    private const int ExitProtected = 3;  // decrypts to garbage — CSP/x4fab protected mod

    public static int Main(string[] args)
    {
        var carArg = new Argument<string>("car")
        {
            Description = "Car folder name under content/cars (e.g. abarth500)",
        };
        var acPathOption = new Option<string?>("--ac-path")
        {
            Description = "Assetto Corsa install root. Overrides ac_path in ./acvc.config.toml.",
        };
        var outOption = new Option<string?>("--out")
        {
            Description = "Output directory. Default: ./unpacked/<car>",
        };

        var unpack = new Command("unpack",
            "Decrypt a car's data.acd into a loose data folder. The source car folder is never written to.");
        unpack.Arguments.Add(carArg);
        unpack.Options.Add(acPathOption);
        unpack.Options.Add(outOption);
        unpack.SetAction(parseResult => RunUnpack(
            parseResult.GetValue(carArg)!,
            parseResult.GetValue(acPathOption),
            parseResult.GetValue(outOption)));

        var root = new RootCommand("acvc — compiles tune specs into non-destructive Assetto Corsa car variants");
        root.Subcommands.Add(unpack);
        return root.Parse(args).Invoke();
    }

    private static int RunUnpack(string car, string? acPathOverride, string? outOverride)
    {
        try
        {
            var acPath = ResolveAcPath(acPathOverride);
            var carsRoot = Path.Combine(acPath, "content", "cars");
            if (!Directory.Exists(carsRoot))
                throw new InvalidOperationException(
                    $"'{acPath}' does not look like an Assetto Corsa install: {carsRoot} does not exist.");

            var carFolder = Path.Combine(carsRoot, car);
            if (!Directory.Exists(carFolder))
                throw new InvalidOperationException(
                    $"Car '{car}' not found at {carFolder}. The argument is the folder name under content/cars.");

            var outDir = outOverride ?? Path.Combine(Environment.CurrentDirectory, "unpacked", car);
            var data = AcdUnpacker.UnpackToDirectory(carFolder, outDir);

            Console.WriteLine($"Unpacked {data.Files.Count} files from {car}/data.acd");
            Console.WriteLine($"  -> {Path.GetFullPath(outDir)}");
            return ExitOk;
        }
        catch (ProtectedDataException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitProtected;
        }
        catch (AcdFormatException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitFormat;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitUsage;
        }
    }

    /// <summary>
    /// AC install root comes from --ac-path or from ac_path in ./acvc.config.toml —
    /// never hardcoded (CLAUDE.md).
    /// </summary>
    private static string ResolveAcPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        var configPath = Path.Combine(Environment.CurrentDirectory, "acvc.config.toml");
        if (File.Exists(configPath))
        {
            AcvcConfig? config;
            try
            {
                config = TomlSerializer.Deserialize<AcvcConfig>(File.ReadAllText(configPath));
            }
            catch (TomlException ex)
            {
                throw new InvalidOperationException(
                    $"{configPath} is not valid TOML: {ex.Message} " +
                    "(tip: use a TOML literal string for Windows paths: ac_path = 'C:\\path\\to\\assettocorsa')");
            }
            if (config?.AcPath is { } acPath && !string.IsNullOrWhiteSpace(acPath))
                return acPath;
            throw new InvalidOperationException(
                $"{configPath} exists but does not define a string 'ac_path' key.");
        }

        throw new InvalidOperationException(
            "No Assetto Corsa path configured. Pass --ac-path <install root> or create " +
            "acvc.config.toml in the working directory with: ac_path = \"C:\\\\path\\\\to\\\\assettocorsa\"");
    }
}
