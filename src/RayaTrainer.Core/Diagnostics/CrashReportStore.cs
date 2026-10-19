using System.Text.Json;
using System.Text.Json.Serialization;

namespace RayaTrainer.Core.Diagnostics;

/// <summary>
/// 闪退报告的本地存储：%LocalApplicationData%\RayaTrainer\CrashReports\&lt;report-id&gt;。
/// 目录即索引：每个报告目录含 report.json（含冻结快照）与 Dump 文件；CrashCapture 的
/// 结果文件也落在同一目录，使 RT 异常退出后仍能扫描恢复报告列表（设计 §5/§6）。
/// 待处理上限与删除语义由调用方（Host 会话/UI）执行；本类只做存储与校验。
/// </summary>
public sealed class CrashReportStore
{
    private const string ReportFileName = "report.json";

    /// <summary>测试沙盒覆盖：非空时替代默认用户根（UI 自动化不得写真实报告目录）。</summary>
    public const string EnvironmentOverrideVariable = "RAYATRAINER_CRASHREPORTS_DIR";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public CrashReportStore(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? FromEnvironmentOverride() ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RayaTrainer", "CrashReports");
    }

    private static string? FromEnvironmentOverride()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentOverrideVariable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public string RootDirectory { get; }

    public string GetReportDirectory(string reportId)
    {
        ValidateReportId(reportId);
        return Path.Combine(RootDirectory, reportId);
    }

    /// <summary>创建待写入的报告目录并立即落盘初始 bundle（含崩前冻结快照）。</summary>
    public string CreateReport(GameCrashReportBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var dir = GetReportDirectory(bundle.Report.ReportId);
        Directory.CreateDirectory(dir);
        SaveBundle(dir, bundle);
        return dir;
    }

    public void SaveBundle(GameCrashReportBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        SaveBundle(GetReportDirectory(bundle.Report.ReportId), bundle);
    }

    /// <summary>扫描全部报告（目录即真相源）。损坏/无法解析的目录按 Failed 报告返回，不静默丢弃。</summary>
    public IReadOnlyList<GameCrashReportBundle> ListReports()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return [];
        }

        var results = new List<GameCrashReportBundle>();
        foreach (var dir in Directory.EnumerateDirectories(RootDirectory).Order())
        {
            var reportId = Path.GetFileName(dir);
            if (!IsValidReportId(reportId))
            {
                continue;
            }

            var path = Path.Combine(dir, ReportFileName);
            if (!File.Exists(path))
            {
                // CrashCapture 结果已到但 Host 未写报告（RT 崩溃路径）：以占位报告呈现。
                results.Add(new GameCrashReportBundle
                {
                    Report = new GameCrashReport
                    {
                        ReportId = reportId,
                        SessionId = reportId,
                        Status = GameCrashReportStatus.Failed,
                        FailureReason = "report-incomplete"
                    }
                });
                continue;
            }

            try
            {
                var bundle = JsonSerializer.Deserialize<GameCrashReportBundle>(
                    File.ReadAllText(path), JsonOptions);
                if (bundle is not null)
                {
                    results.Add(bundle);
                }
            }
            catch (JsonException)
            {
                results.Add(new GameCrashReportBundle
                {
                    Report = new GameCrashReport
                    {
                        ReportId = reportId,
                        SessionId = reportId,
                        Status = GameCrashReportStatus.Failed,
                        FailureReason = "report-json-corrupt"
                    }
                });
            }
        }

        return results;
    }

    /// <summary>删除指定报告（仅限本存储根下、命名合法的报告目录），返回是否删除了内容。</summary>
    public bool DeleteReport(string reportId)
    {
        var dir = GetReportDirectory(reportId);
        if (!Directory.Exists(dir))
        {
            return false;
        }

        var rootFullPath = Path.GetFullPath(RootDirectory);
        var dirFullPath = Path.GetFullPath(dir);
        if (!dirFullPath.StartsWith(rootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("拒绝删除报告存储根之外的目录。");
        }

        Directory.Delete(dirFullPath, recursive: true);
        return true;
    }

    /// <summary>Dump 写入期间 CrashCapture 以 .dmp.part 命名，转正后才可见；此方法只统计转正文件。</summary>
    public static string? FindDumpFile(string reportDirectory)
    {
        return Directory.EnumerateFiles(reportDirectory, "*.dmp").Order().FirstOrDefault();
    }

    private static void SaveBundle(string dir, GameCrashReportBundle bundle)
    {
        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        var finalPath = Path.Combine(dir, ReportFileName);
        var tempPath = finalPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, finalPath, overwrite: true);
    }

    private static void ValidateReportId(string reportId)
    {
        if (!IsValidReportId(reportId))
        {
            throw new InvalidOperationException($"非法报告 ID：{reportId}");
        }
    }

    private static bool IsValidReportId(string reportId)
    {
        if (reportId.Length is < 8 or > 64)
        {
            return false;
        }

        foreach (var c in reportId)
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            {
                continue;
            }
            return false;
        }

        return true;
    }
}
