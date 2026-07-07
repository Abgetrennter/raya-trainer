using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests.Features;

public sealed class ReinforcementUnitCatalogContracts : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ra3-tests-" + Guid.NewGuid().ToString("N"));

    public ReinforcementUnitCatalogContracts() => Directory.CreateDirectory(_tempDir);
    public void Dispose() { try { Directory.Delete(_tempDir, recursive: true); } catch { } }

    [Fact]
    public void Contract_LoadBuiltIn_ReturnsVanillaAndUprisingEntries()
    {
        var entries = ReinforcementUnitCatalog.LoadBuiltIn();
        Assert.NotEmpty(entries);
        // Vanilla + Uprising data currently lives in code.txt embedded resource.
        // No Corona entries are loaded at runtime (file exists but is not wired in).
        Assert.Contains(entries, e => e.Mod == "日冕");
    }

    [Fact]
    public void Contract_LoadBuiltIn_PicksUpEntriesFromAssetPacksOnDisk()
    {
        var entries = ReinforcementUnitCatalog.LoadBuiltIn();
        var coronaEntries = entries.Where(e => e.Mod == "日冕").ToList();
        Assert.NotEmpty(coronaEntries);
        Assert.Contains(coronaEntries, e => !string.IsNullOrEmpty(e.Name));
        Assert.Contains(coronaEntries, e => e.Code != 0);
    }

    [Fact]
    public void Contract_Merge_FirstEntryWinsOnCodeClash()
    {
        // Dedup key is (Mod, Code) case-insensitive — same Mod + same Code triggers clash
        var a = new[]
        {
            new ReinforcementUnitEntry(Mod: "Vanilla", Faction: "Soviets",
                Code: 0x11111111, Name: "unit-a", SourceId: "a"),
        };
        var b = new[]
        {
            new ReinforcementUnitEntry(Mod: "Vanilla", Faction: "Soviets",
                Code: 0x11111111, Name: "unit-b", SourceId: "b"),
        };
        var merged = ReinforcementUnitCatalog.Merge(a, b);
        var match = merged.Single(e => e.Code == 0x11111111);
        Assert.Equal("Vanilla", match.Mod);
        Assert.Equal("unit-a", match.Name);   // first-wins
    }

    [Fact]
    public void Contract_LoadWithCustomFile_NoCustomFile_BehavesLikeBuiltIn()
    {
        var withoutCustom = ReinforcementUnitCatalog.LoadBuiltIn();
        var withCustomMissing = ReinforcementUnitCatalog.LoadWithCustomFile(_tempDir);
        Assert.Equal(withoutCustom.Count, withCustomMissing.Count);
    }
}
