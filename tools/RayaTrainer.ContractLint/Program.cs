namespace RayaTrainer.ContractLint;

internal static class Program
{
    private static int Main(string[] args)
    {
        var repoRoot = args.Length > 0
            ? args[0]
            : Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        // Verify repoRoot contains RayaTrainer.sln
        if (!File.Exists(Path.Combine(repoRoot, "RayaTrainer.sln")))
        {
            Console.Error.WriteLine($"ERROR: RayaTrainer.sln not found under {repoRoot}");
            return 2;
        }

        var violations = new List<ContractViolation>();
        violations.AddRange(ContractParsers.CheckProtocolConstants(repoRoot));
        violations.AddRange(ContractParsers.CheckAgentCommand(repoRoot));
        violations.AddRange(ContractParsers.CheckNativeCatalogEntry(repoRoot));
        violations.AddRange(ContractParsers.CheckNativeFeatureStateId(repoRoot));

        if (violations.Count == 0)
        {
            Console.WriteLine("ContractConsistencyLint: all 4 contracts consistent.");
            return 0;
        }

        foreach (var v in violations)
        {
            Console.WriteLine($"[{v.Severity}] {v.Contract}: {v.Description}");
        }

        Console.WriteLine($"\n{violations.Count} contract violation(s) found.");
        return 1;
    }
}
