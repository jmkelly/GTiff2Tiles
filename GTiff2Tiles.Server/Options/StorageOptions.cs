namespace GTiff2Tiles.Server.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "Local";
    public LocalStorageOptions Local { get; set; } = new();
    public S3StorageOptions S3 { get; set; } = new();
}
