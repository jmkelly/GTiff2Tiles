using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Options;
using GTiff2Tiles.Server.Services;
using GTiff2Tiles.Tests.Constants;
using Microsoft.AspNetCore.Http;
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
        _dbContext.Dispose();
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
    public async Task UploadImageAsync_SavesImageAndSetsItActiveInOneOperation()
    {
        Catalog catalog = await _catalogService.CreateCatalogAsync(new CreateCatalogInput
        {
            Name = "Tokyo"
        }, CancellationToken.None);

        CatalogImage image = await _catalogService.UploadImageAsync(catalog.Id,
                                                                    new UploadGeoTiffInput
                                                                    {
                                                                        File = CreateFormFile(FileSystemEntries.Input3785FilePath)
                                                                    },
                                                                    CancellationToken.None);

        Catalog reloadedCatalog = await _catalogService.GetCatalogAsync(catalog.Id, CancellationToken.None)
                                                       ?? throw new AssertionException("Catalog should exist.");

        Assert.Multiple(() =>
        {
            Assert.That(reloadedCatalog.ActiveImageId, Is.EqualTo(image.Id));
            Assert.That(reloadedCatalog.Images.Select(existingImage => existingImage.Id), Has.Member(image.Id));
            Assert.That(File.Exists(image.OriginalPath), Is.True);
            Assert.That(File.Exists(image.NormalizedPath), Is.True);
        });
    }

    [Test]
    public async Task UploadImageAsync_DeletesFilesAndKeepsDatabaseCleanWhenProcessingFails()
    {
        Catalog catalog = await _catalogService.CreateCatalogAsync(new CreateCatalogInput
        {
            Name = "Broken upload"
        }, CancellationToken.None);

        Exception exception;
        try
        {
            await _catalogService.UploadImageAsync(catalog.Id,
                                                   new UploadGeoTiffInput
                                                   {
                                                       File = CreateFormFile(new byte[] { 1, 2, 3, 4 }, "broken.tif")
                                                   },
                                                   CancellationToken.None);
            throw new AssertionException("Upload should fail for an invalid GeoTIFF.");
        }
        catch (Exception caughtException)
        {
            exception = caughtException;
        }

        Catalog reloadedCatalog = await _catalogService.GetCatalogAsync(catalog.Id, CancellationToken.None)
                                                       ?? throw new AssertionException("Catalog should exist.");
        string catalogDirectory = _fileStorage.GetCatalogDirectory(catalog.Slug);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.Not.Empty);
            Assert.That(reloadedCatalog.ActiveImageId, Is.Null);
            Assert.That(reloadedCatalog.Images, Is.Empty);
            Assert.That(Directory.GetDirectories(catalogDirectory), Is.Empty);
            Assert.That(Directory.GetFiles(catalogDirectory), Is.Empty);
        });
    }

    [Test]
    public async Task SetActiveImageAsync_OnlyAllowsImagesFromTheSameCatalog()
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
            await _catalogService.SetActiveImageAsync(catalogA.Id, imageB.Id, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("does not belong"));
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
