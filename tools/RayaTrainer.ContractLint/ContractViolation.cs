namespace RayaTrainer.ContractLint;

internal sealed record ContractViolation(
    string Contract,
    string Severity,   // "Mismatch" | "CountMismatch" | "ParseError"
    string Description);
