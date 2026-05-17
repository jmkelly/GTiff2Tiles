using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Options;
using GTiff2Tiles.Server.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<LocalStorageOptions>(builder.Configuration.GetSection($"{StorageOptions.SectionName}:Local"));

string dataRoot = Path.GetFullPath(
    Path.Combine(
        builder.Environment.ContentRootPath,
        builder.Configuration["Storage:RootPath"] ?? "App_Data"
    )
);
Directory.CreateDirectory(dataRoot);

string databasePath = Path.Combine(dataRoot, "server.db");

DatabaseOptions databaseOptions = builder.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>() ?? new DatabaseOptions();

builder.Services.AddDbContext<ServerDbContext>(
    (serviceProvider, options) =>
    {
        if (string.Equals(databaseOptions.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            options.UseNpgsql(databaseOptions.ConnectionString);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            options.UseSqlite($"Data Source={databasePath}");
        }
    }
);

// Read AppSettings from database to override configuration at runtime
try
{
    string? connectionString = string.Equals(databaseOptions.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
        ? databaseOptions.ConnectionString
        : $"Data Source={databasePath}";

    DbContextOptionsBuilder<ServerDbContext> tempOptionsBuilder = new();
    if (string.Equals(databaseOptions.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        tempOptionsBuilder.UseNpgsql(connectionString);
    else
        tempOptionsBuilder.UseSqlite(connectionString);

    using ServerDbContext tempContext = new(tempOptionsBuilder.Options);
    if (tempContext.Database.CanConnect())
    {
        List<AppSetting> appSettings = tempContext.AppSettings.ToList();
        if (appSettings.Count > 0)
        {
            Dictionary<string, string?> configOverrides = appSettings
                .ToDictionary(setting => setting.Key, setting => (string?)setting.Value);
            builder.Configuration.AddInMemoryCollection(configOverrides!);
        }
    }
}
catch
{
    // DB not yet created or schema not migrated -- use appsettings.json defaults
}

// Register storage services
string provider = builder.Configuration[$"{StorageOptions.SectionName}:Provider"] ?? "Local";

// Always configure both option types so the factory can resolve either
builder.Services.Configure<S3StorageOptions>(builder.Configuration.GetSection($"{StorageOptions.SectionName}:S3"));
builder.Services.Configure<LocalStorageOptions>(options =>
{
    options.RootPath = dataRoot;
    options.DatabasePath = databasePath;
});

builder.Services.AddSingleton<IStorageFactory, StorageFactory>();

long maxRequestBodySizeBytes =
    builder.Configuration.GetValue<long?>("Uploads:MaxRequestBodySizeBytes")
    ?? ServerUploadConfiguration.DefaultMaxRequestBodySizeBytes;

builder.Services.ConfigureLargeGeoTiffUploads(maxRequestBodySizeBytes);
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
})
.AddEntityFrameworkStores<ServerDbContext>()
.AddDefaultUI();

builder.Services.AddSingleton<SlugGenerator>();
builder.Services.AddSingleton<TileRendererCache>();
builder.Services.AddScoped<StoragePathResolver>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<TileService>();

var app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    ServerDbContext dbContext = scope.ServiceProvider.GetRequiredService<ServerDbContext>();

    try
    {
        dbContext.Database.Migrate();
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("already exists"))
    {
        dbContext.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL)");

        dbContext.Database.ExecuteSqlRaw(
            "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20260516062403_InitialCreate', '10.0.0')");

        dbContext.Database.Migrate();
    }

    // Configure GDAL for S3 if S3 credentials are configured (needed for /vsis3/ tile rendering)
    S3StorageOptions s3Options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<S3StorageOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(s3Options.AccessKeyId) && !string.IsNullOrWhiteSpace(s3Options.SecretAccessKey))
    {
        S3Storage.ConfigureGdal(s3Options);

        S3Storage s3Storage = new(s3Options);
        await s3Storage.EnsureBucketExistsAsync();
        s3Storage.Dispose();
    }

    // Ensure local storage layout exists
    LocalStorageOptions localOptions = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalStorageOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(localOptions.RootPath))
    {
        Directory.CreateDirectory(localOptions.RootPath);
        Directory.CreateDirectory(Path.Combine(localOptions.RootPath, "catalogs"));
    }

    GTiff2Tiles.Core.GdalWorker.ConfigureGdal();

    AdminUserOptions adminUserOptions = app.Configuration
        .GetSection(AdminUserOptions.SectionName)
        .Get<AdminUserOptions>() ?? new AdminUserOptions();

    if (!string.IsNullOrWhiteSpace(adminUserOptions.Email) && !string.IsNullOrWhiteSpace(adminUserOptions.Password))
    {
        UserManager<ApplicationUser> userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        if (!await userManager.Users.AnyAsync())
        {
            ApplicationUser adminUser = new()
            {
                UserName = adminUserOptions.Email,
                Email = adminUserOptions.Email
            };

            IdentityResult result = await userManager.CreateAsync(adminUser, adminUserOptions.Password);
            if (!result.Succeeded)
            {
                Console.Error.WriteLine(
                    $"Failed to seed admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet(
    "/{catalogSlug}/{z:int}/{x:int}/{y:int}",
    async (
        string catalogSlug,
        int z,
        int x,
        int y,
        TileService tileService,
        CancellationToken cancellationToken
    ) =>
    {
        Stream? tileStream = await tileService
            .TryOpenTileStreamAsync(catalogSlug, z, x, y, cancellationToken)
            .ConfigureAwait(false);

        return tileStream is null
            ? Results.NotFound()
            : Results.Stream(tileStream, TileService.TileContentType, enableRangeProcessing: false);
    }
);

app.MapGet(
    "/{catalogSlug}/images/{imageId:int}/thumbnail",
    async (
        string catalogSlug,
        int imageId,
        ServerDbContext dbContext,
        TileRendererCache tileRendererCache,
        StoragePathResolver pathResolver,
        CancellationToken cancellationToken
    ) =>
    {
        var imageData = await dbContext.CatalogImages
            .AsNoTracking()
            .Where(i => i.Id == imageId && i.Catalog.Slug == catalogSlug)
            .Select(i => new
            {
                i.NormalizedPath,
                i.StorageProvider,
                CatalogSlug = i.Catalog.Slug,
                i.Catalog.StorageConfig
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (imageData is null || string.IsNullOrWhiteSpace(imageData.NormalizedPath))
            return Results.NotFound();

        PathResolutionResult resolutionResult = await pathResolver.ResolvePathAsync(
            imageData.NormalizedPath, imageData.StorageProvider, imageData.CatalogSlug, imageData.StorageConfig)
            .ConfigureAwait(false);

        byte[]? thumbnail = await Task.Run(
            () => tileRendererCache.GenerateThumbnail(resolutionResult.Path, s3Options: resolutionResult.S3Options),
            cancellationToken
        ).ConfigureAwait(false);

        return thumbnail is null
            ? Results.NotFound()
            : Results.Stream(new MemoryStream(thumbnail, writable: false), "image/png", enableRangeProcessing: false);
    }
);

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
