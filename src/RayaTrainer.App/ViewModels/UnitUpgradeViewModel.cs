using System.Collections.ObjectModel;
using System.Windows.Input;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Diagnostics;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.App.ViewModels;

/// <summary>
/// Represents a single upgrade entry with resolved name and description.
/// </summary>
public sealed class UnitUpgradeItem
{
    public required uint Hash { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// ViewModel for the "单位升级" subpanel on the selected-unit page.
/// Reads OBJECT-type upgrades from the first selected unit, resolves names
/// via the embedded upgrade table, and grants upgrades on demand.
///
/// State machine: NotReady → Idle → Loading → Loaded/Error → Idle (via re-Refresh).
/// Capability snapshot from <see cref="TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature"/>
/// is the single source of truth for command availability.
/// Slow pipe calls run off the UI thread so Loading state is actually visible.
/// </summary>
public sealed class UnitUpgradeViewModel : ViewModelBase
{
    private readonly Func<ITrainerFeatureController?> _getController;
    private readonly Func<TrainerFeature, FeatureCapabilitySnapshot> _getCapability;
    private readonly UpgradeNameResolver _nameResolver = new();
    private UnitUpgradeState _state = UnitUpgradeState.NotReady;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    public UnitUpgradeViewModel(
        Func<ITrainerFeatureController?> getController,
        Func<TrainerFeature, FeatureCapabilitySnapshot> getCapability)
    {
        _getController = getController;
        _getCapability = getCapability;

        AvailableUpgrades = new ObservableCollection<UnitUpgradeItem>();
        AvailableUpgrades.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasUpgrades));
            OnPropertyChanged(nameof(IsListVisible));
        };

