using System.Windows.Input;
using RayaTrainer.App.Services;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.App.ViewModels;

public sealed class FeatureItemViewModel : ViewModelBase
{
    private readonly IFeatureHost _owner;
    private readonly IFeatureSoundPlayer _soundPlayer;
    private bool _enabled;
    private bool _isExecuting;
    private string _status = "未启用";
    // 运行时热重载用此覆盖替换 Feature.Hotkey。null 表示未设置覆盖（回退 Feature.Hotkey）；
    // 空串是 sentinel，表示显式清除（Hotkey getter 返回 null），避免回退到原始默认值。
    private string? _hotkeyOverride;

    public FeatureItemViewModel(
        TrainerFeature feature,
        IFeatureHost owner,
        IFeatureSoundPlayer? soundPlayer = null)
    {
        Feature = feature;
        _owner = owner;
        _soundPlayer = soundPlayer ?? SystemFeatureSoundPlayer.Shared;
        Command = new RelayCommand(() => _ = ExecuteAsync(), () => _owner.ArePatchesInstalled && IsAvailable && !_isExecuting);
        OpenHotkeySettingsCommand = new RelayCommand(() => _owner.OpenHotkeySettings());
        ClearHotkeyCommand = new RelayCommand(() => _owner.ClearHotkey(Feature), () => !string.IsNullOrWhiteSpace(Hotkey));
    }

    public TrainerFeature Feature { get; }

    public string DisplayName => Feature.DisplayName;

    public string? Hotkey => _hotkeyOverride is null ? Feature.Hotkey : NormalizeOverride(_hotkeyOverride);

    private static string? NormalizeOverride(string? overrideValue) =>
        string.IsNullOrWhiteSpace(overrideValue) ? null : overrideValue;

    /// <summary>徽章显示文本：无热键时显示「＋」引导用户右键添加。</summary>
    public string HotkeyDisplay => string.IsNullOrWhiteSpace(Hotkey) ? "＋" : Hotkey;

    /// <summary>
    /// 运行时热重载入口：用新解析出的热键文本刷新徽章显示，无需重建整个 Feature 列表。
    /// 传入 null 或空串表示该功能已被清除（不再绑定快捷键）；此时用空串 sentinel 覆盖原始 Feature.Hotkey。
    /// </summary>
    public void RefreshHotkey(string? hotkey)
    {
        // 热重载传入的 null/空串统一表示「清除」，用空串 sentinel 覆盖，避免回退到 Feature.Hotkey 默认值。
        _hotkeyOverride = string.IsNullOrWhiteSpace(hotkey) ? string.Empty : hotkey;
        OnPropertyChanged(nameof(Hotkey));
        OnPropertyChanged(nameof(HotkeyDisplay));
        OnPropertyChanged(nameof(HelpText));
        ClearHotkeyCommand.RaiseCanExecuteChanged();
    }

    public string ActionText => IsToggle ? (_enabled ? "关闭" : "开启") : "执行";

    public bool IsToggle => FeatureDispatchDefaults.IsToggle(Feature);

    public bool IsFeatureEnabled => IsToggle && _enabled;

    public FeatureCapabilitySnapshot Capability => _owner.GetFeatureCapability(Feature);

    public FeatureCapabilityState CapabilityState => Capability.State;

    public string CapabilityLabel => Capability.State switch
    {
        FeatureCapabilityState.Ready => "就绪",
        FeatureCapabilityState.Waiting when Capability.ReasonCode == "NO_TARGET" => "待连接",
        FeatureCapabilityState.Waiting when Capability.ReasonCode == "PATCH_NOT_INSTALLED" => "待安装",
        FeatureCapabilityState.Waiting => "等待中",
        _ when Capability.ReasonCode == "DIRECT_GAME_API_REQUIRED" => "需要 Agent",
        _ => "不可用"
    };

    public string CapabilityReason => Capability.Reason;

    public bool IsAvailable => Capability.State == FeatureCapabilityState.Ready;

