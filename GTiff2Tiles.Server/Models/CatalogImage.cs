using System.ComponentModel.DataAnnotations;

namespace GTiff2Tiles.Server.Models;

public sealed class CatalogImage
{
    public int Id { get; set; }

    public int CatalogId { get; set; }

    public Catalog Catalog { get; set; } = null!;

    [Required]
    [StringLength(64)]
    public string StorageKey { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    [StringLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [StringLength(128)]
    public string? ContentType { get; set; }

    [Required]
    [StringLength(2048)]
    public string OriginalPath { get; set; } = string.Empty;

    [Required]
    [StringLength(2048)]
    public string NormalizedPath { get; set; } = string.Empty;

    public long OriginalFileSizeBytes { get; set; }

    public DateTimeOffset UploadedUtc { get; set; } = DateTimeOffset.UtcNow;

    public int SortOrder { get; set; }

    [Required]
    [StringLength(32)]
    public string CoordinateSystem { get; set; } = "EPSG:3857";

    public int Width { get; set; }

    public int Height { get; set; }

    public double MinX { get; set; }

    public double MinY { get; set; }

    public double MaxX { get; set; }

    public double MaxY { get; set; }

    [Required]
    [StringLength(32)]
    public string StorageProvider { get; set; } = "Local";
}
