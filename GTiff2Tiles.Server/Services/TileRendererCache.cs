using System.Collections.Concurrent;
using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Core.Tiles;
using Microsoft.Extensions.Caching.Memory;
using NetVips;

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

    public byte[]? RenderTile(string normalizedPath, int x, int y, int z)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);

        return GetOrCreateRenderer(normalizedPath).RenderTile(x, y, z);
    }

    public byte[]? RenderTile(IReadOnlyList<string> normalizedPaths, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(normalizedPaths);

        byte[]? composedTile = null;
        foreach (string normalizedPath in normalizedPaths)
        {
            byte[]? tile = RenderTile(normalizedPath, x, y, z);
            if (tile is null)
                continue;

            composedTile = composedTile is null ? tile : CompositeTiles(composedTile, tile);
        }

        return composedTile;
    }

    public byte[]? GenerateThumbnail(string normalizedPath, int maxWidth = 256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);

        if (!File.Exists(normalizedPath))
            return null;

        using Image image = Image.NewFromFile(normalizedPath, access: NetVips.Enums.Access.Sequential);
        using Image thumbnail = image.ThumbnailImage(maxWidth);
        return thumbnail.WriteToBuffer(PngFormat);
    }

    public void Dispose() => _cache.Dispose();

    private CachedTileRenderer GetOrCreateRenderer(string normalizedPath)
    {
        if (_cache.TryGetValue(normalizedPath, out CachedTileRenderer? cachedRenderer) && cachedRenderer is not null)
            return cachedRenderer;

        object creationLock = _creationLocks.GetOrAdd(normalizedPath, _ => new object());

        lock (creationLock)
        {
            try
            {
                if (_cache.TryGetValue(normalizedPath, out cachedRenderer) && cachedRenderer is not null)
                    return cachedRenderer;

                cachedRenderer = new CachedTileRenderer(normalizedPath);
                _cache.Set(normalizedPath, cachedRenderer, new MemoryCacheEntryOptions
                {
                    Size = 1,
                    SlidingExpiration = SlidingExpiration,
                    PostEvictionCallbacks = { new PostEvictionCallbackRegistration { EvictionCallback = OnEviction } }
                });

                return cachedRenderer;
            }
            finally
            {
                _creationLocks.TryRemove(normalizedPath, out _);
            }
        }
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

    private sealed class CachedTileRenderer : IDisposable
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
}
