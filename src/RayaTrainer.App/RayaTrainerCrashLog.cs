using System.IO;

namespace RayaTrainer.App;

/// <summary>
/// 统一的崩溃/异常日志入口。写到程序同级目录（exe 所在目录）下的
/// RayaTrainer_crash.log，便于用户和支持人员直接取日志。
/// 内容是 Exception.ToString()（类型名 + 消息 + 完整堆栈）。
/// 所有写入都吞异常——最后兜底的日志本身绝不能再抛把进程搞崩。
/// </summary>
internal static class RayaTrainerCrashLog
{
    private static readonly string LogPath = Path.Combine(
        AppContext.BaseDirectory,
        "RayaTrainer_crash.log");

    public static void Write(Exception exception)
    {
        try
        {
            // exe 所在目录一定存在，CreateDirectory 作为保险（已存在时是空操作）。
            Directory.CreateDirectory(AppContext.BaseDirectory);
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:O}] {exception}\n\n");
        }
        catch
        {
            // Last-resort error logging must never crash the UI.
        }
    }
}
