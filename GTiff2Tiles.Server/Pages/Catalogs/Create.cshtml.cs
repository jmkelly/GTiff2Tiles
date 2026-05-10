using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GTiff2Tiles.Server.Pages.Catalogs;

public sealed class CreateModel(CatalogService catalogService) : PageModel
{
    [BindProperty]
    public CreateCatalogInput CreateCatalog { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            Catalog catalog = await catalogService.CreateCatalogAsync(CreateCatalog, cancellationToken).ConfigureAwait(false);
            return RedirectToPage("/Catalogs/Details", new { id = catalog.Id });
        }
        catch (Exception exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }
}
