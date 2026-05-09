using System.Globalization;
using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.Tiles;

namespace GTiff2Tiles.Console;

public static class SlippyMapHtmlWriter
{
    public const string DefaultFileName = "slippy-map.html";

    public static void Write(string outputDirectoryPath, GeoCoordinate minCoordinate, GeoCoordinate maxCoordinate,
                             CoordinateSystem coordinateSystem, int minZ, int maxZ, bool tmsCompatible,
                             TileExtension tileExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectoryPath);
        ArgumentNullException.ThrowIfNull(minCoordinate);
        ArgumentNullException.ThrowIfNull(maxCoordinate);

        EnsureSupported(coordinateSystem, tmsCompatible);

        string htmlPath = Path.Combine(outputDirectoryPath, DefaultFileName);
        string tileExtensionString = Tile.GetExtensionString(tileExtension).TrimStart('.');
        string html = BuildHtml(minCoordinate, maxCoordinate, minZ, maxZ, tileExtensionString);

        File.WriteAllText(htmlPath, html);
    }

    public static void EnsureSupported(CoordinateSystem coordinateSystem, bool tmsCompatible)
    {
        if (coordinateSystem != CoordinateSystem.Epsg3857)
            throw new InvalidOperationException(Localization.Strings.SlippyMapHtmlRequiresMercator);

        if (tmsCompatible)
            throw new InvalidOperationException(Localization.Strings.SlippyMapHtmlRequiresXyz);
    }

    public static string BuildHtml(GeoCoordinate minCoordinate, GeoCoordinate maxCoordinate, int minZ, int maxZ,
                                   string tileExtension)
    {
        ArgumentNullException.ThrowIfNull(minCoordinate);
        ArgumentNullException.ThrowIfNull(maxCoordinate);
        ArgumentException.ThrowIfNullOrWhiteSpace(tileExtension);

        GeodeticCoordinate southWest = ToGeodetic(minCoordinate, true);
        GeodeticCoordinate northEast = ToGeodetic(maxCoordinate, false);

        string south = southWest.Latitude.ToString("G17", CultureInfo.InvariantCulture);
        string west = southWest.Longitude.ToString("G17", CultureInfo.InvariantCulture);
        string north = northEast.Latitude.ToString("G17", CultureInfo.InvariantCulture);
        string east = northEast.Longitude.ToString("G17", CultureInfo.InvariantCulture);

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <meta name=""referrer"" content=""strict-origin-when-cross-origin"">
    <title>GTiff2Tiles viewer</title>
    <link rel=""stylesheet"" href=""https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"" integrity=""sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY="" crossorigin="""">
    <style>
        html, body {{
            height: 100%;
            margin: 0;
            font-family: sans-serif;
        }}

        body {{
            display: flex;
            flex-direction: column;
        }}

        .toolbar {{
            display: flex;
            flex-wrap: wrap;
            gap: 0.75rem;
            align-items: center;
            padding: 0.75rem 1rem;
            background: #ffffff;
            border-bottom: 1px solid #d0d7de;
        }}

        .toolbar label {{
            font-weight: 600;
        }}

        .toolbar input[type=range] {{
            width: 16rem;
        }}

        .toolbar .value,
        .toolbar .meta {{
            color: #57606a;
            font-size: 0.95rem;
        }}

        #map {{
            flex: 1;
            min-height: 24rem;
        }}
    </style>
</head>
<body>
    <div class=""toolbar"">
        <label for=""overlay-opacity"">Overlay opacity</label>
        <input id=""overlay-opacity"" type=""range"" min=""0"" max=""1"" step=""0.05"" value=""0.7"">
        <span id=""overlay-opacity-value"" class=""value"">70%</span>
        <span class=""meta"">Zoom {minZ}-{maxZ}</span>
    </div>
    <div id=""map""></div>

    <script src=""https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"" integrity=""sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo="" crossorigin=""""></script>
    <script>
        const bounds = L.latLngBounds([
            [{south}, {west}],
            [{north}, {east}]
        ]);

        const map = L.map('map', {{
            minZoom: 0,
            maxZoom: Math.max(19, {maxZ})
        }});

        const osmLayer = L.tileLayer('https://tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png', {{
            attribution: '&copy; <a href=""https://www.openstreetmap.org/copyright"">OpenStreetMap</a> contributors',
            maxZoom: 19,
            referrerPolicy: 'strict-origin-when-cross-origin'
        }}).addTo(map);

        const overlay = L.tileLayer('./{{z}}/{{x}}/{{y}}.{tileExtension}', {{
            minZoom: {minZ},
            maxZoom: {maxZ},
            opacity: 0.7,
            tms: false,
            bounds: bounds,
            errorTileUrl: ''
        }}).addTo(map);

        map.fitBounds(bounds, {{ padding: [20, 20], maxZoom: {maxZ} }});

        const opacityInput = document.getElementById('overlay-opacity');
        const opacityValue = document.getElementById('overlay-opacity-value');

        opacityInput.addEventListener('input', event => {{
            const value = Number(event.target.value);
            overlay.setOpacity(value);
            opacityValue.textContent = `${{Math.round(value * 100)}}%`;
        }});

        L.control.layers(
            {{ 'OpenStreetMap': osmLayer }},
            {{ 'Generated tiles': overlay }},
            {{ collapsed: false }}
        ).addTo(map);
    </script>
</body>
</html>
";
    }

    private static GeodeticCoordinate ToGeodetic(GeoCoordinate coordinate, bool isMinimum)
    {
        return coordinate switch
        {
            GeodeticCoordinate geodeticCoordinate => geodeticCoordinate,
            MercatorCoordinate mercatorCoordinate => mercatorCoordinate.ToGeodeticCoordinate(),
            _ => throw new InvalidOperationException(isMinimum
                ? Localization.Strings.SlippyMapHtmlUnsupportedMinCoordinate
                : Localization.Strings.SlippyMapHtmlUnsupportedMaxCoordinate)
        };
    }
}
