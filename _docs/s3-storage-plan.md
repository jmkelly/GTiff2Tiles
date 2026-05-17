# S3 GeoTIFF Storage Support — Implementation Plan

## Overview

Add configurable S3 bucket storage for GeoTIFFs alongside the existing local filesystem, with admin UI configuration for both storage backends.

---

## Phase 1: Storage Abstraction Layer

Extract an interface from `LocalFileStorage` so the rest of the system is storage-backend-agnostic.

### `IStorage` interface (new: `Services/IStorage.cs`)

```csharp
public interface IStorage
{
    string GetOriginalRasterPath(string catalogSlug, string storageKey);
    string GetNormalizedRasterPath(string catalogSlug, string storageKey);
    Task SaveUploadAsync(Stream source, string catalogSlug, string storageKey, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
    Task DeleteImageAsync(string catalogSlug, string storageKey, CancellationToken ct);
    Task DeleteCatalogAsync(string catalogSlug, CancellationToken ct);
    string Provider { get; }
}
```

Path return values become opaque URIs/storage keys rather than local filesystem paths. For local: local file path. For S3: `/vsis3/bucket/key` URI for GDAL compatibility or a logical key the storage layer resolves.

### `StorageOptions` (new: `Options/StorageOptions.cs`)

```json
{
  "Storage": {
    "Provider": "Local",
    "Local": {
      "RootPath": "/media/james/Local Disk 4/Code"
    },
    "S3": {
      "BucketName": "my-geotiff-bucket",
      "Region": "us-east-1",
      "AccessKeyId": "",
      "SecretAccessKey": "",
      "EndpointUrl": "",
      "LocalCacheRoot": ""
    }
  }
}
```

### `LocalFileStorage` (modify)

Implement `IStorage`, keep existing local disk logic.

### DI Registration (modify `Program.cs`)

```csharp
builder.Services.AddSingleton<IStorage>(sp =>
{
    StorageOptions opts = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
    return opts.Provider switch
    {
        "S3" => new S3Storage(opts.S3, ...),
        _ => new LocalFileStorage(opts.Local)
    };
});
```

---

## Phase 2: S3 Storage Implementation

### New files

| File | Purpose |
|------|---------|
| `Services/S3Storage.cs` | S3 read/write/delete with GDAL VSI integration + local tile cache |
| `Options/S3StorageOptions.cs` | S3-specific config model |

### NuGet packages

- `AWSSDK.S3`
- `AWSSDK.Extensions.NETCore.Setup`

### Key naming pattern

```
catalogs/{catalogSlug}/{storageKey}/original.tif
catalogs/{catalogSlug}/{storageKey}/normalized_3857.tif
```

### Upload flow

- Stream directly to S3 via `PutObjectAsync`
- Return S3 key as "path"

### GDAL operations (CRS check, warp)

GDAL supports `/vsis3/` natively. Set config options from `S3StorageOptions`:

```csharp
GDAL.SetConfigOption("AWS_ACCESS_KEY_ID", opts.AccessKeyId);
GDAL.SetConfigOption("AWS_SECRET_ACCESS_KEY", opts.SecretAccessKey);
GDAL.SetConfigOption("AWS_REGION", opts.Region);
GDAL.SetConfigOption("AWS_S3_ENDPOINT", opts.EndpointUrl);
```

Return `/vsis3/{bucket}/{key}` for GDAL methods. The `GdalWorker.WarpAsync` can read from `/vsis3/` and write to local temp:

```csharp
string vsiPath = $"/vsis3/{bucketName}/{s3Key}";
string tempLocalPath = Path.GetTempFileName() + ".tif";
await GdalWorker.ConvertGeoTiffToTargetSystemAsync(vsiPath, tempLocalPath, epsg3857);
await using FileStream fs = File.OpenRead(tempLocalPath);
await s3Client.PutObjectAsync(bucketName, normalizedS3Key, fs);
File.Delete(tempLocalPath);
```

### Tile serving (NetVips)

NetVips' `Image.NewFromFile()` does not support S3 URIs. **Approach**: download normalized GeoTIFF from S3 to local cache on first access, serve from cache. Cache directory: `{LocalCacheRoot}/s3-cache/{bucket}/{key}`.

The existing `TileRendererCache` LRU (32 renderers, 15 min expiry) naturally bounds total cached bytes.

### Delete operations

- `DeleteObjectAsync` for individual images
- Delete with prefix for whole catalog

---

## Phase 3: Admin Settings UI

### New files

| File | Purpose |
|------|---------|
| `Pages/Admin/Settings.cshtml` | Settings form |
| `Pages/Admin/Settings.cshtml.cs` | Page model |
| `Models/StorageSettingsInput.cs` | Input model |
| `Models/AppSetting.cs` | EF entity for dynamic settings |

### Settings page features