    public string HelpText => string.Join(Environment.NewLine, CreateHelpLines());

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public RelayCommand Command { get; }

    /// <summary>从功能列表徽章跳转到快捷键设置页（集中改键入口）。</summary>
    public RelayCommand OpenHotkeySettingsCommand { get; }

    /// <summary>就地清除本功能快捷键（置空，立即生效）。</summary>
    public RelayCommand ClearHotkeyCommand { get; }

    public void RaiseCommandState() => Command.RaiseCanExecuteChanged();

    public void RaiseAvailabilityChanged()
    {
        if (Capability.State == FeatureCapabilityState.Unavailable)
        {
            Status = "不可用";
        }
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(Capability));
        OnPropertyChanged(nameof(CapabilityState));
        OnPropertyChanged(nameof(CapabilityLabel));
        OnPropertyChanged(nameof(CapabilityReason));
        OnPropertyChanged(nameof(HelpText));
        Command.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 断开/恢复 patch 时把本 toggle 功能状态强制归零，不依赖活跃 controller。
    /// 与 <see cref="RefreshToggleState"/> 不同：后者读取实时状态，会在 controller 为 null 时
    /// 提前返回并保留旧的"已启用"标记——这正是重新加载/恢复时 UI 撒谎的来源。
    /// </summary>
    public void ResetToggleState()
    {
        if (!IsToggle)
        {
            return;
        }

        _enabled = false;
        Status = "未启用";
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(IsFeatureEnabled));
    }

    public void RefreshToggleState()
    {
        if (!IsToggle)
        {
            return;
        }

        var controller = _owner.FeatureController;
        if (controller is null)
        {
            return;
        }

        try
        {
            var state = controller.ReadToggleState(Feature);
            if (_enabled != state)
            {
                _enabled = state;
                Status = _enabled ? "已启用" : "未启用";
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(IsFeatureEnabled));
            }
        }
        catch
        {
            // 读取失败时保持当前状态
        }
    }

    public void ExecuteFromHotkey()
    {
        if (!Command.CanExecute(null))
        {
            return;
        }

        _ = ExecuteAsync();
    }

    /// <summary>
    /// Maps a pause-aware wait status to a short user-facing status string.
    /// R2c: surfaces "waiting for resume" feedback while the trainer holds
    /// a dispatch open during a paused game.
    /// </summary>
    private static string DispatchWaitStatusText(DispatchWaitStatus status)
    {
        return status switch
        {
            DispatchWaitStatus.PausedWaiting => "游戏已暂停，等待恢复…",
            DispatchWaitStatus.Resumed => "游戏已恢复，继续执行…",
            DispatchWaitStatus.GraceExpired => "等待超时，已放弃当前操作。",
            _ => "执行中…"
        };
    }

    private async Task ExecuteAsync()
    {
        try
        {
            var controller = _owner.FeatureController;
            if (controller is null)
            {
                Status = "未连接";
                _owner.StatusMessage = "请先检测进程并安装 patch。";
                return;
            }

            if (!IsAvailable)
            {
                Status = Capability.State == FeatureCapabilityState.Waiting ? "等待中" : "不可用";
                _owner.StatusMessage = Capability.Reason;
                return;
            }

            if (IsToggle)
            {
                var nextEnabled = !_enabled;
                if (nextEnabled)
                {
            _owner.WriteResourceValuesIfNeeded(Feature);
            _owner.WriteTargetHealthIfNeeded(Feature);
                }

                _enabled = nextEnabled;
                controller.SetToggle(Feature, _enabled);
                _owner.OnFeatureToggleChanged(Feature, _enabled);
                Status = _enabled ? "已启用" : "已关闭";
                _soundPlayer.Play(FeatureSoundCueResolver.ForToggleState(_enabled));
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(IsFeatureEnabled));
                return;
            }

            _owner.WriteResourceValuesIfNeeded(Feature);
            _owner.WriteTargetHealthIfNeeded(Feature);
            _isExecuting = true;
            Command.RaiseCanExecuteChanged();
            Status = Feature.DispatchTarget is null ? "已触发" : "触发中";

            var result = IsReinforcementFeature
                ? await controller.TriggerActionAndWaitForConsumptionAsync(
                    Feature,
                    _owner.GetReinforcementSettings(),
                    onWaitStatusChanged: status =>
                    {
                        Status = DispatchWaitStatusText(status);
                        _owner.StatusMessage = DispatchWaitStatusText(status);
                    })
                : await controller.TriggerActionAndWaitForConsumptionAsync(
                    Feature,
                    onWaitStatusChanged: status =>
                    {
                        Status = DispatchWaitStatusText(status);
                        _owner.StatusMessage = DispatchWaitStatusText(status);
                    });

            if (result == ActionDispatchResult.Consumed)
            {
                Status = "已执行";
            }
            else if (result == ActionDispatchResult.TimedOut)
            {
                Status = "超时";
                _owner.StatusMessage = "动作已写入但尚未被游戏循环消费。";
            }
            else if (result == ActionDispatchResult.AbortedDueToPause)
            {
                Status = "已放弃";
                _owner.StatusMessage = "游戏保持暂停状态，已放弃当前操作。";
            }
            else
            {
                Status = "已触发";
            }

            _owner.CompleteActionIfNeeded(Feature, result);

            var cue = FeatureSoundCueResolver.ForActionResult(result);
            if (cue is not null)
            {
                _soundPlayer.Play(cue.Value);
            }
        }
        catch (Exception ex)
        {
            Status = "失败";
            try
            {
                _owner.StatusMessage = ex.Message;
            }
            catch
            {
                // ViewModel may have been disposed during async execution.
            }
        }
        finally
        {
            _isExecuting = false;
            try
            {
                Command.RaiseCanExecuteChanged();
            }
            catch
            {
                // ViewModel may have been disposed during async execution.
            }
        }
    }

    private bool IsReinforcementFeature =>
        Feature.RawName.Equals(TrainerFeatureIds.Reinforcement, StringComparison.Ordinal);

    private IEnumerable<string> CreateHelpLines()
    {
        yield return DisplayName;
        yield return $"能力：{CapabilityLabel} — {CapabilityReason}";
        yield return $"快捷键：{(string.IsNullOrWhiteSpace(Hotkey) ? "未分配" : Hotkey)}";
        yield return $"类型：{(IsToggle ? "开关功能" : "一次性功能")}";
        yield return CreateFeatureDescription();
        if (Capability.State != FeatureCapabilityState.Ready)
        {
            yield return Capability.Reason;
        }
    }

    private string CreateFeatureDescription()
    {
        return Feature.RawName switch
        {
            "Moeny" => "执行一次资金增量写入，金额来自顶部 Money 输入框。",
            "Power" => "把可用电力维持在顶部 Power 输入框指定值，并压低用电量读数。",
            "SC POINT" => "把秘密协议点数维持在顶部 SC Point 输入框指定值。",
            "HAVE ALL SC" => "把秘密协议解锁进度写满，直接开放协议技能树。",
            "FAST BUILD" => "把建造、训练和帝国展开计时推进到快速完成状态。",
            "SUPER POWER" => "压缩玩家超级武器与秘密协议技能冷却计时。",
            "Disable ALL SP" => "阻止非本地玩家的技能进入可用状态。",
            "Zoom" => "解除镜头缩放上下限，允许更大范围拉近或拉远。",
            "MAP" => "把战争迷雾视野值写到高值，持续显示地图。",
            "Enemy Can't Build" => "压制非玩家建造进度，限制电脑生产建筑和单位。",
            "Player God Mode" => "把玩家阵营生命值写到高值并保持。",
            "Player One Kill Mode" => "把敌方可伤害目标生命值压低，一次攻击即可摧毁。",
            "Select Unit Level UP" => "把目标经验/等级推进到升级状态。",
            "Select Unit Super Speed" => "把目标移动速度改为高速值，并保留原速度用于恢复。",
            "Select Unit Slow Speed" => "把目标移动速度改为低速值，并保留原速度用于恢复。",
            "Select Unit Freeze" => "把目标移动速度写为 0，形成冻结效果。",
            "Restore Select Unit Speed" => "把被改过的移动速度还原为保存的原值。",
            "Select Unit HP MAX" => "把目标当前生命值和上限写到高值。",
            "Select Unit HP MIN" => "把目标生命值写到最低生存值，方便捕获或快速击毁。",
            "Restore Select Unit Normal HP" => "从目标最大生命值字段恢复当前生命值。",
            "Select Unit Ammo MAX" => "持续把弹药/炸弹计数写到极高值，避免装填耗尽（作用范围为全部己方单位）。",
            "Fill Selected Unit Ammo" => "一次性把选中单位全部武器的弹药写到极大值。引擎不会自动压回上限，效果会一直保留到正常消耗。",
            "Reset Selected Unit Ammo" => "一次性把选中单位全部武器的弹药归为 1，可用来恢复正常装填状态（也用于撤销\"弹药填满\"）。",
            "Destory Select Unit" => "向目标发送原生摧毁指令。",
            "Danger Level MAX" => "把威胁等级写到高值，让目标更容易吸引火力。",
            "Danger Level MIN" => "把威胁等级归零，降低目标被优先攻击的概率。",
            "Restore Danger Level Normal" => "取消威胁等级强制值，交回游戏正常计算。",
            "Restore Select Ore Mine" => "重置矿点剩余采集量，让矿车继续采集。",
            "Free Build" => "取消建筑放置位置校验，可在通常不可建造的位置落建筑。",
            "Expand Production Queue" => "仅限 DLL Agent：选中建造场后执行，把当前阵营建造场的主建筑与防御建筑队列上限扩展为 999。",
            "Restore Production Queue" => "仅限 DLL Agent：选中建造场后执行，把当前阵营建造场的主建筑与防御建筑队列上限恢复为 1。",
            "Teleport Selected Units To Mouse" => "仅限 DLL Agent：把当前选中的可移动单位整体瞬移到鼠标地图位置；多选时保持相对队形，无移动器的建筑会被跳过。仅建议在单机或遭遇战使用。",
            "Get Me Base" => "在鼠标地图坐标为玩家生成三阵营基地车。",
            "We Need Back" => "按支援面板里的单位代码、数量和等级发起一次增援请求。",
            "Select Unit Copy For Me" => "以目标类型为模板，在鼠标地图坐标为玩家生成副本。",
            "Set Unit Support State" => "把目标状态写成伪装标记，用于触发游戏内伪装/潜伏状态。",
            "Set Selected Unit Target Health" => "把目标当前生命值设置为输入框指定的浮点数值，不修改最大生命值上限。",
            "Toggle Selected Unit Attack Speed" => "逐个切换当前选中单位的满攻速状态：已在实例表中则移除并恢复正常攻速，不在则加入并把四类武器延迟压缩到最小1个游戏 tick；不修改共享武器模板。",
            "Secret Protocol Binding Probe" => "进入对局后执行；固定授予盟军 AirPower 作为同阵营正控，并授予日本 EnhancedKamikaze 观察跨阵营被动绑定。",
            "Soviet Orbital Refuse Rank 1 Probe" => "进入对局后执行；固定授予 PlayerTech_Soviet_OrbitalRefuse_Rank1，用于验证跨阵营主动协议是否出现在协议面板并可释放。",
            "Clear Player Tech Locks" => "清空地图脚本 Lock Player Tech 写入的玩家科技锁 bitmask，并兼容清理当前玩家 PlayerTechManager 锁表。",
            _ => "按原版修改器动作表执行该功能。"
        };
    }
}
