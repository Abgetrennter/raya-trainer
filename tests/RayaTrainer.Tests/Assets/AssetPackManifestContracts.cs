using RayaTrainer.Core.Assets;
using Xunit;

namespace RayaTrainer.Tests.Assets;

public static class AssetPackManifestContracts
{
    [Fact]
    public static void Contract_Manifest_HasRequiredFields()
    {
        var m = new AssetPackManifest(
            SchemaVersion: 1,
            Id: "test",
            Provider: "tester",
            Version: "0.1.0",
            Attribution: "test attribution",
            Assets: new[]
            {
                new AssetPackEntry(Kind: "SecretProtocols", Path: "secret-protocols.txt", Sha256: "0000")
            });
        Assert.Equal(1, m.SchemaVersion);
        Assert.Equal("test", m.Id);
        Assert.Single(m.Assets);
    }

    [Fact]
    public static void Contract_AssetPackException_InheritsException()
    {
        var ex = new AssetPackException("boom");
        Assert.IsAssignableFrom<Exception>(ex);
        Assert.Equal("boom", ex.Message);
    }
}
