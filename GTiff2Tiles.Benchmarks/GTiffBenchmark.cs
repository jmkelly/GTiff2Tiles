using BenchmarkDotNet.Attributes;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Core.Tiles;

namespace GTiff2Tiles.Benchmarks;

[SimpleJob(3, 3, 3)]
public class GTiffBenchmark
{
    private const string InputPathEnvironmentVariable = "GTIFF2TILES_BENCHMARK_INPUT";
    private const string DefaultInputRelativePath = "Examples/Input/Benchmark.tif";
    private const int MinZ = 0;
    private const int MaxZ = 15;

    private readonly int _threadsCount = Environment.ProcessorCount;

    private string _inputPath = string.Empty;

    private string _outputPath = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _inputPath = ResolveInputPath();
    }

    [IterationSetup]
    public void SetupIteration()
    {
        _outputPath = Path.Combine(
            AppContext.BaseDirectory,
            "benchmark-output",
            Guid.NewGuid().ToString("N")
        );
    }

    [Benchmark]
    public void RunGTiff2Tiles()
    {
        using Raster raster = new(_inputPath, CoordinateSystem.Epsg4326);
        raster.WriteTilesToDirectory(
            _outputPath,
            MinZ,
            MaxZ,
            false,
            Tile.DefaultSize,
            TileExtension.Png,
            NetVips.Enums.Kernel.Cubic,
            4,
            threadsCount: _threadsCount
        );
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        if (!Directory.Exists(_outputPath))
            return;

        Directory.Delete(_outputPath, true);
    }

    private static string ResolveInputPath()
    {
        string? environmentPath = Environment.GetEnvironmentVariable(InputPathEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            string fullEnvironmentPath = Path.GetFullPath(environmentPath);

            if (File.Exists(fullEnvironmentPath))
                return fullEnvironmentPath;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, DefaultInputRelativePath);

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return Path.GetFullPath(DefaultInputRelativePath);
    }
}
