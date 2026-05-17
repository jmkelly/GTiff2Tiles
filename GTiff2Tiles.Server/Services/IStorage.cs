namespace GTiff2Tiles.Server.Services;

public interface IStorage
{
    string GetOriginalRasterPath(string catalogSlug, string storageKey);
    string GetNormalizedRasterPath(string catalogSlug, string storageKey);
    Task SaveUploadAsync(Stream source, string path, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
    Task DeleteImageAsync(string catalogSlug, string storageKey, CancellationToken ct);
    Task DeleteCatalogAsync(string catalogSlug, CancellationToken ct);
    string Provider { get; }
    void ConfigureGdal();
}
