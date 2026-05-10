using GTiff2Tiles.Core;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GTiff2Tiles.Server.Services;

public sealed class CatalogService(ServerDbContext dbContext, SlugGenerator slugGenerator, LocalFileStorage fileStorage)
{
    public async Task<IReadOnlyList<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Catalogs
                              .AsNoTracking()
                              .Include(catalog => catalog.ActiveImage)
                              .Include(catalog => catalog.Images)
                              .OrderBy(catalog => catalog.Name)
                              .ToListAsync(cancellationToken)
                              .ConfigureAwait(false);
    }

    public async Task<Catalog?> GetCatalogAsync(int id, CancellationToken cancellationToken)
    {
        return await dbContext.Catalogs
                              .Include(catalog => catalog.ActiveImage)
                              .Include(catalog => catalog.Images)
                              .FirstOrDefaultAsync(catalog => catalog.Id == id, cancellationToken)
                              .ConfigureAwait(false);
    }

    public async Task<Catalog> CreateCatalogAsync(CreateCatalogInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        string baseSlug = slugGenerator.GenerateSlug(string.IsNullOrWhiteSpace(input.Slug) ? input.Name : input.Slug);
        string slug = await EnsureUniqueSlugAsync(baseSlug, cancellationToken: cancellationToken).ConfigureAwait(false);

        Catalog catalog = new()
        {
            Name = input.Name.Trim(),
            Slug = slug,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow
        };

        dbContext.Catalogs.Add(catalog);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        fileStorage.GetCatalogDirectory(catalog.Slug);
        return catalog;
    }

    public async Task<Catalog> UpdateCatalogAsync(int catalogId, UpdateCatalogInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        Catalog catalog = await dbContext.Catalogs
                                         .Include(existingCatalog => existingCatalog.ActiveImage)
                                         .Include(existingCatalog => existingCatalog.Images)
                                         .FirstOrDefaultAsync(existingCatalog => existingCatalog.Id == catalogId, cancellationToken)
                                         .ConfigureAwait(false)
                          ?? throw new InvalidOperationException("Catalog was not found.");

        string baseSlug = slugGenerator.GenerateSlug(input.Slug);
        string slug = await EnsureUniqueSlugAsync(baseSlug, catalog.Id, cancellationToken).ConfigureAwait(false);

        catalog.Name = input.Name.Trim();
        catalog.Slug = slug;
        catalog.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        fileStorage.GetCatalogDirectory(catalog.Slug);
        return catalog;
    }

    public async Task<IReadOnlyList<CatalogImage>> UploadImagesAsync(int catalogId, UploadGeoTiffInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        Catalog catalog = await dbContext.Catalogs
                                         .Include(existingCatalog => existingCatalog.Images)
                                         .FirstOrDefaultAsync(existingCatalog => existingCatalog.Id == catalogId, cancellationToken)
                                         .ConfigureAwait(false)
                          ?? throw new InvalidOperationException("Catalog was not found.");

        if (input.Files.Count == 0)
            throw new InvalidOperationException("At least one GeoTIFF file is required.");

        foreach (IFormFile file in input.Files)
        {
            ValidateGeoTiffFile(file);
        }

        List<CatalogImage> uploadedImages = [];
        foreach (IFormFile file in input.Files)
        {
            CatalogImage image = await UploadSingleImageAsync(catalog, file, cancellationToken).ConfigureAwait(false);
            uploadedImages.Add(image);
        }

        return uploadedImages;
    }

    public async Task<Catalog> SetActiveImageAsync(int catalogId, int imageId, CancellationToken cancellationToken)
    {
        Catalog catalog = await dbContext.Catalogs
                                         .Include(existingCatalog => existingCatalog.Images)
                                         .FirstOrDefaultAsync(existingCatalog => existingCatalog.Id == catalogId, cancellationToken)
                                         .ConfigureAwait(false)
                          ?? throw new InvalidOperationException("Catalog was not found.");

        bool imageExists = catalog.Images.Any(image => image.Id == imageId);
        if (!imageExists)
            throw new InvalidOperationException("The selected image does not belong to this catalog.");

        catalog.ActiveImageId = imageId;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await GetCatalogAsync(catalogId, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Catalog was not found after update.");
    }

    private async Task<CatalogImage> UploadSingleImageAsync(Catalog catalog, IFormFile file, CancellationToken cancellationToken)
    {
        string storageKey = Guid.NewGuid().ToString("N");
        string originalPath = fileStorage.GetOriginalRasterPath(catalog.Slug, storageKey);
        string normalizedPath = fileStorage.GetNormalizedRasterPath(catalog.Slug, storageKey);

        await using (Stream uploadStream = file.OpenReadStream())
        {
            await fileStorage.SaveUploadAsync(uploadStream, originalPath, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            bool isAlreadyNormalized = await GTiff2Tiles.Core.Helpers.CheckHelper.CheckInputFileAsync(originalPath, CoordinateSystem.Epsg3857)
                                                                      .ConfigureAwait(false);

            if (isAlreadyNormalized)
            {
                File.Copy(originalPath, normalizedPath, overwrite: true);
            }
            else
            {
                await GdalWorker.ConvertGeoTiffToTargetSystemAsync(originalPath, normalizedPath, CoordinateSystem.Epsg3857)
                                .ConfigureAwait(false);
            }

            using Raster raster = new(normalizedPath, CoordinateSystem.Epsg3857);

            CatalogImage image = new()
            {
                Catalog = catalog,
                CatalogId = catalog.Id,
                StorageKey = storageKey,
                OriginalFileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                OriginalPath = originalPath,
                NormalizedPath = normalizedPath,
                OriginalFileSizeBytes = file.Length,
                UploadedUtc = DateTimeOffset.UtcNow,
                CoordinateSystem = "EPSG:3857",
                Width = raster.Size.Width,
                Height = raster.Size.Height,
                MinX = raster.MinCoordinate.X,
                MinY = raster.MinCoordinate.Y,
                MaxX = raster.MaxCoordinate.X,
                MaxY = raster.MaxCoordinate.Y
            };

            dbContext.CatalogImages.Add(image);
            catalog.ActiveImage = image;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return image;
        }
        catch
        {
            TryDeleteDirectory(Path.GetDirectoryName(originalPath)!);
            throw;
        }
    }

    private static void ValidateGeoTiffFile(IFormFile file)
    {
        string extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".tif", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".tiff", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Only .tif and .tiff uploads are supported. '{file.FileName}' is not valid.");
        }
    }

    private async Task<string> EnsureUniqueSlugAsync(string baseSlug, int? excludeCatalogId = null, CancellationToken cancellationToken = default)
    {
        string slug = baseSlug;
        int suffix = 2;

        while (await dbContext.Catalogs.AnyAsync(catalog => catalog.Slug == slug && catalog.Id != excludeCatalogId, cancellationToken).ConfigureAwait(false))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }

        return slug;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
