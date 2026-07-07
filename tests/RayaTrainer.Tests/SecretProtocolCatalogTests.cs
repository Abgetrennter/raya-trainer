using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class SecretProtocolCatalogTests
{
    [Fact]
    public void ParseReadsPlayerTechUpgradeAndSpecialPowerRows()
    {
        var protocols = SecretProtocolCatalog.Parse(new[]
        {
            "自定义,盟军,测试协议,PlayerTech_Test,Upgrade_Test,SpecialPower_Test",
            "自定义,神州,超导电枢,Upgrade_CelestialSupplyElectricitySystem",
            "自定义,神州,彻甲惊雷,SpecialPower_Celestial_ElectromagneticGun"
        });

        Assert.Collection(
            protocols,
            protocol =>
            {
                Assert.Equal("PlayerTech_Test", protocol.PlayerTech);
                Assert.Equal("Upgrade_Test", protocol.Upgrade);
                Assert.Equal("SpecialPower_Test", protocol.SpecialPower);
                Assert.True(protocol.CanGrant);
            },
            protocol =>
            {
                Assert.Null(protocol.PlayerTech);
                Assert.Equal("Upgrade_CelestialSupplyElectricitySystem", protocol.Upgrade);
                Assert.True(protocol.CanGrant);
            },
            protocol =>
            {
                Assert.Null(protocol.PlayerTech);
                Assert.Null(protocol.Upgrade);
                Assert.Equal("SpecialPower_Celestial_ElectromagneticGun", protocol.SpecialPower);
                Assert.False(protocol.CanGrant);
            });
    }

    [Fact]
    public void ImportAppendsOnlyNewValidEntriesToCustomFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var existing = new[]
        {
            new SecretProtocolEntry("日冕", "神州", "超导电枢", null, "Upgrade_CelestialSupplyElectricitySystem")
        };

        var result = SecretProtocolCatalog.ImportToCustomFile(
            directory,
            new[]
            {
                "日冕,神州,重复超导电枢,Upgrade_CelestialSupplyElectricitySystem",
                "日冕,神州,穿云定海,Upgrade_CelestialAuxiliaryAimingSystem",
                "bad-line"
            },
            existing);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(1, result.InvalidCount);

        var path = Path.Combine(directory, SecretProtocolCatalog.CustomFileName);
        Assert.Equal("日冕,神州,穿云定海,,Upgrade_CelestialAuxiliaryAimingSystem,", Assert.Single(File.ReadAllLines(path)));
    }

    [Fact]
    public void ExplicitGrantIdsAreDisplayedForManualEntries()
    {
        var protocol = new SecretProtocolEntry(
            "手动",
            "自定义",
            "手动协议",
            null,
            null,
            null,
            0xDD6C4C5B,
            0x33D87C97);

        Assert.True(protocol.CanGrant);
        Assert.Equal("0xDD6C4C5B", protocol.PlayerTechIdText);
        Assert.Equal("0x33D87C97", protocol.UpgradeText);
    }

    [Fact]
    public void BuiltInCatalogIncludesVanillaObjectUpgradeCodes()
    {
        var protocols = SecretProtocolCatalog.LoadBuiltIn();

        Assert.Contains(protocols, protocol => IsVanillaUpgrade(protocol, "Upgrade_AlliedTech2"));
        Assert.Contains(protocols, protocol => IsVanillaUpgrade(protocol, "Upgrade_AlliedTech3"));
        Assert.Contains(protocols, protocol => IsVanillaUpgrade(protocol, "Upgrade_JapanBarracksTech2"));
        Assert.Contains(protocols, protocol => IsVanillaUpgrade(protocol, "Upgrade_JapanBarracksTech3"));
        Assert.Contains(protocols, protocol => IsVanillaUpgrade(protocol, "Upgrade_JapanWarFactoryTech2"));
        Assert.Contains(protocols, protocol => IsVanillaUpgrade(protocol, "Upgrade_JapanWarFactoryTech3"));
        Assert.Contains(protocols, protocol => IsVanillaUpgrade(protocol, "Upgrade_JapanNavalYardTech2"));
        Assert.Contains(protocols, protocol => IsVanillaUpgrade(protocol, "Upgrade_JapanNavalYardTech3"));
    }

    [Fact]
    public void BuiltInCatalogLoadsCoronaProtocolsFromDedicatedResource()
    {
        // Corona protocols are now loaded via AssetPackLoader from the on-disk pack at
        // Assets/Catalogs/Corona/ (refactor Plan 03 Task 5). The old embedded-resource
        // logical name no longer exists.
        var protocols = SecretProtocolCatalog.LoadBuiltIn();
        Assert.Contains(protocols, protocol =>
            protocol.Mod == "日冕" &&
            protocol.Faction == "神州" &&
            protocol.Name == "彻甲惊雷（电磁炮）" &&
            protocol.PlayerTech == "PlayerTech_Celestial_ElectromagneticGun" &&
            protocol.Upgrade == "Upgrade_CelestialElectromagneticGun");
    }

    private static bool IsVanillaUpgrade(SecretProtocolEntry protocol, string upgrade)
    {
        return protocol.Mod == "原版 RA3" &&
            protocol.PlayerTech is null &&
            protocol.Upgrade == upgrade &&
            protocol.CanGrant;
    }
}
