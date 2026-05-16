using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Options;
using GTiff2Tiles.Server.Services;
using GTiff2Tiles.Tests.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Runtime.CompilerServices;

namespace GTiff2Tiles.Tests.Tests.Server;

[TestFixture]
public sealed class TileServiceTests
{
    private static readonly string Input3785Path = GetInput3785Path();

    private string _tempRoot = null!;
    private ServerDbContext _dbContext = null!;
    private TileRendererCache _tileRendererCache = null!;
    private TileService _tileService = null!;

    private static string GetInput3785Path([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!,
                                      "..",
                                      "..",
                                      "..",
                                      "Examples",
                                      "Input",
                                      "Input3785.tif"));

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

        LocalFileStorage fileStorage = new(Options.Create(new LocalStorageOptions
        {
            RootPath = _tempRoot,
            DatabasePath = Path.Combine(_tempRoot, "test.db")
        }));
        fileStorage.EnsureStorageLayout();

        _tileRendererCache = new TileRendererCache();
        _tileService = new TileService(_dbContext, _tileRendererCache);
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
        _tileRendererCache.Dispose();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Test]
    public async Task TryOpenTileStreamAsync_ReturnsPngStreamForCatalogMosaic()
    {
        await CreateCatalogWithImageAsync();

        await using Stream tileStream = await _tileService.TryOpenTileStreamAsync("tokyo",
                                                                                   Locations.TokyoMercatorNtmsNumber.Z,
                                                                                   Locations.TokyoMercatorNtmsNumber.X,
                                                                                   Locations.TokyoMercatorNtmsNumber.Y,
                                                                                   CancellationToken.None)
                                        ?? throw new AssertionException("Expected tile stream for catalog image mosaic.");
        using MemoryStream content = new();
        await tileStream.CopyToAsync(content);

        Assert.That(content.Length, Is.GreaterThan(355));
    }

    [Test]
    public async Task TryOpenTileStreamAsync_ReturnsNullWhenCatalogIsMissing()
    {
        Stream tileStream = await _tileService.TryOpenTileStreamAsync("missing", 12, 345, 678, CancellationToken.None);

        Assert.That(tileStream, Is.Null);
    }

    private async Task<CatalogImage> CreateCatalogWithImageAsync()
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
            OriginalPath = Input3785Path,
            NormalizedPath = Input3785Path,
            SortOrder = 0,
            Width = 512,
            Height = 512,
            MinX = 0,
            MinY = 0,
            MaxX = 0,
            MaxY = 0
        };

        _dbContext.AddRange(catalog, image);
        await _dbContext.SaveChangesAsync();

        return image;
    }
}
