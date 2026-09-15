namespace RayaTrainer.Core.Runtime;

public static class DiagnosticTimeoutBudget
{
    private const int FinalGraceSeconds = 10;
    private const int DiagnosticIterationOverheadSeconds = 2;

    public static TimeSpan Calculate(
        int attachTimeoutSeconds,
        int monitorSeconds,
        bool prefixScan,
        int hookCount)
    {
        if (attachTimeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachTimeoutSeconds));
        }

        if (monitorSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorSeconds));
        }

        if (hookCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hookCount));
        }

        return Calculate(
            attachTimeoutSeconds,
            monitorSeconds,
            diagnosticIterations: prefixScan ? hookCount + 1 : 1,
            includeIterationOverhead: prefixScan);
    }

    public static TimeSpan Calculate(
        int attachTimeoutSeconds,
        int monitorSeconds,
        int diagnosticIterations,
        bool includeIterationOverhead)
    {
        if (attachTimeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attachTimeoutSeconds));
        }

        if (monitorSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorSeconds));
        }

        if (diagnosticIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(diagnosticIterations));
        }

        var seconds = attachTimeoutSeconds
            + (monitorSeconds + (includeIterationOverhead ? DiagnosticIterationOverheadSeconds : 0)) * diagnosticIterations
            + FinalGraceSeconds;
        return TimeSpan.FromSeconds(seconds);
    }
}
