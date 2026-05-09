using GTiff2Tiles.Console;
using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Tests.Constants;
using NUnit.Framework;

namespace GTiff2Tiles.Tests.Tests.ConsoleApp;

[TestFixture]
public sealed class SlippyMapHtmlWriterTests
{
    [Test]
    public void EnsureSupportedMercatorXyzDoesNotThrow() =>
        Assert.DoesNotThrow(() => SlippyMapHtmlWriter.EnsureSupported(CoordinateSystem.Epsg3857, false));

    [Test]
    public void EnsureSupportedGeodeticThrows() =>
        Assert.Throws<InvalidOperationException>(() => SlippyMapHtmlWriter.EnsureSupported(CoordinateSystem.Epsg4326, false));

    [Test]
    public void EnsureSupportedTmsThrows() =>
        Assert.Throws<InvalidOperationException>(() => SlippyMapHtmlWriter.EnsureSupported(CoordinateSystem.Epsg3857, true));

    [Test]
    public void BuildHtmlContainsRelativeOverlayAndOpacitySlider()
    {
        MercatorCoordinate minCoordinate = new(15556898.732197443, 4247491.006264816);
        MercatorCoordinate maxCoordinate = new(15567583.19555743, 4257812.3937404491);

        string html = SlippyMapHtmlWriter.BuildHtml(minCoordinate, maxCoordinate, 0, 11, "png");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<meta name=\"referrer\" content=\"strict-origin-when-cross-origin\">"));
            Assert.That(html, Does.Contain("https://tile.openstreetmap.org/{z}/{x}/{y}.png"));
            Assert.That(html, Does.Contain("./{z}/{x}/{y}.png"));
            Assert.That(html, Does.Contain("overlay-opacity"));
            Assert.That(html, Does.Contain("overlay.setOpacity(value)"));
            Assert.That(html, Does.Contain("map.fitBounds(bounds, { padding: [20, 20], maxZoom: 11 })"));
            Assert.That(html, Does.Contain("OpenStreetMap"));
            Assert.That(html, Does.Contain("referrerPolicy: 'strict-origin-when-cross-origin'"));
        });
    }

    [Test]
    public void WriteCreatesHtmlFile()
    {
        string outputDirectoryPath = Path.Combine(FileSystemEntries.OutputDirectoryPath,
                                                  $"{Guid.NewGuid():N}_slippymap_html");
        Directory.CreateDirectory(outputDirectoryPath);

        try
        {
            MercatorCoordinate minCoordinate = new(15556898.732197443, 4247491.006264816);
            MercatorCoordinate maxCoordinate = new(15567583.19555743, 4257812.3937404491);

            SlippyMapHtmlWriter.Write(outputDirectoryPath, minCoordinate, maxCoordinate,
                                      CoordinateSystem.Epsg3857, 0, 11, false, TileExtension.Png);

            string htmlPath = Path.Combine(outputDirectoryPath, FileSystemEntries.SlippyMapHtmlFileName);

            Assert.That(File.Exists(htmlPath), Is.True);
            Assert.That(File.ReadAllText(htmlPath), Does.Contain("Generated tiles"));
        }
        finally
        {
            if (Directory.Exists(outputDirectoryPath))
                Directory.Delete(outputDirectoryPath, true);
        }
    }
}
