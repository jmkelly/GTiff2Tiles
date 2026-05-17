using System.Collections.Concurrent;
using GTiff2Tiles.Core;
using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Core.Images;
using GTiff2Tiles.Core.Tiles;
using GTiff2Tiles.Server.Options;
using Microsoft.Extensions.Caching.Memory;
using NetVips;
using OSGeo.GDAL;

namespace GTiff2Tiles.Server.Services;

public sealed class TileRendererCache : IDisposable
{
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(15);
    private const string PngFormat = ".png";

    private readonly MemoryCache _cache;
    private readonly ConcurrentDictionary<string, object> _creationLocks = new(StringComparer.OrdinalIgnoreCase);

    public TileRendererCache(int maxRendererCount = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRendererCount);

        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = maxRendererCount
        });
    }

    public byte[]? RenderTile(string normalizedPath, int x, int y, int z, S3StorageOptions? s3Options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);

        return GetOrCreateRenderer(normalizedPath, s3Options).RenderTile(x, y, z);
    }

    public byte[]? RenderTile(IReadOnlyList<string> normalizedPaths, int x, int y, int z, S3StorageOptions? s3Options = null)
    {
        ArgumentNullException.ThrowIfNull(normalizedPaths);

        byte[]? composedTile = null;
        foreach (string normalizedPath in normalizedPaths)
        {
            byte[]? tile = RenderTile(normalizedPath, x, y, z, s3Options);
            if (tile is null)
                continue;

            composedTile = composedTile is null ? tile : CompositeTiles(composedTile, tile);
        }

        return composedTile;
    }

    public byte[]? GenerateThumbnail(string normalizedPath, int maxWidth = 256, S3StorageOptions? s3Options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);

        if (normalizedPath.StartsWith("/vsis3/", StringComparison.OrdinalIgnoreCase))
            return GenerateThumbnailFromVsi(normalizedPath, maxWidth, s3Options);

        if (!File.Exists(normalizedPath))
            return null;

        using Image image = Image.NewFromFile(normalizedPath, access: NetVips.Enums.Access.Sequential);
        using Image thumbnail = image.ThumbnailImage(maxWidth);
        return thumbnail.WriteToBuffer(PngFormat);
    }

    public void Dispose() => _cache.Dispose();

    private IRenderer GetOrCreateRenderer(string normalizedPath, S3StorageOptions? s3Options)
    {
        string cacheKey = GetCacheKey(normalizedPath, s3Options);

        if (_cache.TryGetValue(cacheKey, out IRenderer? cachedRenderer) && cachedRenderer is not null)
            return cachedRenderer;

        object creationLock = _creationLocks.GetOrAdd(cacheKey, _ => new object());

        lock (creationLock)
        {
            try
            {
                if (_cache.TryGetValue(cacheKey, out cachedRenderer) && cachedRenderer is not null)
                    return cachedRenderer;

                IRenderer renderer = normalizedPath.StartsWith("/vsis3/", StringComparison.OrdinalIgnoreCase)
                    ? new S3TileRenderer(normalizedPath, s3Options)
                    : new CachedTileRenderer(normalizedPath);

                _cache.Set(cacheKey, renderer, new MemoryCacheEntryOptions
                {
                    Size = 1,
                    SlidingExpiration = SlidingExpiration,
                    PostEvictionCallbacks = { new PostEvictionCallbackRegistration { EvictionCallback = OnEviction } }
                });

                return renderer;
            }
            finally
            {
                _creationLocks.TryRemove(cacheKey, out _);
            }
        }
    }

    private static string GetCacheKey(string normalizedPath, S3StorageOptions? s3Options)
    {
        if (s3Options is null) return normalizedPath;

        return $"{normalizedPath}|{HashCode.Combine(
            s3Options.AccessKeyId,
            s3Options.SecretAccessKey,
            s3Options.BucketName,
            s3Options.Region,
            s3Options.EndpointUrl)}";
    }

    private static void OnEviction(object key, object? value, EvictionReason reason, object? state)
    {
        if (value is IDisposable disposable)
            disposable.Dispose();
    }

    private static byte[] CompositeTiles(byte[] baseTile, byte[] overlayTile)
    {
        using Image baseImage = Image.NewFromBuffer(baseTile);
        using Image overlayImage = Image.NewFromBuffer(overlayTile);
        using Image compositedImage = baseImage.Composite2(overlayImage, Enums.BlendMode.Over);
        return compositedImage.WriteToBuffer(PngFormat);
    }

    private static byte[]? GenerateThumbnailFromVsi(string vsiPath, int maxWidth, S3StorageOptions? s3Options)
    {
        if (s3Options is not null)
            S3Storage.ConfigureGdal(s3Options);

        using Dataset dataset = OSGeo.GDAL.Gdal.Open(vsiPath, OSGeo.GDAL.Access.GA_ReadOnly);
        if (dataset is null) return null;

        int srcWidth = dataset.RasterXSize;
        int srcHeight = dataset.RasterYSize;
        int bandsCount = dataset.RasterCount;
        if (srcWidth <= 0 || srcHeight <= 0 || bandsCount <= 0)
            return null;

        double scale = Math.Min((double)maxWidth / srcWidth, (double)maxWidth / srcHeight);
        using Image image = ReadSourceImage(dataset, 0, 0, srcWidth, srcHeight, bandsCount);
        using Image resized = scale.Equals(1.0)
            ? image.Copy()
            : image.Resize(scale, NetVips.Enums.Kernel.Lanczos3);

        Image final = resized;
        GTiff2Tiles.Core.Images.Band.AddDefaultBands(ref final, RasterTile.DefaultBandsCount);
        try
        {
            return final.WriteToBuffer(PngFormat);
        }
        finally
        {
            if (final != resized)
                final.Dispose();
        }
    }

    private static Image AddAlphaBand(Image image) => image.AddAlpha();

        private static Image ReadSourceImage(Dataset dataset, int left, int top, int width, int height, int bandsCount)
        {
            int bands = Math.Min(bandsCount, RasterTile.DefaultBandsCount);
            int[] bandMap = Enumerable.Range(1, bands).ToArray();
            byte[] pixels = new byte[width * height * bands];

        dataset.ReadRaster(
            left,
            top,
            width,
            height,
            pixels,
            width,
            height,
            bands,
            bandMap,
            bands,
            width * bands,
            1);

            return Image.NewFromMemory(
                pixels,
                width,
                height,
                bands,
                NetVips.Enums.BandFormat.Uchar);
        }

        private static Image ReadSourceImage(Dataset dataset, int left, int top, int width, int height,
                                             int targetWidth, int targetHeight, int bandsCount)
        {
            int bands = Math.Min(bandsCount, RasterTile.DefaultBandsCount);
            int[] bandMap = Enumerable.Range(1, bands).ToArray();
            byte[] pixels = new byte[targetWidth * targetHeight * bands];

            dataset.ReadRaster(
                left,
                top,
                width,
                height,
                pixels,
                targetWidth,
                targetHeight,
                bands,
                bandMap,
                bands,
                targetWidth * bands,
                1);

            return Image.NewFromMemory(
                pixels,
                targetWidth,
                targetHeight,
                bands,
                NetVips.Enums.BandFormat.Uchar);
        }

    private static Image JoinBands(List<Image> images)
    {
        Image result = images[0];
        for (int i = 1; i < images.Count; i++)
        {
            Image next = result.Bandjoin(images[i]);
            if (result != images[0]) result.Dispose();
            result = next;
        }
        return result;
    }

    private interface IRenderer
    {
        byte[]? RenderTile(int x, int y, int z);
    }

    private sealed class CachedTileRenderer : IRenderer, IDisposable
    {
        private readonly Raster _raster;
        private readonly Image _tileCache;

        public CachedTileRenderer(string normalizedPath)
        {
            _raster = new Raster(normalizedPath, CoordinateSystem.Epsg3857);
            _tileCache = _raster.Data.Tilecache(Tile.DefaultSize.Width, Tile.DefaultSize.Height, 32, threaded: true);
        }

        public byte[]? RenderTile(int x, int y, int z)
        {
            using RasterTile tile = new(new Number(x, y, z), CoordinateSystem.Epsg3857, Tile.DefaultSize, tmsCompatible: false)
            {
                BandsCount = RasterTile.DefaultBandsCount,
                Extension = TileExtension.Png,
                Interpolation = NetVips.Enums.Kernel.Lanczos3
            };

            return _raster.WriteTileToEnumerable(_tileCache, tile)?.ToArray();
        }

        public void Dispose()
        {
            _tileCache.Dispose();
            _raster.Dispose();
        }
    }

        private sealed class S3TileRenderer : IRenderer, IDisposable
    {
        private readonly string _vsiPath;
        private readonly int _width;
        private readonly int _height;
        private readonly GeoCoordinate _imageMinCoord;
        private readonly GeoCoordinate _imageMaxCoord;
        private readonly OSGeo.GDAL.Dataset _dataset;
        private readonly double[] _geoTransform;
        private readonly object _renderLock = new();
        private readonly bool _hasOverviews;

        public S3TileRenderer(string vsiPath, S3StorageOptions? s3Options)
        {
            _vsiPath = vsiPath;

            if (s3Options is not null)
                S3Storage.ConfigureGdal(s3Options);

            _dataset = OSGeo.GDAL.Gdal.Open(_vsiPath, OSGeo.GDAL.Access.GA_ReadOnly)
                       ?? throw new InvalidOperationException($"Failed to open {_vsiPath}");

            _width = _dataset.RasterXSize;
            _height = _dataset.RasterYSize;

            _geoTransform = new double[6];
            _dataset.GetGeoTransform(_geoTransform);
            _hasOverviews = _dataset.GetRasterBand(1)?.GetOverviewCount() > 0;

            double minX = _geoTransform[0];
            double maxX = _geoTransform[0] + _width * _geoTransform[1];
            double maxY = _geoTransform[3];
            double minY = _geoTransform[3] + _height * _geoTransform[5];

            _imageMinCoord = new MercatorCoordinate(minX, minY);
            _imageMaxCoord = new MercatorCoordinate(maxX, maxY);
        }

        public byte[]? RenderTile(int x, int y, int z)
        {
            using RasterTile tile = new(new Number(x, y, z), CoordinateSystem.Epsg3857, Tile.DefaultSize, tmsCompatible: false);

            if (!RasterTileLayoutCalculator.TryGetLayout(
                    _imageMinCoord, _imageMaxCoord, new Size(_width, _height),
                    tile.MinCoordinate, tile.MaxCoordinate, tile.Size,
                    out RasterTileLayout layout))
                return null;

            lock (_renderLock)
            {
                int bandsCount = _dataset.RasterCount;
                if (bandsCount <= 0) return null;
                try
                {
                    using Image image = ReadSourceImage(layout, bandsCount);
                    Image processed = ProcessTileImage(image, layout);
                    byte[] result = processed.WriteToBuffer(PngFormat);
                    if (processed != image) processed.Dispose();
                    return result;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Tile render error ({x},{y},{z}): {ex.Message}");
                    return null;
                }
            }
        }

        public void Dispose() => _dataset.Dispose();

        private Image ReadSourceImage(RasterTileLayout layout, int bandsCount)
        {
            if (_hasOverviews && !layout.HasSameReadAndWriteSize)
            {
                return TileRendererCache.ReadSourceImage(
                    _dataset,
                    layout.ReadLeft,
                    layout.ReadTop,
                    layout.ReadWidth,
                    layout.ReadHeight,
                    layout.WriteWidth,
                    layout.WriteHeight,
                    bandsCount);
            }

            return TileRendererCache.ReadSourceImage(_dataset, layout.ReadLeft, layout.ReadTop, layout.ReadWidth, layout.ReadHeight, bandsCount);
        }

        private static Image ProcessTileImage(Image source, RasterTileLayout layout)
        {
            Image current = source;

            GTiff2Tiles.Core.Images.Band.AddDefaultBands(ref current, RasterTile.DefaultBandsCount);

            if (!layout.HasSameReadAndWriteSize &&
                (source.Width != layout.WriteWidth || source.Height != layout.WriteHeight))
            {
                Image resized = current.Resize(layout.XScale, NetVips.Enums.Kernel.Lanczos3, null, layout.YScale);
                if (current != source) current.Dispose();
                current = resized;
            }

            if (!layout.WritesWholeTile(new Size(256, 256)))
            {
                Image embedded = current.Embed(
                    layout.WriteLeft, layout.WriteTop, 256, 256,
                    extend: NetVips.Enums.Extend.Background,
                    background: new double[] { 0, 0, 0, 0 });
                if (current != source) current.Dispose();
                current = embedded;
            }

            return current;
        }
    }
}
