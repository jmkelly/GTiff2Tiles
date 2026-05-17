using System.ComponentModel.DataAnnotations;

namespace GTiff2Tiles.Server.Models;

public sealed class StoragePresetInput
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(32)]
    public string Provider { get; set; } = "Local";

    [StringLength(1024)]
    public string? LocalRootPath { get; set; }

    [StringLength(512)]
    public string? S3BucketName { get; set; }

    [StringLength(64)]
    public string? S3Region { get; set; }

    [StringLength(256)]
    public string? S3AccessKeyId { get; set; }

    [StringLength(256)]
    public string? S3SecretAccessKey { get; set; }

    [StringLength(512)]
    public string? S3EndpointUrl { get; set; }

    [StringLength(1024)]
    public string? S3LocalCacheRoot { get; set; }
}
