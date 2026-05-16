using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GTiff2Tiles.Server.Pages.Catalogs;

[Authorize]
public sealed class MapModel(CatalogService catalogService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public Catalog Catalog { get; private set; } = null!;

    public bool HasImages => Catalog.Images.Count > 0;

    public string TileRouteTemplate => $"/{Catalog.Slug}/{{z}}/{{x}}/{{y}}";

    public double? South { get; private set; }

    public double? West { get; private set; }

    public double? North { get; private set; }

    public double? East { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Catalog? catalog = await catalogService.GetCatalogAsync(Id, cancellationToken).ConfigureAwait(false);
        if (catalog is null)
            return NotFound();

        Catalog = catalog;

        if (Catalog.Images.Count > 0)
        {
            double minX = Catalog.Images.Min(image => image.MinX);
            double minY = Catalog.Images.Min(image => image.MinY);
            double maxX = Catalog.Images.Max(image => image.MaxX);
            double maxY = Catalog.Images.Max(image => image.MaxY);

            MercatorCoordinate minCoordinate = new(minX, minY);
            MercatorCoordinate maxCoordinate = new(maxX, maxY);

            GeodeticCoordinate minGeodetic = minCoordinate.ToGeodeticCoordinate();
            GeodeticCoordinate maxGeodetic = maxCoordinate.ToGeodeticCoordinate();

            South = Math.Min(minGeodetic.Latitude, maxGeodetic.Latitude);
            West = Math.Min(minGeodetic.Longitude, maxGeodetic.Longitude);
            North = Math.Max(minGeodetic.Latitude, maxGeodetic.Latitude);
            East = Math.Max(minGeodetic.Longitude, maxGeodetic.Longitude);
        }

        return Page();
    }
}
