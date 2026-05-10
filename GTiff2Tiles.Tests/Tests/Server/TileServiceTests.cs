using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Options;
using GTiff2Tiles.Server.Services;
using GTiff2Tiles.Tests.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace GTiff2Tiles.Tests.Tests.Server;

[TestFixture]
public sealed class TileServiceTests
{
    private string _tempRoot = null!;
    private ServerDbContext _dbContext = null!;
    private LocalFileStorage _fileStorage = null!;
    private TileService _tileService = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "gtiff2tiles-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        DbContextOptions<ServerDbContext> options = new DbContextOptionsBuilder<ServerDbContext>()
                                                   .UseSqlite($"Data Source={Path.Combine(_tempRoot, "test.db")}")
                                                   .Options;

        _dbContext = new ServerDbContext(options);
        _dbContext.Database.EnsureCreated();

        _fileStorage = new LocalFileStorage(Options.Create(new LocalStorageOptions
        {
            RootPath = _tempRoot,
            DatabasePath = Path.Combine(_tempRoot, "test.db")
        }));
        _fileStorage.EnsureStorageLayout();

        _tileService = new TileService(_dbContext, _fileStorage);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Test]
    public async Task TryGetTileAsync_RendersAndReusesCachedTileForActiveImage()
    {
        CatalogImage image = await CreateCatalogWithActiveImageAsync();

        TileResponse firstTile = await _tileService.TryGetTileAsync("tokyo",
                                                                    Locations.TokyoMercatorNtmsNumber.Z,
                                                                    Locations.TokyoMercatorNtmsNumber.X,
                                                                    Locations.TokyoMercatorNtmsNumber.Y,
                                                                    CancellationToken.None);

        Assert.That(firstTile, Is.Not.Null);
        Assert.That(firstTile.Content.Length, Is.GreaterThan(355));
        Assert.That(firstTile.ContentType, Is.EqualTo("image/png"));

        string tilePath = _fileStorage.GetTileCachePath("tokyo",
                                                        image.StorageKey,
                                                        Locations.TokyoMercatorNtmsNumber.Z,
                                                        Locations.TokyoMercatorNtmsNumber.X,
                                                        Locations.TokyoMercatorNtmsNumber.Y);

        Assert.That(File.Exists(tilePath), Is.True);

        DateTime marker = new(2024, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(tilePath, marker);

        TileResponse secondTile = await _tileService.TryGetTileAsync("tokyo",
                                                                     Locations.TokyoMercatorNtmsNumber.Z,
                                                                     Locations.TokyoMercatorNtmsNumber.X,
                                                                     Locations.TokyoMercatorNtmsNumber.Y,
                                                                     CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(secondTile, Is.Not.Null);
            Assert.That(secondTile.Content, Is.EqualTo(firstTile.Content));
            Assert.That(File.GetLastWriteTimeUtc(tilePath), Is.EqualTo(marker));
        });
    }

    [Test]
    public void GetTileCachePath_DoesNotCreateDirectories()
    {
        string tilePath = _fileStorage.GetTileCachePath("tokyo", Guid.NewGuid().ToString("N"), 12, 345, 678);

        Assert.Multiple(() =>
        {
            Assert.That(tilePath, Does.EndWith(Path.Combine("12", "345", "678.png")));
            Assert.That(Directory.Exists(Path.GetDirectoryName(tilePath)!), Is.False);
        });
    }

    private async Task<CatalogImage> CreateCatalogWithActiveImageAsync()
    {
        Catalog catalog = new()
        {
            Name = "Tokyo",
            Slug = "tokyo"
        };

        CatalogImage image = new()
        {
            Catalog = catalog,
            StorageKey = Guid.NewGuid().ToString("N"),
            OriginalFileName = "Input3785.tif",
            OriginalPath = FileSystemEntries.Input3785FilePath,
            NormalizedPath = FileSystemEntries.Input3785FilePath,
            Width = 512,
            Height = 512,
            MinX = 0,
            MinY = 0,
            MaxX = 0,
            MaxY = 0
        };

        _dbContext.AddRange(catalog, image);
        await _dbContext.SaveChangesAsync();

        catalog.ActiveImageId = image.Id;
        await _dbContext.SaveChangesAsync();

        return image;
    }
}
