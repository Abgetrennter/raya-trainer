using RayaTrainer.Core.Agent;

namespace RayaTrainer.Host.Services;

// 私有开发面：AttributeModifier 运行时资产包加载转发（计划 Phase 1）。仅私有构建编译
// （公共投影排除 Private/**）。转发到 AgentNamedPipeClient 的手写 cmd 69 实现。
public sealed partial class InjectedAgentBackend
{
    public Task<LoadBundledRuntimeAssetsPayload> LoadBundledRuntimeAssetsAsync(
        int processId,
        string absoluteManifestPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Agent 尚未连接。");
        }
        if (_client is not AgentNamedPipeClient client)
        {
            throw new InvalidOperationException("当前 Agent client 未提供运行时资产加载 API。");
        }

        return client.LoadBundledRuntimeAssetsAsync(processId, absoluteManifestPath, timeout, cancellationToken);
    }

    // Surfaces Agent runtime diagnostics lines (identity/layout/capability/asset state). Best-effort:
    // returns null when the Agent client does not provide the API (e.g. older Agent).
    public async Task<AgentRuntimeDiagnosticsPayload?> TryGetRuntimeDiagnosticsAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected || TargetProcessId is not int processId)
        {
            return null;
        }
        if (_client is not AgentNamedPipeClient client)
        {
            return null;
        }

        try
        {
            return await client
                .GetRuntimeDiagnosticsAsync(processId, timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
