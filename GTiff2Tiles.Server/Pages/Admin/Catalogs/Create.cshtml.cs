using System.Text.Json;
using System.Text.Json.Serialization;
using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GTiff2Tiles.Server.Pages.Admin.Catalogs;

[Authorize]
public sealed class CreateModel(CatalogService catalogService, ServerDbContext dbContext) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [BindProperty]
    public CreateCatalogInput CreateCatalog { get; set; } = new();

    public IReadOnlyList<StoragePreset> Presets { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Presets = await dbContext.StoragePresets
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            Catalog catalog = await catalogService.CreateCatalogAsync(CreateCatalog, cancellationToken).ConfigureAwait(false);
            return RedirectToPage("/Admin/Catalogs/Details", new { id = catalog.Id });
        }
        catch (Exception exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnGetPresetJsonAsync(int id, CancellationToken cancellationToken)
    {
        StoragePreset? preset = await dbContext.StoragePresets
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (preset is null)
            return NotFound();

        return Content(JsonSerializer.Serialize(new
        {
            provider = preset.Provider,
            local = preset.Provider == "Local" ? new
            {
                rootPath = preset.LocalRootPath
            } : null,
            s3 = preset.Provider == "S3" ? new
            {
                bucketName = preset.S3BucketName,
                region = preset.S3Region,
                accessKeyId = preset.S3AccessKeyId,
                secretAccessKey = preset.S3SecretAccessKey,
                endpointUrl = preset.S3EndpointUrl,
                localCacheRoot = preset.S3LocalCacheRoot
            } : null
        }, JsonOptions), "application/json");
    }
}
