# Per-Catalog Storage Provider Configuration

## Problem

Storage provider settings are currently stored **globally** in the `AppSettings` database table and `appsettings.json`. While each `Catalog` can select a provider type (`"Local"` or `"S3"`) via `Catalog.StorageProvider`, all catalogs using the same provider share the same configuration (bucket name, credentials, root path, etc.). There is no way to have two S3 catalogs with different buckets or two local catalogs with different root paths.

## Current Architecture

- **`AppSettings`** DB table stores global key-value overrides (`Storage:Provider`, `Storage:S3:BucketName`, etc.)
- **`StorageFactory`** is a singleton that caches **one** `S3Storage` and **one** `LocalFileStorage` instance
- Both cached instances read their config from global `IOptions<T>` (bound at startup)
- **`TileRendererCache`** is path-based: `/vsis3/` prefix → GDAL (global credentials), else local filesystem
- **GDAL** is configured globally at startup (`Program.cs:132-137`) for S3 credentials
- `Catalog.StorageProvider` is just a string — no associated config

## Solution: Per-Catalog `StorageConfig` JSON Column

Add a nullable JSON column `StorageConfig` to the `Catalog` table. Each catalog stores its own provider-specific settings. A settings **merge** strategy applies: properties defined in the catalog's config override global defaults; undefined properties inherit from global settings.

## Tile Serving Strategy (On-Demand Download + Cache)

For per-catalog S3 catalogs, GDAL's `/vsis3/` driver can't be used (GDAL credentials are global). Instead:
1. `NormalizedPath` stores the authoritative S3 path (`/vsis3/{bucket}/...`)
2. On first tile request, the file is **downloaded from S3** to a local cache directory
3. Subsequent requests use the cached local copy
4. Cache persists until the catalog/image is deleted (no automatic eviction)

Global S3 catalogs (no per-catalog config) continue using GDAL `/vsis3/` as before.

## Implementation Steps

### Step 1: Create `CatalogStorageConfig` model

**File:** `Models/CatalogStorageConfig.cs` (new)

```csharp
namespace GTiff2Tiles.Server.Models;

public sealed class CatalogStorageConfig
{
    public LocalStorageConfig? Local { get; set; }
    public S3StorageConfig? S3 { get; set; }
}

public sealed class LocalStorageConfig
{
    public string? RootPath { get; set; }
}

public sealed class S3StorageConfig
{
    public string? BucketName { get; set; }
    public string? Region { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? EndpointUrl { get; set; }
    public string? LocalCacheRoot { get; set; }
}
```

### Step 2: Update `Catalog` entity

**File:** `Models/Catalog.cs` — add:
```csharp
public CatalogStorageConfig? StorageConfig { get; set; }
```

### Step 3: Configure JSON column in DbContext

**File:** `Data/ServerDbContext.cs` — in `OnModelCreating`, add to the Catalog entity config:
```csharp
entity.OwnsOne(c => c.StorageConfig, owned =>
{
    owned.ToJson();
    owned.OwnsOne(c => c.Local);
    owned.OwnsOne(c => c.S3);
});
```

This stores the config as a JSON text column in the database (works with both SQLite and PostgreSQL).

### Step 4: Update input models

**File:** `Models/CreateCatalogInput.cs` — add:
```csharp
public CatalogStorageConfig? StorageConfig { get; set; }
```

**File:** `Models/UpdateCatalogInput.cs` — same.

### Step 5: Refactor storage services

**File:** `Services/IStorageFactory.cs` — change `GetStorage` to accept optional per-catalog config:
```csharp
IStorage GetStorage(string provider, CatalogStorageConfig? catalogConfig = null);
```

**File:** `Services/StorageFactory.cs` — major refactor:
- Remove singleton caching of `S3Storage` / `LocalFileStorage`
- `GetStorage` merges global defaults (from `IOptions<T>`) with per-catalog overrides (non-null catalog properties win)
- Creates a new `IStorage` instance per call with the merged config

**File:** `Services/LocalFileStorage.cs` — add POCO constructor:
```csharp
public LocalFileStorage(LocalStorageOptions options) { ... }
```

