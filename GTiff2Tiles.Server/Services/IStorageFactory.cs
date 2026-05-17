using GTiff2Tiles.Server.Models;

namespace GTiff2Tiles.Server.Services;

public interface IStorageFactory
{
    IStorage GetStorage(string provider, CatalogStorageConfig? catalogConfig = null);
}
