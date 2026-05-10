using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace GTiff2Tiles.Server.Services;

public static class ServerUploadConfiguration
{
    public const long DefaultMaxRequestBodySizeBytes = 10L * 1024 * 1024 * 1024;

    public static IServiceCollection ConfigureLargeGeoTiffUploads(this IServiceCollection services,
                                                                  long maxRequestBodySizeBytes = DefaultMaxRequestBodySizeBytes)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (maxRequestBodySizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRequestBodySizeBytes), "Upload size limit must be positive.");

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = maxRequestBodySizeBytes;
        });

        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = maxRequestBodySizeBytes;
        });

        return services;
    }
}
