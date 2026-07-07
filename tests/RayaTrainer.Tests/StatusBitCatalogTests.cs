using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class StatusBitCatalogTests
{
    [Fact]
    public void CatalogContainsOfficialObjectStatusAndModelConditionCounts()
    {
        Assert.Equal(223, StatusBitCatalog.ObjectStatuses.Count);
        Assert.Equal(457, StatusBitCatalog.ModelConditions.Count);
        Assert.Equal(680, StatusBitCatalog.All.Count);
    }

    [Theory]
    [InlineData(StatusBitDomain.ObjectStatus, "STEALTHED", 17)]
    [InlineData(StatusBitDomain.ObjectStatus, "NON_AUTOACQUIRABLE", 176)]
    [InlineData(StatusBitDomain.ObjectStatus, "IGNORING_POWER_DOWN", 200)]
    [InlineData(StatusBitDomain.ObjectStatus, "UNDER_IRON_CURTAIN", 208)]
    [InlineData(StatusBitDomain.ObjectStatus, "EXPLOSIVES_ATTACHED", 209)]
    [InlineData(StatusBitDomain.ModelConditionFlags, "INVISIBLE_STEALTH", 332)]
    [InlineData(StatusBitDomain.ModelConditionFlags, "IRONCURTAIN", 430)]
    public void CatalogUsesOfficialEnumOrderForBitIndexes(StatusBitDomain domain, string name, uint expectedBitIndex)
    {
        var status = StatusBitCatalog.All.Single(item =>
            item.Domain == domain &&
            item.Name.Equals(name, StringComparison.Ordinal));

        Assert.Equal(expectedBitIndex, status.BitIndex);
    }

    [Theory]
    [InlineData(StatusBitDomain.ObjectStatus, "DESTROYED")]
    [InlineData(StatusBitDomain.ObjectStatus, "NOT_IN_WORLD")]
    [InlineData(StatusBitDomain.ObjectStatus, "UNSELECTABLE")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "INVALID")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "ALL")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "DEATH_10")]
    public void CatalogMarksDestructiveOrSpecialStatesAsDangerous(StatusBitDomain domain, string name)
    {
        var status = StatusBitCatalog.All.Single(item =>
            item.Domain == domain &&
            item.Name.Equals(name, StringComparison.Ordinal));

        Assert.Equal(StatusBitRiskLevel.Dangerous, status.RiskLevel);
        Assert.True(status.IsDangerous);
    }

    [Fact]
    public void DefaultVisibleStatusesHideDangerousEntries()
    {
        var visible = StatusBitCatalog.DefaultVisible.ToArray();

        Assert.DoesNotContain(visible, item => item.Name.Equals("DESTROYED", StringComparison.Ordinal));
        Assert.DoesNotContain(visible, item => item.Name.Equals("ALL", StringComparison.Ordinal));
        Assert.Contains(visible, item =>
            item.Domain == StatusBitDomain.ObjectStatus &&
            item.Name.Equals("UNDER_IRON_CURTAIN", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(StatusBitDomain.ObjectStatus, "UNDER_IRON_CURTAIN", "吸收除了HEALING和UNRESISTABLE外的一切伤害")]
    [InlineData(StatusBitDomain.ObjectStatus, "REPAIR_ALLIES_WHEN_IDLE", "闲置时维修友军")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "PREATTACK_A", "PRIMARY_WEAPON瞄准")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "INVISIBLE_STEALTH", "处于隐形中")]
    public void HelpTextUsesEngineReferenceNotes(StatusBitDomain domain, string name, string expectedText)
    {
        var status = StatusBitCatalog.All.Single(item =>
            item.Domain == domain &&
            item.Name.Equals(name, StringComparison.Ordinal));

        Assert.Contains(expectedText, status.HelpText);
    }

    [Theory]
    [InlineData(StatusBitDomain.ObjectStatus, "SWITCHED_WEAPONS")]
    [InlineData(StatusBitDomain.ObjectStatus, "PLAYER_POWER_1")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "IRONCURTAIN")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "TIBERIUM_CRYSTAL_TYPE1")]
    public void CatalogMarksReferenceNoEffectStatesAsHiddenByDefault(StatusBitDomain domain, string name)
    {
        var status = StatusBitCatalog.All.Single(item =>
            item.Domain == domain &&
            item.Name.Equals(name, StringComparison.Ordinal));

        Assert.True(status.IsHiddenByDefault);
        Assert.DoesNotContain(StatusBitCatalog.DefaultVisible, item =>
            item.Domain == domain &&
            item.Name.Equals(name, StringComparison.Ordinal));
    }

    [Fact]
    public void CatalogDoesNotHideUsefulReferenceStatesByDefault()
    {
        var status = StatusBitCatalog.All.Single(item =>
            item.Domain == StatusBitDomain.ObjectStatus &&
            item.Name.Equals("UNDER_IRON_CURTAIN", StringComparison.Ordinal));

        Assert.False(status.IsHiddenByDefault);
        Assert.Contains(StatusBitCatalog.DefaultVisible, item =>
            item.Domain == status.Domain &&
            item.Name.Equals(status.Name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(StatusBitDomain.ObjectStatus, "STEALTHED")]
    [InlineData(StatusBitDomain.ObjectStatus, "CAN_ATTACK_WHILE_STEALTHED")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "INVISIBLE_STEALTH")]
    [InlineData(StatusBitDomain.ObjectStatus, "NON_AUTOACQUIRABLE")]
    [InlineData(StatusBitDomain.ObjectStatus, "SKIRMISH_AI_DO_NOT_ATTACK")]
    [InlineData(StatusBitDomain.ObjectStatus, "NO_ATTACK_FROM_AI")]
    [InlineData(StatusBitDomain.ObjectStatus, "IMMOBILE")]
    [InlineData(StatusBitDomain.ObjectStatus, "IMMOBILE_ALLOW_ROTATE")]
    [InlineData(StatusBitDomain.ObjectStatus, "NO_SPECIAL_ABILITY")]
    [InlineData(StatusBitDomain.ObjectStatus, "SCRAMBLED")]
    [InlineData(StatusBitDomain.ObjectStatus, "IGNORING_POWER_DOWN")]
    [InlineData(StatusBitDomain.ObjectStatus, "UNDER_IRON_CURTAIN")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "WEAPONSET_VETERAN")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "WEAPONSET_ELITE")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "WEAPONSET_HERO")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "ARMORSET_VETERAN")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "ARMORSET_ELITE")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "ARMORSET_HERO")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "PARALYZED")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "AFFECTED_BY_EMP")]
    [InlineData(StatusBitDomain.ObjectStatus, "NO_BRIBE")]
    [InlineData(StatusBitDomain.ObjectStatus, "IMMUNE_TO_BARK")]
    [InlineData(StatusBitDomain.ObjectStatus, "UNATTACKABLE")]
    [InlineData(StatusBitDomain.ObjectStatus, "DEFLECT_INCOMING_FIRE")]
    [InlineData(StatusBitDomain.ObjectStatus, "REPAIR_ALLIES_WHEN_IDLE")]
    public void CatalogMarksFirstBatchModifierCandidatesAsRecommended(StatusBitDomain domain, string name)
    {
        var status = StatusBitCatalog.All.Single(item =>
            item.Domain == domain &&
            item.Name.Equals(name, StringComparison.Ordinal));

        Assert.True(status.IsRecommendedFunction);
        Assert.Contains(StatusBitCatalog.RecommendedFunctions, item =>
            item.Domain == domain &&
            item.Name.Equals(name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(StatusBitDomain.ModelConditionFlags, "INVULNERABLE")]
    [InlineData(StatusBitDomain.ObjectStatus, "SHIELDBODY_ENABLED")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "CHRONORIFT")]
    [InlineData(StatusBitDomain.ModelConditionFlags, "IRONCURTAIN")]
    public void CatalogDoesNotMarkExperimentalOrNoEffectStatesAsRecommended(StatusBitDomain domain, string name)
    {
        var status = StatusBitCatalog.All.Single(item =>
            item.Domain == domain &&
            item.Name.Equals(name, StringComparison.Ordinal));

        Assert.False(status.IsRecommendedFunction);
        Assert.DoesNotContain(StatusBitCatalog.RecommendedFunctions, item =>
            item.Domain == domain &&
            item.Name.Equals(name, StringComparison.Ordinal));
    }
}
