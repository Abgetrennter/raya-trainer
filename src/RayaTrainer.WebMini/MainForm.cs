using RayaTrainer.Core.Manifest;
using RayaTrainer.Core.Runtime;
using RayaTrainer.Core.Versions;
using RayaTrainer.Host;
using RayaTrainer.Host.Services;
using RayaTrainer.Host.Web;
using RayaTrainer.WebMini.Services;

namespace RayaTrainer.WebMini;

/// <summary>
/// WebMini 主窗口：最简单的原生 WinForms 单窗口，承载全部交互——
/// 状态行、配对二维码、修改端口、绑定网卡、只读日志区。无托盘、无控制台。
/// 关闭窗口即退出程序（优雅停止 Web 宿主与会话）。
/// </summary>
public sealed class MainForm : Form
{
    private readonly WebMiniSettingsStore _miniSettingsStore = new();
    private readonly GameApiCommandQueue _commandQueue = new();
    private readonly QrCodeBitmapFactory _qrFactory = new();
    private readonly CancellationTokenSource _shutdown = new();

    private WebMiniSettings _settings;

    private TrainerManifest? _manifest;
    private TrainerAppSettingsStore? _settingsStore;
    private TrainerSessionManager? _session;
    private GameProcessWatcher? _watcher;
    private TargetProcessHeartbeatMonitor? _heartbeat;
    private TrainerWebHost? _webHost;
    private WindowDeviceApprovalService? _approval;
    private TaskCompletionSource<bool>? _attachedTcs;

    private Label _statusLabel = null!;
    private PictureBox _qrBox = null!;
    private Label _urlLabel = null!;
    private NumericUpDown _portInput = null!;
    private Button _applyPortButton = null!;
    private ComboBox _bindSelect = null!;
    private Button _applyBindButton = null!;
    private TextBox _logBox = null!;

    public MainForm()
    {
        BuildLayout();
        _settings = _miniSettingsStore.Load();
        InitializeEndpointInputs();

        Load += (_, _) => _ = BootAsync();
    }

    // ─── 布局 ──────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        Text = "RAЯ Trainer WebMini — Web 遥控";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(460, 624);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        _statusLabel = new Label
        {
            Location = new Point(12, 12),
            Size = new Size(436, 20),
            Font = new Font(Font, FontStyle.Bold),
            Text = "正在启动…"
        };

        _qrBox = new PictureBox
        {
            Location = new Point(12, 40),
            Size = new Size(220, 220),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White
        };

        _urlLabel = new Label
        {
            Location = new Point(244, 40),
            Size = new Size(204, 220),
            Text = "等待服务启动…"
        };

        var portCaption = new Label
        {
            Location = new Point(12, 276),
            Size = new Size(64, 20),
            Text = "监听端口"
        };
        _portInput = new NumericUpDown
        {
            Location = new Point(80, 272),
            Size = new Size(90, 24),
            Minimum = 1,
            Maximum = 65535
        };
        _applyPortButton = new Button
        {
            Location = new Point(180, 271),
            Size = new Size(60, 26),
            Text = "应用",
            Enabled = false
        };
        _applyPortButton.Click += async (_, _) =>
            await ApplyEndpointAsync((int)_portInput.Value, _settings.BindAddress);

        var bindCaption = new Label
        {
            Location = new Point(12, 312),
            Size = new Size(64, 20),
            Text = "绑定网卡"
        };
        _bindSelect = new ComboBox
        {
            Location = new Point(80, 308),
            Size = new Size(260, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Enabled = false
        };
        _applyBindButton = new Button
        {
            Location = new Point(348, 307),
            Size = new Size(60, 26),
            Text = "应用",
            Enabled = false
        };
        _applyBindButton.Click += async (_, _) =>
            await ApplyEndpointAsync(_settings.Port, CurrentBindSelection());

        _logBox = new TextBox
        {
            Location = new Point(12, 344),
            Size = new Size(436, 268),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window
        };

        Controls.AddRange(new Control[]
        {
            _statusLabel, _qrBox, _urlLabel,
            portCaption, _portInput, _applyPortButton,
            bindCaption, _bindSelect, _applyBindButton,
            _logBox
        });
    }

