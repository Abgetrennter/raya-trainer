using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests.Features;

public sealed class SecretProtocolCatalogContracts : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ra3-tests-" + Guid.NewGuid().ToString("N"));

    public SecretProtocolCatalogContracts() => Directory.CreateDirectory(_tempDir);
    public void Dispose() { try { Directory.Delete(_tempDir, recursive: true); } catch { } }

    [Fact]
    public void Contract_LoadBuiltIn_ReturnsCoronaEntriesPlusVanilla()
    {
        var entries = SecretProtocolCatalog.LoadBuiltIn();
        Assert.NotEmpty(entries);
        Assert.Contains(entries, e => e.Mod == "日冕");
    }

    [Fact]
    public void Contract_LoadBuiltIn_PicksUpEntriesFromAssetPacksOnDisk()
    {
        var entries = SecretProtocolCatalog.LoadBuiltIn();
        var coronaEntries = entries.Where(e => e.Mod == "日冕").ToList();
        Assert.NotEmpty(coronaEntries);
        Assert.Contains(coronaEntries, e => !string.IsNullOrEmpty(e.Name));
    }

    [Fact]
    public void Contract_Merge_FirstEntryWinsOnIdentityClash()
    {
        // Dedup key is (Mod, Identity) case-insensitive — same Mod + same PlayerTech triggers clash
        var a = new[]
        {
            new SecretProtocolEntry(Mod: "ModA", Faction: "F", Name: "n",
                PlayerTech: "PlayerTech_X", Upgrade: null, SpecialPower: null),
        };
        var b = new[]
        {
            new SecretProtocolEntry(Mod: "ModA", Faction: "F", Name: "n-overridden",
                PlayerTech: "PlayerTech_X", Upgrade: null, SpecialPower: null),
        };
        var merged = SecretProtocolCatalog.Merge(a, b);
        var match = merged.Single(e => e.PlayerTech == "PlayerTech_X");
        Assert.Equal("ModA", match.Mod);
        Assert.Equal("n", match.Name);  // first-wins: 'a' has Name="n"
    }

    [Fact]
    public void Contract_LoadWithCustomFile_NoCustomFile_BehavesLikeBuiltIn()
    {
        var withoutCustom = SecretProtocolCatalog.LoadBuiltIn();
        var withCustomMissing = SecretProtocolCatalog.LoadWithCustomFile(_tempDir);
        Assert.Equal(withoutCustom.Count, withCustomMissing.Count);
    }
}
