using System.Text.Json.Serialization;

namespace RayaTrainer.Core.Manifest;

public sealed record TrainerManifest(
    string TargetProcess,
    IReadOnlyList<TrainerFeature> Features,
    PatchManifest PatchManifest,
    IReadOnlyList<ActionDispatchEntry> ActionDispatch);

public enum SelectionExecutionMode
{
    Apply,
    SmartToggle,
    SingleTarget,
}

public sealed record TrainerFeature(
    string RawName,
    string DisplayName,
    string? Hotkey,
    IReadOnlyList<string> EnableFlags,
    string? DispatchTarget,
    string? ValueHint,
    IReadOnlyList<TrainerFeatureBytePatch>? ToggleBytePatches = null,
    IReadOnlyList<string>? SupportedProfileIds = null,
    bool RequiresDirectGameApi = false,
    SelectionExecutionMode? SelectionMode = null)
{
    public bool SupportsProfile(string profileId) =>
        SupportedProfileIds is not { Count: > 0 } ||
        SupportedProfileIds.Contains(profileId, StringComparer.OrdinalIgnoreCase);
}

public sealed record TrainerFeatureBytePatch(
    string Address,
    byte[] EnabledBytes,
    byte[] DisabledBytes);

public sealed record PatchManifest(IReadOnlyList<PatchHook> Hooks);

public sealed record PatchHook(
    string Address,
    string SectionTitle,
    IReadOnlyList<string> PatchAssembly,
    string? TrampolineTarget,
    string? ReturnLabel,
    IReadOnlyList<string> EnableFlags,
    IReadOnlyList<string> OriginalAssembly,
    IReadOnlyList<string>? SupportedProfileIds = null)
{
    public bool SupportsProfile(string profileId) =>
        SupportedProfileIds is not { Count: > 0 } ||
        SupportedProfileIds.Contains(profileId, StringComparer.OrdinalIgnoreCase);
}

public sealed record ActionDispatchEntry(string Value, string Target, string Description);

internal sealed record TrainerReportDto(
    [property: JsonPropertyName("trainer_metadata")] TrainerMetadataDto TrainerMetadata,
    [property: JsonPropertyName("features")] IReadOnlyList<FeatureDto> Features,
    [property: JsonPropertyName("patch_manifest")] PatchManifestDto PatchManifest,
    [property: JsonPropertyName("action_dispatch")] IReadOnlyList<ActionDispatchDto> ActionDispatch);

internal sealed record TrainerMetadataDto(
    [property: JsonPropertyName("target_process")] string TargetProcess);

internal sealed record FeatureDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("hotkey")] string? Hotkey,
    [property: JsonPropertyName("enable_flags")] IReadOnlyList<string>? EnableFlags,
    [property: JsonPropertyName("dispatch_target")] string? DispatchTarget,
    [property: JsonPropertyName("value_hint")] string? ValueHint,
    [property: JsonPropertyName("supported_profiles")] IReadOnlyList<string>? SupportedProfileIds);

internal sealed record PatchManifestDto(
    [property: JsonPropertyName("hooks")] IReadOnlyList<PatchHookDto> Hooks);

internal sealed record PatchHookDto(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("section_title")] string SectionTitle,
    [property: JsonPropertyName("patch_assembly")] IReadOnlyList<string> PatchAssembly,
    [property: JsonPropertyName("trampoline_target")] string? TrampolineTarget,
    [property: JsonPropertyName("return_label")] string? ReturnLabel,
    [property: JsonPropertyName("enable_flags")] IReadOnlyList<string>? EnableFlags,
    [property: JsonPropertyName("original_assembly")] IReadOnlyList<string> OriginalAssembly,
    [property: JsonPropertyName("supported_profiles")] IReadOnlyList<string>? SupportedProfileIds);

internal sealed record ActionDispatchDto(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("description")] string Description);
