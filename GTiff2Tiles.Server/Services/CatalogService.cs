using GTiff2Tiles.Core;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GTiff2Tiles.Server.Services;

public sealed class CatalogService(ServerDbContext dbContext, SlugGenerator slugGenerator, IStorageFactory storageFactory)
{
    public async Task<IReadOnlyList<Catalog>> GetCatalogsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Catalogs
                              .AsNoTracking()
                              .OrderBy(catalog => catalog.Name)
                              .Select(catalog => new Catalog
                              {
                                  Id = catalog.Id,
                                  Name = catalog.Name,
                                  Slug = catalog.Slug,
                                  Description = catalog.Description,
                                  CreatedUtc = catalog.CreatedUtc,
                                  ImageCount = catalog.Images.Count
                              })
                              .ToListAsync(cancellationToken)
                              .ConfigureAwait(false);
    }

    public async Task<Catalog?> GetCatalogAsync(int id, CancellationToken cancellationToken)
    {
        return await dbContext.Catalogs
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
            StorageProvider = string.IsNullOrWhiteSpace(input.StorageProvider) ? "Local" : input.StorageProvider,
            StorageConfig = input.StorageConfig,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        dbContext.Catalogs.Add(catalog);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return catalog;
    }

    public async Task<Catalog> UpdateCatalogAsync(int catalogId, UpdateCatalogInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        Catalog catalog = await dbContext.Catalogs
                                         .Include(existingCatalog => existingCatalog.Images)
                                         .FirstOrDefaultAsync(existingCatalog => existingCatalog.Id == catalogId, cancellationToken)
                                         .ConfigureAwait(false)
                          ?? throw new InvalidOperationException("Catalog was not found.");

        string baseSlug = slugGenerator.GenerateSlug(input.Slug);
        string slug = await EnsureUniqueSlugAsync(baseSlug, catalog.Id, cancellationToken).ConfigureAwait(false);

        catalog.Name = input.Name.Trim();
        catalog.Slug = slug;
        catalog.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        catalog.StorageProvider = string.IsNullOrWhiteSpace(input.StorageProvider) ? "Local" : input.StorageProvider;
        catalog.StorageConfig = input.StorageConfig;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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

        int nextSortOrder = catalog.Images.Count == 0 ? 0 : catalog.Images.Max(image => image.SortOrder) + 1;
        List<CatalogImage> uploadedImages = [];
        foreach (IFormFile file in input.Files)
        {
            CatalogImage image = await UploadSingleImageAsync(catalog, file, nextSortOrder, cancellationToken).ConfigureAwait(false);
            uploadedImages.Add(image);
            nextSortOrder++;
        }

        return uploadedImages;
    }

    public async Task<Catalog> MoveImageAsync(int catalogId, int imageId, int offset, CancellationToken cancellationToken)
    {
        if (offset is not -1 and not 1)
            throw new InvalidOperationException("Only adjacent image reordering is supported.");

        Catalog catalog = await dbContext.Catalogs
                                         .Include(existingCatalog => existingCatalog.Images)
                                         .FirstOrDefaultAsync(existingCatalog => existingCatalog.Id == catalogId, cancellationToken)
                                         .ConfigureAwait(false)
                          ?? throw new InvalidOperationException("Catalog was not found.");

        CatalogImage? image = catalog.Images.FirstOrDefault(existingImage => existingImage.Id == imageId);
        if (image is null)
            throw new InvalidOperationException("The selected image does not belong to this catalog.");

        List<CatalogImage> orderedImages = catalog.Images
                                                 .OrderBy(existingImage => existingImage.SortOrder)
                                                 .ThenBy(existingImage => existingImage.Id)
                                                 .ToList();
        int index = orderedImages.FindIndex(existingImage => existingImage.Id == imageId);
        int targetIndex = index + offset;
        if (targetIndex < 0 || targetIndex >= orderedImages.Count)
        {
            return await GetCatalogAsync(catalogId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Catalog was not found after update.");
        }

        orderedImages.RemoveAt(index);
        orderedImages.Insert(targetIndex, image);

        for (int orderedIndex = 0; orderedIndex < orderedImages.Count; orderedIndex++)
        {
            orderedImages[orderedIndex].SortOrder = orderedIndex;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await GetCatalogAsync(catalogId, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Catalog was not found after update.");
    }

    public async Task<Catalog> RemoveImageAsync(int catalogId, int imageId, CancellationToken cancellationToken)
    {
        Catalog catalog = await dbContext.Catalogs
                                         .Include(existingCatalog => existingCatalog.Images)
                                         .FirstOrDefaultAsync(existingCatalog => existingCatalog.Id == catalogId, cancellationToken)
                                         .ConfigureAwait(false)
                          ?? throw new InvalidOperationException("Catalog was not found.");

        CatalogImage? image = catalog.Images.FirstOrDefault(existingImage => existingImage.Id == imageId);
        if (image is null)
            throw new InvalidOperationException("The selected image does not belong to this catalog.");

        dbContext.CatalogImages.Remove(image);

        List<CatalogImage> remainingImages = catalog.Images
                                                    .Where(existingImage => existingImage.Id != imageId)
                                                    .OrderBy(existingImage => existingImage.SortOrder)
                                                    .ThenBy(existingImage => existingImage.Id)
                                                    .ToList();
        for (int orderedIndex = 0; orderedIndex < remainingImages.Count; orderedIndex++)
        {
            remainingImages[orderedIndex].SortOrder = orderedIndex;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        IStorage catalogStorage = storageFactory.GetStorage(catalog.StorageProvider, catalog.StorageConfig);
        await catalogStorage.DeleteImageAsync(catalog.Slug, image.StorageKey, cancellationToken).ConfigureAwait(false);

        return await GetCatalogAsync(catalogId, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Catalog was not found after update.");
    }

    public async Task DeleteCatalogAsync(int catalogId, CancellationToken cancellationToken)
    {
        Catalog catalog = await dbContext.Catalogs
                                         .FirstOrDefaultAsync(existingCatalog => existingCatalog.Id == catalogId, cancellationToken)
                                         .ConfigureAwait(false)
                          ?? throw new InvalidOperationException("Catalog was not found.");

        string catalogSlug = catalog.Slug;

        dbContext.Catalogs.Remove(catalog);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        IStorage catalogStorage = storageFactory.GetStorage(catalog.StorageProvider, catalog.StorageConfig);
        await catalogStorage.DeleteCatalogAsync(catalogSlug, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CatalogImage> UploadSingleImageAsync(Catalog catalog, IFormFile file, int sortOrder, CancellationToken cancellationToken)
    {
        IStorage catalogStorage = storageFactory.GetStorage(catalog.StorageProvider, catalog.StorageConfig);

        string storageKey = Guid.NewGuid().ToString("N");
        string originalPath = catalogStorage.GetOriginalRasterPath(catalog.Slug, storageKey);
        string normalizedPath = catalogStorage.GetNormalizedRasterPath(catalog.Slug, storageKey);

        await using (Stream uploadStream = file.OpenReadStream())
        {
            await catalogStorage.SaveUploadAsync(uploadStream, originalPath, cancellationToken).ConfigureAwait(false);
        }

        string localNormalizedPath = null!;

        try
        {
            if (catalogStorage.Provider == "S3")
            {
                string localWarpedPath = Path.GetTempFileName() + ".tif";
                localNormalizedPath = Path.GetTempFileName() + ".tif";
                catalogStorage.ConfigureGdal();
                await GdalWorker.ConvertGeoTiffToTargetSystemAsync(originalPath, localWarpedPath, CoordinateSystem.Epsg3857)
                                .ConfigureAwait(false);
                await GdalWorker.CreateCogAsync(localWarpedPath, localNormalizedPath)
                                .ConfigureAwait(false);
                await using FileStream normalizedStream = File.OpenRead(localNormalizedPath);
                await catalogStorage.SaveUploadAsync(normalizedStream, normalizedPath, cancellationToken).ConfigureAwait(false);

                try { File.Delete(localWarpedPath); } catch { /* best effort */ }
            }
            else
            {
                localNormalizedPath = normalizedPath;
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
            }

            using Raster raster = new(localNormalizedPath, CoordinateSystem.Epsg3857);

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
                SortOrder = sortOrder,
                CoordinateSystem = "EPSG:3857",
                Width = raster.Size.Width,
                Height = raster.Size.Height,
                MinX = raster.MinCoordinate.X,
                MinY = raster.MinCoordinate.Y,
                MaxX = raster.MaxCoordinate.X,
                MaxY = raster.MaxCoordinate.Y,
                StorageProvider = catalogStorage.Provider
            };

            dbContext.CatalogImages.Add(image);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return image;
        }
        catch
        {
            await catalogStorage.DeleteImageAsync(catalog.Slug, storageKey, cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (catalogStorage.Provider == "S3" && localNormalizedPath is not null && File.Exists(localNormalizedPath))
            {
                try { File.Delete(localNormalizedPath); } catch { /* best effort */ }
            }
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
}
