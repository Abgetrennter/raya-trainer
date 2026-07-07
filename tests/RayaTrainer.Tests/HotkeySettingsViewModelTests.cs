using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Manifest;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class HotkeySettingsViewModelTests
{
    private static TrainerFeature Feature(string rawName, string displayName, string? hotkey) =>
        new(rawName, displayName, hotkey, [], null, null);

    private static IReadOnlyDictionary<string, string> Dict(params (string key, string? value)[] pairs)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            dict[key] = value ?? string.Empty;
        }

        return dict;
    }

    [Fact]
    public void ConstructorPopulatesRowsFromFeaturesAndActions()
    {
        var features = new[]
        {
            Feature("Moeny", "增加玩家战场资金", "Ctrl+F1"),
            Feature("Power", "无限电力", "Ctrl+F2")
        };
        var current = Dict(("Moeny", "Alt+F1"), ("Power", "Ctrl+F2"));
        var defaults = Dict(("Moeny", "Ctrl+F1"), ("Power", "Ctrl+F2"));

        var vm = new HotkeySettingsViewModel(features, current, defaults, _ => { });

        Assert.Contains(vm.Rows, r => r.RawName == "Moeny" && r.CurrentHotkey == "Alt+F1");
        Assert.Contains(vm.Rows, r => r.RawName == "Power" && r.CurrentHotkey == "Ctrl+F2");
        // 动作热键两行始终存在。
        Assert.Contains(vm.Rows, r => r.RawName == HotkeySettingsViewModel.ExecuteReinforcementQueueRawName);
        Assert.Contains(vm.Rows, r => r.RawName == HotkeySettingsViewModel.ReadSelectedUnitCodeRawName);
    }

    [Fact]
    public void ConflictsAreDetectedWhenTwoRowsShareSameHotkey()
    {
        var features = new[]
        {
            Feature("Moeny", "增加玩家战场资金", "Ctrl+F1"),
            Feature("Power", "无限电力", "Ctrl+F2")
        };
        // 两个功能当前都已有热键（来自 current），但互不相同。
        var current = Dict(("Moeny", "Ctrl+F1"), ("Power", "Ctrl+F2"));
        var vm = new HotkeySettingsViewModel(features, current, Dict(("Moeny", "Ctrl+F1"), ("Power", "Ctrl+F2")), _ => { });

        var money = vm.Rows.Single(r => r.RawName == "Moeny");
        var power = vm.Rows.Single(r => r.RawName == "Power");
        Assert.False(money.HasConflict);
        Assert.False(power.HasConflict);

        // 让 Power 改成与 Mooney 相同的组合键 -> 冲突。
        power.CurrentHotkey = "Ctrl+F1";
        vm.RecomputeConflicts();

        Assert.True(money.HasConflict);
        Assert.True(power.HasConflict);
        Assert.Equal("无限电力", money.ConflictWith);
        Assert.Equal("增加玩家战场资金", power.ConflictWith);
    }

    [Fact]
    public void ClearingHotkeyResolvesConflict()
    {
        var features = new[]
        {
            Feature("Moeny", "增加玩家战场资金", "Ctrl+F1"),
            Feature("Power", "无限电力", null)
        };
        var current = Dict(("Moeny", "Ctrl+F1"), ("Power", "Ctrl+F1"));
        var vm = new HotkeySettingsViewModel(features, current, Dict(("Moeny", "Ctrl+F1")), _ => { });
        Assert.True(vm.HasConflict);

        vm.Rows.Single(r => r.RawName == "Power").CurrentHotkey = null;
        vm.RecomputeConflicts();

        Assert.False(vm.HasConflict);
    }

    [Fact]
    public void SaveCommandBlockedWhenConflictExists()
    {
        var features = new[]
        {
            Feature("Moeny", "增加玩家战场资金", "Ctrl+F1"),
            Feature("Power", "无限电力", "Ctrl+F1")
        };
        var vm = new HotkeySettingsViewModel(features, Dict(("Moeny", "Ctrl+F1"), ("Power", "Ctrl+F1")), Dict(), _ => { });

        Assert.True(vm.HasConflict);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SaveInvokesApplyChangesCallbackWithMergedDictionary()
    {
        var features = new[]
        {
            Feature("Moeny", "增加玩家战场资金", "Ctrl+F1")
        };
        IReadOnlyDictionary<string, string>? captured = null;
        var vm = new HotkeySettingsViewModel(features, Dict(("Moeny", "Ctrl+F1")), Dict(("Moeny", "Ctrl+F1")), dict => captured = dict);

        vm.Rows.Single(r => r.RawName == "Moeny").CurrentHotkey = "Alt+F9";
        Assert.True(vm.HasUnsavedChanges);
        Assert.True(vm.SaveCommand.CanExecute(null));

        vm.SaveCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.Equal("Alt+F9", captured!["Moeny"]);
        // 动作热键行也被序列化（空串表示未分配）。
        Assert.True(captured!.ContainsKey(HotkeySettingsViewModel.ExecuteReinforcementQueueRawName));
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void ResetAllRestoresDefaultHotkeys()
    {
        var features = new[]
        {
            Feature("Moeny", "增加玩家战场资金", "Ctrl+F1")
        };
        var vm = new HotkeySettingsViewModel(features, Dict(("Moeny", "Alt+F1")), Dict(("Moeny", "Ctrl+F1")), _ => { });
        Assert.Equal("Alt+F1", vm.Rows.Single(r => r.RawName == "Moeny").CurrentHotkey);

        vm.ResetAllCommand.Execute(null);

        Assert.Equal("Ctrl+F1", vm.Rows.Single(r => r.RawName == "Moeny").CurrentHotkey);
    }

    [Fact]
    public void RowClearCommandSetsCurrentHotkeyToNull()
    {
        var features = new[] { Feature("Moeny", "增加玩家战场资金", "Ctrl+F1") };
        var vm = new HotkeySettingsViewModel(features, Dict(("Moeny", "Ctrl+F1")), Dict(("Moeny", "Ctrl+F1")), _ => { });
        var row = vm.Rows.Single(r => r.RawName == "Moeny");

        Assert.True(row.ClearCommand.CanExecute(null));
        row.ClearCommand.Execute(null);

        Assert.Null(row.CurrentHotkey);
        Assert.False(row.HasHotkey);
        // 清除后清除按钮自身应不可用（已无键可清）。
        Assert.False(row.ClearCommand.CanExecute(null));
    }

    [Fact]
    public void RowClearCommandNotExecutableWhenNoHotkeyAssigned()
    {
        var features = new[] { Feature("Moeny", "增加玩家战场资金", "Ctrl+F1") };
        // 当前未分配热键 -> 清除命令不可用。
        var vm = new HotkeySettingsViewModel(features, Dict(("Moeny", (string?)null)), Dict(("Moeny", "Ctrl+F1")), _ => { });
        var row = vm.Rows.Single(r => r.RawName == "Moeny");

        Assert.Null(row.CurrentHotkey);
        Assert.False(row.ClearCommand.CanExecute(null));
    }

    [Fact]
    public void SaveEmitsEmptyStringForUnassignedRows()
    {
        var features = new[] { Feature("Moeny", "增加玩家战场资金", "Ctrl+F1") };
        IReadOnlyDictionary<string, string>? captured = null;
        var vm = new HotkeySettingsViewModel(features, Dict(), Dict(("Moeny", "Ctrl+F1")), dict => captured = dict);

        // 清空后保存，序列化结果应包含空串值（与配置层「空串=未分配」契约一致）。
        vm.Rows.Single(r => r.RawName == "Moeny").CurrentHotkey = null;
        vm.SaveCommand.Execute(null);

        Assert.NotNull(captured);
        Assert.Equal(string.Empty, captured!["Moeny"]);
    }
}