**File:** `Services/S3Storage.cs` — add POCO constructor:
```csharp
public S3Storage(S3StorageOptions options) { ... }
```

### Step 6: Create `StoragePathResolver` service

**File:** `Services/StoragePathResolver.cs` (new)

Resolves normalized paths on-demand for tile serving:

```csharp
public sealed class StoragePathResolver(IStorageFactory storageFactory)
{
    public async Task<string> ResolvePathAsync(
        string normalizedPath,
        string provider,
        string catalogSlug,
        CatalogStorageConfig? catalogConfig);
}
```

- If not per-catalog S3 (`provider != "S3"` or `catalogConfig?.S3 is null`): returns `normalizedPath` unchanged (local file or global `/vsis3/` handled by GDAL)
- If per-catalog S3: computes a stable local cache path, downloads from S3 if not cached, returns the local path
- Uses `ConcurrentDictionary` to prevent double-download on concurrent requests
- Cache path: `{cacheRoot}/s3-cache/{catalogSlug}/{storageKey}/normalized_3857.tif`
- Creates S3 client from the per-catalog config merged with global defaults

### Step 7: Update `CatalogService`

**File:** `Services/CatalogService.cs`

- `CreateCatalogAsync`: store `input.StorageConfig` on new `Catalog` entity
- `UpdateCatalogAsync`: store `input.StorageConfig` on updated `Catalog` entity
- All `storageFactory.GetStorage()` calls: pass `catalog.StorageConfig`
- `UploadSingleImageAsync`: per-catalog S3 stores `NormalizedPath` as `/vsis3/{bucket}/...` (same format as global S3)
- `RemoveImageAsync`: also delete the local cache file for the image
- `DeleteCatalogAsync`: also delete the catalog's cache directory

### Step 8: Update `TileService`

**File:** `Services/TileService.cs`

- Inject `StoragePathResolver`
- Query `Catalog.StorageProvider` and `Catalog.StorageConfig` alongside `NormalizedPath`
- Resolve each path before calling `tileRendererCache.RenderTile`:

```csharp
var imageData = await dbContext.CatalogImages
    .AsNoTracking()
    .Where(i => i.Catalog.Slug == catalogSlug)
    .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
    .Select(i => new {
        i.NormalizedPath,
        i.StorageProvider,
        CatalogSlug = i.Catalog.Slug,
        i.Catalog.StorageConfig
    })
    .ToListAsync(cancellationToken);

var resolvedPaths = new List<string>();
foreach (var img in imageData)
{
    string resolved = await pathResolver.ResolvePathAsync(
        img.NormalizedPath, img.StorageProvider, img.CatalogSlug, img.StorageConfig);
    resolvedPaths.Add(resolved);
}

byte[]? content = await Task.Run(
    () => tileRendererCache.RenderTile(resolvedPaths, x, y, z), ct);
```

### Step 9: Update thumbnail endpoint

**File:** `Program.cs` (lines 208-235)

After fetching the `CatalogImage`, resolve the path via `StoragePathResolver` before generating the thumbnail. Inject `StoragePathResolver` via the endpoint or request services.

### Step 10: Register new services in DI

**File:** `Program.cs` — add:
```csharp
builder.Services.AddScoped<StoragePathResolver>();
```

### Step 11: UI — create catalog form

**File:** `Pages/Admin/_CreateCatalogForm.cshtml`

Add conditional provider-specific config fields after the provider dropdown (togglable via JavaScript, same pattern as `Settings.cshtml`):

**Local fields** (shown when `"Local"` selected):
- Root path (optional, inherits global if blank)

**S3 fields** (shown when `"S3"` selected):
- Bucket name
- Region
- Access key ID
- Secret access key
- Endpoint URL
- Local cache root directory

### Step 12: UI — catalog edit form

**File:** `Pages/Admin/Catalogs/Details.cshtml` — add same config fields to the edit form

**File:** `Pages/Admin/Catalogs/Details.cshtml.cs` — `PopulateUpdateCatalog` includes `StorageConfig` from existing catalog; `OnPostUpdateAsync` passes `UpdateCatalog.StorageConfig`

### Step 13: EF Core migration

