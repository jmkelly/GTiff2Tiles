using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Core.Localization;
using GTiff2Tiles.Core.Tiles;

namespace GTiff2Tiles.Core.Images;

/// <summary>
/// Primitive read/write layout for rendering one raster tile.
/// </summary>
public readonly record struct RasterTileLayout(
    double ReadX,
    double ReadY,
    int ReadWidth,
    int ReadHeight,
    double WriteX,
    double WriteY,
    int WriteWidth,
    int WriteHeight)
{
    /// <summary>
    /// Integer crop left edge.
    /// </summary>
    public int ReadLeft => (int)Math.Floor(ReadX);

    /// <summary>
    /// Integer crop top edge.
    /// </summary>
    public int ReadTop => (int)Math.Floor(ReadY);

    /// <summary>
    /// Integer write left edge.
    /// </summary>
    public int WriteLeft => (int)Math.Floor(WriteX);

    /// <summary>
    /// Integer write top edge.
    /// </summary>
    public int WriteTop => (int)Math.Floor(WriteY);

    /// <summary>
    /// Horizontal resize factor.
    /// </summary>
    public double XScale => (double)WriteWidth / ReadWidth;

    /// <summary>
    /// Vertical resize factor.
    /// </summary>
    public double YScale => (double)WriteHeight / ReadHeight;

    /// <summary>
    /// Returns <see langword="true"/> when resize can be skipped.
    /// </summary>
    public bool HasSameReadAndWriteSize => ReadWidth == WriteWidth && ReadHeight == WriteHeight;

    /// <summary>
    /// Returns <see langword="true"/> when the source pixels cover the whole target tile.
    /// </summary>
    public bool WritesWholeTile(Size tileSize)
    {
        ArgumentNullException.ThrowIfNull(tileSize);

        return
            WriteLeft == 0 &&
            WriteTop == 0 &&
            WriteWidth == tileSize.Width &&
            WriteHeight == tileSize.Height;
    }
}

