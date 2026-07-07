using RayaTrainer.Core.Assets;
using Xunit;

namespace RayaTrainer.Tests.Assets;

public static class CoronaPackOnDiskContracts
{
    private static string CoronaPackDir => Path.Combine(
        AppContext.BaseDirectory, "Assets", "Catalogs", "Corona");

    [Fact]
    public static void Contract_CoronaPack_PresentAndValidOnDisk()
    {
        Assert.True(Directory.Exists(CoronaPackDir), $"Missing pack dir: {CoronaPackDir}");
        var manifest = AssetPackLoader.LoadManifest(CoronaPackDir);
        Assert.Equal("corona", manifest.Id);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Contains(manifest.Assets, a => a.Kind == "SecretProtocols");
        Assert.Contains(manifest.Assets, a => a.Kind == "Reinforcements");
    }

    [Fact]
    public static void Contract_CoronaPack_AllAssetHashesMatchOnDisk()
    {
        var manifest = AssetPackLoader.LoadManifest(CoronaPackDir);
        foreach (var entry in manifest.Assets)
        {
            using var s = AssetPackLoader.OpenAsset(CoronaPackDir, entry);
            Assert.True(s.Length > 0);
        }
    }
}
