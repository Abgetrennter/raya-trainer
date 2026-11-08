using System.ComponentModel;
using System.Diagnostics;

namespace RayaTrainer.Host.Services.GameCrashCapture;

/// <summary>
/// 生产子进程宿主：隐藏控制台启动 CrashCapture，重定向 stdout（逐行协议）与 stdin
/// （CANCEL 命令）。协议行与退出事件从线程池线程上报——回调里不得直接碰 UI 状态。
/// </summary>
public sealed class CrashCaptureProcessLauncher : ICrashCaptureProcessLauncher
{
    public ICrashCaptureProcess Start(CrashCaptureStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(request.ExePath))
        {
            throw new InvalidOperationException($"找不到捕获组件：{request.ExePath}");
        }

        var info = new ProcessStartInfo
        {
            FileName = request.ExePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[]
                 {
                     "--target-pid", request.TargetPid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--target-create-time", request.TargetCreateTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--target-image", request.TargetImage,
                     "--out-dir", request.OutDir,
                     "--session", request.SessionId,
                     "--result-file", request.ResultFilePath,
                 })
        {
            info.ArgumentList.Add(arg);
        }

        try
        {
            var process = Process.Start(info)
                ?? throw new InvalidOperationException("启动捕获组件失败：进程未创建。");
            return new HostedCrashCaptureProcess(process);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"启动捕获组件失败：{ex.Message}", ex);
        }
    }

    private sealed class HostedCrashCaptureProcess : ICrashCaptureProcess
    {
        private readonly Process _process;
        private readonly object _exitLock = new();
        private bool _exitNotified;

        public HostedCrashCaptureProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += OnLine;
            _process.Exited += OnExited;
            _process.EnableRaisingEvents = true;
            _process.BeginOutputReadLine();
        }

        public event Action<string>? OutputLine;
        public event Action? Exited;

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.HasExited ? _process.ExitCode : -1;

        public bool TrySendLine(string line)
        {
            try
            {
                if (_process.HasExited)
                {
                    return false;
                }

                _process.StandardInput.WriteLine(line);
                _process.StandardInput.Flush();
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public async Task<bool> WaitForExitAsync(TimeSpan timeout)
        {
            var remaining = timeout;
            while (!_process.HasExited)
            {
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }

                var step = remaining > TimeSpan.FromMilliseconds(200) ? TimeSpan.FromMilliseconds(200) : remaining;
                await Task.Delay(step).ConfigureAwait(false);
                remaining -= step;
            }

            return true;
        }

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch (Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void Dispose()
        {
            _process.OutputDataReceived -= OnLine;
            _process.Exited -= OnExited;
            _process.Dispose();
        }

        private void OnLine(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is { Length: > 0 } line)
            {
                OutputLine?.Invoke(line);
            }
        }

        private void OnExited(object? sender, EventArgs e) => NotifyExitedOnce();

        private void NotifyExitedOnce()
        {
            lock (_exitLock)
            {
                if (_exitNotified)
                {
                    return;
                }

                _exitNotified = true;
            }

            Exited?.Invoke();
        }
    }
}