Run `dotnet ef migrations add AddCatalogStorageConfig` to generate the migration adding the JSON column.

### Step 14: Tests

**File:** `Tests/Tests/Server/CatalogServiceTests.cs`
- Update factory setup for new `GetStorage` signature
- Add test: `CreateCatalogAsync_WithStorageConfig_SavesConfig`
- Add test: `UploadImagesAsync_WithPerCatalogS3_StoresS3PathInNormalizedPath`

**File:** `Tests/Tests/Server/StoragePathResolverTests.cs` (new)
- Test: `ResolvePathAsync_WithLocalProvider_ReturnsPathUnchanged`
- Test: `ResolvePathAsync_WithGlobalS3_ReturnsPathUnchanged`
- Test: `ResolvePathAsync_WithPerCatalogS3_DownloadsToLocalCache`
- Test: `ResolvePathAsync_WithCachedFile_SkipsDownload`

## Files Changed Summary

| File | Action |
|------|--------|
| `Models/CatalogStorageConfig.cs` | **New** |
| `Services/StoragePathResolver.cs` | **New** |
| `Models/Catalog.cs` | Edit: add `StorageConfig` property |
| `Models/CreateCatalogInput.cs` | Edit: add `StorageConfig` property |
| `Models/UpdateCatalogInput.cs` | Edit: add `StorageConfig` property |
| `Data/ServerDbContext.cs` | Edit: JSON column config for `StorageConfig` |
| `Services/IStorageFactory.cs` | Edit: `GetStorage` signature change |
| `Services/StorageFactory.cs` | Edit: remove singletons, merge config |
| `Services/LocalFileStorage.cs` | Edit: add POCO constructor overload |
| `Services/S3Storage.cs` | Edit: add POCO constructor overload |
| `Services/CatalogService.cs` | Edit: pass/use `StorageConfig` throughout |
| `Services/TileService.cs` | Edit: resolve paths via `StoragePathResolver` |
| `Program.cs` | Edit: register `StoragePathResolver`, update thumbnail endpoint |
| `Pages/Admin/_CreateCatalogForm.cshtml` | Edit: add provider-specific config fields |
| `Pages/Admin/Catalogs/Details.cshtml` | Edit: add config fields to edit form |
| `Pages/Admin/Catalogs/Details.cshtml.cs` | Edit: populate/save `StorageConfig` |
| `Tests/Tests/Server/CatalogServiceTests.cs` | Edit: update + add tests |
| `Tests/Tests/Server/StoragePathResolverTests.cs` | **New** |
| — (autogenerated) | EF Core migration |

## Files NOT Changing

- `TileRendererCache.cs` — path-based, unchanged (per-catalog S3 paths are resolved to local before reaching it)
- `Pages/Admin/Settings.cshtml` + `.cshtml.cs` — global settings page unchanged; still provides defaults
- `Models/AppSetting.cs`, `Models/StorageSettingsInput.cs` — global settings models unchanged
- `Services/IStorage.cs` — interface contract unchanged
- `Pages/Admin/Catalogs/Create.cshtml` + `.cshtml.cs` — lightweight page, delegates to `_CreateCatalogForm`

## Settings Hierarchy (Merge Strategy)

```
Global defaults (appsettings.json + AppSettings DB)
  └── Per-catalog overrides (Catalog.StorageConfig)
        └── Only non-null properties override the global value
```

If a catalog's `S3StorageConfig.BucketName` is set but `Region` is null, `Region` inherits from global settings. This allows minimal config per catalog (just the differing properties).

## Edge Cases

- **Changing provider or config on existing catalog**: Only affects new uploads. Existing images retain their paths. UI already warns about this.
- **Concurrent first tile requests**: `StoragePathResolver` uses `ConcurrentDictionary` to guard downloads — only one download per file.
- **Cache cleanup on delete**: `RemoveImageAsync` and `DeleteCatalogAsync` clean up local cache files.
- **No per-catalog config**: Catalog uses global defaults (existing behavior preserved).
- **Restart with existing cache**: Cache directory persists across restarts. Files are validated by existence check (no staleness tracking — simplest approach).
