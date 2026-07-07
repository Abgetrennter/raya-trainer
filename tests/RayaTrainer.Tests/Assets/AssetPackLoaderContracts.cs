using System.Text;
using RayaTrainer.Core.Assets;
using Xunit;

namespace RayaTrainer.Tests.Assets;

public sealed class AssetPackLoaderContracts : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "raya-tests-" + Guid.NewGuid().ToString("N"));
    public AssetPackLoaderContracts() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string WritePack(string packId, string manifestJson, params (string name, string content)[] files)
    {
        var dir = Path.Combine(_root, packId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pack.json"), manifestJson);
        foreach (var (n, c) in files)
            File.WriteAllText(Path.Combine(dir, n), c, new UTF8Encoding(false));
        return dir;
    }

    private static string Sha(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    }

    [Fact]
    public void Contract_EnumeratePackDirs_NoRoot_ReturnsEmpty()
    {
        var missing = Path.Combine(_root, "does-not-exist");
        Assert.Empty(AssetPackLoader.EnumeratePackDirs(missing));
    }

    [Fact]
    public void Contract_EnumeratePackDirs_EmptyRoot_ReturnsEmpty()
    {
        Assert.Empty(AssetPackLoader.EnumeratePackDirs(_root));
    }

    [Fact]
    public void Contract_EnumeratePackDirs_ReturnsImmediateChildDirsOnly()
    {
        Directory.CreateDirectory(Path.Combine(_root, "pack-a"));
        Directory.CreateDirectory(Path.Combine(_root, "pack-b"));
        Directory.CreateDirectory(Path.Combine(_root, "pack-a", "nested"));
        var dirs = AssetPackLoader.EnumeratePackDirs(_root).OrderBy(p => p).ToList();
        Assert.Equal(2, dirs.Count);
    }

    [Fact]
    public void Contract_LoadManifest_ValidJson_ReturnsManifest()
    {
        var content = "hello";
        var manifestJson = $$"""
        {
          "schemaVersion": 1,
          "id": "test",
          "provider": "tester",
          "version": "0.1.0",
          "attribution": "test",
          "assets": [
            { "kind": "SecretProtocols", "path": "data.txt", "sha256": "{{Sha(content)}}" }
          ]
        }
        """;
        var dir = WritePack("test", manifestJson, ("data.txt", content));

        var m = AssetPackLoader.LoadManifest(dir);

        Assert.Equal(1, m.SchemaVersion);
        Assert.Equal("test", m.Id);
        Assert.Single(m.Assets);
        Assert.Equal("data.txt", m.Assets[0].Path);
    }

    [Fact]
    public void Contract_LoadManifest_MissingFile_ThrowsAssetPackException()
    {
        var dir = Path.Combine(_root, "broken");
        Directory.CreateDirectory(dir);
        var ex = Assert.Throws<AssetPackException>(() => AssetPackLoader.LoadManifest(dir));
        Assert.Contains("pack.json", ex.Message);
    }

    [Fact]
    public void Contract_LoadManifest_BadSchemaVersion_ThrowsAssetPackException()
    {
        var manifestJson = """{"schemaVersion": 99, "id": "x", "provider": "y", "version": "z", "attribution": "a", "assets": []}""";
        var dir = WritePack("bad", manifestJson);
        Assert.Throws<AssetPackException>(() => AssetPackLoader.LoadManifest(dir));
    }

    [Fact]
    public void Contract_OpenAsset_HashMismatch_ThrowsAssetPackException()
    {
        var content = "abc";
        var manifestJson = $$"""
        {
          "schemaVersion": 1, "id": "x", "provider": "y", "version": "1.0.0", "attribution": "a",
          "assets": [{ "kind": "SecretProtocols", "path": "data.txt", "sha256": "deadbeef" }]
        }
        """;
        var dir = WritePack("bad-hash", manifestJson, ("data.txt", content));

        var m = AssetPackLoader.LoadManifest(dir);
        Assert.Throws<AssetPackException>(() => AssetPackLoader.OpenAsset(dir, m.Assets[0]));
    }

    [Fact]
    public void Contract_OpenAsset_HashMatch_ReturnsReadableStream()
    {
        var content = "valid content";
        var manifestJson = $$"""
        {
          "schemaVersion": 1, "id": "x", "provider": "y", "version": "1.0.0", "attribution": "a",
          "assets": [{ "kind": "SecretProtocols", "path": "data.txt", "sha256": "{{Sha(content)}}" }]
        }
        """;
        var dir = WritePack("ok", manifestJson, ("data.txt", content));

        var m = AssetPackLoader.LoadManifest(dir);
        using var s = AssetPackLoader.OpenAsset(dir, m.Assets[0]);
        using var sr = new StreamReader(s);
        Assert.Equal(content, sr.ReadToEnd());
    }
}
