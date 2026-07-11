using RayaTrainer.Core.Agent;
using Xunit;

namespace RayaTrainer.Tests.Agent;

public static class AgentPipeNameContracts
{
    [Fact]
    public static void Contract_Prefix_IsRayaTrainerAgent()
    {
        Assert.Equal("RayaTrainer.Agent.", AgentPipeName.Prefix);
    }

    [Fact]
    public static void Contract_ForProcessId_ConcatenatesPrefixAndPid()
    {
        Assert.Equal("RayaTrainer.Agent.12345", AgentPipeName.ForProcessId(12345));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public static void Contract_ForProcessId_RejectsNonPositivePid(int invalidPid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentPipeName.ForProcessId(invalidPid));
    }
}
