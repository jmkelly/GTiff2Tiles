using GTiff2Tiles.Server.Options;
using Microsoft.Extensions.Options;

namespace GTiff2Tiles.Server.Services;

public sealed class LocalFileStorage(IOptions<LocalStorageOptions> options)
{
    private const string CatalogsDirectoryName = "catalogs";

    private readonly LocalStorageOptions _options = options.Value;

    public string RootPath => _options.RootPath;

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

    public async Task SaveUploadAsync(Stream source, string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        await using FileStream fileStream = File.Create(destinationPath);
        await source.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }
}
