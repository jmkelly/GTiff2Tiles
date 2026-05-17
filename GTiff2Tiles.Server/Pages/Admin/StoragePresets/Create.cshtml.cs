using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GTiff2Tiles.Server.Pages.Admin.StoragePresets;

[Authorize]
public sealed class CreateModel(ServerDbContext dbContext) : PageModel
{
    [BindProperty]
    public StoragePresetInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            StoragePreset preset = new()
            {
                Name = Input.Name.Trim(),
                Provider = Input.Provider,
                LocalRootPath = string.IsNullOrWhiteSpace(Input.LocalRootPath) ? null : Input.LocalRootPath.Trim(),
                S3BucketName = string.IsNullOrWhiteSpace(Input.S3BucketName) ? null : Input.S3BucketName.Trim(),
                S3Region = string.IsNullOrWhiteSpace(Input.S3Region) ? null : Input.S3Region.Trim(),
                S3AccessKeyId = string.IsNullOrWhiteSpace(Input.S3AccessKeyId) ? null : Input.S3AccessKeyId.Trim(),
                S3SecretAccessKey = string.IsNullOrWhiteSpace(Input.S3SecretAccessKey) ? null : Input.S3SecretAccessKey.Trim(),
                S3EndpointUrl = string.IsNullOrWhiteSpace(Input.S3EndpointUrl) ? null : Input.S3EndpointUrl.Trim(),
                S3LocalCacheRoot = string.IsNullOrWhiteSpace(Input.S3LocalCacheRoot) ? null : Input.S3LocalCacheRoot.Trim()
            };

            dbContext.StoragePresets.Add(preset);
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
