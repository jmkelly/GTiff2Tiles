using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Models;
using GTiff2Tiles.Server.Options;
using GTiff2Tiles.Server.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

string dataRoot = Path.GetFullPath(
    Path.Combine(
        builder.Environment.ContentRootPath,
        builder.Configuration["Storage:RootPath"] ?? "App_Data"
    )
);
Directory.CreateDirectory(dataRoot);

builder.Services.Configure<LocalStorageOptions>(options =>
{
    options.RootPath = dataRoot;
    options.DatabasePath = Path.Combine(dataRoot, "server.db");
});

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
            LocalStorageOptions storageOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalStorageOptions>>()
                .Value;
            Directory.CreateDirectory(Path.GetDirectoryName(storageOptions.DatabasePath)!);
            options.UseSqlite($"Data Source={storageOptions.DatabasePath}");
        }
    }
);

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
builder.Services.AddSingleton<LocalFileStorage>();
builder.Services.AddSingleton<TileRendererCache>();
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

    scope.ServiceProvider.GetRequiredService<LocalFileStorage>().EnsureStorageLayout();
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
        CancellationToken cancellationToken
    ) =>
    {
        CatalogImage? image = await dbContext.CatalogImages
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == imageId && i.Catalog.Slug == catalogSlug, cancellationToken)
            .ConfigureAwait(false);

        if (image is null || string.IsNullOrWhiteSpace(image.NormalizedPath))
            return Results.NotFound();

        byte[]? thumbnail = await Task.Run(
            () => tileRendererCache.GenerateThumbnail(image.NormalizedPath),
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
