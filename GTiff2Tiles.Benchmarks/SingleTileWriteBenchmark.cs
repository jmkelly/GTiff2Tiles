using BenchmarkDotNet.Attributes;
using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Core.Tiles;

namespace GTiff2Tiles.Benchmarks;

[SimpleJob]
public class SingleTileWriteBenchmark : IDisposable
{
    private const string InputPathEnvironmentVariable = "GTIFF2TILES_BENCHMARK_INPUT";
    private const string DefaultInputRelativePath = "Examples/Input/Benchmark.tif";
    private const int BenchmarkZoom = 10;
    private const int OperationsPerInvoke = 64;

    private Raster? _raster;

    private Number? _tileNumber;

    private RasterTile[] _tiles = [];

    private string _outputDirectory = string.Empty;

    private string[] _outputPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        _raster = new Raster(ResolveInputPath(), CoordinateSystem.Epsg4326);
        _tileNumber = CreateBenchmarkTileNumber(_raster);
        _outputDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "benchmark-output",
            "single-tile"
        );
    }

    [IterationSetup]
    public void SetupIteration()
    {
        ArgumentNullException.ThrowIfNull(_raster);
        ArgumentNullException.ThrowIfNull(_tileNumber);

        Directory.CreateDirectory(_outputDirectory);

        _outputPaths = new string[OperationsPerInvoke];
        _tiles = new RasterTile[OperationsPerInvoke];

        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            string outputPath = Path.Combine(_outputDirectory, $"{i:D2}-{Guid.NewGuid():N}.png");
            _outputPaths[i] = outputPath;
            _tiles[i] = new RasterTile(_tileNumber, _raster.GeoCoordinateSystem, Tile.DefaultSize)
            {
                Path = outputPath,
                Extension = TileExtension.Png,
                Interpolation = NetVips.Enums.Kernel.Cubic,
            };
        }
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void WriteSingleTileToFile()
    {
        ArgumentNullException.ThrowIfNull(_raster);

        foreach (RasterTile tile in _tiles)
            _raster.WriteTileToFile(_raster.Data, tile);
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        foreach (RasterTile tile in _tiles)
            tile.Dispose();

        _tiles = [];

        foreach (string outputPath in _outputPaths)
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }

        _outputPaths = [];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();

        if (Directory.Exists(_outputDirectory))
            Directory.Delete(_outputDirectory, true);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        foreach (RasterTile tile in _tiles)
            tile.Dispose();

        _tiles = [];
        _outputPaths = [];

        _raster?.Dispose();
        _raster = null;
    }

    private static Number CreateBenchmarkTileNumber(Raster raster)
    {
        (Number minNumber, Number maxNumber) = GeoCoordinate.GetNumbers(
            raster.MinCoordinate,
            raster.MaxCoordinate,
            BenchmarkZoom,
            Tile.DefaultSize,
            false
        );

        return new Number(
            (minNumber.X + maxNumber.X) / 2,
            (minNumber.Y + maxNumber.Y) / 2,
            BenchmarkZoom
        );
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
