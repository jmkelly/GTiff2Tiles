using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GTiff2Tiles.Server.Pages.Catalogs;

[Authorize]
public sealed class DetailsModel(CatalogService catalogService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public UpdateCatalogInput UpdateCatalog { get; set; } = new();

    [BindProperty]
    public UploadGeoTiffInput Upload { get; set; } = new();

    public Catalog Catalog { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Catalog? catalog = await catalogService.GetCatalogAsync(Id, cancellationToken).ConfigureAwait(false);
        if (catalog is null)
            return NotFound();

        Catalog = catalog;
        PopulateUpdateCatalog(Catalog);
        return Page();
    }

    public async Task<PartialViewResult> OnGetImagePanelAsync(CancellationToken cancellationToken)
    {
        Catalog catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
        return Partial("_ImagePanel", catalog);
    }

    public PartialViewResult OnGetUploadForm()
        => Partial("_UploadForm", Upload);

    public async Task<IActionResult> OnPostUpdateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
            return Page();
        }

        try
        {
            Catalog = await catalogService.UpdateCatalogAsync(Id, UpdateCatalog, cancellationToken).ConfigureAwait(false);
            return RedirectToPage("/Catalogs/Details", new { id = Id });
        }
        catch (Exception exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            Catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        if (Upload.Files.Count == 0)
        {
            ModelState.AddModelError("Upload.Files", "Select at least one GeoTIFF file.");
            return Partial("_UploadForm", Upload);
        }

        try
        {
            await catalogService.UploadImagesAsync(Id, Upload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Partial("_UploadForm", Upload);
        }

        Upload = new UploadGeoTiffInput();
        Htmx.Trigger(Response, "image-updated");
        return new EmptyResult();
    }

    public async Task<IActionResult> OnPostRemoveImageAsync(int imageId, CancellationToken cancellationToken)
    {
        try
        {
            await catalogService.RemoveImageAsync(Id, imageId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            Catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
            return Partial("_ImagePanel", Catalog);
        }

        Htmx.Trigger(Response, "image-updated");
        return new EmptyResult();
    }

    public async Task<IActionResult> OnPostMoveImageAsync(int imageId, int offset, CancellationToken cancellationToken)
    {
        try
        {
            await catalogService.MoveImageAsync(Id, imageId, offset, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            Catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
            return Partial("_ImagePanel", Catalog);
        }

        Htmx.Trigger(Response, "image-updated");
        return new EmptyResult();
    }

    private void PopulateUpdateCatalog(Catalog catalog)
    {
        UpdateCatalog = new UpdateCatalogInput
        {
            Name = catalog.Name,
            Slug = catalog.Slug,
            Description = catalog.Description
        };
    }

    private async Task<Catalog> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        return await catalogService.GetCatalogAsync(Id, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Catalog was not found.");
    }
}
