using System.Diagnostics;
using System.Threading.Channels;
using GTiff2Tiles.Core.Constants;
using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.Exceptions;
using GTiff2Tiles.Core.Helpers;
using GTiff2Tiles.Core.Images;
using GTiff2Tiles.Core.Localization;
using GTiff2Tiles.Core.Tiles;
using NetVips;

// ReSharper disable ClassWithVirtualMembersNeverInherited.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Global

namespace GTiff2Tiles.Core.GeoTiffs;

/// <summary>
/// Class, representing <see cref="Raster"/> GeoTiff.
/// Used for creating <see cref="RasterTile"/>s.
/// </summary>
public class Raster : GeoTiff
{
    private static readonly double[] Background1 = [0];
    private static readonly double[] Background2 = [0, 0];
    private static readonly double[] Background3 = [0, 0, 0];
    private static readonly double[] Background4 = [0, 0, 0, 0];

    #region Properties

    /// <summary>
    /// This <see cref="Raster"/>'s data.
    /// </summary>
    public Image Data { get; }

    #endregion

    #region Constructor/Destructor

    /// <summary>
    /// Creates new <see cref="Raster"/> object.
    /// <remarks><para/>Use this version ONLY if you don't know the <see cref="CoordinateSystem"/>
    /// of this <see cref="Raster"/>. In other cases, prefer using other constructors!</remarks>
    /// </summary>
    /// <inheritdoc cref="Raster(string,CoordinateSystem,long)"/>
    public Raster(string inputFilePath, long maxMemoryCache = 2147483648)
    {
        CheckHelper.CheckFile(inputFilePath, true, FileExtensions.Tif);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMemoryCache);

        NetVipsHelper.DisableLog();

        bool memory = new FileInfo(inputFilePath).Length <= maxMemoryCache;
        Data = Image.NewFromFile(inputFilePath, memory, NetVips.Enums.Access.Random);

        Size = new Size(Data.Width, Data.Height);