    private void InitializeEndpointInputs()
    {
        _portInput.Value = _settings.Port;
        RefreshBindOptions();

        // 保存的绑定地址若已不在本机网卡列表里，回退全部网卡（启动时提示）。
        if (_settings.BindAddress != WebMiniSettings.BindAll &&
            _bindSelect.Items.Cast<NicOption>().All(o => o.Address != _settings.BindAddress))
        {
            AppendLog($"设置中的绑定地址 {_settings.BindAddress} 已不存在，回退为全部网卡。");
            _settings.BindAddress = WebMiniSettings.BindAll;
        }
        SyncBindSelection();
    }

    private void RefreshBindOptions()
    {
        var previous = CurrentBindSelectionOrNull();
        _bindSelect.Items.Clear();
        _bindSelect.Items.Add(new NicOption("全部网卡（0.0.0.0）", WebMiniSettings.BindAll));
        foreach (var entry in new LanMobileRemoteLinkProvider().GetAvailableAddresses())
        {
            _bindSelect.Items.Add(new NicOption($"{entry.DisplayName}", entry.IpAddress));
        }

        var restore = previous ?? _settings.BindAddress;
        var match = _bindSelect.Items.Cast<NicOption>().FirstOrDefault(o => o.Address == restore);
        _bindSelect.SelectedItem = match ?? _bindSelect.Items[0];
    }

    private void SyncBindSelection()
    {
        var match = _bindSelect.Items.Cast<NicOption>()
            .FirstOrDefault(o => o.Address == _settings.BindAddress);
        _bindSelect.SelectedItem = match ?? _bindSelect.Items[0];
    }

    private string CurrentBindSelection() => CurrentBindSelectionOrNull() ?? WebMiniSettings.BindAll;

    private string? CurrentBindSelectionOrNull() =>
        _bindSelect.SelectedItem is NicOption option ? option.Address : null;

    private void SetEndpointControlsEnabled(bool enabled)
    {
        _portInput.Enabled = enabled;
        _applyPortButton.Enabled = enabled;
        _bindSelect.Enabled = enabled;
        _applyBindButton.Enabled = enabled;
    }

    // ─── 启动链 ────────────────────────────────────────────────────────

