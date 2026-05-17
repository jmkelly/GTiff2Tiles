using System.ComponentModel.DataAnnotations;

namespace GTiff2Tiles.Server.Models;

public sealed class UpdateCatalogInput
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(512)]
    public string? Description { get; set; }

    [Required]
    [StringLength(32)]
    public string StorageProvider { get; set; } = "Local";

    public CatalogStorageConfig? StorageConfig { get; set; }
}
