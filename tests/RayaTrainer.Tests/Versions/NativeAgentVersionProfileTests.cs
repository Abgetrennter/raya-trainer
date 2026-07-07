using System.Text.RegularExpressions;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Versions;

public sealed partial class NativeAgentVersionProfileTests
{
    [Fact]
    public void NativeAgentRefsCoverHardcodedGameRvasInAgentGameApi()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "RayaTrainer.Agent",
            "AgentGameApi.cpp"));
        var catalogStart = source.IndexOf("constexpr uint32_t kDefaultNativeCatalogRvas", StringComparison.Ordinal);
        var catalogEnd = source.IndexOf("};", catalogStart, StringComparison.Ordinal);
        Assert.True(catalogStart >= 0 && catalogEnd > catalogStart);
        var expectedRvas = CatalogValueRegex()
            .Matches(source[catalogStart..catalogEnd])
            .Select(match =>
            {
                var value = match.Groups["rva"].Value;
                return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToUInt32(value[2..], 16)
                    : Convert.ToUInt32(value, 10);
            })
            .ToArray();
        var profileRvas = Ra3VersionProfileRegistry.Ra3112.BuildNativeAgentCatalogRvas();

        // Entry zero uses the named kGameClientPointerRva constant rather than a literal.
        Assert.Equal(expectedRvas, profileRvas.Skip(1));
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

    [GeneratedRegex(@"^\s*(?<rva>(?:0x[0-9a-f]+)|(?:[0-9]+))u,?\s*//", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CatalogValueRegex();
}
