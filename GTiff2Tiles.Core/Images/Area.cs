using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Core.Tiles;

namespace GTiff2Tiles.Core.Images;

/// <summary>
/// Represents read/write area of an image.
/// </summary>
public class Area
{
    /// <summary>
    /// Area origin in pixels.
    /// </summary>
    public PixelCoordinate OriginCoordinate { get; }

    /// <summary>
    /// Area size.
    /// </summary>
    public Size Size { get; }

    /// <summary>
    /// Creates a new <see cref="Area"/>.
    /// </summary>
    public Area(PixelCoordinate originCoordinate, Size size)
    {
        ArgumentNullException.ThrowIfNull(originCoordinate);
        ArgumentNullException.ThrowIfNull(size);

        OriginCoordinate = originCoordinate;
        Size = size;
    }

    /// <summary>
    /// Calculates source and destination areas for one tile.
    /// </summary>
    public static (Area readArea, Area writeArea)? GetAreas(
        GeoCoordinate imageMinCoordinate,
        GeoCoordinate imageMaxCoordinate,
        Size imageSize,
        GeoCoordinate tileMinCoordinate,
        GeoCoordinate tileMaxCoordinate,
        Size tileSize)
    {
        if (!RasterTileLayoutCalculator.TryGetLayout(
                imageMinCoordinate,
                imageMaxCoordinate,
                imageSize,
                tileMinCoordinate,
                tileMaxCoordinate,
                tileSize,
                out RasterTileLayout layout))
        {
            return null;
        }

        Area readArea = new(new PixelCoordinate(layout.ReadX, layout.ReadY), new Size(layout.ReadWidth, layout.ReadHeight));
        Area writeArea = new(new PixelCoordinate(layout.WriteX, layout.WriteY), new Size(layout.WriteWidth, layout.WriteHeight));

        return (readArea, writeArea);
    }

    /// <summary>
    /// Calculates source and destination areas for one tile.
    /// </summary>
    public static (Area readArea, Area writeArea)? GetAreas(IGeoTiff image, ITile tile)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(tile);

        return GetAreas(
            image.MinCoordinate,
            image.MaxCoordinate,
            image.Size,
            tile.MinCoordinate,
            tile.MaxCoordinate,
            tile.Size);
    }
}
