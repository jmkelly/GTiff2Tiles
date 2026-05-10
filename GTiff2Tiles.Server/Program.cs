using GTiff2Tiles.Server.Data;
using GTiff2Tiles.Server.Options;
using GTiff2Tiles.Server.Services;
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

builder.Services.AddDbContext<ServerDbContext>(
    (serviceProvider, options) =>
    {
        LocalStorageOptions storageOptions = serviceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalStorageOptions>>()
            .Value;
        Directory.CreateDirectory(Path.GetDirectoryName(storageOptions.DatabasePath)!);
        options.UseSqlite($"Data Source={storageOptions.DatabasePath}");
    }
);

long maxRequestBodySizeBytes =
    builder.Configuration.GetValue<long?>("Uploads:MaxRequestBodySizeBytes")
    ?? ServerUploadConfiguration.DefaultMaxRequestBodySizeBytes;

builder.Services.ConfigureLargeGeoTiffUploads(maxRequestBodySizeBytes);
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<SlugGenerator>();
builder.Services.AddSingleton<LocalFileStorage>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<TileService>();

var app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    ServerDbContext dbContext = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
    dbContext.Database.EnsureCreated();

    scope.ServiceProvider.GetRequiredService<LocalFileStorage>().EnsureStorageLayout();
    GTiff2Tiles.Core.GdalWorker.ConfigureGdal();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
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

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
