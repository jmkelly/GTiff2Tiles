using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using GTiff2Tiles.Server.Options;
using Microsoft.Extensions.Options;

namespace GTiff2Tiles.Server.Services;

public sealed class S3Storage : IStorage, IDisposable
{
    private const string VsiS3Prefix = "/vsis3/";

    private readonly S3StorageOptions _options;
    private readonly IAmazonS3 _s3Client;
    private readonly bool _isHttp;

    public S3Storage(S3StorageOptions options)
    {
        _options = options;

        AmazonS3Config s3Config = new()
        {
            RegionEndpoint = string.IsNullOrWhiteSpace(_options.EndpointUrl)
                ? RegionEndpoint.GetBySystemName(_options.Region)
                : RegionEndpoint.USEast1
        };

        if (!string.IsNullOrWhiteSpace(_options.EndpointUrl))
        {
            s3Config.ServiceURL = _options.EndpointUrl;
            s3Config.ForcePathStyle = true;
            _isHttp = _options.EndpointUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            s3Config.UseHttp = _isHttp;
        }

        AWSCredentials credentials = !string.IsNullOrWhiteSpace(_options.AccessKeyId) &&
                                     !string.IsNullOrWhiteSpace(_options.SecretAccessKey)
            ? (AWSCredentials)new BasicAWSCredentials(_options.AccessKeyId, _options.SecretAccessKey)
            : new InstanceProfileAWSCredentials();

        _s3Client = new AmazonS3Client(credentials, s3Config);
    }

    public S3Storage(IOptions<S3StorageOptions> options) : this(options.Value)
    {
    }

    public string Provider => "S3";

    public static void ConfigureGdal(S3StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        OSGeo.GDAL.Gdal.SetConfigOption("AWS_ACCESS_KEY_ID", options.AccessKeyId);
        OSGeo.GDAL.Gdal.SetConfigOption("AWS_SECRET_ACCESS_KEY", options.SecretAccessKey);
        OSGeo.GDAL.Gdal.SetConfigOption("AWS_REGION", options.Region);
        OSGeo.GDAL.Gdal.SetConfigOption("GDAL_DISABLE_READDIR_ON_OPEN", "EMPTY_DIR");
        OSGeo.GDAL.Gdal.SetConfigOption("CPL_VSIL_CURL_ALLOWED_EXTENSIONS", ".tif,.tiff,.ovr");
        OSGeo.GDAL.Gdal.SetConfigOption("GDAL_HTTP_MULTIRANGE", "YES");
        OSGeo.GDAL.Gdal.SetConfigOption("GDAL_HTTP_MERGE_CONSECUTIVE_RANGES", "YES");
        OSGeo.GDAL.Gdal.SetConfigOption("GDAL_INGESTED_BYTES_AT_OPEN", "65536");
        OSGeo.GDAL.Gdal.SetConfigOption("VSI_CACHE", "TRUE");
        OSGeo.GDAL.Gdal.SetConfigOption("VSI_CACHE_SIZE", "1048576");

        if (!string.IsNullOrWhiteSpace(options.EndpointUrl))
        {
            string endpoint = options.EndpointUrl.TrimEnd('/');
            OSGeo.GDAL.Gdal.SetConfigOption("AWS_S3_ENDPOINT", endpoint);
            OSGeo.GDAL.Gdal.SetConfigOption("AWS_HTTPS",
                endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "YES" : "NO");
            OSGeo.GDAL.Gdal.SetConfigOption("AWS_VIRTUAL_HOSTING", "FALSE");
        }
        else
        {
            OSGeo.GDAL.Gdal.SetConfigOption("AWS_S3_ENDPOINT", null);
            OSGeo.GDAL.Gdal.SetConfigOption("AWS_HTTPS", null);
            OSGeo.GDAL.Gdal.SetConfigOption("AWS_VIRTUAL_HOSTING", null);
        }
    }

    public void ConfigureGdal() => ConfigureGdal(_options);

    public string GetOriginalRasterPath(string catalogSlug, string storageKey)
    {
        string s3Key = $"catalogs/{catalogSlug}/{storageKey}/original.tif";
        return $"{VsiS3Prefix}{_options.BucketName}/{s3Key}";
    }

    public string GetNormalizedRasterPath(string catalogSlug, string storageKey)
    {
        string s3Key = $"catalogs/{catalogSlug}/{storageKey}/normalized_3857.tif";
        return $"{VsiS3Prefix}{_options.BucketName}/{s3Key}";
    }

    public async Task SaveUploadAsync(Stream source, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string s3Key = ExtractS3Key(path);
        PutObjectRequest request = new()
        {
            BucketName = _options.BucketName,
            Key = s3Key,
            InputStream = source
        };

        if (!_isHttp)
        {
            request.DisablePayloadSigning = true;
        }

        await _s3Client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string s3Key = ExtractS3Key(path);
        GetObjectRequest request = new()
        {
            BucketName = _options.BucketName,
            Key = s3Key
        };

        GetObjectResponse response = await _s3Client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ResponseStream;
    }

    public async Task DeleteImageAsync(string catalogSlug, string storageKey, CancellationToken cancellationToken)
    {
        string baseKey = $"catalogs/{catalogSlug}/{storageKey}/";

        DeleteObjectsRequest request = new()
        {
            BucketName = _options.BucketName,
            Objects =
            [
                new KeyVersion { Key = $"{baseKey}original.tif" },
                new KeyVersion { Key = $"{baseKey}normalized_3857.tif" }
            ]
        };

        await _s3Client.DeleteObjectsAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCatalogAsync(string catalogSlug, CancellationToken cancellationToken)
    {
        string prefix = $"catalogs/{catalogSlug}/";

        ListObjectsV2Request listRequest = new()
        {
            BucketName = _options.BucketName,
            Prefix = prefix
        };

        ListObjectsV2Response listResponse;
        do
        {
            listResponse = await _s3Client.ListObjectsV2Async(listRequest, cancellationToken).ConfigureAwait(false);

            if (listResponse.S3Objects.Count == 0)
                break;

            DeleteObjectsRequest deleteRequest = new()
            {
                BucketName = _options.BucketName,
                Objects = listResponse.S3Objects.Select(obj => new KeyVersion { Key = obj.Key }).ToList()
            };

            await _s3Client.DeleteObjectsAsync(deleteRequest, cancellationToken).ConfigureAwait(false);

            listRequest.ContinuationToken = listResponse.NextContinuationToken;
        } while (listResponse.IsTruncated);
    }

    public async Task EnsureBucketExistsAsync(CancellationToken cancellationToken = default)
    {
        bool exists = await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, _options.BucketName)
            .ConfigureAwait(false);

        if (!exists)
        {
            PutBucketRequest request = new()
            {
                BucketName = _options.BucketName
            };

            await _s3Client.PutBucketAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose() => _s3Client.Dispose();

    private static string ExtractS3Key(string path)
    {
        if (!path.StartsWith(VsiS3Prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Path must start with '{VsiS3Prefix}'.", nameof(path));

        string withoutPrefix = path[VsiS3Prefix.Length..];
        int bucketEndIndex = withoutPrefix.IndexOf('/');
        if (bucketEndIndex < 0)
            throw new ArgumentException("Invalid VSI path format.", nameof(path));

        return withoutPrefix[(bucketEndIndex + 1)..];
    }
}
