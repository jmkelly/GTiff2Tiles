using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GTiff2Tiles.Server.Pages.Admin.StoragePresets;

[Authorize]
public sealed class EditModel(ServerDbContext dbContext) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public StoragePresetInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        StoragePreset? preset = await dbContext.StoragePresets
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == Id, cancellationToken)
            .ConfigureAwait(false);

        if (preset is null)
            return NotFound();

        Input = new StoragePresetInput
        {
            Name = preset.Name,
            Provider = preset.Provider,
            LocalRootPath = preset.LocalRootPath,
            S3BucketName = preset.S3BucketName,
            S3Region = preset.S3Region,
            S3AccessKeyId = preset.S3AccessKeyId,
            S3SecretAccessKey = preset.S3SecretAccessKey,
            S3EndpointUrl = preset.S3EndpointUrl,
            S3LocalCacheRoot = preset.S3LocalCacheRoot
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        StoragePreset? preset = await dbContext.StoragePresets
            .FirstOrDefaultAsync(p => p.Id == Id, cancellationToken)
            .ConfigureAwait(false);

        if (preset is null)
            return NotFound();

        try
        {
            preset.Name = Input.Name.Trim();
            preset.Provider = Input.Provider;
            preset.LocalRootPath = string.IsNullOrWhiteSpace(Input.LocalRootPath) ? null : Input.LocalRootPath.Trim();
            preset.S3BucketName = string.IsNullOrWhiteSpace(Input.S3BucketName) ? null : Input.S3BucketName.Trim();
            preset.S3Region = string.IsNullOrWhiteSpace(Input.S3Region) ? null : Input.S3Region.Trim();
            preset.S3AccessKeyId = string.IsNullOrWhiteSpace(Input.S3AccessKeyId) ? null : Input.S3AccessKeyId.Trim();
            preset.S3SecretAccessKey = string.IsNullOrWhiteSpace(Input.S3SecretAccessKey) ? null : Input.S3SecretAccessKey.Trim();
            preset.S3EndpointUrl = string.IsNullOrWhiteSpace(Input.S3EndpointUrl) ? null : Input.S3EndpointUrl.Trim();
            preset.S3LocalCacheRoot = string.IsNullOrWhiteSpace(Input.S3LocalCacheRoot) ? null : Input.S3LocalCacheRoot.Trim();

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return RedirectToPage("/Admin/StoragePresets/Index");
        }
        catch (Exception exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }
}
