namespace GTiff2Tiles.Server.Models;

public sealed class CatalogStorageConfig
{
    public LocalStorageConfig? Local { get; set; }
    public S3StorageConfig? S3 { get; set; }
}

public sealed class LocalStorageConfig
{
    public string? RootPath { get; set; }
}

public sealed class S3StorageConfig
{
    public string? BucketName { get; set; }
    public string? Region { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? EndpointUrl { get; set; }
    public string? LocalCacheRoot { get; set; }
}
