using System.ComponentModel.DataAnnotations;

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

    public int? ActiveImageId { get; set; }

    public CatalogImage? ActiveImage { get; set; }

    public ICollection<CatalogImage> Images { get; set; } = [];
}
