using System.ComponentModel.DataAnnotations;

namespace GTiff2Tiles.Server.Models;

public sealed class AppSetting
{
    [Key]
    [StringLength(128)]
    public string Key { get; set; } = string.Empty;

    [StringLength(2048)]
    public string Value { get; set; } = string.Empty;
}
