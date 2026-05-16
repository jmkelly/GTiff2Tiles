using System.Reflection;

namespace GTiff2Tiles.Tests.Constants;

internal static class FileSystemEntries
{
    #region Examples

    private const string ExamplesDirectoryName = "Examples";

    private static string ExamplesDirectoryPath => FindExamplesDirectory();

    #endregion

    private static string FindExamplesDirectory()
    {
        DirectoryInfo? directory = new(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                                       ?? throw new InvalidOperationException());

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, ExamplesDirectoryName);
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate '{ExamplesDirectoryName}' from the test assembly path.");
    }

    #region Input

    private const string InputDirectoryName = "Input";

    private static string InputDirectoryPath => Path.Combine(ExamplesDirectoryPath, InputDirectoryName);

    #endregion

    #region Input files

    private const string Input4326FileName = "Input4326.tif";

    internal static string Input4326FilePath => Path.Combine(InputDirectoryPath, Input4326FileName);

    private const string Input3785FileName = "Input3785.tif";

    internal static string Input3785FilePath => Path.Combine(InputDirectoryPath, Input3785FileName);

    private const string Input3395FileName = "Input3395.tif";

    internal static string Input3395FilePath => Path.Combine(InputDirectoryPath, Input3395FileName);

    private const string TileMapResourceXmlName = "tilemapresource.xml";

    internal static string TileMapResourceXmlPath => Path.Combine(InputDirectoryPath, TileMapResourceXmlName);

    #endregion

    #region Output

    private const string OutputDirectoryName = "Output";

    internal const string SlippyMapHtmlFileName = "slippy-map.html";

    internal static string OutputDirectoryPath => Path.Combine(ExamplesDirectoryPath, OutputDirectoryName);

    internal static DirectoryInfo OutputDirectoryInfo => new(OutputDirectoryPath);

    #endregion
}
