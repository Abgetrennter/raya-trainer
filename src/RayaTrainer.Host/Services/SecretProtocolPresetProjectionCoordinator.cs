using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;

namespace RayaTrainer.Host.Services;

/// <summary>
/// App 会话内的秘密协议预设投影协调器（计划 P3，镜像增援协调器的第二实例）。只做三件事：
///   1. 接收 WPF 保存/删除后的完整 presets 快照（成功后直接通知，不轮询设置文件）；
///   2. 维护 App 内 projectionSessionId / generation / 最近选择名会话缓存；
///   3. 在 Agent 就绪或快照变化时通过命令 64 原子替换 Agent 投影。
///
/// 会话身份：sessionId 是 App 启动时生成的非零 63 位随机值（与增援协调器的 session 相互
/// 独立），App 重启即更换；即使接管仍在进程内的旧 Agent，也用新 session 原子替换投影并
/// 清空旧选择。generation 在同一 session 内严格递增。防竞态：同一时刻至多一个在途替换，
/// 发送期间再次保存只保证最后一个 generation 胜出；旧响应不会覆盖新状态。构建失败
/// （超限/双零 ID 等）时改发 Invalid 投影，让 Agent 禁用执行并保留用户可读错误。
/// </summary>
public sealed class SecretProtocolPresetProjectionCoordinator
{
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private readonly ISecretProtocolPresetConsoleClient _client;
    private readonly Action<string?> _reportSyncStatus;
    private readonly ulong _sessionId;

    private ulong _generation;
    private IReadOnlyList<SecretProtocolQueuePreset>? _latestPresets;
    private IReadOnlyList<SecretProtocolQueuePreset>? _lastSyncedPresets;
    private string? _cachedSelectedName;
    private bool _agentReady;
    private int _processId;
    private Task _activeSync = Task.CompletedTask;
    private bool _syncRunning;

