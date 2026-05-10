using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Core.Enums;
using GTiff2Tiles.Core.GeoTiffs;
using GTiff2Tiles.Core.Helpers;
using GTiff2Tiles.Core.Images;
using GTiff2Tiles.Core.Tiles;
using GTiff2Tiles.Tests.Constants;
using NetVips;
using NUnit.Framework;

namespace GTiff2Tiles.Tests.Tests.GeoTiffs;

[TestFixture]
public sealed class RasterTileSeamTests
{
    private readonly string _in3785 = FileSystemEntries.Input3785FilePath;

    private const CoordinateSystem Cs3857 = CoordinateSystem.Epsg3857;

    [SetUp]
    public void SetUp() => NetVipsHelper.DisableLog();

    [Test]
    public void Level19HorizontalSeamIsMateriallyReduced()
    {
        using Raster raster = new(_in3785, Cs3857);
        using Image tileCache = raster.Data.Tilecache(Tile.DefaultSize.Width, Tile.DefaultSize.Height, 32, threaded: true);

        using RasterTile leftTile = CreateTile(465749, 206482, 19);
        using RasterTile rightTile = CreateTile(465750, 206482, 19);

        using Image legacyLeft = CreateLegacyTileImage(raster, tileCache, leftTile);
        using Image legacyRight = CreateLegacyTileImage(raster, tileCache, rightTile);
        using Image fixedLeft = CreateCurrentTileImage(raster, tileCache, leftTile);
        using Image fixedRight = CreateCurrentTileImage(raster, tileCache, rightTile);

        double legacyDifference = ComputeHorizontalEdgeDifference(legacyLeft, legacyRight);
        double fixedDifference = ComputeHorizontalEdgeDifference(fixedLeft, fixedRight);

        TestContext.Out.WriteLine($"legacy horizontal edge diff: {legacyDifference}");
        TestContext.Out.WriteLine($"fixed horizontal edge diff: {fixedDifference}");

        Assert.That(fixedDifference, Is.LessThan(legacyDifference * 0.8));
    }

    [Test]
    public void Level19VerticalSeamIsMateriallyReduced()
    {
        using Raster raster = new(_in3785, Cs3857);
        using Image tileCache = raster.Data.Tilecache(Tile.DefaultSize.Width, Tile.DefaultSize.Height, 32, threaded: true);

        using RasterTile topTile = CreateTile(465780, 206488, 19);
        using RasterTile bottomTile = CreateTile(465780, 206489, 19);

        using Image legacyTop = CreateLegacyTileImage(raster, tileCache, topTile);
        using Image legacyBottom = CreateLegacyTileImage(raster, tileCache, bottomTile);
        using Image fixedTop = CreateCurrentTileImage(raster, tileCache, topTile);
        using Image fixedBottom = CreateCurrentTileImage(raster, tileCache, bottomTile);

        double legacyDifference = ComputeVerticalEdgeDifference(legacyTop, legacyBottom);
        double fixedDifference = ComputeVerticalEdgeDifference(fixedTop, fixedBottom);

        TestContext.Out.WriteLine($"legacy vertical edge diff: {legacyDifference}");
        TestContext.Out.WriteLine($"fixed vertical edge diff: {fixedDifference}");

        Assert.That(fixedDifference, Is.LessThan(legacyDifference * 0.8));
    }

    private static RasterTile CreateTile(int x, int y, int z)
    {
        return new RasterTile(new Number(x, y, z), Cs3857, Tile.DefaultSize, tmsCompatible: false)
        {
            BandsCount = 3,
            Interpolation = NetVips.Enums.Kernel.Lanczos3
        };
    }

    private static Image CreateCurrentTileImage(Raster raster, Image tileCache, RasterTile tile)
    {
        (Area readArea, Area writeArea)? areas = Area.GetAreas(raster, tile);
        Assert.That(areas, Is.Not.Null);

        (Area readArea, Area writeArea) = areas!.Value;
        return raster.CreateTileImage(tileCache, tile, readArea, writeArea);
    }

    private static Image CreateLegacyTileImage(Raster raster, Image tileCache, RasterTile tile)
    {
        (Area readArea, Area writeArea)? areas = GetLegacyAreas(raster, tile);
        Assert.That(areas, Is.Not.Null);

        (Area readArea, Area writeArea) = areas!.Value;
        return raster.CreateTileImage(tileCache, tile, readArea, writeArea);
    }

