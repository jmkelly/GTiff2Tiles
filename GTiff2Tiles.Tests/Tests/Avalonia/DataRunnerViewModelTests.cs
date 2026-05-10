#nullable enable

using GTiff2Tiles.Avalonia.ViewModels;
using GTiff2Tiles.Core.Enums;
using NUnit.Framework;

namespace GTiff2Tiles.Tests.Tests.Avalonia;

[TestFixture]
public class DataRunnerViewModelTests
{
    [Test]
    public async Task GenerateTilesUsesSelectedPathsAndWritesOutput()
    {
        string repoRoot = FindRepoRoot();
        string inputFilePath = Path.Combine(repoRoot, "Examples", "Input", "Input3785.tif");
        string tempRoot = Path.Combine(Path.GetTempPath(), "GTiff2Tiles", nameof(DataRunnerViewModelTests), Guid.NewGuid().ToString("N"));
        string outputDirectoryPath = Path.Combine(tempRoot, "output");
        string tempDirectoryPath = Path.Combine(tempRoot, "temp");

        Directory.CreateDirectory(outputDirectoryPath);
        Directory.CreateDirectory(tempDirectoryPath);

        try
        {
            _ = new SettingsViewModel();

            using var viewModel = new DataRunnerViewModel();
            viewModel.InputDataSelector.SelectorPath = inputFilePath;
            viewModel.OutputDataSelector.SelectorPath = outputDirectoryPath;
            viewModel.MinZoom = 0;
            viewModel.MaxZoom = 0;

            var runSettings = viewModel.Settings.Clone();
            runSettings.CoordinateSystem = CoordinateSystem.Epsg3857;
            runSettings.TempPath = tempDirectoryPath;

            await viewModel.GenerateTiles(runSettings);

            Assert.That(Directory.EnumerateFiles(outputDirectoryPath, "*", SearchOption.AllDirectories), Is.Not.Empty,
                "Expected tile generation to write files into the selected output directory.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GTiff2Tiles.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the GTiff2Tiles repository root from the test directory.");
    }
}
