internal sealed record CompatibilitySampleReportDto(
    CompatibilitySampleTargetDto Target,
    CompatibilitySampleOptionsDto Options,
    CompatibilitySampleSummaryDto Summary,
    IReadOnlyList<CompatibilitySamplePointDto> Points);

internal sealed record CompatibilitySampleTargetDto(
    int? ProcessId,
    string ProcessName,
    string ModulePath,
    string ModuleBase,
    string FileVersion,
    bool Is32Bit,
    bool VersionSupported);

internal sealed record CompatibilitySampleOptionsDto(int BytesBefore, int BytesAfter);

internal sealed record CompatibilitySampleSummaryDto(
    int Total,
    int HookCount,
    int DependencyCount,
    int MismatchCount,
    int ReadFailureCount);

internal sealed record CompatibilitySamplePointDto(
    string AddressExpression,
    string AbsoluteAddress,
    string Category,
    string Title,
    IReadOnlyList<string> EnableFlags,
    string? ExpectedBytes,
    string? ActualBytes,
    bool? MatchesExpected,
    string RangeStart,
    string Bytes,
    IReadOnlyList<CompatibilitySampleInstructionDto> Instructions,
    string? Error);

internal sealed record CompatibilitySampleInstructionDto(
    string Ip,
    int Length,
    string Bytes,
    string Text);
