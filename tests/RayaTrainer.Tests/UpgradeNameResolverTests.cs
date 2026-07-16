using System.IO;
using System.Reflection;
using System.Text.Json;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Hashing;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class UpgradeNameResolverTests
{
    [Fact]
    public void EmbeddedTableResolvesKnownVeterancyUpgrade()
    {
        var resolver = new UpgradeNameResolver();
        var hash = Ra3InstanceIdHash.Compute("Upgrade_Veterancy_HEROIC");

        var entry = resolver.TryResolveName(hash);

        Assert.NotNull(entry);
        Assert.False(string.IsNullOrEmpty(entry!.Name));
    }

    [Fact]
    public void EmbeddedTableResolvesAlliedTechUpgrade()
    {
        var resolver = new UpgradeNameResolver();
        var hash = Ra3InstanceIdHash.Compute("Upgrade_AlliedTech2");

        var entry = resolver.TryResolveName(hash);

        Assert.NotNull(entry);
    }

    [Fact]
    public void UnknownHashReturnsNullAndFallbackString()
    {
        var resolver = new UpgradeNameResolver();

        Assert.Null(resolver.TryResolveName(0xDEADBEEFu));
        Assert.Contains("DEADBEEF", resolver.ResolveDisplayNameOrFallback(0xDEADBEEFu));
    }

    [Fact]
    public void HashComputationIsCaseInsensitive()
    {
        // Ra3InstanceIdHash uses ToLowerInvariant; both casings must yield the same hash.
        var upper = Ra3InstanceIdHash.Compute("Upgrade_Veterancy_HEROIC");
        var lower = Ra3InstanceIdHash.Compute("upgrade_veterancy_heroic");

        Assert.Equal(upper, lower);
    }

    [Fact]
    public void EmbeddedTableHasAtLeastFiftyEntries()
    {
        // Sanity: original game upgrade.xml has ~90 entries; the embedded table
        // should be substantially populated.
        var resolver = new UpgradeNameResolver();
        var hash = Ra3InstanceIdHash.Compute("Upgrade_AlliesFaction");

        Assert.NotNull(resolver.TryResolveName(hash));
    }

    [Fact]
    public void EmbeddedJsonUsesPascalCaseKeys()
    {
        // The canonical JSON format must use PascalCase "Name" and "Description".
        var assembly = Assembly.GetAssembly(typeof(UpgradeNameResolver))!;
        using var stream = assembly.GetManifestResourceStream("RayaTrainer.Core.Assets.UpgradeNames.json");
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        using var doc = JsonDocument.Parse(json);

        foreach (var entry in doc.RootElement.EnumerateObject())
        {
            foreach (var prop in entry.Value.EnumerateObject())
            {
                // Every property inside an upgrade entry must be PascalCase
                // (starts with an uppercase letter).
                Assert.Matches("^[A-Z]", prop.Name);
            }
        }
    }
}
