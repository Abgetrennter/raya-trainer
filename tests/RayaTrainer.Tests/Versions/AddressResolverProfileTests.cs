using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Versions;
using Xunit;

namespace RayaTrainer.Tests.Versions;

public sealed class AddressResolverProfileTests
{
    [Fact]
    public void ResolveKeepsLegacyRa3112ExpressionWhenProfileIsProvided()
    {
        var resolver = new AddressResolver(
            0x400000,
            new Dictionary<string, nint>(),
            Ra3VersionProfileRegistry.Ra3112);

        Assert.Equal((nint)0xACFDFE, resolver.Resolve("ra3_1.12.game+6CFDFE"));
    }

    [Fact]
    public void ResolveRejectsLegacyRa3112ExpressionForDifferentProfile()
    {
        var resolver = new AddressResolver(
            0x400000,
            new Dictionary<string, nint>(),
            Ra3VersionProfileRegistry.Ra3113);

        Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve("ra3_1.12.game+6CFDFE"));
    }

    [Fact]
    public void ResolveVerifiedRvaRejectsUnsupportedSymbols()
    {
        var profile = new Ra3VersionProfile
        {
            Id = "test",
            DisplayName = "Test",
            ProcessName = "test.game",
            FileVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Hooks = new Dictionary<string, VersionedAddress>
            {
                ["UnsupportedHook"] = new(
                    "UnsupportedHook",
                    null,
                    AddressSupportStatus.Unsupported,
                    "test")
            },
            RemoteGlobals = new Dictionary<string, VersionedAddress>(),
            EngineFunctions = new Dictionary<string, VersionedAddress>(),
            NativeAgentRefs = new Dictionary<string, VersionedAddress>()
        };

        Assert.Throws<UnsupportedSymbolException>(
            () => profile.ResolveVerifiedRva("Hooks", "UnsupportedHook"));
    }
}
