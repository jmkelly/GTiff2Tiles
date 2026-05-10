using GTiff2Tiles.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace GTiff2Tiles.Server.Data;

public sealed class ServerDbContext(DbContextOptions<ServerDbContext> options) : DbContext(options)
{
    public DbSet<Catalog> Catalogs => Set<Catalog>();

    public DbSet<CatalogImage> CatalogImages => Set<CatalogImage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

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

            entity.HasMany(catalog => catalog.Images)
                  .WithOne(image => image.Catalog)
                  .HasForeignKey(image => image.CatalogId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(catalog => catalog.ActiveImage)
                  .WithMany()
                  .HasForeignKey(catalog => catalog.ActiveImageId)
                  .OnDelete(DeleteBehavior.SetNull);
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
                  .HasMaxLength(1024);

            entity.Property(image => image.NormalizedPath)
                  .HasMaxLength(1024);

            entity.Property(image => image.CoordinateSystem)
                  .HasMaxLength(32);
        });
    }
}
