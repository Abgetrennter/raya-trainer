using RayaTrainer.Core.Features;
using RayaTrainer.App.ViewModels;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class ReinforcementUnitPickerSearchTests
{
    [Fact]
    public void FilterSearchesByNameOrCodeCaseInsensitively()
    {
        var filtered = ReinforcementUnitCatalog.Filter(new[]
        {
            new ReinforcementUnitEntry("原版 RA3", "盟军", 0xDDFC28DE, "警犬"),
            new ReinforcementUnitEntry("原版 RA3", "升阳", 0x6586A5A0, "奥米茄百合子"),
            new ReinforcementUnitEntry("原版 RA3", "苏联", 0xAF4C0DA5, "MCV")
        }, "mcv");

        Assert.Single(filtered);
        Assert.Equal(0xAF4C0DA5u, filtered[0].Code);

        filtered = ReinforcementUnitCatalog.Filter(new[]
        {
            new ReinforcementUnitEntry("原版 RA3", "盟军", 0xDDFC28DE, "警犬"),
            new ReinforcementUnitEntry("原版 RA3", "升阳", 0x6586A5A0, "奥米茄百合子"),
            new ReinforcementUnitEntry("原版 RA3", "苏联", 0xAF4C0DA5, "MCV")
        }, "6586a5a0");

        Assert.Single(filtered);
        Assert.Equal("奥米茄百合子", filtered[0].Name);

        filtered = ReinforcementUnitCatalog.Filter(new[]
        {
            new ReinforcementUnitEntry("原版 RA3", "盟军", 0xDDFC28DE, "警犬"),
            new ReinforcementUnitEntry("原版 RA3", "升阳", 0x6586A5A0, "奥米茄百合子"),
            new ReinforcementUnitEntry("原版 RA3", "苏联", 0xAF4C0DA5, "MCV")
        }, "0xDDFC28DE");

        Assert.Single(filtered);
        Assert.Equal("警犬", filtered[0].Name);
    }

    [Fact]
    public void FilterSearchesBySourceIdCaseInsensitively()
    {
        var filtered = ReinforcementUnitCatalog.Filter(new[]
        {
            new ReinforcementUnitEntry("日冕", "神州", 0x19CE7A0E, "守护者坦克", "CUAntiVehicleVehicleTech0"),
            new ReinforcementUnitEntry("日冕", "神州", 0x9C6C7CE6, "磁弩防空系统", "CelestialAntiAirShip")
        }, "antiair");

        Assert.Collection(
            filtered,
            unit => Assert.Equal("CelestialAntiAirShip", unit.SourceId));
    }

    [Fact]
    public void FilterReturnsAllUnitsForBlankSearch()
    {
        var filtered = ReinforcementUnitCatalog.Filter(new[]
        {
            new ReinforcementUnitEntry("原版 RA3", "盟军", 0xDDFC28DE, "警犬"),
            new ReinforcementUnitEntry("原版 RA3", "苏联", 0xAF4C0DA5, "MCV")
        }, "  ");

        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void ViewModelBuildsModTabsAndFactionTabsFromUnits()
    {
        var viewModel = new ReinforcementUnitPickerViewModel(
            CreateUnits(),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Collection(
            viewModel.Mods,
            mod => Assert.Equal("原版 RA3", mod.Name),
            mod => Assert.Equal("起义时刻", mod.Name));

        Assert.NotNull(viewModel.SelectedMod);
        Assert.Equal("原版 RA3", viewModel.SelectedMod.Name);
        Assert.Equal(["全部", "盟军", "苏联"], viewModel.Factions.Select(faction => faction.Name).ToArray());
        Assert.Equal(2, viewModel.FilteredUnits.Count);
    }

    [Fact]
    public void ViewModelRebuildsFactionsWhenSelectedModChanges()
    {
        var viewModel = new ReinforcementUnitPickerViewModel(
            CreateUnits(),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        viewModel.SelectedMod = viewModel.Mods.Single(mod => mod.Name == "起义时刻");

        Assert.Equal(["全部", "盟军"], viewModel.Factions.Select(faction => faction.Name).ToArray());
        Assert.Equal("全部", viewModel.SelectedFaction?.Name);
        Assert.Collection(
            viewModel.FilteredUnits,
            unit => Assert.Equal("冷冻兵团", unit.Name));
    }

    [Fact]
    public void ViewModelFiltersBySelectedFactionAndSearch()
    {
        var viewModel = new ReinforcementUnitPickerViewModel(
            CreateUnits(),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        viewModel.SelectedFaction = viewModel.Factions.Single(faction => faction.Name == "盟军");
        viewModel.SearchText = "ddfc";

        Assert.Collection(
            viewModel.FilteredUnits,
            unit => Assert.Equal("警犬", unit.Name));
    }

    [Fact]
    public void ViewModelBuildsSourceIdVariantsForDuplicateNames()
    {
        var units = new[]
        {
            new ReinforcementUnitEntry("日冕", "苏联", 0xD40F74F6, "基地车", "SovietMCV_Enhanced"),
            new ReinforcementUnitEntry("日冕", "苏联", 0x4A6423C7, "基地车", "SovietMCV_Naval"),
            new ReinforcementUnitEntry("日冕", "神州", 0x19CE7A0E, "守护者坦克", "CUAntiVehicleVehicleTech0")
        };
        var viewModel = new ReinforcementUnitPickerViewModel(
            units,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        viewModel.SelectedUnit = units[0];

        Assert.True(viewModel.HasSelectedUnitVariants);
        Assert.Collection(
            viewModel.SelectedUnitVariants,
            variant => Assert.Equal("SovietMCV_Enhanced", variant.SourceId),
            variant => Assert.Equal("SovietMCV_Naval", variant.SourceId));
    }

    [Fact]
    public void SelectUnitVariantUsesChosenDuplicateId()
    {
        var units = new[]
        {
            new ReinforcementUnitEntry("日冕", "苏联", 0xD40F74F6, "基地车", "SovietMCV_Enhanced"),
            new ReinforcementUnitEntry("日冕", "苏联", 0x4A6423C7, "基地车", "SovietMCV_Naval")
        };
        var viewModel = new ReinforcementUnitPickerViewModel(
            units,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        viewModel.SelectedUnit = units[0];

        viewModel.SelectUnitVariant(viewModel.SelectedUnitVariants.Single(variant => variant.SourceId == "SovietMCV_Naval"));

        Assert.Equal(0x4A6423C7u, viewModel.SelectedUnit?.Code);
        Assert.Equal("SovietMCV_Naval", viewModel.SelectedUnit?.SourceId);
        Assert.False(viewModel.HasSelectedUnitVariants);
    }

    [Fact]
    public void LoadFromFilePersistsCustomEntriesAndRefreshesTabs()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var importPath = Path.Combine(directory, "import.txt");
        Directory.CreateDirectory(directory);
        File.WriteAllLines(importPath, new[]
        {
            "自定义模组,天启军团,0x11111111,自定义坦克"
        });
        var viewModel = new ReinforcementUnitPickerViewModel(CreateUnits(), directory);

        viewModel.LoadFromFile(importPath);

        Assert.Contains(viewModel.Mods, mod => mod.Name == "自定义模组");
        viewModel.SelectedMod = viewModel.Mods.Single(mod => mod.Name == "自定义模组");
        Assert.Equal(["全部", "天启军团"], viewModel.Factions.Select(faction => faction.Name).ToArray());
        Assert.Equal("已导入 1 条单位码，跳过重复 0 条，无效 0 行。已保存到 RayaTrainer.reinforcements.txt。", viewModel.StatusMessage);
        Assert.True(File.Exists(Path.Combine(directory, ReinforcementUnitCatalog.CustomFileName)));
    }

    [Fact]
    public void HelpTextsDescribePickerButtonEffects()
    {
        var viewModel = new ReinforcementUnitPickerViewModel(
            CreateUnits(),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Contains("单位名、单位代码、模组或阵营", viewModel.SearchHelpText);
        Assert.Equal("按单位名、单位代码、模组或阵营搜索", viewModel.SearchPlaceholderText);
        Assert.Contains("合并到自定义单位代码列表", viewModel.ImportHelpText);
        Assert.Contains("RayaTrainer.reinforcements.txt", viewModel.ImportHelpText);
        Assert.Contains("带回主窗口增援单位ID", viewModel.ConfirmHelpText);
        Assert.Contains("不修改主窗口增援单位ID", viewModel.CancelHelpText);
    }

    private static IReadOnlyList<ReinforcementUnitEntry> CreateUnits()
    {
        return new[]
        {
            new ReinforcementUnitEntry("原版 RA3", "盟军", 0xDDFC28DE, "警犬"),
            new ReinforcementUnitEntry("原版 RA3", "苏联", 0xAF4C0DA5, "MCV"),
            new ReinforcementUnitEntry("起义时刻", "盟军", 0xEA8F676B, "冷冻兵团")
        };
    }
}
