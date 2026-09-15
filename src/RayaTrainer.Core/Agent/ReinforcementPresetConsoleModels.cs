using System.Security.Cryptography;
using System.Text;
using RayaTrainer.Core.Features;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Reinforcement Preset Console v1 (commands 62/63) — bounded projection limits.
/// Frozen alongside the wire schema in <see cref="ReinforcementPresetConsoleWireCodec"/>;
/// the native mirror lives in src/RayaTrainer.Agent/ReinforcementConsole/ReinforcementPresetModels.h.
/// The projection is rejected (never truncated) when any bound is exceeded so the Overlay
/// always shows exactly what the user saved in WPF.
/// </summary>
public static class ReinforcementPresetConsoleLimits
{
    public const int MaxPresets = 16;
    public const int MaxEntriesPerPreset = 32;
    public const int MaxPresetNameBytes = 96;
    public const int MaxDisplayNameBytes = 96;
    public const int MaxSyncErrorBytes = 256;
    public const ushort SchemaVersion = 1;
}

/// <summary>Projection validity wire values (request field <c>validity</c>).</summary>
public enum ReinforcementProjectionValidity : byte
{
    Valid = 1,
    Invalid = 2,
}

/// <summary>Structured rejection reasons returned by command 62.</summary>
public enum ReinforcementProjectionRejectReason : ushort
{
    None = 0,
    SchemaMismatch = 1,
    MalformedPayload = 2,
    LimitExceeded = 3,
    DuplicatePresetName = 4,
    EmptyPreset = 5,
    InvalidEntry = 6,
    SessionZero = 7,
    GenerationRegressed = 8,
    ReservedNonZero = 9,
}

/// <summary>Native batch state mirrored by command 63 (summary only, no entries).</summary>
public enum ReinforcementBatchWireState : byte
{
    None = 0,
    Running = 1,
    Completed = 2,
    Aborted = 3,
}

public sealed record ReinforcementProjectionEntry(string DisplayName, uint UnitId, int Count, int Rank);

public sealed record ReinforcementProjectionPreset(string Name, IReadOnlyList<ReinforcementProjectionEntry> Entries);

/// <summary>
/// Immutable read-only projection replaced atomically on the Agent by command 62.
/// <see cref="ProjectionSessionId"/> is a positive 63-bit random value minted once per App
/// run (never derived from settings-file timestamps) so it round-trips losslessly through a
/// Product Intent Integer parameter; <see cref="Generation"/> is strictly increasing within
/// one session.
/// </summary>
public sealed record ReinforcementPresetProjection(
    ReinforcementProjectionValidity Validity,
    ulong ProjectionSessionId,
    ulong Generation,
    string? PreferredSelectedName,
    string? SyncError,
    IReadOnlyList<ReinforcementProjectionPreset> Presets)
{
    public static ulong CreateSessionId()
    {
        Span<byte> bytes = stackalloc byte[8];
        ulong value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = BitConverter.ToUInt64(bytes) & 0x7FFF_FFFF_FFFF_FFFFUL;
        }
        while (value == 0);
        return value;
    }
}

public sealed record ReinforcementProjectionBuildResult(
    ReinforcementPresetProjection? Projection,
    string? Error)
{
    public bool Success => Projection is not null;
}

