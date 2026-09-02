using System.Text;
using RayaTrainer.Core.Features;

namespace RayaTrainer.Core.Agent;

/// <summary>
/// Secret Protocol Preset Console v1 (commands 64/65) — bounded projection limits.
/// Frozen alongside the wire schema in <see cref="SecretProtocolPresetConsoleWireCodec"/>;
/// the native mirror lives in src/RayaTrainer.Agent/SecretProtocolConsole/SecretProtocolPresetModels.h.
/// Second instantiation of the reinforcement preset console form: the projection is rejected
/// (never truncated) when any bound is exceeded so the Overlay always shows exactly what the
/// user saved in WPF. The two consoles share no wire schema and no code — each is frozen alone.
/// </summary>
public static class SecretProtocolPresetConsoleLimits
{
    public const int MaxPresets = 16;
    public const int MaxEntriesPerPreset = 32;
    public const int MaxPresetNameBytes = 96;
    public const int MaxDisplayNameBytes = 96;
    public const int MaxSyncErrorBytes = 256;
    public const ushort SchemaVersion = 1;
}

/// <summary>Projection validity wire values (request field <c>validity</c>).</summary>
public enum SecretProtocolProjectionValidity : byte
{
    Valid = 1,
    Invalid = 2,
}

/// <summary>Structured rejection reasons returned by command 64.</summary>
public enum SecretProtocolProjectionRejectReason : ushort
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

/// <summary>Native batch state mirrored by command 65 (summary only, no entries).</summary>
public enum SecretProtocolBatchWireState : byte
{
    None = 0,
    Running = 1,
    Completed = 2,
    Aborted = 3,
}

/// <summary>
/// A single grant entry: PlayerTechId and UpgradeId may each be zero (protocol-only or
/// upgrade-only grants) but never both — that is rejected as InvalidEntry.
/// </summary>
public sealed record SecretProtocolProjectionEntry(string DisplayName, uint PlayerTechId, uint UpgradeId);

public sealed record SecretProtocolProjectionPreset(string Name, IReadOnlyList<SecretProtocolProjectionEntry> Entries);

/// <summary>
/// Immutable read-only projection replaced atomically on the Agent by command 64.
/// <see cref="ProjectionSessionId"/> is a positive 63-bit random value minted once per App
/// run — independent from the reinforcement console's session — so it round-trips losslessly
/// through a Product Intent Integer parameter; <see cref="Generation"/> is strictly
/// increasing within one session.
/// </summary>
public sealed record SecretProtocolPresetProjection(
    SecretProtocolProjectionValidity Validity,
    ulong ProjectionSessionId,
    ulong Generation,
    string? PreferredSelectedName,
    string? SyncError,
    IReadOnlyList<SecretProtocolProjectionPreset> Presets)
{
    public static ulong CreateSessionId() => ReinforcementPresetProjection.CreateSessionId();
}

public sealed record SecretProtocolProjectionBuildResult(
    SecretProtocolPresetProjection? Projection,
    string? Error)
{
    public bool Success => Projection is not null;
}

