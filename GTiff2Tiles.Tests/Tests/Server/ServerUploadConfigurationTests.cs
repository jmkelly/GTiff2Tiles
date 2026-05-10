using GTiff2Tiles.Server.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace GTiff2Tiles.Tests.Tests.Server;

[TestFixture]
public sealed class ServerUploadConfigurationTests
{
    [Test]
    public void ConfigureLargeGeoTiffUploads_SetsMultipartAndRequestBodyLimits()
    {
        ServiceCollection services = [];

        services.ConfigureLargeGeoTiffUploads(123456789);

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        FormOptions formOptions = serviceProvider.GetRequiredService<IOptions<FormOptions>>().Value;
        KestrelServerOptions kestrelOptions = serviceProvider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(formOptions.MultipartBodyLengthLimit, Is.EqualTo(123456789));
            Assert.That(kestrelOptions.Limits.MaxRequestBodySize, Is.EqualTo(123456789));
        });
    }

    [Test]
    public void ConfigureLargeGeoTiffUploads_RejectsNonPositiveLimits()
    {
        ServiceCollection services = [];

        Assert.That(() => services.ConfigureLargeGeoTiffUploads(0),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
    }
}
