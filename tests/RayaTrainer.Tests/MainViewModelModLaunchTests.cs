using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class MainViewModelModLaunchTests
{
    [Fact]
    public void ModLaunchMenuIncludesNoModEntryByDefault()
    {
        var viewModel = LoadViewModel();

        var entry = Assert.Single(viewModel.GameLaunch.ModLaunchEntries);
        Assert.Equal("无 MOD", entry.DisplayName);
        Assert.Equal(string.Empty, entry.SkudefPath);
        Assert.Same(entry, viewModel.GameLaunch.SelectedModLaunchEntry);
    }

    [Fact]
    public void ModLaunchMenuKeepsNoModEntryBeforeScannedMods()
    {
        var modsRoot = CreateTempDirectory();
        var modDirectory = Path.Combine(modsRoot, "Custom Mod");
        Directory.CreateDirectory(modDirectory);
        var skudefPath = Path.Combine(modDirectory, "CustomMod_1.2.skudef");
        File.WriteAllText(skudefPath, "mod-game 1.12");

        var viewModel = LoadViewModel(modsRoot);

        Assert.Collection(
            viewModel.GameLaunch.ModLaunchEntries,
            entry =>
            {
                Assert.Equal("无 MOD", entry.DisplayName);
                Assert.Equal(string.Empty, entry.SkudefPath);
            },
            entry =>
            {
                Assert.Equal("CustomMod 1.2", entry.DisplayName);
                Assert.Equal(skudefPath, entry.SkudefPath);
            });
        Assert.Equal("无 MOD", viewModel.GameLaunch.SelectedModLaunchEntry?.DisplayName);

        viewModel.GameLaunch.GenerateLauncherArgumentsCommand.Execute(null);

        Assert.DoesNotContain("-modConfig", viewModel.GameLaunch.LauncherArguments);
    }

    private static MainViewModel LoadViewModel(string modsRootPath = "")
    {
        var directory = CreateTempDirectory();
        var settingsPath = Path.Combine(directory, "settings.json");
        var settingsStore = new TrainerAppSettingsStore(settingsPath);
        settingsStore.Save(new TrainerAppSettings(
            string.Empty,
            "-ui",
            30,
            ResourceValueSettings.Default,
            Array.Empty<ReinforcementPreset>(),
            new Dictionary<string, string>(),
            modsRootPath,
            string.Empty));

        return MainViewModel.Load(TestAssets.LoadManifest(), settingsStore);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
