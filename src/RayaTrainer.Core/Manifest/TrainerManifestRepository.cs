using System.Text.Json;

namespace RayaTrainer.Core.Manifest;

public static class TrainerManifestRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static TrainerManifest Load(string analysisDirectory)
    {
        var reportPath = Path.Combine(analysisDirectory, "trainer_report.json");
        using var stream = File.OpenRead(reportPath);
        return Load(stream, reportPath);
    }

    public static TrainerManifest Load(Stream stream, string sourceName)
    {
        var report = JsonSerializer.Deserialize<TrainerReportDto>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Unable to read trainer manifest from {sourceName}.");

        return new TrainerManifest(
            report.TrainerMetadata.TargetProcess,
            report.Features.Select(ToFeature).ToArray(),
            new PatchManifest(report.PatchManifest.Hooks.Select(ToHook).ToArray()),
            report.ActionDispatch.Select(item => new ActionDispatchEntry(item.Value, item.Target, item.Description)).ToArray());
    }

    private static TrainerFeature ToFeature(FeatureDto feature)
    {
        return new TrainerFeature(
            feature.Name,
            DisplayName(feature.Name),
            NormalizeHotkey(feature.Hotkey),
            ResolveEnableFlags(feature),
            feature.DispatchTarget,
            feature.ValueHint,
            SupportedProfileIds: feature.SupportedProfileIds?.ToArray());
    }

    private static IReadOnlyList<string> ResolveEnableFlags(FeatureDto feature)
    {
        if (feature.EnableFlags is { Count: > 0 })
        {
            return feature.EnableFlags.ToArray();
        }

        return Array.Empty<string>();
    }

    private static PatchHook ToHook(PatchHookDto hook)
    {
        return new PatchHook(
            hook.Address,
            hook.SectionTitle,
            hook.PatchAssembly.ToArray(),
            hook.TrampolineTarget,
            hook.ReturnLabel,
            hook.EnableFlags?.ToArray() ?? Array.Empty<string>(),
            hook.OriginalAssembly.ToArray(),
            hook.SupportedProfileIds?.ToArray());
    }

    private static string DisplayName(string rawName)
    {
        return rawName
            // Retained for legacy manifest compatibility — any external stale
            // "Moeny" references are corrected here. No-op on current data.
            .Replace("Moeny", "Money", StringComparison.Ordinal)
            .Replace("Destory", "Destroy", StringComparison.Ordinal);
    }

    private static string? NormalizeHotkey(string? hotkey)
    {
        return hotkey switch
        {
            "elete." => "Delete",
            "Page Up!" => "Page Up",
            _ => hotkey
        };
    }
}