/// <summary>
/// Pure builder from the WPF-saved <see cref="ReinforcementPreset"/> list to a bounded
/// projection. Rejects (whole projection, no truncation) on any capacity/validity violation
/// so the Agent never sees a projection that differs from what the user saved.
/// </summary>
public static class ReinforcementPresetProjectionBuilder
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public static ReinforcementProjectionBuildResult Build(
        IReadOnlyList<ReinforcementPreset> presets,
        ulong projectionSessionId,
        ulong generation,
        string? preferredSelectedName)
    {
        ArgumentNullException.ThrowIfNull(presets);
        if (projectionSessionId == 0 || projectionSessionId > 0x7FFF_FFFF_FFFF_FFFFUL)
        {
            return new ReinforcementProjectionBuildResult(null, "投影会话标识必须是非零 63 位正数。");
        }
        if (generation == 0)
        {
            return new ReinforcementProjectionBuildResult(null, "投影 generation 必须从 1 开始。");
        }
        if (presets.Count > ReinforcementPresetConsoleLimits.MaxPresets)
        {
            return new ReinforcementProjectionBuildResult(
                null,
                $"预设数量 {presets.Count} 超过上限 {ReinforcementPresetConsoleLimits.MaxPresets}，请删除部分预设后重试。");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var projected = new List<ReinforcementProjectionPreset>(presets.Count);
        foreach (var preset in presets)
        {
            var name = preset.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return new ReinforcementProjectionBuildResult(null, "存在名称为空的预设，无法同步。");
            }
            if (!TryMeasureUtf8(name, ReinforcementPresetConsoleLimits.MaxPresetNameBytes, out var nameError))
            {
                return new ReinforcementProjectionBuildResult(null, $"预设名“{name}”{nameError}");
            }
            if (!names.Add(name))
            {
                return new ReinforcementProjectionBuildResult(null, $"预设名“{name}”重复，无法同步。");
            }
            if (preset.Entries.Count == 0)
            {
                return new ReinforcementProjectionBuildResult(null, $"预设“{name}”没有条目，无法同步。");
            }
            if (preset.Entries.Count > ReinforcementPresetConsoleLimits.MaxEntriesPerPreset)
            {
                return new ReinforcementProjectionBuildResult(
                    null,
                    $"预设“{name}”有 {preset.Entries.Count} 个条目，超过上限 {ReinforcementPresetConsoleLimits.MaxEntriesPerPreset}。");
            }

            var entries = new List<ReinforcementProjectionEntry>(preset.Entries.Count);
            foreach (var entry in preset.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    return new ReinforcementProjectionBuildResult(null, $"预设“{name}”存在显示名为空的条目。");
                }
                if (!TryMeasureUtf8(entry.Name, ReinforcementPresetConsoleLimits.MaxDisplayNameBytes, out var entryError))
                {
                    return new ReinforcementProjectionBuildResult(null, $"预设“{name}”条目“{entry.Name}”{entryError}");
                }
                if (entry.UnitId == 0 ||
                    entry.Count is < ReinforcementSettings.MinCount or > ReinforcementSettings.MaxCount ||
                    entry.Rank is < ReinforcementSettings.MinRank or > ReinforcementSettings.MaxRank)
                {
                    return new ReinforcementProjectionBuildResult(null, $"预设“{name}”条目“{entry.Name}”参数非法。");
                }
                entries.Add(new ReinforcementProjectionEntry(entry.Name, entry.UnitId, entry.Count, entry.Rank));
            }
            projected.Add(new ReinforcementProjectionPreset(name, entries));
        }

        string? preferred = null;
        if (!string.IsNullOrEmpty(preferredSelectedName) && names.Contains(preferredSelectedName))
        {
            preferred = preferredSelectedName;
        }

        return new ReinforcementProjectionBuildResult(
            new ReinforcementPresetProjection(
                ReinforcementProjectionValidity.Valid,
                projectionSessionId,
                generation,
                preferred,
                SyncError: null,
                projected),
            null);
    }

    /// <summary>Builds an explicit Invalid projection so the Agent disables execution.</summary>
    public static ReinforcementPresetProjection BuildInvalid(
        ulong projectionSessionId,
        ulong generation,
        string syncError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncError);
        return new ReinforcementPresetProjection(
            ReinforcementProjectionValidity.Invalid,
            projectionSessionId,
            generation,
            PreferredSelectedName: null,
            syncError,
            Array.Empty<ReinforcementProjectionPreset>());
    }

    private static bool TryMeasureUtf8(string value, int maxBytes, out string error)
    {
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            error = "包含非法字符，无法同步。";
            return false;
        }
        var byteCount = Utf8.GetByteCount(value);
        if (byteCount > maxBytes)
        {
            error = $"超过 {maxBytes} 字节（UTF-8）上限，请缩短名称。";
            return false;
        }
        error = string.Empty;
        return true;
    }
}

/// <summary>Command 62 response — the Agent echoes the accepted identity and final selection.</summary>
public sealed record ReplaceReinforcementProjectionResponse(
    ushort AgentStatusCode,
    bool Accepted,
    ReinforcementProjectionRejectReason RejectReason,
    ulong AcceptedSessionId,
    ulong AcceptedGeneration,
    string? SelectedName);

/// <summary>
/// Command 63 response — summary only (no entries; the full entries flow Agent-internally
/// into the Overlay ViewState). Used by the App session cache and diagnostics.
/// </summary>
public sealed record ReinforcementPresetConsoleState(
    ushort AgentStatusCode,
    bool HasProjection,
    ReinforcementProjectionValidity Validity,
    ulong ProjectionSessionId,
    ulong Generation,
    uint PresetCount,
    string? SelectedName,
    string? SyncError,
    ReinforcementBatchWireState BatchState,
    uint BatchTotal,
    uint BatchCompleted,
    uint BatchFailed,
    uint BatchNotAttempted,
    ulong ActiveIntentId);
