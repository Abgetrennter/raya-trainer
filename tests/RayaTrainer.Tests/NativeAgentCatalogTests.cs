using System.Buffers.Binary;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class NativeAgentCatalogTests
{
    [Fact]
    public void EntryNamesCoverEveryNativeAgentRefInProfile()
    {
        // The catalog entry names are the host<->DLL contract and must cover exactly the
        // NativeAgentRefs keys every profile exposes (1.12 is the reference set).
        var profileNames = Ra3VersionProfileRegistry.Ra3112.NativeAgentRefs.Keys
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var catalogNames = NativeAgentCatalog.EntryNames
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(profileNames, catalogNames);
    }

    [Fact]
    public void ExpectedEntryCountMatchesEntryNamesLength()
    {
        Assert.Equal(NativeAgentCatalog.ExpectedEntryCount, NativeAgentCatalog.EntryNames.Count);
    }

    [Fact]
    public void EncodeProducesCountPlusOrderedRvas()
    {
        var rvas = Enumerable.Range(1, NativeAgentCatalog.ExpectedEntryCount)
            .Select(value => (uint)value)
            .ToArray();

        var payload = NativeAgentCatalog.Encode(rvas);

        // uint32 count + N * uint32 rva
        Assert.Equal(sizeof(uint) * (1 + NativeAgentCatalog.ExpectedEntryCount), payload.Length);
        Assert.Equal(
            (uint)NativeAgentCatalog.ExpectedEntryCount,
            BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, sizeof(uint))));
        for (var index = 0; index < NativeAgentCatalog.ExpectedEntryCount; index++)
        {
            var offset = sizeof(uint) * (1 + index);
            Assert.Equal(
                rvas[index],
                BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, sizeof(uint))));
        }
    }

    [Fact]
    public void EncodeRejectsWrongEntryCount()
    {
        Assert.Throws<ArgumentException>(() =>
            NativeAgentCatalog.Encode([0x8E21A4, 0x40D5B0]));
    }

    [Fact]
    public void Ra3113BuildsNativeAgentCatalogRvasInEntryOrder()
    {
        // The profile must emit RVAs in NativeAgentCatalog.EntryNames order; this is the exact
        // payload the host ships to the DLL via SetNativeCatalog.
        var rvas = Ra3VersionProfileRegistry.Ra3113.BuildNativeAgentCatalogRvas();

        Assert.Equal(NativeAgentCatalog.ExpectedEntryCount, rvas.Count);
        // (ExpectedEntryCount is the constant expected; rvas.Count is the actual.)
        Assert.Equal(0x8E21A4u, rvas[0]); // GameClientPointer
        Assert.Equal(0x40D5B0u, rvas[1]); // GetThingClass
        Assert.Equal(0x385600u, rvas[2]); // LevelUpSelected
        Assert.Equal(0x22E3E0u, rvas[3]); // CreateUnit
        Assert.Equal(0x3C7190u, rvas[4]); // KillUnit
        Assert.Equal(0x8E6650u, rvas[5]); // PlayerManager
        Assert.Equal(0x4627D0u, rvas[6]); // GetCurrentPlayer
        Assert.Equal(0x8F0108u, rvas[7]); // ThingTemplateStore
        Assert.Equal(0x8F2CE8u, rvas[8]); // SelectedUnitCode
        Assert.Equal(0x8E50B0u, rvas[9]); // ScienceStore
        Assert.Equal(0x16FC00u, rvas[10]); // ScienceStoreFindScience
        Assert.Equal(0x171740u, rvas[11]); // ScienceStoreFindUpgrade
        Assert.Equal(0x4657A0u, rvas[12]); // ScienceManagerFindScience
        Assert.Equal(0x4739C0u, rvas[13]); // PlayerGetUpgradeStore
        Assert.Equal(0x1320u, rvas[16]); // PlayerScienceManagerOffset
    }

    [Fact]
    public void Ra3112BuildsNativeAgentCatalogRvasMatchingDefaults()
    {
        // 1.12 catalog values must equal the DLL's compile-time defaults so behavior is
        // identical whether or not the host delivers a catalog for 1.12.
        var rvas = Ra3VersionProfileRegistry.Ra3112.BuildNativeAgentCatalogRvas();

        Assert.Equal(0x8D8CE4u, rvas[0]); // GameClientPointer
        Assert.Equal(0x3E4230u, rvas[1]); // GetThingClass
        Assert.Equal(0x35C200u, rvas[2]); // LevelUpSelected
        Assert.Equal(0x205240u, rvas[3]); // CreateUnit
        Assert.Equal(0x39EA50u, rvas[4]); // KillUnit
    }

    [Fact]
    public void Entry39And40AreUpgradeGrantEntries()
    {
        // Index 39 = GameObjectAddUpgrade, 40 = UpgradeTemplateTypeOffset (object-level
        // upgrade grant). The DLL handler resolves GameObjectAddUpgrade to GameObject_AddUpgrade
        // (ra3_1.12 IDA VA 0x779650 = catalog RVA 0x379650) and reads the Type field at the
        // UpgradeTemplateTypeOffset within UpgradeTemplateDefinition.
        Assert.Equal("GameObjectAddUpgrade", NativeAgentCatalog.EntryNames[39]);
        Assert.Equal("UpgradeTemplateTypeOffset", NativeAgentCatalog.EntryNames[40]);
        Assert.Equal(41, NativeAgentCatalog.ExpectedEntryCount);
    }

    [Fact]
    public void Ra3112GameObjectAddUpgradeRvaIsVerified()
    {
        var rvas = Ra3VersionProfileRegistry.Ra3112.BuildNativeAgentCatalogRvas();

        // Index 39 = GameObjectAddUpgrade. RVA 0x379650 = IDA VA 0x779650 - module base 0x400000.
        // The no-scan catalog path reads the profile value verbatim, so it must be the RVA.
        Assert.Equal(0x379650u, rvas[39]);
    }

    [Fact]
    public void Ra3112UpgradeTemplateTypeOffsetIsVerified()
    {
        var rvas = Ra3VersionProfileRegistry.Ra3112.BuildNativeAgentCatalogRvas();

        // Index 40 = UpgradeTemplateTypeOffset. 1.12 value 0x24 = offset of UpgradeTemplate.Type
        // within UpgradeTemplateDefinition (reached via [UpgradeTemplate+0xC]). OBJECT == 1.
        Assert.Equal(0x24u, rvas[40]);
    }

    [Fact]
    public void EveryProfileResolvesAllNativeAgentRefEntries()
    {
        // Adding a new EntryName requires every profile to provide a value (Verified RVA,
        // even if 0 for "unavailable") so BuildNativeAgentCatalogRvas does not throw.
        foreach (var profile in Ra3VersionProfileRegistry.Profiles)
        {
            var rvas = profile.BuildNativeAgentCatalogRvas();
            Assert.Equal(NativeAgentCatalog.ExpectedEntryCount, rvas.Count);
        }
    }
}
