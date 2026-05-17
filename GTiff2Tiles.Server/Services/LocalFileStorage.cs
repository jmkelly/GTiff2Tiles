using GTiff2Tiles.Server.Options;
using Microsoft.Extensions.Options;

namespace GTiff2Tiles.Server.Services;

public sealed class LocalFileStorage : IStorage
{
    private const string CatalogsDirectoryName = "catalogs";

    private readonly LocalStorageOptions _options;

    public LocalFileStorage(LocalStorageOptions options)
    {
        _options = options;
    }

    public LocalFileStorage(IOptions<LocalStorageOptions> options) : this(options.Value)
    {
    }

    public string RootPath => _options.RootPath;

    public string Provider => "Local";

    public void ConfigureGdal()
    {
    }

    public void EnsureStorageLayout()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(Path.Combine(RootPath, CatalogsDirectoryName));
    }

    public string GetCatalogDirectory(string catalogSlug)
    {
        string path = Path.Combine(RootPath, CatalogsDirectoryName, catalogSlug);
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetImageDirectory(string catalogSlug, string storageKey)
    {
        string path = Path.Combine(GetCatalogDirectory(catalogSlug), storageKey);
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetOriginalRasterPath(string catalogSlug, string storageKey)
        => Path.Combine(GetImageDirectory(catalogSlug, storageKey), "original.tif");

    public string GetNormalizedRasterPath(string catalogSlug, string storageKey)
        => Path.Combine(GetImageDirectory(catalogSlug, storageKey), "normalized_3857.tif");

    public async Task SaveUploadAsync(Stream source, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using FileStream fileStream = File.Create(path);
        await source.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task DeleteImageAsync(string catalogSlug, string storageKey, CancellationToken cancellationToken)
    {
        string imageDirectory = GetImageDirectory(catalogSlug, storageKey);
        TryDeleteDirectory(imageDirectory);
        return Task.CompletedTask;
    }

    public Task DeleteCatalogAsync(string catalogSlug, CancellationToken cancellationToken)
    {
        string catalogDirectory = GetCatalogDirectory(catalogSlug);
        TryDeleteDirectory(catalogDirectory);
        return Task.CompletedTask;
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
