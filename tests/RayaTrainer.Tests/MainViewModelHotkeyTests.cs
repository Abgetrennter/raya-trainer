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
                "Money": "Alt+F1",
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
                "Money": "Ctrl+F1"
              }
            }
            """);

        var viewModel = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(settingsPath));
        var money = viewModel.FeatureToggle.Groups
            .SelectMany(group => group.Features)
            .Single(feature => feature.DisplayName == "增加玩家战场资金");
        Assert.Equal("Ctrl+F1", money.Hotkey);

        // 运行时热重载：把资金热键改成 Alt+F9。
        var newHotkeys = new Dictionary<string, string> { ["Money"] = "Alt+F9" };
        viewModel.ReloadHotkeys(newHotkeys);

        Assert.Equal("Alt+F9", money.Hotkey);
        // 改动通过 Persistence 防抖协调器异步写文件，flush 确保持久化完毕。
        viewModel.Persistence.Flush();
        var savedText = File.ReadAllText(settingsPath);
        Assert.Contains("\"Money\"", savedText);
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
                "Money": "Ctrl+F1"
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
        // 持久化到配置文件：Money 键值为空串（契约：空串=未分配）。
        viewModel.Persistence.Flush();
        var savedText = File.ReadAllText(settingsPath);
        Assert.Contains("\"Money\"", savedText);
        Assert.Contains("\"\"", savedText);
    }

    [Fact]
    public void DefaultHotkeysIncludeTrainerControlActions()
    {
        // 主控操作热键（立刻检测 / 装载并启动）默认应有 Ctrl+Alt+D / Ctrl+Alt+L，
        // 并在首次启动时持久化到 settings.json。
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");

        var viewModel = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(settingsPath));

        Assert.Equal("立刻检测 (Ctrl+Alt+D)", viewModel.RefreshProcessButtonText);
        Assert.Equal("装载并启动 (Ctrl+Alt+L)", viewModel.LaunchAndLoadButtonText);

        // 持久化层：默认键应写入 settings.json，便于用户后续编辑。
        var savedText = File.ReadAllText(settingsPath);
        Assert.Contains("\"DetectProcess\"", savedText);
        Assert.Contains("\"Ctrl+Alt+D\"", savedText);
        Assert.Contains("\"LaunchAndLoad\"", savedText);
        Assert.Contains("\"Ctrl+Alt+L\"", savedText);
    }

    [Fact]
    public void ReloadHotkeysRefreshesTrainerControlButtonText()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");

        var viewModel = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(settingsPath));
        // 默认值：Ctrl+Alt+D / Ctrl+Alt+L。
        Assert.Equal("立刻检测 (Ctrl+Alt+D)", viewModel.RefreshProcessButtonText);

        // 运行时改成 F9 / F10。
        viewModel.ReloadHotkeys(new Dictionary<string, string>
        {
            ["DetectProcess"] = "F9",
            ["LaunchAndLoad"] = "F10"
        });

        Assert.Equal("立刻检测 (F9)", viewModel.RefreshProcessButtonText);
        Assert.Equal("装载并启动 (F10)", viewModel.LaunchAndLoadButtonText);

        // 清空：按钮文本应回到无括号形式。
        viewModel.ReloadHotkeys(new Dictionary<string, string>
        {
            ["DetectProcess"] = "",
            ["LaunchAndLoad"] = ""
        });
        Assert.Equal("立刻检测", viewModel.RefreshProcessButtonText);
        Assert.Equal("装载并启动", viewModel.LaunchAndLoadButtonText);
    }

}
