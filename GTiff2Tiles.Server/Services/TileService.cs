using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Options;
using Microsoft.EntityFrameworkCore;

namespace GTiff2Tiles.Server.Services;

public sealed class TileService(ServerDbContext dbContext, TileRendererCache tileRendererCache, StoragePathResolver pathResolver)
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

        var imageData = await dbContext.CatalogImages
                                       .AsNoTracking()
                                       .Where(image => image.Catalog.Slug == catalogSlug)
                                       .OrderBy(image => image.SortOrder)
                                       .ThenBy(image => image.Id)
                                       .Select(image => new
                                       {
                                           image.NormalizedPath,
                                           image.StorageProvider,
                                           CatalogSlug = image.Catalog.Slug,
                                           image.Catalog.StorageConfig
                                       })
                                       .ToListAsync(cancellationToken)
                                       .ConfigureAwait(false);
        if (imageData.Count == 0)
            return null;

        List<string> resolvedPaths = [];
        S3StorageOptions? s3Options = null;
        foreach (var img in imageData)
        {
            PathResolutionResult result = await pathResolver.ResolvePathAsync(
                img.NormalizedPath, img.StorageProvider, img.CatalogSlug, img.StorageConfig).ConfigureAwait(false);
            resolvedPaths.Add(result.Path);
            s3Options ??= result.S3Options;
        }

        byte[]? content = await Task.Run(() => tileRendererCache.RenderTile(resolvedPaths, x, y, z, s3Options), cancellationToken)
                                    .ConfigureAwait(false);
        return content is null ? null : new MemoryStream(content, writable: false);
    }
}
