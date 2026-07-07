using RayaTrainer.App.Web;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerSavedPresetFileSourceTests
{
    [Fact]
    public void LoadSavedSettingsReadsSettingsFromRepositoryOutputDirectories()
    {
        var root = CreateRepositoryLikeDirectory();
        var currentSettingsPath = Path.Combine(root, "src", "RayaTrainer.App", "bin", "Release", "net8.0-windows", "win-x86", "RayaTrainer.settings.json");
        var savedSettingsPath = Path.Combine(root, "src", "RayaTrainer.App", "bin", "Debug", "net8.0-windows", "win-x86", "RayaTrainer.settings.json");
        SaveSettings(currentSettingsPath, [], []);
        SaveSettings(
            savedSettingsPath,
            [new ReinforcementPreset("保存的调试增援", 0x4B816FC8, 4, 0)],
            [new SecretProtocolQueuePreset("保存的调试协议",
            [
                new SecretProtocolPresetEntry("原版 RA3", "盟军", "先进航空学", 0xDD6C4C5B, 0x33D87C97)
            ])]);
        var source = new TrainerSavedPresetFileSource(
            Path.GetDirectoryName(currentSettingsPath),
            currentSettingsPath);

        var settings = source.LoadSavedSettings();

        var loaded = Assert.Single(settings);
        Assert.Equal("保存的调试增援", Assert.Single(loaded.ReinforcementPresets).Name);
        Assert.Equal("保存的调试协议", Assert.Single(loaded.SecretProtocolPresets).Name);
    }

    private static string CreateRepositoryLikeDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "RayaTrainer.sln"), string.Empty);
        return root;
    }

    private static void SaveSettings(
        string path,
        IReadOnlyList<ReinforcementPreset> reinforcementPresets,
        IReadOnlyList<SecretProtocolQueuePreset> secretProtocolPresets)
    {
        new TrainerAppSettingsStore(path).Save(new TrainerAppSettings(
            LauncherPath: string.Empty,
            LauncherArguments: "-ui",
            AttachTimeoutSeconds: 30,
            ResourceValues: ResourceValueSettings.Default,
            ReinforcementPresets: reinforcementPresets,
            Hotkeys: new Dictionary<string, string>(),
            ModsRootPath: string.Empty,
            SelectedModSkudefPath: string.Empty,
            SecretProtocolPresets: secretProtocolPresets));
    }
}
