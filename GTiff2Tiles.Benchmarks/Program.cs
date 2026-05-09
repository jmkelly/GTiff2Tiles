using BenchmarkDotNet.Running;
using CommandLine;

namespace GTiff2Tiles.Benchmarks;

internal static class Program
{
    private const string InputPathEnvironmentVariable = "GTIFF2TILES_BENCHMARK_INPUT";

    private static string _inputPath = ResolveDefaultInputPath();

    private static bool _runSingleTileBenchmark;

    private static bool _isParsingErrors;

    private static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args).WithParsed(ParseConsoleOptions)
              .WithNotParsed(_ => _isParsingErrors = true);

        if (_isParsingErrors)
            return;

        Environment.SetEnvironmentVariable(InputPathEnvironmentVariable, Path.GetFullPath(_inputPath));

        if (_runSingleTileBenchmark)
        {
            BenchmarkRunner.Run<SingleTileWriteBenchmark>();
            return;
        }

        BenchmarkRunner.Run<GTiffBenchmark>();
    }

    private static string ResolveDefaultInputPath()
    {
        const string relativeInputPath = "Examples/Input/Benchmark.tif";

        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativeInputPath);

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return Path.GetFullPath(relativeInputPath);
    }

    private static void ParseConsoleOptions(Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.InputFilePath))
            _inputPath = options.InputFilePath;

        _runSingleTileBenchmark = options.RunSingleTileBenchmark;
    }
}
