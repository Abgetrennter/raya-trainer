using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests;

public class SignatureFirstCatalogTests
{
    private static Ra3VersionProfile BuildTestProfile()
    {
        // Build a profile whose NativeAgentRefs use distinct dummy RVAs so we can
        // observe which ones get overridden by the signature scan.
        var refs = NativeAgentCatalog.EntryNames
            .Select((name, idx) => (name, rva: 0x10000 + idx * 0x10))
            .ToDictionary(
                x => x.name,
                x => Ra3VersionProfileFactory.Verified(x.name, x.rva, "test"),
                StringComparer.OrdinalIgnoreCase);

        return new Ra3VersionProfile
        {
            Id = "test",
            DisplayName = "Test",
            ProcessName = "ra3_1.12.game",
            FileVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1.12" },
            SupportsSignatureScanning = true,
            Hooks = new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase),
            RemoteGlobals = new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase),
            EngineFunctions = new Dictionary<string, VersionedAddress>(StringComparer.OrdinalIgnoreCase),
            NativeAgentRefs = refs
        };
    }

    [Fact]
    public void Null_scan_returns_profile_fixed_rvas()
    {
        var profile = BuildTestProfile();
        var rvas = profile.BuildNativeAgentCatalogRvas(null);
        Assert.Equal(NativeAgentCatalog.ExpectedEntryCount, rvas.Count);
        // GameClientPointer is index 0 in EntryNames, fixed RVA 0x10000
        Assert.Equal(0x10000u, rvas[0]);
    }

    [Fact]
    public void Non_zero_scan_overrides_address_class_entries()
    {
        var profile = BuildTestProfile();
        // Scanner returns VAs; catalog stores RVAs (VA - module base).
        var scanned = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rva_8D8CE4"] = 0x4ABCDEF  // GameClientPointer, VA with module base 0x400000
        };
        var rvas = profile.BuildNativeAgentCatalogRvas(scanned);
        var gameClientIdx = Array.IndexOf(NativeAgentCatalog.EntryNames.ToArray(), "GameClientPointer");
        Assert.Equal(0x4ABCDEFu - (uint)profile.ModuleBaseVa, rvas[gameClientIdx]);
    }

    [Fact]
    public void Scanned_VA_is_converted_to_RVA_in_catalog()
    {
        var profile = BuildTestProfile();
        // Realistic scenario: TW 1.12 PlayerManager global at VA 0xCE8C9C.
        // Profile fixed RVA = 0x8E8C9C. Module base = 0x400000.
        // Scanner returns VA 0xCE8C9C → catalog should store RVA 0x8E8C9C.
        var scanned = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rva_8E8C9C"] = 0xCE8C9C  // PlayerManager VA
        };
        var rvas = profile.BuildNativeAgentCatalogRvas(scanned);
        var playerMgrIdx = Array.IndexOf(NativeAgentCatalog.EntryNames.ToArray(), "PlayerManager");
        Assert.Equal(0x8E8C9Cu, rvas[playerMgrIdx]);  // VA - 0x400000 = RVA
    }

    [Fact]
    public void Zero_scan_value_falls_back_to_profile_rva()
    {
        var profile = BuildTestProfile();
        var scanned = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rva_8D8CE4"] = 0  // scan miss
        };
        var rvas = profile.BuildNativeAgentCatalogRvas(scanned);
        var gameClientIdx = Array.IndexOf(NativeAgentCatalog.EntryNames.ToArray(), "GameClientPointer");
        Assert.Equal(0x10000u, rvas[gameClientIdx]);  // profile fixed value
    }

    [Fact]
    public void Missing_scan_key_falls_back_to_profile_rva()
    {
        var profile = BuildTestProfile();
        var scanned = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            // GameClientPointer's Rva_8D8CE4 not in scan dict at all
            ["Rva_205240"] = 0xDEAD
        };
        var rvas = profile.BuildNativeAgentCatalogRvas(scanned);
        var gameClientIdx = Array.IndexOf(NativeAgentCatalog.EntryNames.ToArray(), "GameClientPointer");
        Assert.Equal(0x10000u, rvas[gameClientIdx]);
    }

    [Fact]
    public void Constant_class_entries_always_use_profile_value()
    {
        var profile = BuildTestProfile();
        var scanned = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["PlayerScienceManagerOffset"] = 0x999  // bogus — this is a constant, not address
        };
        var rvas = profile.BuildNativeAgentCatalogRvas(scanned);
        var offsetIdx = Array.IndexOf(NativeAgentCatalog.EntryNames.ToArray(), "PlayerScienceManagerOffset");
        // PlayerScienceManagerOffset is index 16 in EntryNames order offset region; profile value 0x10000 + index*0x10
        var expectedFixed = (uint)(0x10000 + offsetIdx * 0x10);
        Assert.Equal(expectedFixed, rvas[offsetIdx]);  // NOT overridden by bogus scan
    }

    [Fact]
    public void Parameterless_overload_still_works()
    {
        var profile = BuildTestProfile();
        var rvas = profile.BuildNativeAgentCatalogRvas();
        Assert.Equal(NativeAgentCatalog.ExpectedEntryCount, rvas.Count);
    }
}
