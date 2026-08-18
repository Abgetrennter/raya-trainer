using System.Security.Cryptography;
using System.Text.Json;

namespace RayaTrainer.Core.Assets;

public static class AssetPackLoader
{
    private const string ManifestFileName = "pack.json";
    private const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Enumerate pack directories under <paramref name="root"/>. Returns empty if root missing.</summary>
    public static IReadOnlyList<string> EnumeratePackDirs(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(root).ToList();
    }

    /// <summary>Load and validate pack.json. Throws AssetPackException on schema/hash violation.</summary>
    public static AssetPackManifest LoadManifest(string packDir)
    {
        var manifestPath = Path.Combine(packDir, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new AssetPackException($"pack.json missing in {packDir}");

        AssetPackManifestDto? dto;
        try
        {
            var json = File.ReadAllText(manifestPath);
            dto = JsonSerializer.Deserialize<AssetPackManifestDto>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new AssetPackException($"pack.json malformed in {packDir}: {ex.Message}", ex);
        }

        if (dto is null)
            throw new AssetPackException($"pack.json deserialized to null in {packDir}");
        if (dto.SchemaVersion != SupportedSchemaVersion)
            throw new AssetPackException(
                $"Unsupported schemaVersion {dto.SchemaVersion} in {packDir}; expected {SupportedSchemaVersion}");
        if (string.IsNullOrWhiteSpace(dto.Id))
            throw new AssetPackException($"pack.json id missing in {packDir}");
        if (string.IsNullOrWhiteSpace(dto.Provider))
            throw new AssetPackException($"pack.json provider missing in {packDir}");
        if (string.IsNullOrWhiteSpace(dto.Version))
            throw new AssetPackException($"pack.json version missing in {packDir}");
        if (dto.Attribution is null)
            throw new AssetPackException($"pack.json attribution missing in {packDir}");
        if (dto.Assets is null)
            throw new AssetPackException($"pack.json assets array missing in {packDir}");

        var entries = dto.Assets.Select(a =>
        {
            if (string.IsNullOrWhiteSpace(a.Kind) || string.IsNullOrWhiteSpace(a.Path) || string.IsNullOrWhiteSpace(a.Sha256))
                throw new AssetPackException($"pack.json asset entry incomplete in {packDir}: kind/path/sha256 required");
            return new AssetPackEntry(a.Kind, a.Path, a.Sha256.ToLowerInvariant());
        }).ToList();

        return new AssetPackManifest(
            dto.SchemaVersion, dto.Id, dto.Provider, dto.Version, dto.Attribution, entries);
    }

    /// <summary>Open asset stream. Caller disposes. Verifies SHA256 against manifest entry.</summary>
    public static Stream OpenAsset(string packDir, AssetPackEntry entry)
    {
        var fullPath = Path.Combine(packDir, entry.Path);
        if (!File.Exists(fullPath))
            throw new AssetPackException($"asset missing: {entry.Path} in {packDir}");

        using var fs = File.OpenRead(fullPath);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(fs);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(hex, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new AssetPackException(
                $"asset hash mismatch for {entry.Path}: expected {entry.Sha256}, actual {hex}");

        // Re-open for the caller (previous stream was consumed for hashing).
        return File.OpenRead(fullPath);
    }

    private sealed class AssetPackManifestDto
    {
        public int SchemaVersion { get; init; }
        public string Id { get; init; } = string.Empty;
        public string Provider { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Attribution { get; init; } = string.Empty;
        public List<AssetPackEntryDto> Assets { get; init; } = new();
    }

    private sealed class AssetPackEntryDto
    {
        public string Kind { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
    }
}