- Dropdown: Storage Provider (`Local` | `S3`)
- Local selected: show `RootPath` text field
- S3 selected: show `BucketName`, `Region`, `AccessKeyId`, `SecretAccessKey`, `EndpointUrl`, `LocalCacheRoot`
- "Test Connection" button for S3
- Save to `AppSettings` table in EF Core

### Settings persistence

EF Core `AppSetting` entity:

```csharp
public sealed class AppSetting
{
    public string Key { get; set; }   // "Storage:Provider", "Storage:S3:BucketName", etc.
    public string Value { get; set; }
}
```

---

## Phase 4: Integration & Server Modifications

### `CatalogImage` model

| Field | Change |
|-------|--------|
| `OriginalPath` | Increase max length 1024 → 2048 for S3 keys |
| `NormalizedPath` | Increase max length 1024 → 2048 |
| `StorageProvider` **(new)** | `string` — `"Local"` or `"S3"`, per-image |

### `CatalogService`

- Constructor takes `IStorage` instead of `LocalFileStorage`
- `UploadSingleImageAsync`: calls `_storage.SaveUploadAsync`; GDAL reads from `/vsis3/` when S3; writes warp to local temp, uploads normalized; uses existing `Raster(Stream, CoordinateSystem)` for metadata
- `RemoveImageAsync`: calls `_storage.DeleteImageAsync`
- `DeleteCatalogAsync`: calls `_storage.DeleteCatalogAsync`

### `TileRendererCache`

- Accept `IStorage` to download S3 blobs to local cache before NetVips open
- `GenerateThumbnail` — same: download first
- `CachedTileRenderer` — needs local file path (from cache)

### `TileService`

No major changes — uses path strings from DB, which are resolved by storage layer.

### `Program.cs` changes

```csharp
string provider = builder.Configuration["Storage:Provider"] ?? "Local";
if (string.Equals(provider, "S3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<S3StorageOptions>(builder.Configuration.GetSection("Storage:S3"));
    builder.Services.AddSingleton<IStorage, S3Storage>();
}
else
{
    string dataRoot = /* existing logic */;
    builder.Services.Configure<LocalStorageOptions>(opts => { ... });
    builder.Services.AddSingleton<IStorage, LocalFileStorage>();
}
```

GDAL config for S3 at startup:

```csharp
if (storage is S3Storage s3Storage)
    s3Storage.ConfigureGdal();
```

`EnsureStorageLayout()` only runs for local storage.

---

## Key Technical Considerations

| Concern | Solution |
|---------|----------|
| GDAL vs NetVips for S3 | GDAL uses `/vsis3/` (native range requests); NetVips gets a local cached copy |
| Large GeoTIFF download cost | Download-on-first-tile-request; 32-renderer LRU cache bounds total cached bytes |
| MinIO / S3-compatible | Support `EndpointUrl` + `AWS_HTTPS` config options |
| Security | S3 creds stored in `AppSettings` table; support IAM roles on EC2 |
| Range requests | GDAL's `/vsis3/` handles this natively |
| Local cache cleanup | Cleanup on image/catalog deletion; periodic temp cleanup |

---

## File Change Summary

| File | Action |
|------|--------|
| `Server/Services/IStorage.cs` | **Create** |
| `Server/Services/LocalFileStorage.cs` | **Modify** — implement `IStorage` |
| `Server/Services/S3Storage.cs` | **Create** |
| `Server/Options/StorageOptions.cs` | **Create** |
| `Server/Options/S3StorageOptions.cs` | **Create** |
| `Server/Options/LocalStorageOptions.cs` | **Modify** — keep only local props |
| `Server/Models/AppSetting.cs` | **Create** |
| `Server/Models/StorageSettingsInput.cs` | **Create** |
| `Server/Models/CatalogImage.cs` | **Modify** — add `StorageProvider`, increase path lengths |
| `Server/Data/ServerDbContext.cs` | **Modify** — add `AppSettings` DbSet |
| `Server/Pages/Admin/Settings.cshtml` | **Create** |
| `Server/Pages/Admin/Settings.cshtml.cs` | **Create** |
| `Server/Services/CatalogService.cs` | **Modify** — use `IStorage` |
| `Server/Services/TileRendererCache.cs` | **Modify** — handle remote/local |
| `Server/Services/TileService.cs` | **Modify** — minor, storage-aware |
| `Server/Program.cs` | **Modify** — DI registration, startup |
| `Server/appsettings.json` | **Modify** — add `Storage` section |
| `Server/GTiff2Tiles.Server.csproj` | **Modify** — add AWS SDK packages |

---

## Testing Strategy

1. **Unit tests**: `FakeStorage` — in-memory `IStorage` backed by `Dictionary<string, byte[]>` — tests `CatalogService` without disk or network
2. **Integration**: `S3Storage` tests with MinIO Docker container
3. **Manual**: Admin settings UI → configure S3 → upload GeoTIFF → verify tile serving
4. **Edge cases**: Connection loss during upload, invalid S3 credentials, large files, bucket doesn't exist
