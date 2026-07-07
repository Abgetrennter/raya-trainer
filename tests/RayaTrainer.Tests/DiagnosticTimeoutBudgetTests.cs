using RayaTrainer.Core.Runtime;
using Xunit;

namespace RayaTrainer.Tests;

public sealed class DiagnosticTimeoutBudgetTests
{
    [Fact]
    public void CalculateForSingleProbeKeepsLegacyBudget()
    {
        var budget = DiagnosticTimeoutBudget.Calculate(
            attachTimeoutSeconds: 30,
            monitorSeconds: 5,
            prefixScan: false,
            hookCount: 22);

        Assert.Equal(TimeSpan.FromSeconds(45), budget);
    }

    [Fact]
    public void CalculateForPrefixScanAllowsEveryHookPrefixToBeMonitored()
    {
        var budget = DiagnosticTimeoutBudget.Calculate(
            attachTimeoutSeconds: 30,
            monitorSeconds: 5,
            prefixScan: true,
            hookCount: 22);

        Assert.Equal(TimeSpan.FromSeconds(201), budget);
    }

    [Fact]
    public void CalculateForDiagnosticIterationsAllowsEveryFeatureToBeMonitored()
    {
        var budget = DiagnosticTimeoutBudget.Calculate(
            attachTimeoutSeconds: 30,
            monitorSeconds: 5,
            diagnosticIterations: 32,
            includeIterationOverhead: true);

        Assert.Equal(TimeSpan.FromSeconds(264), budget);
    }
}
