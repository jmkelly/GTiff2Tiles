using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Options;
using Microsoft.Extensions.Options;

namespace GTiff2Tiles.Server.Services;

public sealed class StorageFactory : IStorageFactory
{
    private readonly S3StorageOptions _s3Options;
    private readonly LocalStorageOptions _localOptions;

    public StorageFactory(IOptions<S3StorageOptions> s3Options, IOptions<LocalStorageOptions> localOptions)
    {
        _s3Options = s3Options.Value;
        _localOptions = localOptions.Value;
    }

    public IStorage GetStorage(string provider, CatalogStorageConfig? catalogConfig = null)
    {
        if (string.Equals(provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            S3StorageOptions merged = MergeWithGlobal(_s3Options, catalogConfig?.S3);
            return new S3Storage(merged);
        }
        else
        {
            LocalStorageOptions merged = MergeWithGlobal(_localOptions, catalogConfig?.Local);
            return new LocalFileStorage(merged);
        }
    }

    private static S3StorageOptions MergeWithGlobal(S3StorageOptions global, S3StorageConfig? catalog)
    {
        if (catalog is null) return global;

        return new S3StorageOptions
        {
            BucketName = catalog.BucketName ?? global.BucketName,
            Region = catalog.Region ?? global.Region,
            AccessKeyId = catalog.AccessKeyId ?? global.AccessKeyId,
            SecretAccessKey = catalog.SecretAccessKey ?? global.SecretAccessKey,
            EndpointUrl = catalog.EndpointUrl ?? global.EndpointUrl,
            LocalCacheRoot = catalog.LocalCacheRoot ?? global.LocalCacheRoot
        };
    }

    private static LocalStorageOptions MergeWithGlobal(LocalStorageOptions global, LocalStorageConfig? catalog)
    {
        if (catalog is null) return global;

        return new LocalStorageOptions
        {
            RootPath = catalog.RootPath ?? global.RootPath,
            DatabasePath = global.DatabasePath
        };
    }
}
