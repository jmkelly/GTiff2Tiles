using Microsoft.AspNetCore.Http;

namespace GTiff2Tiles.Server.Models;

public sealed class UploadGeoTiffInput
{
    public List<IFormFile> Files { get; set; } = [];
}
