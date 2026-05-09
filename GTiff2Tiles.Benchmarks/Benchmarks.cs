using System.Globalization;
using BenchmarkDotNet.Attributes;
using DotNet.Testcontainers.Containers;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Core.Tiles;

namespace GTiff2Tiles.Benchmarks;

[SimpleJob(1, 1, 3)]
public class Benchmark
{
    /*
     * We're creating GEODETIC png tiles from EPSG:4326 input file
     * resampling is cubic, zooms 0-10, process counter 8
     */

    private const string InputPathEnvironmentVariable = "GTIFF2TILES_BENCHMARK_INPUT";

    private const string DefaultInputRelativePath = "Examples/Input/Benchmark.tif";

    #region Properties and consts

    private static string GetOutDirectory(string dirName)
    {
        string ts = DateTime.Now.ToString(
            Core.Constants.DateTimePatterns.LongWithMs,
            CultureInfo.InvariantCulture
        );

        return $"{dirName}_{ts}";
    }

    private const string GTiff2TilesOut = "gtiff2tiles";

    private const string Gdal2TilesOut = "gdal2tiles";

    private const string MaptilerOut = "maptiler";

    internal const string GdalName = "ghcr.io/osgeo/gdal:ubuntu-small-3.10.2";

    internal const string MaptilerName = "maptiler/engine:10.3";

    private readonly int _tc = Environment.ProcessorCount;

    private IContainer? _gdalContainer;

    private IContainer? _maptilerContainer;

    private const int MinZ = 0;

    private const int MaxZ = 15;

    internal const string Data = "data";

    internal const string In = "in";

    internal const string Itif = "i.tif";

    private static readonly string MountDir = AppContext.BaseDirectory;

    private static readonly string HostDataDirectory = Path.Combine(MountDir, Data);

    private static readonly string HostInputDirectory = Path.Combine(HostDataDirectory, In);

    private const string ContainerDataDirectory = "/data";

    private static string GetHostOutputDirectory(string dirName) =>
        Path.Combine(HostDataDirectory, GetOutDirectory(dirName));

    private static string GetContainerOutputDirectory(string dirName) =>
        $"{ContainerDataDirectory}/{GetOutDirectory(dirName)}";

    private static (string HostInputFilePath, string ContainerInputFilePath) PrepareInputFile()
    {
        Directory.CreateDirectory(HostInputDirectory);

        string inputFileName =
            $"{Path.GetFileNameWithoutExtension(Itif)}_{Guid.NewGuid():N}{Path.GetExtension(Itif)}";
        string hostInputFilePath = Path.Combine(HostInputDirectory, inputFileName);

        File.Copy(ResolveInputSourcePath(), hostInputFilePath, true);

        return (hostInputFilePath, $"{ContainerDataDirectory}/{In}/{inputFileName}");
    }

    private static string ResolveInputSourcePath()
    {
        string? environmentPath = Environment.GetEnvironmentVariable(InputPathEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            string fullEnvironmentPath = Path.GetFullPath(environmentPath);

            if (File.Exists(fullEnvironmentPath))
                return fullEnvironmentPath;
        }

        DirectoryInfo? directory = new(MountDir);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, DefaultInputRelativePath);

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return Path.GetFullPath(DefaultInputRelativePath);
    }

    #endregion

    #region Benchmarks

    [Benchmark]
    public void RunGTiff2Tiles()
    {
        string path = PrepareInputFile().HostInputFilePath;

        using Raster raster = new(path, CoordinateSystem.Epsg4326);
        raster.WriteTilesToDirectory(
            GetHostOutputDirectory(GTiff2TilesOut),
            MinZ,
            MaxZ,
            false,
            Tile.DefaultSize,
            TileExtension.Png,
            NetVips.Enums.Kernel.Cubic,
            4,
            threadsCount: _tc
        );
    }

    // [IterationSetup(Target = nameof(RunGdal2Tiles))]
    // public void SetupGdal2Tiles()
    // {
    //     _gdalContainer = ContainerRunner
    //         .StartPersistentAsync(GdalName, HostDataDirectory, ContainerDataDirectory)
    //         .GetAwaiter()
    //         .GetResult();
    // }
    //
    // //[Benchmark]
    // public Task RunGdal2Tiles()
    // {
    //     (_, string containerInputFilePath) = PrepareInputFile();
    //
    //     return ContainerRunner.RunInExistingAsync(
    //         _gdalContainer
    //             ?? throw new InvalidOperationException(
    //                 "GDAL benchmark container was not initialized."
    //             ),
    //         "gdal2tiles.py",
    //         "-s",
    //         "EPSG:4326",
    //         "-p",
    //         "geodetic",
    //         "-r",
    //         "cubic",
    //         "-z",
    //         $"{MinZ}-{MaxZ}",
    //         "--processes",
    //         _tc.ToString(CultureInfo.InvariantCulture),
    //         containerInputFilePath,
    //         GetContainerOutputDirectory(Gdal2TilesOut)
    //     );
    // }
    //
    // [IterationCleanup(Target = nameof(RunGdal2Tiles))]
    // public void CleanupGdal2Tiles()
    // {
    //     ContainerRunner.StopAndDisposeAsync(_gdalContainer).GetAwaiter().GetResult();
    //     _gdalContainer = null;
    // }

    // [IterationSetup(Target = nameof(RunMaptiler))]
    // public void SetupMaptiler()
    // {
    //     _maptilerContainer = ContainerRunner
    //         .StartPersistentAsync(MaptilerName, HostDataDirectory, ContainerDataDirectory)
    //         .GetAwaiter()
    //         .GetResult();
    // }

    //[Benchmark]
    // public Task RunMaptiler()
    // {
    //     (_, string containerInputFilePath) = PrepareInputFile();
    //
    //     return ContainerRunner.RunInExistingAsync(
    //         _maptilerContainer
    //             ?? throw new InvalidOperationException(
    //                 "MapTiler benchmark container was not initialized."
    //             ),
    //         "maptiler",
    //         "-srs",
    //         "EPSG:4326",
    //         "-preset",
    //         "geodetic",
    //         "-resampling",
    //         "cubic",
    //         "-zoom",
    //         MinZ.ToString(CultureInfo.InvariantCulture),
    //         MaxZ.ToString(CultureInfo.InvariantCulture),
    //         "-P",
    //         _tc.ToString(CultureInfo.InvariantCulture),
    //         "-f",
    //         "png32",
    //         "-o",
    //         GetContainerOutputDirectory(MaptilerOut),
    //         containerInputFilePath
    //     );
    // }

    // [IterationCleanup(Target = nameof(RunMaptiler))]
    // public void CleanupMaptiler()
    // {
    //     ContainerRunner.StopAndDisposeAsync(_maptilerContainer).GetAwaiter().GetResult();
    //     _maptilerContainer = null;
    // }

    #endregion
}
