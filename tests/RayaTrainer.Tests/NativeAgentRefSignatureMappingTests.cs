using RayaTrainer.Core.Agent;
using Xunit;

namespace RayaTrainer.Tests;

public class NativeAgentRefSignatureMappingTests
{
    [Theory]
    [InlineData("GameClientPointer", "Rva_8D8CE4")]
    [InlineData("GetThingClass", "Rva_3E4230")]
    [InlineData("LevelUpSelected", "Rva_35C200")]
    [InlineData("CreateUnit", "Rva_205240")]
    [InlineData("KillUnit", "Rva_39EA50")]
    [InlineData("PlayerManager", "Rva_8E8C9C")]
    [InlineData("GetCurrentPlayer", "Rva_4393E0")]
    [InlineData("ThingTemplateStore", "Rva_8E6C58")]
    [InlineData("SelectedUnitCode", "Rva_8E9838")]
    [InlineData("ScienceStore", "Rva_8DBBF0")]
    [InlineData("ScienceStoreFindScience", "Rva_1456F0")]
    [InlineData("ScienceStoreFindUpgrade", "Rva_147260")]
    [InlineData("ScienceManagerFindScience", "Rva_43C300")]
    [InlineData("PlayerGetUpgradeStore", "Rva_44A2D0")]
    [InlineData("ScienceManagerHasScience", "Rva_44D7C0")]
    [InlineData("PlayerGrantScience", "Rva_454300")]
    [InlineData("SelectionManager", "Rva_8DB73C")]
    [InlineData("MouseWorldPointer", "Rva_8DAEFC")]
    [InlineData("MouseWorldToMapPosition", "Rva_1ED4A0")]
    [InlineData("ObjectSetPosition", "Rva_3E3D00")]
    [InlineData("OneHitCaller1", "Rva_3AD79E")]
    [InlineData("OneHitCaller2", "Rva_3ADEE2")]
    [InlineData("OneHitCaller3", "Rva_38E651")]
    [InlineData("GameObjectAddUpgrade", "Rva_379650")]
    public void Address_class_refs_map_to_signature_keys(string refName, string expectedKey)
    {
        Assert.True(NativeAgentRefSignatureMapping.TryGetSignatureKey(refName, out var key));
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData("PlayerScienceManagerOffset")]
    [InlineData("SelectionListHeadOffset")]
    [InlineData("SelectionCountOffset")]
    [InlineData("MovementModuleOffset")]
    [InlineData("MovementContainerOffset")]
    [InlineData("ObjectOwnerOffset")]
    [InlineData("BodyOffset")]
    [InlineData("VeterancyOffset")]
    [InlineData("WeaponContainerOffset")]
    [InlineData("UnitStatePrimaryOffset")]
    [InlineData("UnitStateSecondaryOffset")]
    [InlineData("OneHitDamageDeltaMode")]
    [InlineData("DestroySelectionListHeadOffset")]
    [InlineData("ProductionModulesOffset")]
    [InlineData("LocalContextSiblingOffset")]
    [InlineData("RestoreOreCapacityMode")]
    public void Constant_class_refs_have_no_signature(string refName)
    {
        Assert.False(NativeAgentRefSignatureMapping.TryGetSignatureKey(refName, out _));
    }

    [Fact]
    public void Every_native_agent_ref_classified_by_hand_is_covered()
    {
        // EntryNames 的 41 条必须要么在映射表里（地址类），要么明确是常量类。
        var classified = new HashSet<string>(NativeAgentCatalog.EntryNames, StringComparer.OrdinalIgnoreCase);
        foreach (var name in NativeAgentCatalog.EntryNames)
        {
            NativeAgentRefSignatureMapping.TryGetSignatureKey(name, out _);
        }
        // 24 地址类 + 17 常量类 = 41（UpgradeTemplateTypeOffset 属常量类，无签名）
        var addressCount = NativeAgentCatalog.EntryNames
            .Count(n => NativeAgentRefSignatureMapping.TryGetSignatureKey(n, out _));
        Assert.Equal(24, addressCount);
        Assert.Equal(41, NativeAgentCatalog.EntryNames.Count);
    }
}
