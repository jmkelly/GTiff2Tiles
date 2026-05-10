using GTiff2Tiles.Core.Coordinates;
using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GTiff2Tiles.Server.Pages.Catalogs;

public sealed class MapModel(CatalogService catalogService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public Catalog Catalog { get; private set; } = null!;

    public bool HasActiveImage => Catalog.ActiveImage is not null;

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

        if (Catalog.ActiveImage is not null)
        {
            MercatorCoordinate minCoordinate = new(Catalog.ActiveImage.MinX, Catalog.ActiveImage.MinY);
            MercatorCoordinate maxCoordinate = new(Catalog.ActiveImage.MaxX, Catalog.ActiveImage.MaxY);

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