        GdalWorker.RasterMetadata rasterMetadata = GdalWorker.ReadRasterMetadata(inputFilePath, Size);
        GeoCoordinateSystem = rasterMetadata.CoordinateSystem;
        MinCoordinate = rasterMetadata.MinCoordinate;
        MaxCoordinate = rasterMetadata.MaxCoordinate;
    }

    /// <summary>
    /// Creates new <see cref="Raster"/> object.
    /// </summary>
    public Raster(string inputFilePath, CoordinateSystem coordinateSystem, long maxMemoryCache = 2147483648)
    {
        CheckHelper.CheckFile(inputFilePath, true, FileExtensions.Tif);

        if (coordinateSystem == CoordinateSystem.Other)
        {
            string err = string.Format(Strings.Culture, Strings.NotSupported, coordinateSystem);
            throw new NotSupportedException(err);
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMemoryCache);

        NetVipsHelper.DisableLog();

        bool memory = new FileInfo(inputFilePath).Length <= maxMemoryCache;
        Data = Image.NewFromFile(inputFilePath, memory, NetVips.Enums.Access.Random);

        Size = new Size(Data.Width, Data.Height);

        GeoCoordinateSystem = coordinateSystem;
        (MinCoordinate, MaxCoordinate) = GdalWorker.GetImageBorders(inputFilePath, Size, GeoCoordinateSystem);
    }

    /// <inheritdoc cref="Raster(string,CoordinateSystem,long)"/>
    public Raster(Stream inputStream, CoordinateSystem coordinateSystem)
    {
        ArgumentNullException.ThrowIfNull(inputStream);

        if (coordinateSystem == CoordinateSystem.Other)
        {
            string err = string.Format(Strings.Culture, Strings.NotSupported, coordinateSystem);
            throw new NotSupportedException(err);
        }

        NetVipsHelper.DisableLog();

        (MinCoordinate, MaxCoordinate) = GetBorders(inputStream, coordinateSystem);
        Data = Image.NewFromStream(inputStream, access: NetVips.Enums.Access.Random);

        inputStream.Seek(0, SeekOrigin.Begin);

        Size = new Size(Data.Width, Data.Height);
        GeoCoordinateSystem = coordinateSystem;
    }

    /// <summary>
    /// Calls <see cref="Dispose(bool)"/> on this <see cref="Raster"/>.
    /// </summary>
    ~Raster() => Dispose(false);

    #endregion

    #region Methods

    #region Dispose

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        base.Dispose(disposing);
        Data?.Dispose();
    }

    #endregion

    #region Create tile image

    /// <summary>
    /// Create <see cref="Image"/> for one <see cref="RasterTile"/>
    /// from input <see cref="Image"/> or tile cache.
    /// </summary>
    public Image CreateTileImage(Image tileCache, RasterTile tile, Area readArea, Area writeArea)
    {
        ArgumentNullException.ThrowIfNull(tileCache);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(readArea);
        ArgumentNullException.ThrowIfNull(writeArea);

        RasterTileLayout layout = new(
            readArea.OriginCoordinate.X,
            readArea.OriginCoordinate.Y,
            readArea.Size.Width,
            readArea.Size.Height,
            writeArea.OriginCoordinate.X,
            writeArea.OriginCoordinate.Y,
            writeArea.Size.Width,
            writeArea.Size.Height);

        return CreateTileImage(tileCache, tile, layout);
    }

    private static Image CreateTileImage(Image tileCache, RasterTile tile, RasterTileLayout layout)
    {
        ArgumentNullException.ThrowIfNull(tileCache);
        ArgumentNullException.ThrowIfNull(tile);

        Image tileImage = tileCache.Crop(layout.ReadLeft, layout.ReadTop, layout.ReadWidth, layout.ReadHeight);

        if (!layout.HasSameReadAndWriteSize)
            tileImage = tileImage.Resize(layout.XScale, tile.Interpolation, null, layout.YScale);

        Band.AddDefaultBands(ref tileImage, tile.BandsCount);

        if (layout.WritesWholeTile(tile.Size))
            return tileImage;

        return tileImage.Embed(
            layout.WriteLeft,
            layout.WriteTop,
            tile.Size.Width,
            tile.Size.Height,
            extend: NetVips.Enums.Extend.Background,
            background: GetTransparentBackground(tile.BandsCount));
    }

    #endregion

    #region WriteTile

    /// <summary>
    /// Gets data from source <see cref="Image"/>
    /// or tile cache for specified <see cref="RasterTile"/>
    /// and writes it to ready file.
    /// </summary>
    public void WriteTileToFile(Image tileCache, RasterTile tile)
    {
        ArgumentNullException.ThrowIfNull(tileCache);
        ArgumentNullException.ThrowIfNull(tile);
        CheckHelper.CheckOutputFilePath(tile.Path);

        if (!RasterTileLayoutCalculator.TryGetLayout(this, tile, out RasterTileLayout layout))
            return;

        using Image tileImage = CreateTileImage(tileCache, tile, layout);
        tileImage.WriteToFile(tile.Path);
    }

    /// <inheritdoc cref="WriteTileToFile"/>
    public Task WriteTileToFileAsync(Image tileCache, RasterTile tile) =>
        Task.Run(() => WriteTileToFile(tileCache, tile));

    /// <summary>
    /// Gets data from source <see cref="Image"/>
    /// or tile cache for specified <see cref="RasterTile"/>
    /// and writes it to <see cref="IEnumerable{T}"/>.
    /// </summary>
    public IEnumerable<byte> WriteTileToEnumerable(Image tileCache, RasterTile tile)
    {
        ArgumentNullException.ThrowIfNull(tileCache);
        ArgumentNullException.ThrowIfNull(tile);

        if (!RasterTileLayoutCalculator.TryGetLayout(this, tile, out RasterTileLayout layout))
            return null;

        using Image tileImage = CreateTileImage(tileCache, tile, layout);
        return tileImage.WriteToBuffer(tile.GetExtensionString());
    }

    /// <summary>
    /// Gets data from source <see cref="Image"/>
    /// or tile cache for specified <see cref="RasterTile"/>
    /// and writes it to <see cref="ChannelWriter{T}"/>.
    /// </summary>
    public bool WriteTileToChannel(Image tileCache, RasterTile tile, ChannelWriter<RasterTile> channelWriter)
    {
        ArgumentNullException.ThrowIfNull(tileCache);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(channelWriter);

        tile.Bytes = WriteTileToEnumerable(tileCache, tile);
        return tile.Bytes != null && tile.Validate(false) && channelWriter.TryWrite(tile);
    }

    /// <inheritdoc cref="WriteTileToChannel"/>
    public ValueTask WriteTileToChannelAsync(Image tileCache, RasterTile tile, ChannelWriter<RasterTile> channelWriter)
    {
        ArgumentNullException.ThrowIfNull(tileCache);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(channelWriter);

        tile.Bytes = WriteTileToEnumerable(tileCache, tile);
        return tile.Bytes != null && tile.Validate(false)
            ? channelWriter.WriteAsync(tile)
            : ValueTask.CompletedTask;
    }

    #endregion

    #region WriteTiles

    /// <summary>
    /// Crops current <see cref="RasterTile"/> on <see cref="RasterTile"/>s
    /// and writes them to <paramref name="outputDirectoryPath"/>.
    /// </summary>
    public void WriteTilesToDirectory(string outputDirectoryPath, int minZ, int maxZ, bool tmsCompatible = false,
                                      Size tileSize = null, TileExtension tileExtension = TileExtension.Png,
                                      NetVips.Enums.Kernel interpolation = NetVips.Enums.Kernel.Lanczos3,
                                      int bandsCount = RasterTile.DefaultBandsCount, int tileCacheCount = 1000,
                                      int threadsCount = 0, IProgress<double> progress = null,
                                      Action<string> printTimeAction = null)
    {
        CheckHelper.CheckDirectory(outputDirectoryPath, true);
        tileSize ??= Tile.DefaultSize;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileCacheCount);

        ParallelOptions parallelOptions = new();
        if (threadsCount > 0)
            parallelOptions.MaxDegreeOfParallelism = threadsCount;

        Stopwatch stopwatch = printTimeAction == null ? null : Stopwatch.StartNew();

        int tilesCount = Number.GetCount(MinCoordinate, MaxCoordinate, minZ, maxZ, tmsCompatible, tileSize);
        if (tilesCount <= 0)
            throw new RasterException(Strings.NoTilesToCrop);

        TileProgressReporter tileProgressReporter =
            ProgressHelper.CreateTileProgressReporter(tilesCount, progress, stopwatch, printTimeAction);
        Dictionary<(int Z, int X), string> tileDirectories =
            CreateTileDirectories(outputDirectoryPath, MinCoordinate, MaxCoordinate, minZ, maxZ, tileSize,
                                  tmsCompatible);

        using Image tileCache = Data.Tilecache(tileSize.Width, tileSize.Height, tileCacheCount, threaded: true);

        void MakeTile(int x, int y, int z)
        {
            string tileDirectoryPath = tileDirectories[(z, x)];

            RasterTile tile = new(new Number(x, y, z), GeoCoordinateSystem, tileSize, tmsCompatible)
            {
                Extension = tileExtension,
                BandsCount = bandsCount,
                Interpolation = interpolation,
                Path = Path.Combine(tileDirectoryPath, $"{y}{Tile.GetExtensionString(tileExtension)}")
            };

            WriteTileToFile(tileCache, tile);
            tileProgressReporter?.Advance();
        }

        for (int zoom = minZ; zoom <= maxZ; zoom++)
        {
            (Number minNumber, Number maxNumber) = GeoCoordinate.GetNumbers(MinCoordinate, MaxCoordinate,
                                                                            zoom, tileSize, tmsCompatible);

            for (int tileY = minNumber.Y; tileY <= maxNumber.Y; tileY++)
            {
                int y = tileY;
                int z = zoom;
                Parallel.For(minNumber.X, maxNumber.X + 1, parallelOptions, x => MakeTile(x, y, z));
            }
        }
    }

    /// <inheritdoc cref="WriteTilesToDirectory"/>
    public Task WriteTilesToDirectoryAsync(string outputDirectoryPath, int minZ, int maxZ, bool tmsCompatible = false,
                                           Size tileSize = null, TileExtension tileExtension = TileExtension.Png,
                                           NetVips.Enums.Kernel interpolation = NetVips.Enums.Kernel.Lanczos3,
                                           int bandsCount = RasterTile.DefaultBandsCount, int tileCacheCount = 1000,
                                           int threadsCount = 0, IProgress<double> progress = null,
                                           Action<string> printTimeAction = null) =>
        Task.Run(() => WriteTilesToDirectory(outputDirectoryPath, minZ, maxZ, tmsCompatible, tileSize, tileExtension,
                                             interpolation, bandsCount, tileCacheCount, threadsCount, progress,
                                             printTimeAction));

    /// <summary>
    /// Crops current <see cref="Raster"/> on <see cref="RasterTile"/>s
    /// and writes them to <paramref name="channelWriter"/>.
    /// </summary>
    public void WriteTilesToChannel(ChannelWriter<RasterTile> channelWriter, int minZ, int maxZ,
                                    bool tmsCompatible = false, Size tileSize = null,
                                    NetVips.Enums.Kernel interpolation = NetVips.Enums.Kernel.Lanczos3,
                                    int bandsCount = RasterTile.DefaultBandsCount, int tileCacheCount = 1000,
                                    int threadsCount = 0, IProgress<double> progress = null,
                                    Action<string> printTimeAction = null)
    {
        tileSize ??= Tile.DefaultSize;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileCacheCount);

        ParallelOptions parallelOptions = new();
        if (threadsCount > 0)
            parallelOptions.MaxDegreeOfParallelism = threadsCount;

        Stopwatch stopwatch = printTimeAction == null ? null : Stopwatch.StartNew();

        int tilesCount = Number.GetCount(MinCoordinate, MaxCoordinate, minZ, maxZ, tmsCompatible, tileSize);
        if (tilesCount <= 0)
            throw new RasterException(Strings.NoTilesToCrop);

        TileProgressReporter tileProgressReporter =
            ProgressHelper.CreateTileProgressReporter(tilesCount, progress, stopwatch, printTimeAction);

        using Image tileCache = Data.Tilecache(tileSize.Width, tileSize.Height, tileCacheCount, threaded: true);

        void MakeTile(int x, int y, int z)
        {
            RasterTile tile = new(new Number(x, y, z), GeoCoordinateSystem, tileSize, tmsCompatible)
            {
                BandsCount = bandsCount,
                Interpolation = interpolation
            };

            if (!WriteTileToChannel(tileCache, tile, channelWriter))
                return;

            tileProgressReporter?.Advance();
        }

        for (int zoom = minZ; zoom <= maxZ; zoom++)
        {
            (Number minNumber, Number maxNumber) = GeoCoordinate.GetNumbers(MinCoordinate, MaxCoordinate,
                                                                            zoom, tileSize, tmsCompatible);

            for (int tileY = minNumber.Y; tileY <= maxNumber.Y; tileY++)
            {
                int y = tileY;
                int z = zoom;
                Parallel.For(minNumber.X, maxNumber.X + 1, parallelOptions, x => MakeTile(x, y, z));
            }
        }
    }

    /// <inheritdoc cref="WriteTilesToChannel"/>
    public Task WriteTilesToChannelAsync(ChannelWriter<RasterTile> channelWriter, int minZ, int maxZ,
                                         bool tmsCompatible = false, Size tileSize = null,
                                         NetVips.Enums.Kernel interpolation = NetVips.Enums.Kernel.Lanczos3,
                                         int bandsCount = RasterTile.DefaultBandsCount, int tileCacheCount = 1000,
                                         int threadsCount = 0, IProgress<double> progress = null,
                                         Action<string> printTimeAction = null) =>
        Task.Run(() => WriteTilesToChannel(channelWriter, minZ, maxZ, tmsCompatible, tileSize, interpolation,
                                           bandsCount, tileCacheCount, threadsCount, progress, printTimeAction));

    /// <summary>
    /// Crops current <see cref="Raster"/> on <see cref="RasterTile"/>s
    /// and writes them to <see cref="IEnumerable{T}"/>.
    /// </summary>
    public IEnumerable<RasterTile> WriteTilesToEnumerable(int minZ, int maxZ, bool tmsCompatible = false,
                                                          Size tileSize = null,
                                                          NetVips.Enums.Kernel interpolation = NetVips.Enums.Kernel.Lanczos3,
                                                          int bandsCount = RasterTile.DefaultBandsCount,
                                                          int tileCacheCount = 1000, IProgress<double> progress = null,
                                                          Action<string> printTimeAction = null)
    {
        tileSize ??= Tile.DefaultSize;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileCacheCount);

        Stopwatch stopwatch = printTimeAction == null ? null : Stopwatch.StartNew();

        int tilesCount = Number.GetCount(MinCoordinate, MaxCoordinate, minZ, maxZ, tmsCompatible, tileSize);
        if (tilesCount <= 0)
            throw new RasterException(Strings.NoTilesToCrop);

        TileProgressReporter tileProgressReporter =
            ProgressHelper.CreateTileProgressReporter(tilesCount, progress, stopwatch, printTimeAction);

        using Image tileCache = Data.Tilecache(tileSize.Width, tileSize.Height, tileCacheCount, threaded: true);

        RasterTile MakeTile(int x, int y, int z)
        {
            RasterTile tile = new(new Number(x, y, z), GeoCoordinateSystem, tileSize, tmsCompatible)
            {
                BandsCount = bandsCount,
                Interpolation = interpolation
            };

            tile.Bytes = WriteTileToEnumerable(tileCache, tile);
            tileProgressReporter?.Advance();

            return tile;
        }

        for (int zoom = minZ; zoom <= maxZ; zoom++)
        {
            (Number minNumber, Number maxNumber) = GeoCoordinate.GetNumbers(MinCoordinate, MaxCoordinate,
                                                                            zoom, tileSize, tmsCompatible);

            for (int tileY = minNumber.Y; tileY <= maxNumber.Y; tileY++)
            {
                for (int tileX = minNumber.X; tileX <= maxNumber.X; tileX++)
                    yield return MakeTile(tileX, tileY, zoom);
            }
        }
    }

    /// <summary>
    /// Crops current <see cref="Raster"/> on <see cref="RasterTile"/>s
    /// and writes them to <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    public IAsyncEnumerable<RasterTile> WriteTilesToAsyncEnumerable(int minZ, int maxZ, bool tmsCompatible = false,
                                                                     Size tileSize = null,
                                                                     NetVips.Enums.Kernel interpolation = NetVips.Enums.Kernel.Lanczos3,
                                                                     int bandsCount = RasterTile.DefaultBandsCount,
                                                                     int tileCacheCount = 1000, int threadsCount = 0,
                                                                     IProgress<double> progress = null,
                                                                     Action<string> printTimeAction = null)
    {
        Channel<RasterTile> channel = Channel.CreateUnbounded<RasterTile>();

        WriteTilesToChannelAsync(channel.Writer, minZ, maxZ, tmsCompatible, tileSize, interpolation, bandsCount,
                                 tileCacheCount, threadsCount, progress, printTimeAction)
           .ContinueWith(_ => channel.Writer.Complete(), TaskScheduler.Current);

        return channel.Reader.ReadAllAsync();
    }

    private static Dictionary<(int Z, int X), string> CreateTileDirectories(
        string outputDirectoryPath,
        GeoCoordinate minCoordinate,
        GeoCoordinate maxCoordinate,
        int minZ,
        int maxZ,
        Size tileSize,
        bool tmsCompatible)
    {
        Dictionary<(int Z, int X), string> tileDirectories = new();

        for (int zoom = minZ; zoom <= maxZ; zoom++)
        {
            (Number minNumber, Number maxNumber) =
                GeoCoordinate.GetNumbers(minCoordinate, maxCoordinate, zoom, tileSize, tmsCompatible);

            string zoomDirectoryPath = Path.Combine(outputDirectoryPath, $"{zoom}");
            Directory.CreateDirectory(zoomDirectoryPath);

            for (int x = minNumber.X; x <= maxNumber.X; x++)
            {
                string tileDirectoryPath = Path.Combine(zoomDirectoryPath, $"{x}");
                Directory.CreateDirectory(tileDirectoryPath);
                tileDirectories[(zoom, x)] = tileDirectoryPath;
            }
        }

        return tileDirectories;
    }

    #endregion

    #region Join tiles

    #region Create overview tiles

    /// <summary>
    /// Create overview <see cref="RasterTile"/>s for specified <see cref="GeoCoordinate"/>s.
    /// </summary>
    public void CreateOverviewTiles(ChannelWriter<RasterTile> channelWriter, int minZ, int maxZ,
                                    HashSet<RasterTile> tiles, bool isBuffered, CoordinateSystem coordinateSystem,
                                    Size tileSize = null, TileExtension extension = TileExtension.Png,
                                    bool tmsCompatible = false, int bandsCount = 4)
    {
        ArgumentNullException.ThrowIfNull(channelWriter);
        ArgumentOutOfRangeException.ThrowIfNegative(minZ);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxZ, minZ);

        Dictionary<Number, RasterTile> tileLookup = CreateTileLookup(tiles);

        for (int z = minZ; z <= maxZ; z++)
        {
            (Number minNumber, Number maxNumber) =
                GeoCoordinate.GetNumbers(MinCoordinate, MaxCoordinate, z, Tile.DefaultSize, false);

            for (int x = minNumber.X; x <= maxNumber.X; x++)
            {
                int x1 = x;
                int z1 = z;
                Parallel.For(minNumber.Y, maxNumber.Y + 1, y =>
                {
                    RasterTile tile = new(new Number(x1, y, z1), coordinateSystem, tileSize, tmsCompatible)
                    {
                        Extension = extension,
                        BandsCount = bandsCount
                    };

                    CreateOverviewTile(ref tile, tileLookup, isBuffered);
                    channelWriter.TryWrite(tile);
                });
            }
        }
    }

    /// <inheritdoc cref="CreateOverviewTiles"/>
    public Task CreateOverviewTilesAsync(ChannelWriter<RasterTile> channelWriter, int minZ, int maxZ,
                                         HashSet<RasterTile> tiles, bool isBuffered, CoordinateSystem coordinateSystem,
                                         Size tileSize = null, TileExtension extension = TileExtension.Png,
                                         bool tmsCompatible = false, int bandsCount = 4) =>
        Task.Run(() => CreateOverviewTiles(channelWriter, minZ, maxZ, tiles, isBuffered, coordinateSystem, tileSize,
                                           extension, tmsCompatible, bandsCount));

    #endregion

    #region Create overview tile

    /// <summary>
    /// Creates specified overview <see cref="RasterTile"/> from lower tiles.
    /// </summary>
    public static void CreateOverviewTile(ref RasterTile targetTile, HashSet<RasterTile> baseTiles, bool isBuffered)
    {
        ArgumentNullException.ThrowIfNull(targetTile);
        ArgumentNullException.ThrowIfNull(baseTiles);

        CreateOverviewTile(ref targetTile, CreateTileLookup(baseTiles), isBuffered);
    }

    private static void CreateOverviewTile(ref RasterTile targetTile,
                                           Dictionary<Number, RasterTile> baseTileLookup,
                                           bool isBuffered)
    {
        ArgumentNullException.ThrowIfNull(targetTile);
        ArgumentNullException.ThrowIfNull(baseTileLookup);

        Number[] numbers = targetTile.Number.GetLowerNumbers();

        baseTileLookup.TryGetValue(numbers[0], out RasterTile lower0);
        baseTileLookup.TryGetValue(numbers[1], out RasterTile lower1);
        baseTileLookup.TryGetValue(numbers[2], out RasterTile lower2);
        baseTileLookup.TryGetValue(numbers[3], out RasterTile lower3);

        CreateOverviewTile(ref targetTile, lower0, lower1, lower2, lower3, isBuffered);
    }

    /// <summary>
    /// Creates specified overview <see cref="RasterTile"/> from 4 lower <see cref="RasterTile"/>s.
    /// </summary>
    public static void CreateOverviewTile(ref RasterTile targetTile, RasterTile tile0, RasterTile tile1,
                                          RasterTile tile2, RasterTile tile3, bool isBuffered)
    {
        ArgumentNullException.ThrowIfNull(targetTile);

        targetTile.Bytes = JoinTilesIntoBytes(tile0, tile1, tile2, tile3, isBuffered, targetTile.Size,
                                              targetTile.BandsCount, targetTile.Extension);
    }

    private static Dictionary<Number, RasterTile> CreateTileLookup(IEnumerable<RasterTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        Dictionary<Number, RasterTile> tileLookup = new();

        foreach (RasterTile tile in tiles)
        {
            if (tile?.Number == null)
                continue;

            tileLookup[tile.Number] = tile;
        }

        return tileLookup;
    }

    #endregion

    #region Join tiles into bytes

    /// <summary>
    /// Join 4 <see cref="RasterTile"/>s into image bytes.
    /// </summary>
    public static IEnumerable<byte> JoinTilesIntoBytes(RasterTile tile0, RasterTile tile1, RasterTile tile2,
                                                       RasterTile tile3, bool isBuffered, Size tileSize, int bandsCount,
                                                       TileExtension extension)
    {
        Image image = JoinTilesIntoImage(tile0, tile1, tile2, tile3, isBuffered, tileSize, bandsCount);

        byte[] result = image?.WriteToBuffer(Tile.GetExtensionString(extension));
        image?.Dispose();

        return result;
    }

    #endregion

    #region Join tiles into image

    /// <summary>
    /// Join 4 <see cref="RasterTile"/>s into one <see cref="Image"/>.
    /// </summary>
    public static Image JoinTilesIntoImage(RasterTile tile0, RasterTile tile1, RasterTile tile2, RasterTile tile3,
                                           bool isBuffered, Size tileSize, int bandsCount)
    {
        if (isBuffered)
            return JoinTilesIntoImage(tile0?.Bytes, tile1?.Bytes, tile2?.Bytes, tile3?.Bytes, tileSize, bandsCount);

        return JoinTilesIntoImage(tile0?.Path, tile1?.Path, tile2?.Path, tile3?.Path, tileSize, bandsCount);
    }

    /// <inheritdoc cref="JoinTilesIntoImage(RasterTile,RasterTile,RasterTile,RasterTile,bool,Size,int)"/>
    public static Task<Image> JoinTilesIntoImageAsync(RasterTile tile0, RasterTile tile1, RasterTile tile2,
                                                      RasterTile tile3, bool isBuffered, Size tileSize,
                                                      int bandsCount) =>
        Task.Run(() => JoinTilesIntoImage(tile0, tile1, tile2, tile3, isBuffered, tileSize, bandsCount));

    /// <summary>
    /// Join tile byte arrays into one <see cref="Image"/>.
    /// </summary>
    public static Image JoinTilesIntoImage(IEnumerable<byte> tile0Bytes, IEnumerable<byte> tile1Bytes,
                                           IEnumerable<byte> tile2Bytes, IEnumerable<byte> tile3Bytes, Size tileSize,
                                           int bandsCount)
    {
        ArgumentNullException.ThrowIfNull(tileSize);
        if (bandsCount < 1 || bandsCount > 4)
            throw new ArgumentOutOfRangeException(nameof(bandsCount));

        byte[][] bytes =
        [
            tile0Bytes?.ToArray() ?? Array.Empty<byte>(),
            tile1Bytes?.ToArray() ?? Array.Empty<byte>(),
            tile2Bytes?.ToArray() ?? Array.Empty<byte>(),
            tile3Bytes?.ToArray() ?? Array.Empty<byte>()
        ];

        Image[] images = new Image[4];

        Size size = new(tileSize.Width / 2, tileSize.Height / 2);
        bool empty = true;

        for (int i = 0; i < 4; i++)
        {
            if (bytes[i].Length > 0)
            {
                empty = false;
                images[i] = Image.NewFromBuffer(bytes[i]).ThumbnailImage(size.Width, size.Height);
            }
            else
            {
                images[i] = Image.Black(size.Width, size.Height, bandsCount);
            }
        }

        return empty ? null : Image.Arrayjoin(images, 2);
    }

    /// <inheritdoc cref="JoinTilesIntoImage(IEnumerable{byte},IEnumerable{byte},IEnumerable{byte},IEnumerable{byte},Size,int)"/>
    public static Task<Image> JoinTilesIntoImageAsync(IEnumerable<byte> tile0Bytes, IEnumerable<byte> tile1Bytes,
                                                      IEnumerable<byte> tile2Bytes, IEnumerable<byte> tile3Bytes,
                                                      Size tileSize, int bandsCount) =>
        Task.Run(() => JoinTilesIntoImage(tile0Bytes, tile1Bytes, tile2Bytes, tile3Bytes, tileSize, bandsCount));

    /// <summary>
    /// Join 4 tile paths into one <see cref="Image"/>.
    /// </summary>
    public static Image JoinTilesIntoImage(string tile0Path, string tile1Path, string tile2Path, string tile3Path,
                                           Size tileSize, int bandsCount)
    {
        ArgumentNullException.ThrowIfNull(tileSize);
        if (bandsCount < 1 || bandsCount > 4)
            throw new ArgumentOutOfRangeException(nameof(bandsCount));

        string[] paths =
        [
            string.IsNullOrWhiteSpace(tile0Path) ? string.Empty : tile0Path,
            string.IsNullOrWhiteSpace(tile1Path) ? string.Empty : tile1Path,
            string.IsNullOrWhiteSpace(tile2Path) ? string.Empty : tile2Path,
            string.IsNullOrWhiteSpace(tile3Path) ? string.Empty : tile3Path
        ];

        Image[] images = new Image[4];

        Size size = new(tileSize.Width / 2, tileSize.Height / 2);
        bool empty = true;

        for (int i = 0; i < 4; i++)
        {
            if (File.Exists(paths[i]))
            {
                empty = false;
                byte[] bytes = File.ReadAllBytes(paths[i]);
                images[i] = Image.NewFromBuffer(bytes).ThumbnailImage(size.Width, size.Height);
            }
            else
            {
                images[i] = Image.Black(size.Width, size.Height, bandsCount);
            }
        }

        return empty ? null : Image.Arrayjoin(images, 2);
    }

    /// <inheritdoc cref="JoinTilesIntoImage(string,string,string,string,Size,int)"/>
    public static Task<Image> JoinTilesIntoImageAsync(string tile0Path, string tile1Path, string tile2Path,
                                                      string tile3Path, Size tileSize, int bandsCount) =>
        Task.Run(() => JoinTilesIntoImage(tile0Path, tile1Path, tile2Path, tile3Path, tileSize, bandsCount));

    #endregion

    #endregion

    private static double[] GetTransparentBackground(int bandsCount) => bandsCount switch
    {
        1 => Background1,
        2 => Background2,
        3 => Background3,
        _ => Background4
    };

    #endregion
}
