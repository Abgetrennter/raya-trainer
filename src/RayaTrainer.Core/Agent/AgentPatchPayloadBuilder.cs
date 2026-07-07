using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Memory;
using RayaTrainer.Core.Patching;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;

namespace RayaTrainer.Core.Agent;

public sealed record AgentPatchPayloadBuildResult(
    AgentInstallPatchesRequest Request,
    IReadOnlyList<SkippedPatchHookPlan> SkippedHooks);

public static class AgentPatchPayloadBuilder
{
    public static AgentInstallPatchesRequest Build(
        TrainerManifest manifest,
        TrainerTarget target,
        AgentStatusPayload status,
        IReadOnlyDictionary<string, uint>? scannedAddresses = null)
    {
        return BuildWithDiagnostics(manifest, target, status, scannedAddresses).Request;
    }

    public static AgentPatchPayloadBuildResult BuildWithDiagnostics(
        TrainerManifest manifest,
        TrainerTarget target,
        AgentStatusPayload status,
        IReadOnlyDictionary<string, uint>? scannedAddresses = null)
    {
        if (status.StatusCode != AgentStatusCode.Ok)
        {
            throw new InvalidOperationException($"Agent status is not ready: {status.StatusCode}.");
        }

        var profile = Ra3VersionProfileRegistry.ResolveTargetProfile(target);
        if (profile?.SupportsAgentBackend != true)
        {
            throw new InvalidOperationException(
                profile is null
                    ? "无法确认目标版本配置，当前不会生成 Agent Patch payload。"
                    : $"已识别 {profile.DisplayName}，但该版本尚未完成 DLL Agent 地址验证，当前不会生成 Agent Patch payload。");
        }

        var resolver = new AddressResolver(
            target.ModuleBase,
            new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase),
            profile);

        // Profile-aware hook planning: legacy 1.12 profiles use the raw manifest addresses
        // verbatim; non-legacy profiles (e.g. 1.13, Uprising) re-derive each hook address +
        // expected bytes from the profile catalog. Unverified hooks are skipped and returned
        // as diagnostics rather than silently installed with a wrong address. This filtering
        // applies equally whether addresses come from the profile catalog or from a live
        // signature scan — the scan may return 0 for rewritten hooks (NeedsReanalysis).
        var planResult = profile.Id.Equals("ra3_1.12", StringComparison.OrdinalIgnoreCase)
            ? new PatchHookPlanResult(PatchHookPlanner.CreatePlans(manifest.PatchManifest), [])
            : PatchHookPlanner.CreateSupportedPlans(manifest.PatchManifest, profile, includeUnlistedHooks: scannedAddresses is not null);

        var plans = planResult.Plans;
        var hooks = plans
            .Select(plan => new AgentPatchHook(
                ToUInt32(ResolveHookAddress(resolver, plan, scannedAddresses)),
                NativeHookCatalog.GetHookId(plan.ReturnLabel),
                checked((uint)plan.PatchLength),
                plan.OriginalBytes.ToArray()))
            .ToArray();

        return new AgentPatchPayloadBuildResult(
            new AgentInstallPatchesRequest([], hooks),
            planResult.SkippedHooks);
    }

    private static nint ResolveHookAddress(
        AddressResolver resolver,
        PatchHookPlan plan,
        IReadOnlyDictionary<string, uint>? scannedAddresses)
    {
        if (scannedAddresses is null)
        {
            return resolver.Resolve(plan.Address);
        }

        var key = string.IsNullOrWhiteSpace(plan.ReturnLabel)
            ? plan.Address
            : plan.ReturnLabel;
        return ToAddress(RequireScannedAddress(scannedAddresses, key));
    }

    private static uint RequireScannedAddress(
        IReadOnlyDictionary<string, uint> scannedAddresses,
        string symbolicName)
    {
        if (!scannedAddresses.TryGetValue(symbolicName, out var address) || address == 0)
        {
            throw new InvalidOperationException($"Agent signature catalog is missing '{symbolicName}'.");
        }

        return address;
    }

    private static nint ToAddress(uint address)
    {
        return unchecked((nint)address);
    }

    private static uint ToUInt32(nint address)
    {
        var value = address.ToInt64();
        if (value is < 0 or > uint.MaxValue)
        {
            throw new OverflowException($"Address 0x{value:X} does not fit in x86 address space.");
        }

        return (uint)value;
    }
}
