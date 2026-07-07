using System.IO.Pipes;
using RayaTrainer.Core.Agent;
using Xunit;

namespace RayaTrainer.Tests.Agent;

/// <summary>
/// Verifies AgentInjector.Inject refuses injection when a legacy Ra3Trainer.Agent.&lt;pid&gt; pipe responds.
/// refactor.md §21-22 — no dual-protocol long-term support.
/// </summary>
public sealed class LegacyAgentDetectionContracts
{
    [Fact]
    public void Contract_Inject_WhenLegacyPipeResponds_ReturnsFailureWithRestartGuidance()
    {
        // Pick an unlikely-to-collide PID and stand up a fake legacy pipe server.
        const int fakePid = 42421;
        var legacyPipeName = AgentPipeName.LegacyForProcessId(fakePid);
        using var server = new NamedPipeServerStream(
            legacyPipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.None);
        var acceptTask = Task.Run(() => server.WaitForConnection());

        try
        {
            var injector = new AgentInjector();
            // dummy DLL path — legacy probe must fire after file existence check,
            // so we use a real temp file to pass the validation in step 1 of Inject.
            var dummyDll = Path.GetTempFileName();
            try
            {
                var result = injector.Inject(fakePid, dummyDll, TimeSpan.FromSeconds(5));
                Assert.False(result.Success);
                Assert.Contains("重启游戏", result.Message);
            }
            finally
            {
                File.Delete(dummyDll);
            }
        }
        finally
        {
            // Best-effort cleanup of the waiting accept task
            if (!acceptTask.IsCompleted)
            {
                // Trigger cancellation by disposing the client side (we never connected)
                // The server will dispose when the test scope ends.
            }
        }
    }
}
