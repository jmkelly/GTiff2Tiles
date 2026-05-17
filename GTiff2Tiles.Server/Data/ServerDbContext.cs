using GTiff2Tiles.Server.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GTiff2Tiles.Server.Data;

public sealed class ServerDbContext(DbContextOptions<ServerDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Catalog> Catalogs => Set<Catalog>();

    public DbSet<CatalogImage> CatalogImages => Set<CatalogImage>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<StoragePreset> StoragePresets => Set<StoragePreset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.Property(setting => setting.Key)
                  .HasMaxLength(128);

            entity.Property(setting => setting.Value)
                  .HasMaxLength(2048);
        });

        modelBuilder.Entity<Catalog>(entity =>
        {
            entity.HasIndex(catalog => catalog.Slug)
                  .IsUnique();

            entity.Property(catalog => catalog.Name)
                  .HasMaxLength(128);

            entity.Property(catalog => catalog.Slug)
                  .HasMaxLength(128);

            entity.Property(catalog => catalog.Description)
                  .HasMaxLength(512);

            entity.Property(catalog => catalog.StorageProvider)
                  .HasMaxLength(32);

            entity.OwnsOne(catalog => catalog.StorageConfig, owned =>
            {
                owned.ToJson();
                owned.OwnsOne(static config => config.Local);
                owned.OwnsOne(static config => config.S3);
            });

            entity.HasMany(catalog => catalog.Images)
                  .WithOne(image => image.Catalog)
                  .HasForeignKey(image => image.CatalogId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StoragePreset>(entity =>
        {
            entity.HasIndex(preset => preset.Name)
                  .IsUnique();

            entity.Property(preset => preset.Name)
                  .HasMaxLength(128);

            entity.Property(preset => preset.Provider)
                  .HasMaxLength(32);

            entity.Property(preset => preset.LocalRootPath)
                  .HasMaxLength(1024);

            entity.Property(preset => preset.S3BucketName)
                  .HasMaxLength(512);

            entity.Property(preset => preset.S3Region)
                  .HasMaxLength(64);

            entity.Property(preset => preset.S3AccessKeyId)
                  .HasMaxLength(256);

            entity.Property(preset => preset.S3SecretAccessKey)
                  .HasMaxLength(256);

            entity.Property(preset => preset.S3EndpointUrl)
                  .HasMaxLength(512);

            entity.Property(preset => preset.S3LocalCacheRoot)
                  .HasMaxLength(1024);
        });

        modelBuilder.Entity<CatalogImage>(entity =>
        {
            entity.HasIndex(image => image.StorageKey)
                  .IsUnique();

            entity.Property(image => image.StorageKey)
                  .HasMaxLength(64);

            entity.Property(image => image.OriginalFileName)
                  .HasMaxLength(260);

            entity.Property(image => image.ContentType)
                  .HasMaxLength(128);

            entity.Property(image => image.OriginalPath)
                  .HasMaxLength(2048);

            entity.Property(image => image.NormalizedPath)
                  .HasMaxLength(2048);

            entity.Property(image => image.CoordinateSystem)
                  .HasMaxLength(32);

            entity.Property(image => image.StorageProvider)
                  .HasMaxLength(32);
        });
    }
}
