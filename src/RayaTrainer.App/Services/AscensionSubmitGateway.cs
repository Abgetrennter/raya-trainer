using System.Text;

using RayaTrainer.Core.Agent;
using RayaTrainer.Host.Services;

namespace RayaTrainer.App.Services;

/// <summary>
/// Submit gateway for the Ascension matrix page (design doc 2026-08-17, slice 2d). The ViewModel
/// produces wire rows ("attributeType:valueBits:scopeMask:faction", decimal); the gateway fetches
/// the submit-time selection when the Selected scope is requested, splits the rows over the two
/// 4096-byte String parameters, and submits through the shared <see cref="ProductFeatureSubmitter"/>
/// so the ascension products settle exactly like every other Product Intent feature entry.
/// </summary>
public interface IAscensionSubmitGateway
{
    Task<(bool Success, string Message)> ApplyAsync(IReadOnlyList<string> entryRows, bool needsSelectedIds);

    Task<(bool Success, string Message)> RestoreAsync();

    /// <summary>
    /// Manual read-back (command 71): the Agent's committed policy table reverse-mapped to
    /// (attributeType, valueBits, scopeMask, faction). Entries is null on failure.
    /// </summary>
    Task<(bool Success, string Message, IReadOnlyList<AscensionPolicyReadbackEntry>? Entries)> ReadbackAsync();
}

internal sealed class AscensionSubmitGateway : IAscensionSubmitGateway
{
    public const string ApplyProductId = "ascension.apply.batch";
    public const string RestoreProductId = "ascension.restore.batch";

    // One wire String parameter body caps at 4096 bytes INCLUDING the embedded u32 text length
    // prefix (ProductControl contract §3/§4); segment text therefore budgets 4092 bytes. Rows
    // are split across the entries1/entries2 parameters on row boundaries.
    internal const int SegmentBudgetBytes = ProductControlWireCodec.MaxGenericStringBytes - sizeof(uint);
    private static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<IProductControlSession?> _sessionAccessor;
    private readonly Func<int?> _targetProcessIdAccessor;

    public AscensionSubmitGateway(
        Func<IProductControlSession?> sessionAccessor,
        Func<int?> targetProcessIdAccessor)
    {
        _sessionAccessor = sessionAccessor;
        _targetProcessIdAccessor = targetProcessIdAccessor;
    }

    public async Task<(bool Success, string Message)> ApplyAsync(
        IReadOnlyList<string> entryRows, bool needsSelectedIds)
    {
        var session = _sessionAccessor();
        if (session is null)
        {
            return (false, "尚未连接游戏，请先检测进程并安装 patch。");
        }

        var selectedIdsText = string.Empty;
        if (needsSelectedIds)
        {
            if (_targetProcessIdAccessor() is not int targetProcessId)
            {
                return (false, "尚未连接游戏进程。");
            }

            IReadOnlyList<uint> selectedIds;
            try
            {
                var payload = await new AgentNamedPipeClient()
                    .GetSelectedObjectIdsAsync(targetProcessId, SelectionTimeout)
                    .ConfigureAwait(true);
                selectedIds = payload.ObjectIds;
            }
            catch (Exception exception)
            {
                return (false, $"读取游戏内选中对象失败：{exception.Message}");
            }

            if (selectedIds.Count == 0)
            {
                return (false, "勾选了“选中单位”范围，但游戏里当前没有选中对象。");
            }

            selectedIdsText = string.Join(',', selectedIds);
            if (Encoding.UTF8.GetByteCount(selectedIdsText) > SegmentBudgetBytes)
            {
                return (false,
                    $"选中对象过多（{selectedIds.Count} 个），超出单次提交的 wire 容量，请缩小选择范围后重试。");
            }
        }

        var segments = SplitRowsIntoSegments(entryRows, SegmentBudgetBytes);
        if (segments.Count > 2)
        {
            return (false, "启用的修正行超出 wire 两段容量，请减少同时启用的行。");
        }

        var parameters = new[]
        {
            ScriptValue.String(segments[0]),
            ScriptValue.String(segments.Count > 1 ? segments[1] : string.Empty),
            ScriptValue.String(selectedIdsText),
        };
        var submission = await ProductFeatureSubmitter
            .SubmitAsync(session, ApplyProductId, parameters)
            .ConfigureAwait(true);
        return (submission.Success, submission.Message);
    }

    public async Task<(bool Success, string Message)> RestoreAsync()
    {
        var session = _sessionAccessor();
        if (session is null)
        {
            return (false, "尚未连接游戏，请先检测进程并安装 patch。");
        }

        var submission = await ProductFeatureSubmitter
            .SubmitAsync(session, RestoreProductId, Array.Empty<ScriptValue>())
            .ConfigureAwait(true);
        return (submission.Success, submission.Message);
    }

    public async Task<(bool Success, string Message, IReadOnlyList<AscensionPolicyReadbackEntry>? Entries)> ReadbackAsync()
    {
        if (_targetProcessIdAccessor() is not int targetProcessId)
        {
            return (false, "尚未连接游戏进程。", null);
        }

        try
        {
            var payload = await new AgentNamedPipeClient()
                .GetAscensionPoliciesAsync(targetProcessId, SelectionTimeout)
                .ConfigureAwait(true);
            if (payload.StatusCode != AgentStatusCode.Ok)
            {
                return (false, $"读回属性修改策略表失败：Agent 状态码 {payload.StatusCode}。", null);
            }

            return (true, string.Empty, payload.Entries);
        }
        catch (Exception exception)
        {
            return (false, $"读回属性修改策略表失败：{exception.Message}", null);
        }
    }

    // Join rows with ';' into segments that stay within the UTF-8 byte budget. Never splits a
    // row; callers reject more than two segments.
    internal static IReadOnlyList<string> SplitRowsIntoSegments(IReadOnlyList<string> rows, int budgetBytes)
    {
        var segments = new List<string>();
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            var candidate = builder.Length == 0 ? row : $"{builder};{row}";
            if (builder.Length > 0 && Encoding.UTF8.GetByteCount(candidate) > budgetBytes)
            {
                segments.Add(builder.ToString());
                builder.Clear().Append(row);
                continue;
            }

            builder.Clear().Append(candidate);
        }

        segments.Add(builder.ToString());
        return segments;
    }
}