        RefreshCommand = new RelayCommand(
            () => _ = RefreshUpgradesAsync(),
            () => CanRefresh());
        GrantCommand = new RelayCommand<UnitUpgradeItem>(
            item => _ = GrantUpgradeAsync(item),
            item => CanGrant(item));
        UpdateState();
    }

    public ObservableCollection<UnitUpgradeItem> AvailableUpgrades { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand<UnitUpgradeItem> GrantCommand { get; }

    /// <summary>当前面板状态，驱动 UI 显示。</summary>
    public UnitUpgradeState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotReady));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsListVisible));
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    /// <summary>状态文案，始终可见，根据 State 变化。</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (string.Equals(_statusMessage, value, StringComparison.Ordinal)) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>True when at least one upgrade is available.</summary>
    public bool HasUpgrades => AvailableUpgrades.Count > 0;

    /// <summary>patch 未安装或 controller 不可用。</summary>
    public bool IsNotReady => State == UnitUpgradeState.NotReady;

    /// <summary>已就绪但尚未刷新（初始或清空后）。</summary>
    public bool IsIdle => State == UnitUpgradeState.Idle;

    /// <summary>正在读取（保留，当前同步实现未真正使用）。</summary>
    public bool IsLoading => State == UnitUpgradeState.Loading;

    /// <summary>列表可见：有结果且已加载。</summary>
    public bool IsListVisible => State == UnitUpgradeState.Loaded && HasUpgrades;

    /// <summary>
    /// 由 SelectedUnitViewModel 在 patch 状态变化时调用，刷新命令可用性。
    /// </summary>
    public void RaiseCommands()
    {
        UpdateState();
        RefreshCommand.RaiseCanExecuteChanged();
        GrantCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 根据 <see cref="TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature"/>
    /// 的能力快照更新面板状态和引导文案。
    /// 能力快照是唯一准入来源，不直接检查 controller/patch/profile。
    /// </summary>
    private void UpdateState()
    {
        var cap = _getCapability(TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature);
        if (cap.State != FeatureCapabilityState.Ready)
        {
            State = UnitUpgradeState.NotReady;
            StatusMessage = cap.State switch
            {
                FeatureCapabilityState.Waiting when cap.ReasonCode == "NO_TARGET" =>
                    "请先检测并连接受支持的游戏进程。",
                FeatureCapabilityState.Waiting when cap.ReasonCode == "PATCH_NOT_INSTALLED" =>
                    "请先安装 patch 后再使用单位升级功能。",
                _ => $"单位升级功能当前不可用：{cap.Reason}"
            };
            return;
        }

        if (State == UnitUpgradeState.NotReady)
        {
            State = UnitUpgradeState.Idle;
            StatusMessage = "点击「刷新可用升级」读取当前选中单位可授予的升级。";
        }
    }

    private bool IsCapabilityReady() =>
        _getCapability(TrainerFeatureCatalog.SelectedUnitObjectUpgradeFeature).State == FeatureCapabilityState.Ready;

    private bool CanRefresh() =>
        IsCapabilityReady() && !_isBusy;

    private bool CanGrant(UnitUpgradeItem? item) =>
        IsCapabilityReady() && !_isBusy && item is not null;

    /// <summary>
    /// 异步刷新升级列表。慢速管道调用在后台线程执行，让 Loading 状态可见。
    /// </summary>
    internal async Task RefreshUpgradesAsync()
    {
        if (_isBusy) return;
        if (!IsCapabilityReady())
        {
            UpdateState();
            return;
        }

        _isBusy = true;

        try
        {
            State = UnitUpgradeState.Loading;
            StatusMessage = "正在读取单位升级数据…";
            RefreshCommand.RaiseCanExecuteChanged();
            GrantCommand.RaiseCanExecuteChanged();

            var controller = _getController();
            if (controller is null)
            {
                State = UnitUpgradeState.NotReady;
                StatusMessage = "请先检测进程并安装 patch。";
                return;
            }

            // 慢速管道调用在后台执行，UI 线程不被阻塞
            var snapshot = await Task.Run(() => controller.ReadSelectedUnitUpgrades());

            AvailableUpgrades.Clear();
            foreach (var hash in snapshot.Hashes)
            {
                if (hash == 0) continue;
                var entry = _nameResolver.TryResolveName(hash);
                AvailableUpgrades.Add(new UnitUpgradeItem
                {
                    Hash = hash,
                    Name = entry?.Name ?? $"升级 #0x{hash:X8}",
                    Description = entry?.Description ?? string.Empty
                });
            }

            if (snapshot.ThingTemplateAddress == 0)
            {
                State = UnitUpgradeState.Loaded;
                StatusMessage = "请先在游戏中选中一个单位。";
            }
            else if (snapshot.Count == 0)
            {
                State = UnitUpgradeState.Loaded;
                StatusMessage = "当前单位没有可授予的对象级升级。";
            }
            else
            {
                State = UnitUpgradeState.Loaded;
                StatusMessage = $"共 {AvailableUpgrades.Count} 项升级可用，点击「授予」对选中同类单位生效。";
            }
        }
        catch (Exception ex)
        {
            State = UnitUpgradeState.Error;
            StatusMessage = $"刷新升级列表失败：{ex.Message}";
        }
        finally
        {
            _isBusy = false;
            RefreshCommand.RaiseCanExecuteChanged();
            GrantCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// 异步授予选中升级。慢速管道调用在后台线程执行，避免卡 UI。
    /// 操作完成后 StatusMessage 持久显示结果（即使列表非空）。
    /// </summary>
    internal async Task GrantUpgradeAsync(UnitUpgradeItem? item)
    {
        if (item is null || _isBusy) return;
        if (!IsCapabilityReady())
        {
            UpdateState();
            return;
        }
        _isBusy = true;

        try
        {
            State = UnitUpgradeState.Loading;
            StatusMessage = $"正在授予升级：{item.Name}…";
            RefreshCommand.RaiseCanExecuteChanged();
            GrantCommand.RaiseCanExecuteChanged();

            var controller = _getController();
            if (controller is null)
            {
                State = UnitUpgradeState.NotReady;
                StatusMessage = "请先检测进程并安装 patch。";
                return;
            }

            // 慢速管道调用在后台执行
            var status = await Task.Run(() => controller.GrantObjectUpgradeOnSelectedSameType(item.Hash));

            // 保持 Loaded 状态以使列表仍然可见
            State = UnitUpgradeState.Loaded;
            StatusMessage = status switch
            {
                GameApiDispatchStatus.Completed => $"已授予升级：{item.Name}。",
                GameApiDispatchStatus.NoSelectedUnit => "请先在游戏内选中一个单位。",
                GameApiDispatchStatus.Disabled => "升级授予在当前状态下不可用。",
                GameApiDispatchStatus.Failed => "升级授予失败，请重试。",
                GameApiDispatchStatus.TimedOut => "升级授予超时，游戏可能已暂停。",
                GameApiDispatchStatus.NoGameTick => "游戏已暂停，无法执行。",
                _ => $"升级授予返回状态：{status}。"
            };
        }
        catch (Exception ex)
        {
            State = UnitUpgradeState.Error;
            StatusMessage = $"授予升级失败：{ex.Message}";
        }
        finally
        {
            _isBusy = false;
            RefreshCommand.RaiseCanExecuteChanged();
            GrantCommand.RaiseCanExecuteChanged();
        }
    }
}

/// <summary>
/// 单位升级面板的 UI 状态机。
/// </summary>
public enum UnitUpgradeState
{
    /// <summary>patch 未安装或 controller 不可用。</summary>
    NotReady,
    /// <summary>已就绪，等待用户点击刷新。</summary>
    Idle,
    /// <summary>正在读取（预留）。</summary>
    Loading,
    /// <summary>已加载完成（可能为空列表）。</summary>
    Loaded,
    /// <summary>操作出错。</summary>
    Error
}