    private static (Area readArea, Area writeArea)? GetLegacyAreas(Raster raster, RasterTile tile)
    {
        double readPosMinX = raster.Size.Width * (tile.MinCoordinate.X - raster.MinCoordinate.X) /
                             (raster.MaxCoordinate.X - raster.MinCoordinate.X);
        double readPosMaxX = raster.Size.Width * (tile.MaxCoordinate.X - raster.MinCoordinate.X) /
                             (raster.MaxCoordinate.X - raster.MinCoordinate.X);
        double readPosMinY = raster.Size.Height - raster.Size.Height * (tile.MaxCoordinate.Y - raster.MinCoordinate.Y) /
                             (raster.MaxCoordinate.Y - raster.MinCoordinate.Y);
        double readPosMaxY = raster.Size.Height - raster.Size.Height * (tile.MinCoordinate.Y - raster.MinCoordinate.Y) /
                             (raster.MaxCoordinate.Y - raster.MinCoordinate.Y);

        readPosMinX = Math.Clamp(readPosMinX, 0.0, raster.Size.Width);
        readPosMaxX = Math.Clamp(readPosMaxX, 0.0, raster.Size.Width);
        readPosMinY = Math.Clamp(readPosMinY, 0.0, raster.Size.Height);
        readPosMaxY = Math.Clamp(readPosMaxY, 0.0, raster.Size.Height);

        double tilePixMinX = readPosMinX.Equals(0.0)
            ? raster.MinCoordinate.X
            : readPosMinX.Equals(raster.Size.Width)
                ? raster.MaxCoordinate.X
                : tile.MinCoordinate.X;
        double tilePixMaxX = readPosMaxX.Equals(0.0)
            ? raster.MinCoordinate.X
            : readPosMaxX.Equals(raster.Size.Width)
                ? raster.MaxCoordinate.X
                : tile.MaxCoordinate.X;
        double tilePixMinY = readPosMaxY.Equals(0.0)
            ? raster.MaxCoordinate.Y
            : readPosMaxY.Equals(raster.Size.Height)
                ? raster.MinCoordinate.Y
                : tile.MinCoordinate.Y;
        double tilePixMaxY = readPosMinY.Equals(0.0)
            ? raster.MaxCoordinate.Y
            : readPosMinY.Equals(raster.Size.Height)
                ? raster.MinCoordinate.Y
                : tile.MaxCoordinate.Y;

        double writePosMinX = tile.Size.Width - tile.Size.Width * (tile.MaxCoordinate.X - tilePixMinX) /
                              (tile.MaxCoordinate.X - tile.MinCoordinate.X);
        double writePosMaxX = tile.Size.Width - tile.Size.Width * (tile.MaxCoordinate.X - tilePixMaxX) /
                              (tile.MaxCoordinate.X - tile.MinCoordinate.X);
        double writePosMinY = tile.Size.Height * (tile.MaxCoordinate.Y - tilePixMaxY) /
                              (tile.MaxCoordinate.Y - tile.MinCoordinate.Y);
        double writePosMaxY = tile.Size.Height * (tile.MaxCoordinate.Y - tilePixMinY) /
                              (tile.MaxCoordinate.Y - tile.MinCoordinate.Y);

        double readWidth = readPosMaxX - readPosMinX;
        double writeWidth = writePosMaxX - writePosMinX;
        double readHeight = Math.Abs(readPosMaxY - readPosMinY);
        double writeHeight = Math.Abs(writePosMaxY - writePosMinY);

        double readXShift = readPosMinX - (int)readPosMinX;
        readWidth += readXShift;
        double readYShift = readPosMinY - (int)readPosMinY;
        readHeight += readYShift;
        double writeXShift = writePosMinX - (int)writePosMinX;
        writeWidth += writeXShift;
        double writeYShift = writePosMinY - (int)writePosMinY;
        writeHeight += writeYShift;

        writeWidth = writeWidth > 1.0 ? writeWidth : 1.0;
        writeHeight = writeHeight > 1.0 ? writeHeight : 1.0;

        if (readWidth < 1 || readHeight < 1 || writeWidth < 1 || writeHeight < 1)
            return null;

        Area readArea = new(new PixelCoordinate(readPosMinX, readPosMinY), new Size((int)readWidth, (int)readHeight));
        Area writeArea = new(new PixelCoordinate(writePosMinX, writePosMinY), new Size((int)writeWidth, (int)writeHeight));

        return (readArea, writeArea);
    }

    private static double ComputeHorizontalEdgeDifference(Image leftTile, Image rightTile)
    {
        double difference = 0.0;

        for (int y = 0; y < Tile.DefaultSize.Height; y++)
        {
            double[] leftPixel = leftTile[Tile.DefaultSize.Width - 1, y];
            double[] rightPixel = rightTile[0, y];
            difference += ComputePixelDifference(leftPixel, rightPixel);
        }

        return difference;
    }

    private static double ComputeVerticalEdgeDifference(Image topTile, Image bottomTile)
    {
        double difference = 0.0;

        for (int x = 0; x < Tile.DefaultSize.Width; x++)
        {
            double[] topPixel = topTile[x, Tile.DefaultSize.Height - 1];
            double[] bottomPixel = bottomTile[x, 0];
            difference += ComputePixelDifference(topPixel, bottomPixel);
        }

        return difference;
    }

    private static double ComputePixelDifference(double[] leftPixel, double[] rightPixel)
    {
        int comparedBandsCount = Math.Min(3, Math.Min(leftPixel.Length, rightPixel.Length));
        double difference = 0.0;

        for (int i = 0; i < comparedBandsCount; i++)
            difference += Math.Abs(leftPixel[i] - rightPixel[i]);

        return difference;
    }
}
