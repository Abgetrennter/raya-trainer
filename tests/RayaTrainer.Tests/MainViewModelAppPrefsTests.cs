using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class MainViewModelAppPrefsTests
{
    [Fact]
    public void Load_RestoresThemeFromSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var settings = new TrainerAppSettings("", "-ui", 30) { IsDarkTheme = false };
        new TrainerAppSettingsStore(path).Save(settings);

        var vm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));

        Assert.False(vm.Theme.IsDarkTheme);
    }

    [Fact]
    public void Load_RestoresSelectedPageId()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var settings = new TrainerAppSettings("", "-ui", 30) { SelectedPageId = PageIds.SelectedUnit };
        new TrainerAppSettingsStore(path).Save(settings);

        var vm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));

        Assert.Equal(PageIds.ToIndex(PageIds.SelectedUnit), vm.SelectedPageIndex);
    }

    [Fact]
    public void Load_RestoresDesiredToggleStates_AsPending()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var settings = new TrainerAppSettings("", "-ui", 30)
        {
            DesiredToggleStates = new Dictionary<string, bool> { ["Power"] = true }
        };
        new TrainerAppSettingsStore(path).Save(settings);

        var vm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));

        var powerItem = vm.FeatureToggle.AllFeatureItems()
            .First(f => f.Feature.RawName == "Power");
        Assert.True(powerItem.DesiredEnabled);
        Assert.Null(powerItem.ObservedEnabled); // 未连接 → observed 仍 null
    }

    [Fact]
    public void SelectedPageIdChange_MarksDirty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var vm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));

        vm.SelectedPageIndex = 1;
        vm.Persistence.Flush(); // 同步 flush

        var reloaded = new TrainerAppSettingsStore(path).Load();
        Assert.Equal(PageIds.SelectedUnit, reloaded.SelectedPageId);
    }

    [Fact]
    public void ToggleChange_MarksDirty_PersistsDesiredState()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var vm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));

        // 找一个 toggle 功能（Power），模拟用户开启
        var powerItem = vm.FeatureToggle.AllFeatureItems()
            .First(f => f.Feature.RawName == TrainerFeatureIds.Power);
        powerItem.Command.Execute(null); // 触发 ExecuteAsync → SetDesired(true, false)
        // controller 为 null（未连接），SetDesired 走 suppressApply=false 但 controller null 分支 → desired=true, observed=null

        vm.Persistence.Flush();

        var reloaded = new TrainerAppSettingsStore(path).Load();
        Assert.True(reloaded.DesiredToggleStates.GetValueOrDefault("Power"));
    }

    [Fact]
    public void CaptureParameterValues_IncludesSelectedUnitTargetHealth()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var vm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));

        vm.SelectedUnit.SelectedUnitTargetHealthText = "7500";
        vm.SelectedUnit.SelectedUnitTargetMaxHealthText = "9000";
        vm.Persistence.Flush();

        var reloaded = new TrainerAppSettingsStore(path).Load();
        Assert.Equal("7500", reloaded.FeatureParameterValues["selectedUnit.targetHealth.current"]);
        Assert.Equal("9000", reloaded.FeatureParameterValues["selectedUnit.targetHealth.max"]);
    }

    [Fact]
    public void RestoreParameterValues_RestoresSelectedUnitTargetHealthOnStartup()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var settings = new TrainerAppSettings("", "-ui", 30)
        {
            FeatureParameterValues = new Dictionary<string, string>
            {
                ["selectedUnit.targetHealth.current"] = "3333",
                ["selectedUnit.targetHealth.max"] = "5555"
            }
        };
        new TrainerAppSettingsStore(path).Save(settings);

        var vm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));

        Assert.Equal("3333", vm.SelectedUnit.SelectedUnitTargetHealthText);
        Assert.Equal("5555", vm.SelectedUnit.SelectedUnitTargetMaxHealthText);
    }

    [Fact]
    public void CaptureParameterValues_ReturnsResourceProviderValues()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var vm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));

        // 设置资源值
        vm.FeatureToggle.MoneyAmountText = "5000";
        vm.FeatureToggle.PowerValueText = "200";
        vm.FeatureToggle.ScPointValueText = "10";
        vm.Persistence.Flush();

        var reloaded = new TrainerAppSettingsStore(path).Load();
        Assert.Equal("5000", reloaded.FeatureParameterValues["resources.moneyAmount"]);
        Assert.Equal("200", reloaded.FeatureParameterValues["resources.powerValue"]);
        Assert.Equal("10", reloaded.FeatureParameterValues["resources.scPointValue"]);
    }

    [Fact]
    public void CaptureParameterValues_IncludesTemplateReplacementIds()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var vm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));

        vm.Reinforcement.TemplateReplacement.TargetUnitIdText = "0x12345678";
        vm.Reinforcement.TemplateReplacement.DonorUnitIdText = "0xABCDEF00";
        vm.Persistence.Flush();

        var reloaded = new TrainerAppSettingsStore(path).Load();
        Assert.Equal("0x12345678", reloaded.FeatureParameterValues["templateReplacement.targetUnitId"]);
        Assert.Equal("0xABCDEF00", reloaded.FeatureParameterValues["templateReplacement.donorUnitId"]);
    }

    [Fact]
    public void RestoreParameterValues_RestoresTemplateReplacementIdsOnStartup()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "RayaTrainer.settings.json");
        var settings = new TrainerAppSettings("", "-ui", 30)
        {
            FeatureParameterValues = new Dictionary<string, string>
            {
                ["templateReplacement.targetUnitId"] = "0xDEADBEEF",
                ["templateReplacement.donorUnitId"] = "0xCAFEBABE"
            }
        };
        new TrainerAppSettingsStore(path).Save(settings);

        var vm = MainViewModel.Load(TestAssets.LoadManifest(), new TrainerAppSettingsStore(path));

        Assert.Equal("0xDEADBEEF", vm.Reinforcement.TemplateReplacement.TargetUnitIdText);
        Assert.Equal("0xCAFEBABE", vm.Reinforcement.TemplateReplacement.DonorUnitIdText);
    }
}