    private async Task BootAsync()
    {
        try
        {
            AppendLog("RAЯ Trainer WebMini —— Web 遥控独立可选组件");

            TrainerManifest manifest;
            try
            {
                manifest = TrainerRuntimeAssets.LoadManifest();
            }
            catch (Exception exception)
            {
                AppendLog($"清单加载失败：{exception.Message}");
                SetStatus("启动失败");
                MessageBox.Show(this, $"清单加载失败：{exception.Message}",
                    "RAЯ Trainer WebMini", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _manifest = manifest;
            _settingsStore = new TrainerAppSettingsStore();
            _session = new TrainerSessionManager();
            _approval = new WindowDeviceApprovalService(this, AppendLog);

            if (!await AttachGameAsync(manifest).ConfigureAwait(true))
            {
                return; // 窗口已关闭。
            }

            StartHeartbeat();
            var started = await StartWebAsync(_settings.Port, _settings.BindAddress, previousHost: null)
                .ConfigureAwait(true);
            if (!started)
            {
                // 首选绑定失败（如端口占用）时回退默认端口 + 全部网卡再试一次。
                AppendLog("回退默认端口与全部网卡重试…");
                _portInput.Value = TrainerWebEndpointDefaults.Port;
                _settings.BindAddress = WebMiniSettings.BindAll;
                SyncBindSelection();
                await StartWebAsync(TrainerWebEndpointDefaults.Port, WebMiniSettings.BindAll, previousHost: null)
                    .ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            RayaTrainerCrashLog.Write(exception);
            AppendLog($"启动失败：{exception.Message}");
            SetStatus("启动失败");
        }
    }

    /// <summary>等待游戏进程并完成附加 + Patch 安装（复用主程序的自动捕获语义与指纹门禁）。</summary>
    private Task<bool> AttachGameAsync(TrainerManifest manifest)
    {
        SetStatus("等待红色警戒3进程…");
        AppendLog("正在等待红色警戒3进程（先启动游戏；如主程序已附加，本程序将按指纹门禁接管或拒绝）…");

        _attachedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _shutdown.Token.Register(() => _attachedTcs.TrySetResult(false));

        var locator = new TrainerProcessLocator();
        _watcher = new GameProcessWatcher(selectTargets: () => locator.SelectDefault());
        _watcher.TargetFound += (_, e) => TryAttachAsync(e.Target);
        _watcher.AmbiguousCandidatesDetected += (_, e) =>
        {
            // 无界面选择：多个候选时取第一个可安装的，其余忽略。
            var pick = e.Candidates.FirstOrDefault(c => c.CanAttemptInstallation);
            if (pick is not null)
            {
                AppendLog($"发现多个红色警戒3进程，自动选择 PID {pick.ProcessId}。");
                _watcher?.ResolveAmbiguity(pick);
            }
            else
            {
                AppendLog("发现多个红色警戒3进程，但没有可安装的候选，继续等待。");
                _watcher?.CancelAmbiguity();
            }
        };
        _watcher.Start();
        return _attachedTcs.Task;
    }

    private void TryAttachAsync(DetectedRa3Target target)
    {
        Task.Run(async () =>
        {
            if (_shutdown.IsCancellationRequested || _session is null || _manifest is null)
            {
                return;
            }

            AppendLog($"检测到红色警戒3（PID {target.ProcessId}），正在附加…");
            var attach = await _session.AttachTargetAsync(_manifest, target.ToTrainerTarget());
            if (!attach.Success)
            {
                AppendLog($"附加失败：{attach.Message}");
                _watcher?.NotifyAttachFailed();
                return;
            }

            var install = await _session.InstallPatchesAsync(_manifest, DefaultDiagnosticsDirectory());
            AppendLog(install.StatusMessage);
            if (!_session.ArePatchesInstalled)
            {
                await _session.MarkTargetOfflineAsync();
                _watcher?.NotifyAttachFailed();
                return;
            }

            _watcher?.NotifyAttached();
            AppendLog("附加完成，Patch 已就绪。");
            SetStatus($"已附加（PID {target.ProcessId}），正在启动 Web 服务…");
            _attachedTcs?.TrySetResult(true);
        });
    }

    private void StartHeartbeat()
    {
        _heartbeat = new TargetProcessHeartbeatMonitor();
        _heartbeat.OfflineDetected += (_, _) =>
        {
            _ = _session?.MarkTargetOfflineAsync();
            AppendLog("目标游戏已退出，会话已标记离线；重新启动游戏后请重跑本程序。");
            SetStatus("游戏已退出，会话离线");
        };
        if (_session?.TargetProcessId is int targetProcessId)
        {
            _heartbeat.Start(targetProcessId);
        }
    }

    /// <summary>
    /// 热重启语义：先用新参数创建并启动新宿主，成功后才释放旧宿主并更新设置；
    /// 启动失败（端口占用等）保留旧宿主与旧设置。
    /// </summary>
    private async Task<bool> StartWebAsync(int port, string bindAddress, TrainerWebHost? previousHost)
    {
        try
        {
            var newHost = TrainerWebHost.Create(
                _session!,
                _manifest!,
                commandQueue: _commandQueue,
                settingsStore: _settingsStore,
                presetSource: new SettingsPresetSource(_settingsStore!),
                deviceApprovalService: _approval,
                port: port,
                bindAddress: bindAddress);
            try
            {
                await newHost.StartAsync(_shutdown.Token).ConfigureAwait(true);
            }
            catch
            {
                await newHost.DisposeAsync().ConfigureAwait(true);
                throw;
            }

            if (previousHost is not null)
            {
                await previousHost.DisposeAsync().ConfigureAwait(true);
            }

            _webHost = newHost;
            _settings.Port = port;
            _settings.BindAddress = bindAddress;
            AppendLog($"Web 服务已启动：http://{bindAddress}:{port}/");
            SetStatus($"已附加（PID {_session!.TargetProcessId}），监听 http://{bindAddress}:{port}/");
            UpdateEndpointUi();
            return true;
        }
        catch (Exception exception)
        {
            AppendLog($"监听失败（{bindAddress}:{port}）：{exception.Message}");
            return false;
        }
    }

    private async Task ApplyEndpointAsync(int port, string bindAddress)
    {
        if (_webHost is null || _session is null)
        {
            AppendLog("Web 服务尚未启动，暂不能修改端口/网卡。");
            return;
        }
        if (port == _settings.Port && bindAddress == _settings.BindAddress)
        {
            AppendLog("端口/网卡未变化。");
            return;
        }

        SetEndpointControlsEnabled(false);
        try
        {
            RefreshBindOptions();
            var started = await StartWebAsync(port, bindAddress, _webHost).ConfigureAwait(true);
            if (started)
            {
                try
                {
                    _miniSettingsStore.Save(_settings);
                    AppendLog("设置已保存。");
                }
                catch (Exception exception)
                {
                    AppendLog($"设置保存失败：{exception.Message}");
                }
            }
            else
            {
                MessageBox.Show(
                    this,
                    $"无法在 {bindAddress}:{port} 上监听，已保留原设置。\r\n详情见日志区。",
                    "RAЯ Trainer WebMini",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _portInput.Value = _settings.Port;
                SyncBindSelection();
            }
        }
        finally
        {
            SetEndpointControlsEnabled(true);
        }
    }

    private void UpdateEndpointUi()
    {
        var linkProvider = new LanMobileRemoteLinkProvider(_settings.Port);
        string url;
        if (_settings.BindAddress != WebMiniSettings.BindAll)
        {
            url = $"http://{_settings.BindAddress}:{_settings.Port}/";
        }
        else
        {
            var first = linkProvider.GetAvailableAddresses().FirstOrDefault();
            url = first is not null
                ? linkProvider.CreateRemoteUrl(first.IpAddress)
                : $"http://localhost:{_settings.Port}/";
        }

        _urlLabel.Text = $"遥控地址：\n{url}\n\n手机（与本机同一网络）\n扫码或浏览器打开；\n首次连接需在本窗口批准配对。";
        try
        {
            var previous = _qrBox.Image;
            _qrBox.Image = _qrFactory.Create(url);
            previous?.Dispose();
        }
        catch (Exception exception)
        {
            AppendLog($"二维码生成失败：{exception.Message}");
        }
    }

    // ─── 日志与状态（线程安全） ────────────────────────────────────────

    private void SetStatus(string text)
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(() => SetStatus(text));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            return;
        }

        _statusLabel.Text = text;
    }

    private void AppendLog(string line)
    {
        if (_logBox.IsDisposed)
        {
            return;
        }
        if (_logBox.InvokeRequired)
        {
            try
            {
                _logBox.BeginInvoke(() => AppendLog(line));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            return;
        }

        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
    }

    // ─── 退出 ──────────────────────────────────────────────────────────

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _shutdown.Cancel();
        try
        {
            Task.Run(ShutdownAsync).Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // 退出路径尽力而为；宿主停止超时不阻塞窗口关闭。
        }
        base.OnFormClosed(e);
    }

    private async Task ShutdownAsync()
    {
        _heartbeat?.Dispose();
        _watcher?.Dispose();
        if (_webHost is not null)
        {
            await _webHost.DisposeAsync().ConfigureAwait(false);
            _webHost = null;
        }
        _session?.Dispose();
    }

    private static string DefaultDiagnosticsDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "artifacts", "diagnostics");

    /// <summary>网卡绑定下拉项。</summary>
    private sealed record NicOption(string Display, string Address)
    {
        public override string ToString() => Display;
    }
}
