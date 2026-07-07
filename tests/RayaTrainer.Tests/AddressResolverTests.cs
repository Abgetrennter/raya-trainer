using RayaTrainer.Core.Memory;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class AddressResolverTests
{
    [Fact]
    public void ResolveModuleRelativeAddressUsesTargetModuleBase()
    {
        var resolver = new AddressResolver(0x400000, new Dictionary<string, nint>
        {
            ["ID"] = 0x500000,
            ["iEnable"] = 0x500100,
            ["MustCode"] = 0x510000,
            ["MustCode2"] = 0x520000
        });

        Assert.Equal((nint)0xACFDFE, resolver.Resolve("ra3_1.12.game+6CFDFE"));
        Assert.Equal((nint)0x500109, resolver.Resolve("iEnable+9"));
        Assert.Equal((nint)0x510029, resolver.Resolve("MustCode+29"));
        Assert.Equal((nint)0x520700, resolver.Resolve("MustCode2+700"));
    }

    [Fact]
    public void ResolveSymbolAddressesCaseInsensitively()
    {
        var resolver = new AddressResolver(0x400000, new Dictionary<string, nint>
        {
            ["ID"] = 0x500000,
            ["iEnable"] = 0x500100
        });

        Assert.Equal((nint)0x500000, resolver.Resolve("id"));
        Assert.Equal((nint)0x500124, resolver.Resolve("IENABLE+24"));
    }

    [Fact]
    public void ResolveOneKillDataHookUsesObservedModuleBase()
    {
        var resolver = new AddressResolver(0x400000, new Dictionary<string, nint>());

        Assert.Equal((nint)0x6E24E3, resolver.Resolve("ra3_1.12.game+2E24E3"));
    }
}
