using RayaTrainer.App.ViewModels;
using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class SecretProtocolPickerViewModelTests
{
    [Fact]
    public void ViewModelBuildsModTabsAndFactionTabsFromProtocols()
    {
        var viewModel = new SecretProtocolPickerViewModel(CreateProtocols());

        Assert.Collection(
            viewModel.Mods,
            mod =>
            {
                Assert.Equal("原版 RA3", mod.Name);
            },
            mod =>
            {
                Assert.Equal("日冕", mod.Name);
            });

        Assert.Equal("原版 RA3", viewModel.SelectedMod?.Name);
        Assert.Collection(
            viewModel.Factions,
            faction =>
            {
                Assert.Equal("全部", faction.Name);
            },
            faction =>
            {
                Assert.Equal("盟军", faction.Name);
            },
            faction =>
            {
                Assert.Equal("苏联", faction.Name);
            });
        Assert.Contains(viewModel.FilteredProtocols, protocol => protocol.PlayerTech == "PlayerTech_Allied_AirPower");

        viewModel.SelectedMod = viewModel.Mods.Single(mod => mod.Name == "日冕");

        Assert.Collection(
            viewModel.Factions,
            faction =>
            {
                Assert.Equal("全部", faction.Name);
            },
            faction =>
            {
                Assert.Equal("神州", faction.Name);
            });
        Assert.Collection(
            viewModel.FilteredProtocols,
            protocol =>
            {
                Assert.Equal("超导电枢", protocol.Name);
            });
    }

    [Fact]
    public void SelectingProtocolEnablesConfirmAndRequestsClose()
    {
        bool? requestedClose = null;
        var viewModel = new SecretProtocolPickerViewModel(CreateProtocols());
        var protocol = viewModel.FilteredProtocols
            .Single(protocol => protocol.PlayerTech == "PlayerTech_Allied_AirPower");
        viewModel.RequestClose += result => requestedClose = result;

        protocol.SelectCommand.Execute(null);
        viewModel.ConfirmCommand.Execute(null);

        Assert.Equal("先进航空学", viewModel.SelectedProtocol?.Name);
        Assert.True(requestedClose);
    }

    [Fact]
    public void HelpTextsDescribePickerEffects()
    {
        var viewModel = new SecretProtocolPickerViewModel(CreateProtocols());

        Assert.Contains("官方协议", viewModel.StatusMessage);
        Assert.Contains("MOD", viewModel.StatusMessage);
        Assert.Contains("带回主窗口秘密协议授予栏", viewModel.ConfirmHelpText);
        Assert.Contains("不修改主窗口", viewModel.CancelHelpText);
    }

    private static IReadOnlyList<SecretProtocolEntry> CreateProtocols()
    {
        return new[]
        {
            new SecretProtocolEntry("原版 RA3", "盟军", "先进航空学", "PlayerTech_Allied_AirPower", "Upgrade_AlliedAirPower"),
            new SecretProtocolEntry("原版 RA3", "苏联", "轨道垃圾 1", "PlayerTech_Soviet_OrbitalRefuse_Rank1", null),
            new SecretProtocolEntry("日冕", "神州", "超导电枢", null, "Upgrade_CelestialSupplyElectricitySystem")
        };
    }
}
