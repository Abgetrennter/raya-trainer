using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Tests.Features;

/// <summary>
/// Verifies refactor.md §38 invariant: missing optional packs must not affect startup.
/// Catalogs must still return vanilla entries when pack directory is unavailable.
/// </summary>
public static class EmptyCatalogContracts
{
    [Fact]
    public static void Contract_SecretProtocolCatalog_LoadBuiltInNeverThrows()
    {
        var entries = SecretProtocolCatalog.LoadBuiltIn();
        Assert.NotNull(entries);
        // Vanilla hardcoded records are always present.
        Assert.NotEmpty(entries);
    }

    [Fact]
    public static void Contract_ReinforcementUnitCatalog_LoadBuiltInNeverThrows()
    {
        var entries = ReinforcementUnitCatalog.LoadBuiltIn();
        Assert.NotNull(entries);
        Assert.NotEmpty(entries);
    }

    [Fact]
    public static void Contract_SecretProtocolCatalog_LoadWithCustomFile_NoCustomFile_NeverThrows()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "raya-empty-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var entries = SecretProtocolCatalog.LoadWithCustomFile(tempDir);
            Assert.NotEmpty(entries);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public static void Contract_ReinforcementUnitCatalog_LoadWithCustomFile_NoCustomFile_NeverThrows()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "raya-empty-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var entries = ReinforcementUnitCatalog.LoadWithCustomFile(tempDir);
            Assert.NotEmpty(entries);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