    public SecretProtocolPresetProjectionCoordinator(
        ISecretProtocolPresetConsoleClient? client = null,
        Action<string?>? reportSyncStatus = null,
        ulong? sessionId = null)
    {
        _client = client ?? new AgentNamedPipeClient();
        _reportSyncStatus = reportSyncStatus ?? (_ => { });
        _sessionId = sessionId ?? SecretProtocolPresetProjection.CreateSessionId();
        if (_sessionId == 0 || _sessionId > 0x7FFF_FFFF_FFFF_FFFFUL)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionId), "Session id must be a nonzero positive 63-bit value.");
        }
    }

    public ulong ProjectionSessionId => _sessionId;

    /// <summary>本次 App 运行内 Agent 最近上报的选择名（重连同一 Agent 时作为首选选择下发）。</summary>
    public string? CachedSelectedName
    {
        get
        {
            lock (_gate)
            {
                return _cachedSelectedName;
            }
        }
    }

    /// <summary>当前在途/最近一次同步任务（测试与关停同步点）。</summary>
    public Task ActiveSync
    {
        get
        {
            lock (_gate)
            {
                return _activeSync;
            }
        }
    }

    /// <summary>WPF 保存/删除成功后推入完整快照。快照未变化时不重复同步。</summary>
    public void UpdatePresets(IReadOnlyList<SecretProtocolQueuePreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        lock (_gate)
        {
            if (_lastSyncedPresets is not null &&
                _latestPresets is null &&
                PresetsEqual(_lastSyncedPresets, presets))
            {
                return;
            }
            _latestPresets = presets;
            ScheduleLocked();
        }
    }

    /// <summary>Agent attach 完成（协议与指纹已验证）后调用；触发一次投影替换。</summary>
    public void OnAgentReady(int processId)
    {
        lock (_gate)
        {
            _agentReady = true;
            _processId = processId;
            // 重连/接管都必须用当前 session 重新替换投影，即使快照没变。
            if (_latestPresets is null && _lastSyncedPresets is not null)
            {
                _latestPresets = _lastSyncedPresets;
            }
            ScheduleLocked();
        }
    }

    public void OnAgentDetached()
    {
        lock (_gate)
        {
            _agentReady = false;
        }
    }

    /// <summary>
    /// 以低频（现有会话刷新节奏）从命令 65 读取 Agent 最近选择名，更新会话缓存。
    /// 仅当返回的投影 session 是本 App 会话时才吸收，防止旧 Agent 状态污染缓存。
    /// </summary>
    public async Task RefreshCachedSelectionAsync(CancellationToken cancellationToken = default)
    {
        int processId;
        lock (_gate)
        {
            if (!_agentReady)
            {
                return;
            }
            processId = _processId;
        }

        try
        {
            var state = await _client
                .GetSecretProtocolPresetConsoleStateAsync(processId, SyncTimeout, cancellationToken)
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (_agentReady &&
                    _processId == processId &&
                    state.AgentStatusCode == SecretProtocolPresetConsoleWireCodec.AgentStatusOk &&
                    state.HasProjection &&
                    state.ProjectionSessionId == _sessionId)
                {
                    _cachedSelectedName = state.SelectedName;
                }
            }
        }
        catch
        {
            // 状态查询是尽力而为的会话缓存刷新；失败不影响投影一致性。
        }
    }

    private void ScheduleLocked()
    {
        if (!_agentReady || _latestPresets is null || _syncRunning)
        {
            return;
        }
        _syncRunning = true;
        _activeSync = Task.Run(SyncLoopAsync);
    }

    private async Task SyncLoopAsync()
    {
        while (true)
        {
            IReadOnlyList<SecretProtocolQueuePreset> presets;
            string? preferred;
            ulong generation;
            int processId;
            lock (_gate)
            {
                if (!_agentReady || _latestPresets is null)
                {
                    _syncRunning = false;
                    return;
                }
                presets = _latestPresets;
                _latestPresets = null;
                preferred = _cachedSelectedName;
                generation = ++_generation;
                processId = _processId;
            }

            string? statusError = null;
            ReplaceSecretProtocolProjectionResponse? response = null;
            var build = SecretProtocolPresetProjectionBuilder.Build(presets, _sessionId, generation, preferred);
            var projection = build.Projection;
            if (!build.Success)
            {
                // 超限/非法快照：整个投影拒绝，不截断。向 Agent 发送 Invalid 让旧投影失效，
                // 并把可操作的错误保留给 WPF 状态栏。
                statusError = build.Error;
                projection = SecretProtocolPresetProjectionBuilder.BuildInvalid(
                    _sessionId, generation, build.Error!);
            }

            try
            {
                response = await _client
                    .ReplaceSecretProtocolPresetProjectionAsync(processId, projection!, SyncTimeout)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                statusError ??= $"秘密协议预设同步失败：{ex.Message}";
            }

            string? report = null;
            var reportNow = false;
            lock (_gate)
            {
                var isLatest = generation == _generation;
                if (isLatest && response is not null &&
                    response.AgentStatusCode == SecretProtocolPresetConsoleWireCodec.AgentStatusOk)
                {
                    if (response.Accepted)
                    {
                        _cachedSelectedName = response.SelectedName;
                        if (build.Success)
                        {
                            _lastSyncedPresets = presets;
                        }
                    }
                    else if (statusError is null &&
                             response.RejectReason != SecretProtocolProjectionRejectReason.GenerationRegressed)
                    {
                        statusError = $"Agent 拒绝了秘密协议预设投影（{response.RejectReason}）。";
                    }
                }
                else if (isLatest && response is not null && statusError is null)
                {
                    statusError = $"秘密协议预设同步失败：Agent 状态码 {response.AgentStatusCode}。";
                }

                if (isLatest)
                {
                    report = statusError;
                    reportNow = true;
                }
            }

            if (reportNow)
            {
                _reportSyncStatus(report);
            }

            lock (_gate)
            {
                if (_latestPresets is not null && _agentReady)
                {
                    continue;
                }
                _syncRunning = false;
                return;
            }
        }
    }

    private static bool PresetsEqual(
        IReadOnlyList<SecretProtocolQueuePreset> left,
        IReadOnlyList<SecretProtocolQueuePreset> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }
        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Name, right[i].Name, StringComparison.Ordinal) ||
                left[i].Entries.Count != right[i].Entries.Count)
            {
                return false;
            }
            for (var j = 0; j < left[i].Entries.Count; j++)
            {
                if (!left[i].Entries[j].Equals(right[i].Entries[j]))
                {
                    return false;
                }
            }
        }
        return true;
    }
}
