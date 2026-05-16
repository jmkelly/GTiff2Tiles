using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GTiff2Tiles.Server.Services;

public sealed class TileService(ServerDbContext dbContext, TileRendererCache tileRendererCache)
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

        IReadOnlyList<string> normalizedPaths = await dbContext.CatalogImages
                                                               .AsNoTracking()
                                                               .Where(image => image.Catalog.Slug == catalogSlug)
                                                               .OrderBy(image => image.SortOrder)
                                                               .ThenBy(image => image.Id)
                                                               .Select(image => image.NormalizedPath)
                                                               .ToListAsync(cancellationToken)
                                                               .ConfigureAwait(false);
        if (normalizedPaths.Count == 0)
            return null;

        byte[]? content = await Task.Run(() => tileRendererCache.RenderTile(normalizedPaths, x, y, z), cancellationToken)
                                    .ConfigureAwait(false);
        return content is null ? null : new MemoryStream(content, writable: false);
    }
}
