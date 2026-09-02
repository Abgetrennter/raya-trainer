namespace RayaTrainer.Core.Versions;

public sealed record VersionedAddress(
    string SymbolicName,
    int? Rva,
    AddressSupportStatus Status,
    string Source,
    string? Notes = null,
    IReadOnlyList<byte>? ExpectedBytes = null);