/// <summary>
/// Pure builder from the WPF-saved <see cref="SecretProtocolQueuePreset"/> list to a bounded
/// projection. Composes the wire display name as "Faction - Name" (falling back to the bare
/// name when the faction is blank); Mod/Faction never travel separately. Rejects (whole
/// projection, no truncation) on any capacity/validity violation so the Agent never sees a
/// projection that differs from what the user saved.
/// </summary>
public static class SecretProtocolPresetProjectionBuilder
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public static string ComposeDisplayName(SecretProtocolPresetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var name = string.IsNullOrWhiteSpace(entry.Name) ? string.Empty : entry.Name.Trim();
        var faction = string.IsNullOrWhiteSpace(entry.Faction) ? string.Empty : entry.Faction.Trim();
        return faction.Length == 0 ? name : $"{faction} - {name}";
    }

    public static SecretProtocolProjectionBuildResult Build(
        IReadOnlyList<SecretProtocolQueuePreset> presets,
        ulong projectionSessionId,
        ulong generation,
        string? preferredSelectedName)
    {
        ArgumentNullException.ThrowIfNull(presets);
        if (projectionSessionId == 0 || projectionSessionId > 0x7FFF_FFFF_FFFF_FFFFUL)
        {
            return new SecretProtocolProjectionBuildResult(null, "投影会话标识必须是非零 63 位正数。");
        }
        if (generation == 0)
        {
            return new SecretProtocolProjectionBuildResult(null, "投影 generation 必须从 1 开始。");
        }
        if (presets.Count > SecretProtocolPresetConsoleLimits.MaxPresets)
        {
            return new SecretProtocolProjectionBuildResult(
                null,
                $"预设数量 {presets.Count} 超过上限 {SecretProtocolPresetConsoleLimits.MaxPresets}，请删除部分预设后重试。");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var projected = new List<SecretProtocolProjectionPreset>(presets.Count);
        foreach (var preset in presets)
        {
            var name = preset.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return new SecretProtocolProjectionBuildResult(null, "存在名称为空的预设，无法同步。");
            }
            if (!TryMeasureUtf8(name, SecretProtocolPresetConsoleLimits.MaxPresetNameBytes, out var nameError))
            {
                return new SecretProtocolProjectionBuildResult(null, $"预设名“{name}”{nameError}");
            }
            if (!names.Add(name))
            {
                return new SecretProtocolProjectionBuildResult(null, $"预设名“{name}”重复，无法同步。");
            }
            if (preset.Entries.Count == 0)
            {
                return new SecretProtocolProjectionBuildResult(null, $"预设“{name}”没有条目，无法同步。");
            }
            if (preset.Entries.Count > SecretProtocolPresetConsoleLimits.MaxEntriesPerPreset)
            {
                return new SecretProtocolProjectionBuildResult(
                    null,
                    $"预设“{name}”有 {preset.Entries.Count} 个条目，超过上限 {SecretProtocolPresetConsoleLimits.MaxEntriesPerPreset}。");
            }

            var entries = new List<SecretProtocolProjectionEntry>(preset.Entries.Count);
            foreach (var entry in preset.Entries)
            {
                var displayName = ComposeDisplayName(entry);
                if (displayName.Length == 0)
                {
                    return new SecretProtocolProjectionBuildResult(null, $"预设“{name}”存在显示名为空的条目。");
                }
                if (!TryMeasureUtf8(displayName, SecretProtocolPresetConsoleLimits.MaxDisplayNameBytes, out var entryError))
                {
                    return new SecretProtocolProjectionBuildResult(null, $"预设“{name}”条目“{displayName}”{entryError}");
                }
                if (entry.PlayerTechId == 0 && entry.UpgradeId == 0)
                {
                    return new SecretProtocolProjectionBuildResult(
                        null, $"预设“{name}”条目“{displayName}”的 PlayerTech 与 Upgrade ID 不能同时为 0。");
                }
                entries.Add(new SecretProtocolProjectionEntry(displayName, entry.PlayerTechId, entry.UpgradeId));
            }
            projected.Add(new SecretProtocolProjectionPreset(name, entries));
        }

        string? preferred = null;
        if (!string.IsNullOrEmpty(preferredSelectedName) && names.Contains(preferredSelectedName))
        {
            preferred = preferredSelectedName;
        }

        return new SecretProtocolProjectionBuildResult(
            new SecretProtocolPresetProjection(
                SecretProtocolProjectionValidity.Valid,
                projectionSessionId,
                generation,
                preferred,
                SyncError: null,
                projected),
            null);
    }

    /// <summary>Builds an explicit Invalid projection so the Agent disables execution.</summary>
    public static SecretProtocolPresetProjection BuildInvalid(
        ulong projectionSessionId,
        ulong generation,
        string syncError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncError);
        return new SecretProtocolPresetProjection(
            SecretProtocolProjectionValidity.Invalid,
            projectionSessionId,
            generation,
            PreferredSelectedName: null,
            syncError,
            Array.Empty<SecretProtocolProjectionPreset>());
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

/// <summary>Command 64 response — the Agent echoes the accepted identity and final selection.</summary>
public sealed record ReplaceSecretProtocolProjectionResponse(
    ushort AgentStatusCode,
    bool Accepted,
    SecretProtocolProjectionRejectReason RejectReason,
    ulong AcceptedSessionId,
    ulong AcceptedGeneration,
    string? SelectedName);

/// <summary>
/// Command 65 response — summary only (no entries; the full entries flow Agent-internally
/// into the Overlay ViewState). Used by the App session cache and diagnostics.
/// </summary>
public sealed record SecretProtocolPresetConsoleState(
    ushort AgentStatusCode,
    bool HasProjection,
    SecretProtocolProjectionValidity Validity,
    ulong ProjectionSessionId,
    ulong Generation,
    uint PresetCount,
    string? SelectedName,
    string? SyncError,
    SecretProtocolBatchWireState BatchState,
    uint BatchTotal,
    uint BatchCompleted,
    uint BatchFailed,
    uint BatchNotAttempted,
    ulong ActiveIntentId);
