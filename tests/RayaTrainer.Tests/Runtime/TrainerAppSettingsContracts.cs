using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests.Runtime;

public sealed class TrainerAppSettingsContracts : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "raya-tests-" + Guid.NewGuid().ToString("N"));

    public TrainerAppSettingsContracts() => Directory.CreateDirectory(_tempDir);
    public void Dispose() { try { Directory.Delete(_tempDir, recursive: true); } catch { } }

    private string NewPath => TrainerAppSettingsStore.DefaultPath(_tempDir);
    private string LegacyPath => Path.Combine(_tempDir, TrainerAppSettingsStore.LegacySettingsFileName);

    [Fact]
    public void Contract_SettingsFileName_IsRayaTrainerPrefixed()
    {
        Assert.Equal("RayaTrainer.settings.json", TrainerAppSettingsStore.SettingsFileName);
    }

    [Fact]
    public void Contract_LegacySettingsFileName_IsRa3TrainerPrefixed()
    {
        Assert.Equal("Ra3Trainer.settings.json", TrainerAppSettingsStore.LegacySettingsFileName);
    }

    [Fact]
    public void Contract_Load_WhenNoFile_ReturnsDefaultsWithSchemaVersion2()
    {
        var store = new TrainerAppSettingsStore(NewPath);
        var settings = store.Load();
        Assert.Equal(2, settings.SchemaVersion);
        Assert.NotNull(settings.Hotkeys);
        Assert.Empty(settings.Hotkeys);
    }

    [Fact]
    public void Contract_Load_PreservesFileOnDisk()
    {
        var store = new TrainerAppSettingsStore(NewPath);
        var settings = store.Load();
        store.Save(settings);
        Assert.True(File.Exists(NewPath));
    }

    [Fact]
    public void Contract_Load_WhenLegacyFileOnly_MigratesAndKeepsLegacy()
    {
        // Write legacy file with a distinctive hotkey
        var legacyContent = """{"SchemaVersion":1,"Hotkeys":{"FAST_BUILD":"Ctrl+Shift+F1"}}""";
        File.WriteAllText(LegacyPath, legacyContent);

        var store = new TrainerAppSettingsStore(NewPath);
        var settings = store.Load();

        // Upgrade: new file written
        Assert.True(File.Exists(NewPath), "new settings file should be created during upgrade");
        // Legacy file preserved (not deleted)
        Assert.True(File.Exists(LegacyPath), "legacy file should be preserved");
        // Hotkey migrated
        Assert.True(settings.Hotkeys.ContainsKey("FAST_BUILD"));
        Assert.Equal("Ctrl+Shift+F1", settings.Hotkeys["FAST_BUILD"]);
    }

    [Fact]
    public void Contract_Load_WhenNewFileExists_IgnoresLegacyFile()
    {
        // Both files exist; new file takes priority
        File.WriteAllText(LegacyPath, """{"SchemaVersion":1,"Hotkeys":{"FROM_LEGACY":"Ctrl+Shift+F2"}}""");
        File.WriteAllText(NewPath, """{"SchemaVersion":1,"Hotkeys":{"FROM_NEW":"Ctrl+Shift+F3"}}""");

        var store = new TrainerAppSettingsStore(NewPath);
        var settings = store.Load();

        Assert.True(settings.Hotkeys.ContainsKey("FROM_NEW"));
        Assert.False(settings.Hotkeys.ContainsKey("FROM_LEGACY"));
    }
}
