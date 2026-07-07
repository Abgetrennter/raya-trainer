namespace RayaTrainer.Core.Diagnostics;

public enum CompatibilitySamplePointCategory
{
    Hook,
    NativeCodeDependency,
    NativeDataDependency
}

public sealed record CompatibilitySamplePoint(
    string AddressExpression,
    nint AbsoluteAddress,
    CompatibilitySamplePointCategory Category,
    string Title,
    IReadOnlyList<string> EnableFlags,
    byte[]? ExpectedBytes,
    bool Disassemble);

public sealed record CompatibilitySampleOptions(int BytesBefore, int BytesAfter);

public sealed record CompatibilityInstruction(
    ulong Ip,
    int Length,
    string Bytes,
    string Text);

public sealed record CompatibilitySampleResult(
    CompatibilitySamplePoint Point,
    nint RangeStart,
    byte[] Bytes,
    byte[]? ActualBytes,
    bool? MatchesExpected,
    IReadOnlyList<CompatibilityInstruction> Instructions,
    string? Error);