/// <summary>
/// Computes primitive raster tile layouts without allocating <see cref="Area"/> objects.
/// </summary>
public static class RasterTileLayoutCalculator
{
    /// <summary>
    /// Computes the layout for one raster tile.
    /// </summary>
    public static bool TryGetLayout(
        GeoCoordinate imageMinCoordinate,
        GeoCoordinate imageMaxCoordinate,
        Size imageSize,
        GeoCoordinate tileMinCoordinate,
        GeoCoordinate tileMaxCoordinate,
        Size tileSize,
        out RasterTileLayout layout)
    {
        ArgumentNullException.ThrowIfNull(imageMinCoordinate);
        ArgumentNullException.ThrowIfNull(imageMaxCoordinate);

        string err = string.Format(Strings.Culture, Strings.Equal, nameof(imageMinCoordinate), nameof(imageMaxCoordinate));

        if (imageMinCoordinate == imageMaxCoordinate)
            throw new ArgumentException(err);

        ArgumentNullException.ThrowIfNull(imageSize);
        ArgumentNullException.ThrowIfNull(tileMinCoordinate);
        ArgumentNullException.ThrowIfNull(tileMaxCoordinate);

        err = string.Format(Strings.Culture, Strings.Equal, nameof(tileMinCoordinate), nameof(tileMaxCoordinate));

        if (tileMinCoordinate == tileMaxCoordinate)
            throw new ArgumentException(err);

        ArgumentNullException.ThrowIfNull(tileSize);

        double readPosMinX = imageSize.Width * (tileMinCoordinate.X - imageMinCoordinate.X) /
                             (imageMaxCoordinate.X - imageMinCoordinate.X);
        double readPosMaxX = imageSize.Width * (tileMaxCoordinate.X - imageMinCoordinate.X) /
                             (imageMaxCoordinate.X - imageMinCoordinate.X);
        double readPosMinY = imageSize.Height - imageSize.Height * (tileMaxCoordinate.Y - imageMinCoordinate.Y) /
                             (imageMaxCoordinate.Y - imageMinCoordinate.Y);
        double readPosMaxY = imageSize.Height - imageSize.Height * (tileMinCoordinate.Y - imageMinCoordinate.Y) /
                             (imageMaxCoordinate.Y - imageMinCoordinate.Y);

        readPosMinX = Math.Clamp(readPosMinX, 0.0, imageSize.Width);
        readPosMaxX = Math.Clamp(readPosMaxX, 0.0, imageSize.Width);
        readPosMinY = Math.Clamp(readPosMinY, 0.0, imageSize.Height);
        readPosMaxY = Math.Clamp(readPosMaxY, 0.0, imageSize.Height);

        double tilePixMinX = readPosMinX.Equals(0.0)
            ? imageMinCoordinate.X
            : readPosMinX.Equals(imageSize.Width)
                ? imageMaxCoordinate.X
                : tileMinCoordinate.X;
        double tilePixMaxX = readPosMaxX.Equals(0.0)
            ? imageMinCoordinate.X
            : readPosMaxX.Equals(imageSize.Width)
                ? imageMaxCoordinate.X
                : tileMaxCoordinate.X;
        double tilePixMinY = readPosMaxY.Equals(0.0)
            ? imageMaxCoordinate.Y
            : readPosMaxY.Equals(imageSize.Height)
                ? imageMinCoordinate.Y
                : tileMinCoordinate.Y;
        double tilePixMaxY = readPosMinY.Equals(0.0)
            ? imageMaxCoordinate.Y
            : readPosMinY.Equals(imageSize.Height)
                ? imageMinCoordinate.Y
                : tileMaxCoordinate.Y;

        double writePosMinX = tileSize.Width - tileSize.Width * (tileMaxCoordinate.X - tilePixMinX) /
                              (tileMaxCoordinate.X - tileMinCoordinate.X);
        double writePosMaxX = tileSize.Width - tileSize.Width * (tileMaxCoordinate.X - tilePixMaxX) /
                              (tileMaxCoordinate.X - tileMinCoordinate.X);
        double writePosMinY = tileSize.Height * (tileMaxCoordinate.Y - tilePixMaxY) /
                              (tileMaxCoordinate.Y - tileMinCoordinate.Y);
        double writePosMaxY = tileSize.Height * (tileMaxCoordinate.Y - tilePixMinY) /
                              (tileMaxCoordinate.Y - tileMinCoordinate.Y);

        int readLeft = (int)Math.Floor(readPosMinX);
        int readTop = (int)Math.Floor(readPosMinY);
        int readRight = (int)Math.Ceiling(readPosMaxX);
        int readBottom = (int)Math.Ceiling(readPosMaxY);

        int writeLeft = (int)Math.Floor(writePosMinX);
        int writeTop = (int)Math.Floor(writePosMinY);
        int writeRight = (int)Math.Ceiling(writePosMaxX);
        int writeBottom = (int)Math.Ceiling(writePosMaxY);

        int readWidth = readRight - readLeft;
        int readHeight = readBottom - readTop;
        int writeWidth = writeRight - writeLeft;
        int writeHeight = writeBottom - writeTop;

        if (readWidth < 1 || readHeight < 1 || writeWidth < 1 || writeHeight < 1)
        {
            layout = default;
            return false;
        }

        layout = new RasterTileLayout(
            readPosMinX,
            readPosMinY,
            readWidth,
            readHeight,
            writePosMinX,
            writePosMinY,
            writeWidth,
            writeHeight);

        return true;
    }

    /// <summary>
    /// Computes the layout for one raster tile.
    /// </summary>
    public static bool TryGetLayout(IGeoTiff image, ITile tile, out RasterTileLayout layout)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(tile);

        return TryGetLayout(
            image.MinCoordinate,
            image.MaxCoordinate,
            image.Size,
            tile.MinCoordinate,
            tile.MaxCoordinate,
            tile.Size,
            out layout);
    }
}
