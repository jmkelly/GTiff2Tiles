using Amazon.S3.Model;
using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GTiff2Tiles.Server.Pages.Admin;

[Authorize]
public sealed class SettingsModel(ServerDbContext dbContext) : PageModel
{
    [BindProperty]
    public StorageSettingsInput Input { get; set; } = new();

    public string? StatusMessage { get; set; }

    public bool StatusIsError { get; set; }

    public IReadOnlyList<StoragePreset> Presets { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
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
            await SaveSettingAsync("Storage:Provider", Input.Provider, cancellationToken).ConfigureAwait(false);
            await SaveSettingAsync("Storage:Local:RootPath", Input.Local.RootPath, cancellationToken).ConfigureAwait(false);
            await SaveSettingAsync("Storage:S3:BucketName", Input.S3.BucketName, cancellationToken).ConfigureAwait(false);
            await SaveSettingAsync("Storage:S3:Region", Input.S3.Region, cancellationToken).ConfigureAwait(false);
            await SaveSettingAsync("Storage:S3:AccessKeyId", Input.S3.AccessKeyId, cancellationToken).ConfigureAwait(false);
            await SaveSettingAsync("Storage:S3:SecretAccessKey", Input.S3.SecretAccessKey, cancellationToken).ConfigureAwait(false);
            await SaveSettingAsync("Storage:S3:EndpointUrl", Input.S3.EndpointUrl, cancellationToken).ConfigureAwait(false);
            await SaveSettingAsync("Storage:S3:LocalCacheRoot", Input.S3.LocalCacheRoot, cancellationToken).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            StatusMessage = "Settings saved successfully. Restart the application for changes to take effect.";
            StatusIsError = false;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Failed to save settings: {exception.Message}";
            StatusIsError = true;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostTestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            Amazon.S3.IAmazonS3 client = CreateS3Client(Input.S3);
            ListBucketsResponse response = await client.ListBucketsAsync(cancellationToken).ConfigureAwait(false);

            bool bucketExists = response.Buckets.Any(b =>
                string.Equals(b.BucketName, Input.S3.BucketName, StringComparison.OrdinalIgnoreCase));

            if (bucketExists)
            {
                StatusMessage = "Connection successful. Bucket found.";
            }
            else
            {
                StatusMessage = "Connection successful, but bucket was not found. It will be created on first upload.";
            }

            StatusIsError = false;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Connection failed: {exception.Message}";
            StatusIsError = true;
        }

        return Page();
    }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, string?> settings = await dbContext.AppSettings
            .ToDictionaryAsync(s => s.Key, s => (string?)s.Value, cancellationToken)
            .ConfigureAwait(false);

        Input.Provider = GetSetting(settings, "Storage:Provider", "Local");
        Input.Local.RootPath = GetSetting(settings, "Storage:Local:RootPath", string.Empty);
        Input.S3.BucketName = GetSetting(settings, "Storage:S3:BucketName", string.Empty);
        Input.S3.Region = GetSetting(settings, "Storage:S3:Region", "us-east-1");
        Input.S3.AccessKeyId = GetSetting(settings, "Storage:S3:AccessKeyId", string.Empty);
        Input.S3.SecretAccessKey = GetSetting(settings, "Storage:S3:SecretAccessKey", string.Empty);
        Input.S3.EndpointUrl = GetSetting(settings, "Storage:S3:EndpointUrl", string.Empty);
        Input.S3.LocalCacheRoot = GetSetting(settings, "Storage:S3:LocalCacheRoot", string.Empty);
    }

    private async Task SaveSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        AppSetting? existing = await dbContext.AppSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Value = value;
        }
        else
        {
            dbContext.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
    }

    private static string GetSetting(IReadOnlyDictionary<string, string?> settings, string key, string defaultValue)
        => settings.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : defaultValue;

    private static Amazon.S3.IAmazonS3 CreateS3Client(Models.S3StorageInput input)
    {
        Amazon.S3.AmazonS3Config config = new()
        {
            RegionEndpoint = string.IsNullOrWhiteSpace(input.EndpointUrl)
                ? Amazon.RegionEndpoint.GetBySystemName(input.Region)
                : Amazon.RegionEndpoint.USEast1
        };

        if (!string.IsNullOrWhiteSpace(input.EndpointUrl))
        {
            config.ServiceURL = input.EndpointUrl;
            config.ForcePathStyle = true;
            config.UseHttp = input.EndpointUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        Amazon.Runtime.AWSCredentials credentials = !string.IsNullOrWhiteSpace(input.AccessKeyId) &&
                                                   !string.IsNullOrWhiteSpace(input.SecretAccessKey)
            ? new Amazon.Runtime.BasicAWSCredentials(input.AccessKeyId, input.SecretAccessKey)
            : new Amazon.Runtime.InstanceProfileAWSCredentials();

        return new Amazon.S3.AmazonS3Client(credentials, config);
    }
}
