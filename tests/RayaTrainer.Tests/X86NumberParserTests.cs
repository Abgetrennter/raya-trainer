using RayaTrainer.Core.Codegen;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class X86NumberParserTests
{
    [Theory]
    [InlineData("#100000", 100000)]
    [InlineData("000186A0", 0x186A0)]
    [InlineData("A", 0xA)]
    [InlineData("d", 0xD)]
    [InlineData("28", 0x28)]
    [InlineData("0x10", 0x10)]
    [InlineData("0x28", 0x28)]
    [InlineData("28h", 0x28)]
    [InlineData("000001BCh", 0x1BC)]
    [InlineData("#40", 40)]
    public void ParseSupportsBootstrapNumberFormats(string input, int expected)
    {
        Assert.Equal(expected, X86NumberParser.ParseInt32(input));
    }

    [Fact]
    public void ParseUInt32KeepsHighBitConstants()
    {
        Assert.Equal(0xAF4C0DA5u, X86NumberParser.ParseUInt32("AF4C0DA5"));
        Assert.Equal(99_999_999u, X86NumberParser.ParseUInt32("#99999999"));
    }
}
