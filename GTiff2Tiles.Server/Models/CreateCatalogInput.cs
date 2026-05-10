using System.ComponentModel.DataAnnotations;

namespace GTiff2Tiles.Server.Models;

public sealed class CreateCatalogInput
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    [StringLength(128)]
    public string? Slug { get; set; }

    [StringLength(512)]
    public string? Description { get; set; }
}
