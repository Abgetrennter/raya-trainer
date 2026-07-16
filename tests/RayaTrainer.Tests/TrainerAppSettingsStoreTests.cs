using RayaTrainer.Core.Features;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class TrainerAppSettingsStoreTests
{
    [Fact]
    public void LoadCreatesDefaultSettingsFileWhenSettingsFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "RayaTrainer.settings.json");
        var store = new TrainerAppSettingsStore(path);
        var defaultHotkeys = new Dictionary<string, string>
        {
            ["增加玩家战场资金"] = "Ctrl+F1",
            ["执行队列"] = "Insert"
        };

        var settings = store.Load(defaultHotkeys);

        Assert.Equal(string.Empty, settings.LauncherPath);
        Assert.Equal("-ui", settings.LauncherArguments);
        Assert.Equal(string.Empty, settings.ModsRootPath);
        Assert.Equal(string.Empty, settings.SelectedModSkudefPath);
        Assert.Equal(30, settings.AttachTimeoutSeconds);
        Assert.Equal(TrainerAppSettingsStore.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(ResourceValueSettings.Default, settings.ResourceValues);
        Assert.Empty(settings.ReinforcementPresets);
        Assert.Equal(defaultHotkeys, settings.Hotkeys);
        Assert.True(File.Exists(path));
        var savedText = File.ReadAllText(path);
        Assert.Contains("\"Hotkeys\"", savedText);
        Assert.Contains("\"SchemaVersion\": 2", savedText);
        Assert.DoesNotContain("\"Runtime\"", savedText);
        Assert.Contains("\"增加玩家战场资金\"", savedText);
        Assert.Contains("\"执行队列\"", savedText);

        var loadedAgain = new TrainerAppSettingsStore(path).Load(defaultHotkeys);
        Assert.Equal(settings.LauncherPath, loadedAgain.LauncherPath);
        Assert.Equal(settings.LauncherArguments, loadedAgain.LauncherArguments);
        Assert.Equal(settings.ModsRootPath, loadedAgain.ModsRootPath);
        Assert.Equal(settings.SelectedModSkudefPath, loadedAgain.SelectedModSkudefPath);
        Assert.Equal(settings.AttachTimeoutSeconds, loadedAgain.AttachTimeoutSeconds);
        Assert.Equal(settings.ResourceValues, loadedAgain.ResourceValues);
        Assert.Equal(settings.ReinforcementPresets, loadedAgain.ReinforcementPresets);
        Assert.Equal(settings.Hotkeys, loadedAgain.Hotkeys);
    }

    [Fact]
    public void DefaultPathUsesApplicationDirectoryAndRayaTrainerSettingsFile()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var path = TrainerAppSettingsStore.DefaultPath(baseDirectory);

        Assert.Equal(Path.Combine(baseDirectory, "RayaTrainer.settings.json"), path);
    }

    [Fact]
    public void SaveAndLoadRoundTripsLauncherSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new TrainerAppSettingsStore(Path.Combine(directory, "settings.json"));
        var expected = new TrainerAppSettings(
            LauncherPath: @"D:\Games\RA3.exe",
            LauncherArguments: "-win",
            AttachTimeoutSeconds: 45,
            ResourceValues: new ResourceValueSettings(123456, 234567, 9),
            ReinforcementPresets:
            [
                new ReinforcementPreset("突击编队",
                [
                    new ReinforcementPresetEntry("Yuriko", 0x6586A5A0, 8, 3),
                    new ReinforcementPresetEntry("MCV", 0xAF4C0DA5, 2, 0)
                ]),
                new ReinforcementPreset("MCV", 0xAF4C0DA5, 2, 0)
            ],
            Hotkeys: new Dictionary<string, string>
            {
                ["增加玩家战场资金"] = "Alt+F1",
                ["执行队列"] = "Ctrl+Insert"
            },
            ModsRootPath: @"D:\Documents\Red Alert 3\Mods",
            SelectedModSkudefPath: @"D:\Documents\Red Alert 3\Mods\CustomMod\CustomMod_1.2.skudef");

        store.Save(expected);
        var loaded = store.Load();

        Assert.Equal(expected.LauncherPath, loaded.LauncherPath);
        Assert.Equal(expected.LauncherArguments, loaded.LauncherArguments);
        Assert.Equal(expected.ModsRootPath, loaded.ModsRootPath);
        Assert.Equal(expected.SelectedModSkudefPath, loaded.SelectedModSkudefPath);
        Assert.Equal(expected.AttachTimeoutSeconds, loaded.AttachTimeoutSeconds);
        Assert.Equal(TrainerAppSettingsStore.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(expected.ResourceValues, loaded.ResourceValues);
        Assert.Equal(2, loaded.ReinforcementPresets.Count);
        Assert.Equal("突击编队", loaded.ReinforcementPresets[0].Name);
        Assert.Collection(
            loaded.ReinforcementPresets[0].Entries,
            entry => Assert.Equal("Yuriko", entry.Name),
            entry => Assert.Equal("MCV", entry.Name));
        Assert.Equal("MCV", loaded.ReinforcementPresets[1].Name);
        Assert.Single(loaded.ReinforcementPresets[1].Entries);
        Assert.Equal(expected.Hotkeys, loaded.Hotkeys);
    }

    [Fact]
    public void LoadMergesChineseHotkeyOverridesWithDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {
              "SchemaVersion": 1,
              "Hotkeys": {
                "增加玩家战场资金": "Alt+F1",
                "执行队列": "Ctrl+Insert",
                "无限电力": ""
              }
            }
            """);
        var defaultHotkeys = new Dictionary<string, string>
        {
            ["增加玩家战场资金"] = "Ctrl+F1",
            ["执行队列"] = "Insert",
            ["无限电力"] = "Ctrl+F2",
            ["无限秘密协议点数"] = "Ctrl+F3"
        };
        var store = new TrainerAppSettingsStore(path);

        var loaded = store.Load(defaultHotkeys);

        Assert.Equal("Alt+F1", loaded.Hotkeys["增加玩家战场资金"]);
        Assert.Equal("Ctrl+Insert", loaded.Hotkeys["执行队列"]);
        Assert.Equal(string.Empty, loaded.Hotkeys["无限电力"]);
        Assert.Equal("Ctrl+F3", loaded.Hotkeys["无限秘密协议点数"]);
    }

    [Fact]
    public void LoadResetsSettingsWithoutSchemaVersion()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {
              "UnsupportedLegacyField": true
            }
            """);
        var store = new TrainerAppSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(TrainerAppSettingsStore.CurrentSchemaVersion, loaded.SchemaVersion);
        var backups = Directory.GetFiles(directory, "RayaTrainer.settings.legacy.*.json");
        Assert.Single(backups);
    }

    [Fact]
    public void SaveAndLoad_SecretProtocolPresets()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new TrainerAppSettingsStore(Path.Combine(directory, "settings.json"));
        var expected = new TrainerAppSettings(
            LauncherPath: "",
            LauncherArguments: "-ui",
            AttachTimeoutSeconds: 30,
            ResourceValues: ResourceValueSettings.Default,
            ReinforcementPresets: [],
            Hotkeys: new Dictionary<string, string>(),
            ModsRootPath: "",
            SelectedModSkudefPath: "",
            SecretProtocolPresets:
            [
                new SecretProtocolQueuePreset("苏联标准",
                [
                    new SecretProtocolPresetEntry("原版", "苏联", "轨道垃圾", 12345678u, 87654321u)
                ]),
                new SecretProtocolQueuePreset("盟军空战",
                [
                    new SecretProtocolPresetEntry("原版", "盟军", "先进航空学", 0xDD6C4C5Bu, 0x33D87C97u),
                    new SecretProtocolPresetEntry("原版", "盟军", "精确空袭", 0xAABBCCDDu, 0x11223344u)
                ])
            ]);

        store.Save(expected);
        var loaded = store.Load();

        Assert.Equal(2, loaded.SecretProtocolPresets.Count);

        var soviet = loaded.SecretProtocolPresets[0];
        Assert.Equal("苏联标准", soviet.Name);
        Assert.Single(soviet.Entries);
        Assert.Equal("原版", soviet.Entries[0].Mod);
        Assert.Equal("苏联", soviet.Entries[0].Faction);
        Assert.Equal("轨道垃圾", soviet.Entries[0].Name);
        Assert.Equal(12345678u, soviet.Entries[0].PlayerTechId);
        Assert.Equal(87654321u, soviet.Entries[0].UpgradeId);

        var allied = loaded.SecretProtocolPresets[1];
        Assert.Equal("盟军空战", allied.Name);
        Assert.Equal(2, allied.Entries.Count);
        Assert.Equal("先进航空学", allied.Entries[0].Name);
        Assert.Equal("精确空袭", allied.Entries[1].Name);
    }

    [Fact]
    public void Load_SecretProtocolPresets_EmptyWhenMissing()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {
              "SchemaVersion": 1,
              "LauncherPath": "D:\\Games\\RA3.exe"
            }
            """);
        var store = new TrainerAppSettingsStore(path);

        var loaded = store.Load();

        Assert.Empty(loaded.SecretProtocolPresets);
    }

    [Fact]
    public void Load_SecretProtocolPresets_SkipsInvalidEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {
              "SchemaVersion": 1,
              "SecretProtocolPresets": [
                {
                  "Name": "混合",
                  "Entries": [
                    { "Mod": "原版", "Faction": "苏联", "Name": "轨道垃圾", "PlayerTechId": 12345678, "UpgradeId": 87654321 },
                    { "Mod": "原版", "Faction": "盟军", "Name": "缺PlayerTechId", "UpgradeId": 11111111 },
                    { "Mod": "原版", "Faction": "盟军", "Name": "缺UpgradeId", "PlayerTechId": 22222222 },
                    { "Mod": "原版", "Faction": "帝国", "Name": "完整条目", "PlayerTechId": 33333333, "UpgradeId": 44444444 }
                  ]
                }
              ]
            }
            """);
        var store = new TrainerAppSettingsStore(path);

        var loaded = store.Load();

        var preset = Assert.Single(loaded.SecretProtocolPresets);
        Assert.Equal("混合", preset.Name);
        Assert.Equal(2, preset.Entries.Count);
        Assert.Equal("轨道垃圾", preset.Entries[0].Name);
        Assert.Equal("完整条目", preset.Entries[1].Name);
    }

    [Fact]
    public void Load_SecretProtocolPresets_SkipsInvalidPreset()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {
              "SchemaVersion": 1,
              "SecretProtocolPresets": [
                {
                  "Name": "无Entries字段",
                  "NotEntries": []
                },
                "not an object",
                {
                  "Name": "有效预设",
                  "Entries": [
                    { "Mod": "原版", "Faction": "苏联", "Name": "轨道垃圾", "PlayerTechId": 12345678, "UpgradeId": 87654321 }
                  ]
                }
              ]
            }
            """);
        var store = new TrainerAppSettingsStore(path);

        var loaded = store.Load();

        var preset = Assert.Single(loaded.SecretProtocolPresets);
        Assert.Equal("有效预设", preset.Name);
        Assert.Single(preset.Entries);
        Assert.Equal("轨道垃圾", preset.Entries[0].Name);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsV2Fields()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "RayaTrainer.settings.json");
        var store = new TrainerAppSettingsStore(path);
        var desired = new Dictionary<string, bool> { ["Power"] = true, ["MAP"] = false };
        var params_ = new Dictionary<string, string> { ["selectedUnit.targetHealth.current"] = "5000" };
        var groupExpand = new Dictionary<string, bool> { ["玩家资源"] = false };
        var presets = new[]
        {
            new FeaturePreset("战斗",
                new FeatureStateSnapshot(desired, params_),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)
        };

        var original = new TrainerAppSettings(
            "C:\\game.exe", "-ui", 30,
            ResourceValueSettings.Default,
            Array.Empty<ReinforcementPreset>(),
            new Dictionary<string, string>(),
            "", "",
            null, false, false,
            IsDarkTheme: false,
            WindowBounds: new WindowBounds(10, 20, 1000, 800, true),
            SelectedPageId: PageIds.SelectedUnit,
            GroupExpandedStates: groupExpand,
            DesiredToggleStates: desired,
            FeatureParameterValues: params_,
            FeaturePresets: presets,
            LastAppliedFeaturePresetName: "战斗");

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(2, loaded.SchemaVersion);
        Assert.False(loaded.IsDarkTheme);
        Assert.Equal(new WindowBounds(10, 20, 1000, 800, true), loaded.WindowBounds);
        Assert.Equal(PageIds.SelectedUnit, loaded.SelectedPageId);
        Assert.False(loaded.GroupExpandedStates["玩家资源"]);
        Assert.True(loaded.DesiredToggleStates["Power"]);
        Assert.False(loaded.DesiredToggleStates["MAP"]);
        Assert.Equal("5000", loaded.FeatureParameterValues["selectedUnit.targetHealth.current"]);
        Assert.Equal("战斗", loaded.FeaturePresets[0].Name);
        Assert.Equal("战斗", loaded.LastAppliedFeaturePresetName);
    }

    [Fact]
    public void Load_MigratesV1ToV2_PreservesFields_AddsDefaults_CreatesBackup()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var v1Json = """
            {
              "SchemaVersion": 1,
              "LauncherPath": "C:\\old.exe",
              "LauncherArguments": "-win",
              "AttachTimeoutSeconds": 45,
              "ResourceValues": { "MoneyAmount": 5000, "PowerValue": 200, "ScPointValue": 10 },
              "Hotkeys": { "Power": "Ctrl+F2" },
              "HidePrimaryActionCard": true,
              "AutoCaptureEnabled": true
            }
            """;
        File.WriteAllText(path, v1Json);

        var store = new TrainerAppSettingsStore(path);
        var loaded = store.Load();

        // v1 字段保留
        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal("C:\\old.exe", loaded.LauncherPath);
        Assert.Equal("-win", loaded.LauncherArguments);
        Assert.Equal(45, loaded.AttachTimeoutSeconds);
        Assert.Equal(5000, loaded.ResourceValues.MoneyAmount);
        Assert.Equal("Ctrl+F2", loaded.Hotkeys["Power"]);
        Assert.True(loaded.HidePrimaryActionCard);
        Assert.True(loaded.AutoCaptureEnabled);

        // v2 字段默认
        Assert.True(loaded.IsDarkTheme);
        Assert.Null(loaded.WindowBounds);
        Assert.Equal(PageIds.Features, loaded.SelectedPageId);
        Assert.Empty(loaded.GroupExpandedStates);
        Assert.Empty(loaded.DesiredToggleStates);
        Assert.Empty(loaded.FeatureParameterValues);
        Assert.Empty(loaded.FeaturePresets);
        Assert.Null(loaded.LastAppliedFeaturePresetName);

        // 备份文件存在（.v1.<timestamp>.json）
        var backups = Directory.GetFiles(dir, "RayaTrainer.settings.v1.*.json");
        Assert.Single(backups);
    }

    [Fact]
    public void Load_CorruptJson_CreatesCorruptBackup_UsesDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        File.WriteAllText(path, "{ this is not valid json }}}");

        var store = new TrainerAppSettingsStore(path);
        var loaded = store.Load();

        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal("", loaded.LauncherPath); // 默认

        var backups = Directory.GetFiles(dir, "RayaTrainer.settings.corrupt.*.json");
        Assert.Single(backups);
    }
}
