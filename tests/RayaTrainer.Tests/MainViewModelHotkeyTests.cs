using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class MainViewModelHotkeyTests
{
    [Fact]
    public void LoadUsesRawNameHotkeySettingsForFeaturesAndQueueAction()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
              "SchemaVersion": 1,
              "Hotkeys": {
                "Moeny": "Alt+F1",
                "ExecuteReinforcementQueue": "Ctrl+Insert"
              }
            }
            """);

        var viewModel = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(settingsPath));

        var money = viewModel.FeatureToggle.Groups
            .SelectMany(group => group.Features)
            .Single(feature => feature.DisplayName == "增加玩家战场资金");
        Assert.Equal("Alt+F1", money.Hotkey);
        Assert.Equal("执行队列 (Ctrl+Insert)", viewModel.Reinforcement.ExecuteReinforcementQueueButtonText);
    }

    [Fact]
    public void LoadUsesHomeAsDefaultReadSelectedUnitCodeHotkey()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "settings.json");

        var viewModel = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(settingsPath));

        Assert.Equal("读取选中单位 (Home)", viewModel.Reinforcement.ReadSelectedUnitCodeButtonText);
        var savedText = File.ReadAllText(settingsPath);
        Assert.Contains("\"ReadSelectedUnitCode\"", savedText);
        Assert.Contains("\"Home\"", savedText);
    }

    [Fact]
    public void ReloadHotkeysUpdatesFeatureDisplayAndPersistsToSettingsFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
              "SchemaVersion": 1,
              "Hotkeys": {
                "Moeny": "Ctrl+F1"
              }
            }
            """);

        var viewModel = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(settingsPath));
        var money = viewModel.FeatureToggle.Groups
            .SelectMany(group => group.Features)
            .Single(feature => feature.DisplayName == "增加玩家战场资金");
        Assert.Equal("Ctrl+F1", money.Hotkey);

        // 运行时热重载：把资金热键改成 Alt+F9。
        var newHotkeys = new Dictionary<string, string> { ["Moeny"] = "Alt+F9" };
        viewModel.ReloadHotkeys(newHotkeys);

        Assert.Equal("Alt+F9", money.Hotkey);
        // 改动应被持久化到配置文件（RawName 作 key）。
        var savedText = File.ReadAllText(settingsPath);
        Assert.Contains("\"Moeny\"", savedText);
        Assert.Contains("\"Alt+F9\"", savedText);
    }

    [Fact]
    public void ReloadHotkeysRefreshesReinforcementActionButtonText()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");

        var viewModel = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(settingsPath));
        // 默认执行队列热键为 Insert。
        Assert.Equal("执行队列 (Insert)", viewModel.Reinforcement.ExecuteReinforcementQueueButtonText);

        // 运行时改成 Ctrl+Home。
        viewModel.ReloadHotkeys(new Dictionary<string, string> { ["ExecuteReinforcementQueue"] = "Ctrl+Home" });

        Assert.Equal("执行队列 (Ctrl+Home)", viewModel.Reinforcement.ExecuteReinforcementQueueButtonText);
    }

    [Fact]
    public void ClearHotkeyViaFeatureHostBlanksHotkeyAndPersists()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
              "SchemaVersion": 1,
              "Hotkeys": {
                "Moeny": "Ctrl+F1"
              }
            }
            """);

        var viewModel = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(settingsPath));
        var moneyItem = viewModel.FeatureToggle.Groups
            .SelectMany(group => group.Features)
            .Single(feature => feature.DisplayName == "增加玩家战场资金");
        Assert.Equal("Ctrl+F1", moneyItem.Hotkey);
        Assert.True(moneyItem.ClearHotkeyCommand.CanExecute(null));

        // 模拟用户在功能列表右键徽章 → 清除快捷键。
        moneyItem.ClearHotkeyCommand.Execute(null);

        // 徽章显示即时刷新为占位。
        Assert.Null(moneyItem.Hotkey);
        Assert.False(moneyItem.ClearHotkeyCommand.CanExecute(null));
        // 持久化到配置文件：Moeny 键值为空串（契约：空串=未分配）。
        var savedText = File.ReadAllText(settingsPath);
        Assert.Contains("\"Moeny\"", savedText);
        Assert.Contains("\"\"", savedText);
    }

}
