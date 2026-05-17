using GTiff2Tiles.Server.Options;

namespace GTiff2Tiles.Server.Services;

public sealed record PathResolutionResult(string Path, S3StorageOptions? S3Options);
