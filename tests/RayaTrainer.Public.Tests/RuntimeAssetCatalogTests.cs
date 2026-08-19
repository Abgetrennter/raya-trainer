using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using RayaTrainer.Core.Features;
using Xunit;

namespace RayaTrainer.Public.Tests;

// Public mirror of the runtime asset bundle hash contract (private side: RayaTrainer.Tests).
// The bundle streams are local build products of scripts/build-runtime-assets.ps1; this test
// locks them against the embedded asset-manifest.json so a rebuilt tree cannot silently drift
// from the published contract. Streams are absent on machines without the RA3 MOD SDK - in CI
// the test skips with the reason recorded (see docs/runtime-assets.md).
public sealed class RuntimeAssetCatalogTests
{
    private const string LogicalManifest = "RayaTrainer.Core.RuntimeAssets.AttributeModifiers.asset-manifest.json";
    private const string StreamPrefix = "RayaTrainer.Core.RuntimeAssets.AttributeModifiers.mod.";

    private static readonly string[] StreamKeys = { "manifest", "bin", "relo", "imp" };

    private sealed class AssetManifest
    {
        public Dictionary<string, StreamEntry> Streams { get; set; } = new();
        public List<TemplateEntry> Templates { get; set; } = new();
    }

    private sealed class StreamEntry
    {
        public string Sha256Upper { get; set; } = string.Empty;
    }

    private sealed class TemplateEntry
    {
        public string Name { get; set; } = string.Empty;
        public string InstanceId { get; set; } = string.Empty;
    }

    private static AssetManifest LoadManifest()
    {
        var assembly = typeof(UpgradeNameResolver).Assembly;
        using var stream = assembly.GetManifestResourceStream(LogicalManifest)
            ?? throw new FileNotFoundException($"Embedded manifest '{LogicalManifest}' not found in {assembly.GetName().Name}.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<AssetManifest>(stream, options)
               ?? throw new InvalidDataException("Embedded manifest deserialized to null.");
    }

    private static byte[] ReadStream(string key)
    {
        var assembly = typeof(UpgradeNameResolver).Assembly;
        var logical = StreamPrefix + key;
        using var stream = assembly.GetManifestResourceStream(logical)
            ?? throw new FileNotFoundException($"Embedded stream '{logical}' not found in {assembly.GetName().Name}.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Sha256Upper(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
    }

    [SkippableFact]
    public void AllFourStreamsMatchFrozenSha256()
    {
        // The projection tree carries source truth only; streams exist once rebuilt with the
        // RA3 MOD SDK (docs/runtime-assets.md). Absent streams mean "nothing to verify" here -
        // enforcement lives in the release pipeline, which force-rebuilds and hashes.
        var assembly = typeof(UpgradeNameResolver).Assembly;
        using var probe = assembly.GetManifestResourceStream(StreamPrefix + "manifest");
        Skip.If(probe is null,
            "Runtime asset streams are not generated in this tree; see docs/runtime-assets.md for rebuild steps.");

        var manifest = LoadManifest();

        foreach (var key in StreamKeys)
        {
            var actual = Sha256Upper(ReadStream(key));
            var expected = manifest.Streams[key].Sha256Upper;
            Assert.True(actual == expected,
                $"Stream '{key}' hash drift: expected {expected}, got {actual}. " +
                "Rebuild via scripts/build-runtime-assets.ps1 and update asset-manifest.json deliberately.");
        }
    }

    [Fact]
    public void TemplateInstanceIdsAreUnique()
    {
        var manifest = LoadManifest();
        var seen = new HashSet<string>();
        foreach (var template in manifest.Templates)
        {
            Assert.True(seen.Add(template.InstanceId),
                $"Duplicate InstanceID '{template.InstanceId}' on template '{template.Name}'.");
        }
        Assert.True(manifest.Templates.Count >= 41,
            $"Bundle must carry at least the 41 ladder templates; found {manifest.Templates.Count}.");
    }

    [Fact]
    public void V1InstanceIdsRemainFrozen()
    {
        // Append-only contract: IDs that entered the save/compatibility contract must never be
        // reused or resemanticized across rebuilds.
        var frozen = new Dictionary<string, string>
        {
            ["Atlas_RateOfFire_200"] = "0xA7D07BF4",
            ["Atlas_RateOfFire_300"] = "0x0825531F",
            ["Atlas_RateOfFire_500"] = "0xD3263026",
            ["Atlas_RateOfFire_900"] = "0x2AC4836D",
            ["Atlas_RateOfFire_10000"] = "0x4436FF35",
            ["Atlas_HealthMult_1000"] = "0x489234A9",
            ["Atlas_DamageMult_1000"] = "0xCA5BECEE",
            // Atlas_DamageMult_200_Stack2 (0x0A720BE5) retired 2026-08: research probe, never
            // product-wired; removed from the bundle, and its ID stays unreused forever.
            ["Atlas_Range_500"] = "0x1AE13538",
            ["Atlas_Speed_200"] = "0x74E774BC",
            ["Atlas_Speed_50"] = "0x488B6315",
            ["Atlas_Speed_0"] = "0xBCF48582",
        };

        var manifest = LoadManifest();
        var byName = new Dictionary<string, string>();
        foreach (var template in manifest.Templates)
        {
            byName[template.Name] = template.InstanceId;
        }

        foreach (var (name, expectedId) in frozen)
        {
            Assert.True(byName.TryGetValue(name, out var actualId), $"Frozen template '{name}' missing.");
            Assert.True(actualId == expectedId, $"Frozen InstanceID drift on '{name}': expected {expectedId}, got {actualId}.");
        }
    }
}
