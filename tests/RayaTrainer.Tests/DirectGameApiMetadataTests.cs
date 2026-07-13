using System.Text.Json;
using RayaTrainer.Core.Agent;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class DirectGameApiMetadataTests
{
    [Fact]
    public void CatalogIsV3NativeOnlyAndCommandsAreUnique()
    {
        using var stream = File.OpenRead(Path.Combine(RepositoryRoot(), "src", "RayaTrainer.Core", "Agent", "apis.json"));

        var catalog = DirectGameApiCatalog.Load(stream);

        Assert.Equal(3, catalog.Version);
        Assert.Equal(33, catalog.Apis.Count);
        Assert.All(catalog.Apis, api => Assert.Equal(DirectGameApiImplementation.Native, api.Implementation));
        Assert.Equal(catalog.Apis.Count, catalog.Apis.Select(api => api.PipeCommand.Value).Distinct().Count());
        Assert.Contains(catalog.Apis, api => api.Name == "ToggleSelectedAttackSpeed" && api.PipeCommand.Value == 42);
        Assert.Contains(catalog.Apis, api => api.Name == "ClearSelectedAttackSpeedEffects" && api.PipeCommand.Value == 44);
        Assert.Contains(catalog.Apis, api => api.Name == "ClearSelectedAttackRangeEffects" && api.PipeCommand.Value == 45);
    }

    [Fact]
    public void SchemaDefinesNativeOwnershipWithoutLegacyRouting()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "RayaTrainer.Core", "Agent", "apis.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var apiProperties = root.GetProperty("properties").GetProperty("apis").GetProperty("items").GetProperty("properties");

        Assert.Equal(3, root.GetProperty("properties").GetProperty("version").GetProperty("const").GetInt32());
        Assert.True(apiProperties.TryGetProperty("implementation", out _));
        Assert.False(apiProperties.TryGetProperty("mailbox", out _));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
