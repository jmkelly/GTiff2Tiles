using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Core.Tiles;
using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using Microsoft.EntityFrameworkCore;
using NetVips;

namespace GTiff2Tiles.Server.Services;

public sealed class TileService(ServerDbContext dbContext)
{
    public const string TileContentType = "image/png";

    public async Task<Stream?> TryOpenTileStreamAsync(
        string catalogSlug,
        int z,
        int x,
        int y,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(catalogSlug) || z < 0 || x < 0 || y < 0)
            return null;

        CatalogImage? image = await dbContext.Catalogs
                                             .AsNoTracking()
                                             .Where(existingCatalog => existingCatalog.Slug == catalogSlug)
                                             .Select(existingCatalog => existingCatalog.ActiveImage)
                                             .FirstOrDefaultAsync(cancellationToken)
                                             .ConfigureAwait(false);
        if (image is null)
            return null;

        byte[]? content = await Task.Run(() => RenderTile(image.NormalizedPath, x, y, z), cancellationToken)
                                    .ConfigureAwait(false);
        return content is null ? null : new MemoryStream(content, writable: false);
    }

    private static byte[]? RenderTile(string normalizedPath, int x, int y, int z)
    {
        using Raster raster = new(normalizedPath, CoordinateSystem.Epsg3857);
        using Image tileCache = raster.Data.Tilecache(Tile.DefaultSize.Width, Tile.DefaultSize.Height, 32, threaded: true);
        using RasterTile tile = new(new Number(x, y, z), CoordinateSystem.Epsg3857, Tile.DefaultSize, tmsCompatible: false)
        {
            BandsCount = RasterTile.DefaultBandsCount,
            Extension = TileExtension.Png,
            Interpolation = NetVips.Enums.Kernel.Lanczos3
        };

        return raster.WriteTileToEnumerable(tileCache, tile)?.ToArray();
    }
}
