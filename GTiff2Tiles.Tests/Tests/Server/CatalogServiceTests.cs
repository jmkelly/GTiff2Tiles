using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Options;
using GTiff2Tiles.Server.Services;
using GTiff2Tiles.Tests.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace GTiff2Tiles.Tests.Tests.Server;

[TestFixture]
public sealed class CatalogServiceTests
{
    private string _tempRoot = null!;
    private ServerDbContext _dbContext = null!;
    private LocalFileStorage _fileStorage = null!;
    private CatalogService _catalogService = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "gtiff2tiles-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _dbContext = CreateDbContext();
        _dbContext.Database.EnsureCreated();

        _fileStorage = new LocalFileStorage(Options.Create(new LocalStorageOptions
        {
            RootPath = _tempRoot,
            DatabasePath = Path.Combine(_tempRoot, "test.db")
        }));
        _fileStorage.EnsureStorageLayout();

        _catalogService = new CatalogService(_dbContext, new SlugGenerator(), _fileStorage);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Test]
    public async Task CreateCatalogAsync_GeneratesUniqueSlug()
    {
        Catalog first = await _catalogService.CreateCatalogAsync(new CreateCatalogInput
        {
            Name = "Tokyo Mosaic"
        }, CancellationToken.None);

        Catalog second = await _catalogService.CreateCatalogAsync(new CreateCatalogInput
        {
            Name = "Tokyo Mosaic"
        }, CancellationToken.None);

        Assert.That(first.Slug, Is.EqualTo("tokyo-mosaic"));
        Assert.That(second.Slug, Is.EqualTo("tokyo-mosaic-2"));
    }

    [Test]
    public async Task UploadImagesAsync_SavesImageAndAddsItToTheMosaic()
    {
        Catalog catalog = await _catalogService.CreateCatalogAsync(new CreateCatalogInput
        {
            Name = "Tokyo"
        }, CancellationToken.None);

        IReadOnlyList<CatalogImage> images = await _catalogService.UploadImagesAsync(catalog.Id,
                                                                                      new UploadGeoTiffInput
                                                                                      {
                                                                                          Files = [CreateFormFile(FileSystemEntries.Input3785FilePath)]
                                                                                      },
                                                                                      CancellationToken.None);
        CatalogImage image = images.Single();

        Catalog reloadedCatalog = await _catalogService.GetCatalogAsync(catalog.Id, CancellationToken.None)
                                                       ?? throw new AssertionException("Catalog should exist.");

        Assert.Multiple(() =>
        {
            Assert.That(reloadedCatalog.Images.Select(existingImage => existingImage.Id), Has.Member(image.Id));
            Assert.That(reloadedCatalog.Images.Single(existingImage => existingImage.Id == image.Id).SortOrder, Is.EqualTo(0));
            Assert.That(File.Exists(image.OriginalPath), Is.True);
            Assert.That(File.Exists(image.NormalizedPath), Is.True);
        });
    }

    [Test]
    public async Task UploadImagesAsync_DeletesFilesAndKeepsDatabaseCleanWhenProcessingFails()
    {
        Catalog catalog = await _catalogService.CreateCatalogAsync(new CreateCatalogInput
        {
            Name = "Broken upload"
        }, CancellationToken.None);

        Exception exception = Assert.ThrowsAsync(Is.InstanceOf<Exception>(), async () =>
            await _catalogService.UploadImagesAsync(catalog.Id,
                                                    new UploadGeoTiffInput
                                                    {
                                                        Files = [CreateFormFile(new byte[] { 1, 2, 3, 4 }, "broken.tif")]
                                                    },
                                                    CancellationToken.None))!;

        Catalog reloadedCatalog = await _catalogService.GetCatalogAsync(catalog.Id, CancellationToken.None)
                                                       ?? throw new AssertionException("Catalog should exist.");
        string catalogDirectory = _fileStorage.GetCatalogDirectory(catalog.Slug);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.Not.Empty);
            Assert.That(reloadedCatalog.Images, Is.Empty);
            Assert.That(Directory.GetDirectories(catalogDirectory), Is.Empty);
            Assert.That(Directory.GetFiles(catalogDirectory), Is.Empty);
        });
    }

    [Test]
    public async Task MoveImageAsync_OnlyAllowsImagesFromTheSameCatalog()
    {
        Catalog catalogA = new() { Name = "A", Slug = "a" };
        Catalog catalogB = new() { Name = "B", Slug = "b" };

        CatalogImage imageA = new()
        {
            Catalog = catalogA,
            OriginalFileName = "a.tif",
            OriginalPath = "a-original.tif",
            NormalizedPath = "a-normalized.tif"
        };
        CatalogImage imageB = new()
        {
            Catalog = catalogB,
            OriginalFileName = "b.tif",
            OriginalPath = "b-original.tif",
            NormalizedPath = "b-normalized.tif"
        };

        _dbContext.AddRange(catalogA, catalogB, imageA, imageB);
        await _dbContext.SaveChangesAsync();

        InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _catalogService.MoveImageAsync(catalogA.Id, imageB.Id, -1, CancellationToken.None))!;

        Assert.That(exception.Message, Does.Contain("does not belong"));
    }

    [Test]
    public async Task MoveImageAsync_SwapsImageOrderWithinTheCatalog()
    {
        Catalog catalog = new() { Name = "A", Slug = "a" };

        CatalogImage first = new()
        {
            Catalog = catalog,
            OriginalFileName = "a.tif",
            OriginalPath = "a-original.tif",
            NormalizedPath = "a-normalized.tif",
            SortOrder = 0
        };
        CatalogImage second = new()
        {
            Catalog = catalog,
            OriginalFileName = "b.tif",
            OriginalPath = "b-original.tif",
            NormalizedPath = "b-normalized.tif",
            SortOrder = 1
        };

        _dbContext.AddRange(catalog, first, second);
        await _dbContext.SaveChangesAsync();

        Catalog updatedCatalog = await _catalogService.MoveImageAsync(catalog.Id, second.Id, -1, CancellationToken.None);

        Assert.That(updatedCatalog.Images.OrderBy(image => image.SortOrder).Select(image => image.Id).ToArray(),
                    Is.EqualTo(new[] { second.Id, first.Id }));
    }

    [Test]
    public async Task DeleteCatalogAsync_RemovesCatalogImagesAndStorageDirectory()
    {
        Catalog catalog = await _catalogService.CreateCatalogAsync(new CreateCatalogInput
        {
            Name = "Tokyo"
        }, CancellationToken.None);

        string catalogDirectory = _fileStorage.GetCatalogDirectory(catalog.Slug);
        string imageDirectory = _fileStorage.GetImageDirectory(catalog.Slug, "image-1");
        string originalPath = Path.Combine(imageDirectory, "original.tif");
        string normalizedPath = Path.Combine(imageDirectory, "normalized_3857.tif");

        await File.WriteAllBytesAsync(originalPath, [1, 2, 3], CancellationToken.None);
        await File.WriteAllBytesAsync(normalizedPath, [4, 5, 6], CancellationToken.None);

        CatalogImage image = new()
        {
            CatalogId = catalog.Id,
            Catalog = catalog,
            StorageKey = "image-1",
            OriginalFileName = "tokyo.tif",
            OriginalPath = originalPath,
            NormalizedPath = normalizedPath
        };

        _dbContext.CatalogImages.Add(image);
        await _dbContext.SaveChangesAsync();

        await _catalogService.DeleteCatalogAsync(catalog.Id, CancellationToken.None);

        Catalog? deletedCatalog = await _catalogService.GetCatalogAsync(catalog.Id, CancellationToken.None);
        int imageCount = await _dbContext.CatalogImages.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(deletedCatalog, Is.Null);
            Assert.That(imageCount, Is.Zero);
            Assert.That(Directory.Exists(catalogDirectory), Is.False);
        });
    }

    private ServerDbContext CreateDbContext(string databaseFileName = "test.db", int timeoutSeconds = 30)
    {
        string connectionString = $"Data Source={Path.Combine(_tempRoot, databaseFileName)};Default Timeout={timeoutSeconds}";
        DbContextOptions<ServerDbContext> options = new DbContextOptionsBuilder<ServerDbContext>()
                                                   .UseSqlite(connectionString)
                                                   .Options;

        return new ServerDbContext(options);
    }

    private static IFormFile CreateFormFile(string path)
        => CreateFormFile(File.ReadAllBytes(path), Path.GetFileName(path));

    private static IFormFile CreateFormFile(byte[] content, string fileName)
    {
        MemoryStream stream = new(content);
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/tiff"
        };
    }
}
