using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class FeaturePresetViewModelTests
{
    [Fact]
    public void SaveCommand_WithNonEmptyName_CreatesPreset()
    {
        var vm = CreateVm();
        vm.NewPresetName = "战斗配置";

        vm.SaveCommand.Execute(null);

        Assert.Single(vm.PresetNames);
        Assert.Contains("战斗配置", vm.PresetNames);
    }

    [Fact]
    public void SaveCommand_EmptyName_DoesNotExecute()
    {
        var vm = CreateVm();
        vm.NewPresetName = "";
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void ApplyCommand_WithSelection_AppliesPreset()
    {
        var vm = CreateVm();
        vm.NewPresetName = "配置A";
        vm.SaveCommand.Execute(null);
        vm.SelectedPresetName = "配置A";

        vm.ApplyCommand.Execute(null);

        Assert.Equal("已装载「配置A」（无 toggle 变更）", vm.LastAppliedSummary);
    }

    [Fact]
    public void DeleteCommand_RemovesPreset()
    {
        var vm = CreateVm();
        vm.NewPresetName = "临时";
        vm.SaveCommand.Execute(null);
        vm.SelectedPresetName = "临时";

        vm.DeleteCommand.Execute(null);

        Assert.Empty(vm.PresetNames);
    }

    private static FeaturePresetViewModel CreateVm()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var mainVm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));
        return new FeaturePresetViewModel(mainVm);
    }
}
