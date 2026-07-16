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
        IReadOnlyDictionary<string, uint>? scannedAddresses = null,
        IReadOnlyDictionary<string, byte[]>? attestedOriginalBytes = null)
    {
        return BuildWithDiagnostics(
            manifest,
            target,
            status,
            scannedAddresses,
            attestedOriginalBytes).Request;
    }

    public static AgentPatchPayloadBuildResult BuildWithDiagnostics(
        TrainerManifest manifest,
        TrainerTarget target,
        AgentStatusPayload status,
        IReadOnlyDictionary<string, uint>? scannedAddresses = null,
        IReadOnlyDictionary<string, byte[]>? attestedOriginalBytes = null)
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

        if (target.SignatureCompatibilityMode && scannedAddresses is null)
        {
            throw new InvalidOperationException("签名兼容候选缺少扫描结果，禁止回退到固定 RVA。");
        }

        var resolver = new AddressResolver(
            target.ModuleBase,
            new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase),
            profile);

        // Profile-aware hook planning: re-derive each hook address + expected bytes from
        // the profile catalog. Unverified hooks are skipped and returned as diagnostics
        // rather than silently installed with a wrong address. This filtering applies
        // equally whether addresses come from the profile catalog or from a live signature
        // scan — the scan may return 0 for rewritten hooks (NeedsReanalysis).
        var planResult = PatchHookPlanner.CreateSupportedPlans(
            manifest.PatchManifest,
            profile,
            includeUnlistedHooks: scannedAddresses is not null && !target.SignatureCompatibilityMode);

        var plans = planResult.Plans;
        var hooks = plans
            .Select(plan =>
            {
                var key = HookKey(plan);
                var address = ResolveHookAddress(
                    resolver,
                    plan,
                    scannedAddresses,
                    allowFixedRvaFallback: !target.SignatureCompatibilityMode);
                var originalBytes = ResolveOriginalBytes(
                    plan,
                    key,
                    target.SignatureCompatibilityMode,
                    attestedOriginalBytes);
                return new AgentPatchHook(
                    ToUInt32(address),
                    NativeHookCatalog.GetHookId(plan.ReturnLabel),
                    checked((uint)plan.PatchLength),
                    originalBytes);
            })
            .ToArray();

        return new AgentPatchPayloadBuildResult(
            new AgentInstallPatchesRequest([], hooks),
            planResult.SkippedHooks);
    }

    private static nint ResolveHookAddress(
        AddressResolver resolver,
        PatchHookPlan plan,
        IReadOnlyDictionary<string, uint>? scannedAddresses,
        bool allowFixedRvaFallback)
    {
        if (scannedAddresses is null)
        {
            return resolver.Resolve(plan.Address);
        }

        var key = HookKey(plan);
        if (scannedAddresses.TryGetValue(key, out var scanned) && scanned != 0)
        {
            return ToAddress(scanned);
        }

        if (!allowFixedRvaFallback)
        {
            throw new InvalidOperationException($"签名兼容候选的必需 Hook 未唯一定位：{key}。");
        }

        // 签名未命中：fallback 到 profile/manifest 固定 RVA。Agent 安装阶段的
        // original_assembly 字节校验（PatchMismatch）会区分 TW（地址正确）和 EN（地址错误）。
        return resolver.Resolve(plan.Address);
    }

    private static byte[] ResolveOriginalBytes(
        PatchHookPlan plan,
        string key,
        bool requireAttestedBytes,
        IReadOnlyDictionary<string, byte[]>? attestedOriginalBytes)
    {
        if (!requireAttestedBytes)
        {
            return plan.OriginalBytes.ToArray();
        }

        if (attestedOriginalBytes is null ||
            !attestedOriginalBytes.TryGetValue(key, out var attested) ||
            attested.Length != plan.OriginalBytes.Length)
        {
            throw new InvalidOperationException($"签名兼容候选缺少 Hook {key} 的实时指令证明，禁止安装。");
        }

        return attested.ToArray();
    }

    private static string HookKey(PatchHookPlan plan)
    {
        return string.IsNullOrWhiteSpace(plan.ReturnLabel) ? plan.Address : plan.ReturnLabel;
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
