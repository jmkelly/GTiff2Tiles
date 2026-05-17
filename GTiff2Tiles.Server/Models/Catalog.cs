using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GTiff2Tiles.Server.Models;

public sealed class Catalog
{
    public int Id { get; set; }

    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(512)]
    public string? Description { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    [Required]
    [StringLength(32)]
    public string StorageProvider { get; set; } = "Local";

    public CatalogStorageConfig? StorageConfig { get; set; }

    [NotMapped]
    public int ImageCount { get; set; }

    public ICollection<CatalogImage> Images { get; set; } = [];
}
