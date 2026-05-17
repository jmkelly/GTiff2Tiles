using System.ComponentModel.DataAnnotations;

namespace GTiff2Tiles.Server.Models;

public sealed class StorageSettingsInput
{
    [Required]
    [StringLength(32)]
    public string Provider { get; set; } = "Local";

    public LocalStorageInput Local { get; set; } = new();
    public S3StorageInput S3 { get; set; } = new();
}

public sealed class LocalStorageInput
{
    [StringLength(1024)]
    public string RootPath { get; set; } = string.Empty;
}

public sealed class S3StorageInput
{
    [StringLength(512)]
    public string BucketName { get; set; } = string.Empty;

    [StringLength(64)]
    public string Region { get; set; } = "us-east-1";

    [StringLength(256)]
    public string AccessKeyId { get; set; } = string.Empty;

    [StringLength(256)]
    public string SecretAccessKey { get; set; } = string.Empty;

    [StringLength(512)]
    public string EndpointUrl { get; set; } = string.Empty;

    [StringLength(1024)]
    public string LocalCacheRoot { get; set; } = string.Empty;
}
