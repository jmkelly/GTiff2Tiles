using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GTiff2Tiles.Server.Pages.Admin;

[Authorize]
public sealed class IndexModel(CatalogService catalogService) : PageModel
{
    public IReadOnlyList<Catalog> Catalogs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Catalogs = await catalogService.GetCatalogsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PartialViewResult> OnGetCatalogListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Catalog> catalogs = await catalogService.GetCatalogsAsync(cancellationToken).ConfigureAwait(false);
        return Partial("_CatalogList", catalogs);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await catalogService.DeleteCatalogAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }

        IReadOnlyList<Catalog> catalogs = await catalogService.GetCatalogsAsync(cancellationToken).ConfigureAwait(false);
        return Partial("_CatalogList", catalogs);
    }
}
