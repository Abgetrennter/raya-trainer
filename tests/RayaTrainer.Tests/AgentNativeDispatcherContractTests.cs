using Xunit;

namespace RayaTrainer.Tests;

public sealed class AgentNativeDispatcherContractTests
{
    [Fact]
    public void NativeHookBridgePumpsDllDispatcher()
    {
        var root = RepositoryRoot();
        var patchManager = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.Agent", "AgentPatchManager.cpp"));
        var hook = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.Agent", "AgentHooks.asm"));
        var dispatcher = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.Agent", "AgentGameThreadDispatcher.cpp"));

        Assert.Contains("BuildTrampoline", patchManager, StringComparison.Ordinal);
        Assert.Contains("AgentNativeHookBridge", patchManager, StringComparison.Ordinal);
        Assert.Contains("call AgentNativeHookHandler", hook, StringComparison.Ordinal);
        Assert.Contains("fxsave [eax]", hook, StringComparison.Ordinal);
        Assert.Contains("fxrstor [eax]", hook, StringComparison.Ordinal);
        Assert.Contains("AgentGameThreadDispatcher::Pump", dispatcher, StringComparison.Ordinal);
        Assert.Contains("InterlockedCompareExchange", dispatcher, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiCatalogGeneratesSingleRouteAndNativeBitmap()
    {
        var root = RepositoryRoot();
        var catalog = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.Core", "Agent", "apis.json"));
        var routing = File.ReadAllText(Path.Combine(root, "src", "RayaTrainer.Agent", "Generated", "AgentGameApi.NativeRouting.generated.h"));

        Assert.Contains("\"implementation\": \"native\"", catalog, StringComparison.Ordinal);
        Assert.Contains("kGeneratedGameApiRoutes", routing, StringComparison.Ordinal);
        Assert.Contains("GeneratedGameApiRoute::Native, // GetThingClass", routing, StringComparison.Ordinal);
        Assert.Contains("GeneratedGameApiRoute::Native, // ReadSelectedUnitCode", routing, StringComparison.Ordinal);
        Assert.Contains("GeneratedGameApiRoute::Native, // GetCurrentPlayer", routing, StringComparison.Ordinal);
        Assert.Contains("kGeneratedNativeGameApiBitmap = 0x0000000003FFFFFFull", routing, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RayaTrainer.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
