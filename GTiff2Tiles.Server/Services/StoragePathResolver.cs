using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Options;
using Microsoft.Extensions.Options;

namespace GTiff2Tiles.Server.Services;

public sealed class StoragePathResolver
{
    private readonly S3StorageOptions _globalS3Options;

    public StoragePathResolver(IOptions<S3StorageOptions> s3Options)
    {
        _globalS3Options = s3Options.Value;
    }

    public Task<PathResolutionResult> ResolvePathAsync(
        string normalizedPath,
        string provider,
        string catalogSlug,
        CatalogStorageConfig? catalogConfig)
    {
        if (!string.Equals(provider, "S3", StringComparison.OrdinalIgnoreCase) || catalogConfig?.S3 is null)
            return Task.FromResult(new PathResolutionResult(normalizedPath, null));

        S3StorageOptions mergedOptions = MergeS3Options(catalogConfig);

        return Task.FromResult(new PathResolutionResult(normalizedPath, mergedOptions));
    }

    private S3StorageOptions MergeS3Options(CatalogStorageConfig catalogConfig)
    {
        S3StorageConfig? s3 = catalogConfig.S3;
        if (s3 is null)
            return _globalS3Options;

        return new S3StorageOptions
        {
            BucketName = s3.BucketName ?? _globalS3Options.BucketName,
            Region = s3.Region ?? _globalS3Options.Region,
            AccessKeyId = s3.AccessKeyId ?? _globalS3Options.AccessKeyId,
            SecretAccessKey = s3.SecretAccessKey ?? _globalS3Options.SecretAccessKey,
            EndpointUrl = s3.EndpointUrl ?? _globalS3Options.EndpointUrl,
            LocalCacheRoot = s3.LocalCacheRoot ?? _globalS3Options.LocalCacheRoot
        };
    }
}
