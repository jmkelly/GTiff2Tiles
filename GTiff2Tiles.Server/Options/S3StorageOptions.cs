namespace GTiff2Tiles.Server.Options;

public sealed class S3StorageOptions
{
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string LocalCacheRoot { get; set; } = string.Empty;
}
