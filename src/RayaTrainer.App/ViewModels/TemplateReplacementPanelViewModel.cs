using System.Collections.ObjectModel;
using RayaTrainer.Core.Agent;
using RayaTrainer.Core.Features;
using RayaTrainer.Core.Manifest;

namespace RayaTrainer.App.ViewModels;

public sealed class TemplateReplacementPanelViewModel : ViewModelBase
{
    private readonly TrainerFeature _modelFeature;
    private readonly TrainerFeature _weaponFeature;
    private readonly TrainerFeature _weaponSetFeature;
    private readonly TrainerFeature _weaponAttachFeature;
    private readonly Func<ITrainerFeatureController?> _controllerProvider;
    private readonly Func<bool> _canExecute;
    private readonly Action<string> _setStatusMessage;
    private string _targetUnitIdText = "0x00000000";
    private string _donorUnitIdText = "0x00000000";
    private int _weaponSetListIndex;
    private string _weaponHashText = "0x00000000";
    private int _weaponOrderingIndex;
    private int _shufflePoolSize = 8;

    /// <summary>武器集槽位选择项：全部 / 仅 Hardpoint / 仅炮塔（cmd 33 ListSelector）。</summary>
    /// <remarks>必须是实例属性：WPF 实例路径绑定解析不到 static 成员，静态会让下拉栏空空如也。</remarks>
    public IReadOnlyList<string> WeaponSetListOptions { get; } =
    [
        "全部槽位",
        "仅 Hardpoint 武器",
        "仅炮塔武器"
    ];

    public IReadOnlyList<string> WeaponOrderingOptions { get; } =
    [
        "主武器 (PRIMARY)",
        "副武器 (SECONDARY)",
        "三号槽 (TERTIARY)",
        "四号槽 (QUATERNARY)",
        "五号槽 (QUINARY)"
    ];

    public TemplateReplacementPanelViewModel(
        TrainerFeature modelFeature,
        TrainerFeature weaponFeature,
        TrainerFeature weaponSetFeature,
        TrainerFeature weaponAttachFeature,
        Func<ITrainerFeatureController?> controllerProvider,
        Func<bool> canExecute,
        Action<string> setStatusMessage)
    {
        _modelFeature = modelFeature;
        _weaponFeature = weaponFeature;
        _weaponSetFeature = weaponSetFeature;
        _weaponAttachFeature = weaponAttachFeature;
        _controllerProvider = controllerProvider;
        _canExecute = canExecute;
        _setStatusMessage = setStatusMessage;
        ReplaceModelCommand = new RelayCommand(() => _ = ReplaceModelAsync(), CanExecute);
        ReplaceWeaponCommand = new RelayCommand(() => _ = ReplaceWeaponAsync(), CanExecute);
        ReplaceWeaponSetCommand = new RelayCommand(() => _ = ReplaceWeaponSetAsync(), CanExecute);
        AttachWeaponCommand = new RelayCommand(() => _ = AttachWeaponAsync(), CanExecute);
        ReadWeaponSlotsCommand = new RelayCommand(() => _ = ReadWeaponSlotsAsync(), CanExecute);
        ShuffleWeaponsCommand = new RelayCommand(() => _ = ShuffleWeaponsAsync(), CanExecute);
        AttachSlotEntryCommand = new RelayCommand<WeaponSlotLine>(
            line => _ = AttachSlotEntryAsync(line), _ => CanExecute());
    }

    public RelayCommand ReplaceModelCommand { get; }

    public RelayCommand ReplaceWeaponCommand { get; }

    public RelayCommand ReplaceWeaponSetCommand { get; }

    public RelayCommand AttachWeaponCommand { get; }

    public RelayCommand ReadWeaponSlotsCommand { get; }

    public RelayCommand ShuffleWeaponsCommand { get; }

    public RelayCommand<WeaponSlotLine> AttachSlotEntryCommand { get; }

    public ObservableCollection<WeaponSlotLine> WeaponSlotLines { get; } = [];

    public string TargetUnitIdText
    {
        get => _targetUnitIdText;
        set
        {
            _targetUnitIdText = value;
            OnPropertyChanged();
        }
    }

    public string DonorUnitIdText
    {
        get => _donorUnitIdText;
        set
        {
            _donorUnitIdText = value;
            OnPropertyChanged();
        }
    }

    public int WeaponSetListIndex
    {
        get => _weaponSetListIndex;
        set
        {
            _weaponSetListIndex = value;
            OnPropertyChanged();
        }
    }

    public string WeaponHashText
    {
        get => _weaponHashText;
        set
        {
            _weaponHashText = value;
            OnPropertyChanged();
        }
    }

    public int WeaponOrderingIndex
    {
        get => _weaponOrderingIndex;
        set
        {
            _weaponOrderingIndex = value;
            OnPropertyChanged();
        }
    }

    public int ShufflePoolSize
    {
        get => _shufflePoolSize;
        set
        {
            _shufflePoolSize = value;
            OnPropertyChanged();
        }
    }

    public string TargetUnitIdHelpText => "被替换模板的单位 ID；主要影响后续创建的同模板单位。";

    public string DonorUnitIdHelpText => "来源模板的单位 ID；用于提供模型记录或 WeaponSetUpdate 槽定义。";

    public string ReplaceTemplateModelHelpText => "按目标/来源单位模板 ID 执行模板级模型替换；MVP 不修改已在场单位实例，也不复制武器、炮塔或特殊模块。";

    public string ReplaceTemplateWeaponHelpText => "按目标/来源单位模板 ID 执行模板级武器模块替换；MVP 复用来源模板的 WeaponSetUpdate 槽定义，主要影响后续创建的同模板单位。";

    public string WeaponSetListHelpText => "选择接管哪些槽定义列表：全部、仅 Hardpoint（主武器组）或仅炮塔（含炮塔 AI 设置）。";

    public string WeaponHashHelpText => "武器名哈希（如 0x…）；经引擎资产库解析为 WeaponTemplate 后写入目标模板的默认武器条目。";

    public string WeaponOrderingHelpText => "要替换的武器槽位序；只改写该槽位序上当前已绑定的默认武器条目，升级换装链不受影响。";

    public string ReplaceWeaponSetHelpText => "把来源模板选中的槽定义列表描述符整体接管到目标模板；主要影响后续创建的同模板单位。";

    public string AttachWeaponHelpText => "按武器名哈希把武器直挂到目标模板的指定槽位序；主要影响后续创建的同模板单位。";

    public string ReadWeaponSlotsHelpText => "读取来源单位模板当前绑定的全部武器槽条目（含炮塔槽与升级条件条目），填充下方列表。";

    public string ShuffleHelpText => "从增援目录随机取样若干单位模板，把它们各槽位序的默认武器跨模板随机置换；只影响之后生产出的单位（错误科技式效果）。";

    public string ShufflePoolSizeHelpText => "参与打乱的单位数量（2-16），从增援目录随机取样。";

    public TemplateModelReplacementSettings GetTemplateModelReplacementSettings()
    {
        return TemplateModelReplacementSettings.Parse(TargetUnitIdText, DonorUnitIdText);
    }

    public TemplateWeaponReplacementSettings GetTemplateWeaponReplacementSettings()
    {
        return TemplateWeaponReplacementSettings.Parse(TargetUnitIdText, DonorUnitIdText);
    }

    public TemplateWeaponSetReplacementSettings GetTemplateWeaponSetReplacementSettings()
    {
        return TemplateWeaponSetReplacementSettings.Parse(
            TargetUnitIdText, DonorUnitIdText, ResolveListSelector());
    }

    public WeaponAttachSettings GetWeaponAttachSettings()
    {
        return WeaponAttachSettings.Parse(TargetUnitIdText, WeaponHashText, (uint)WeaponOrderingIndex);
    }

    public async Task ReplaceModelAsync()
    {
        await DispatchAsync(
            _modelFeature,
            () => GetTemplateModelReplacementSettings(),
            (controller, settings) => controller.WriteTemplateModelReplacementSettings(settings),
            settings => $"已派发模板模型替换：{UnitCodeParser.Format(settings.TargetUnitId)} <- {UnitCodeParser.Format(settings.DonorUnitId)}。",
            "模板模型替换");
    }

    public async Task ReplaceWeaponAsync()
    {
        await DispatchAsync(
            _weaponFeature,
            () => GetTemplateWeaponReplacementSettings(),
            (controller, settings) => controller.WriteTemplateWeaponReplacementSettings(settings),
            settings => $"已派发模板武器替换：{UnitCodeParser.Format(settings.TargetUnitId)} <- {UnitCodeParser.Format(settings.DonorUnitId)}。",
            "模板武器替换");
    }

    public async Task ReplaceWeaponSetAsync()
    {
        await DispatchAsync(
            _weaponSetFeature,
            () => GetTemplateWeaponSetReplacementSettings(),
            (controller, settings) => controller.WriteTemplateWeaponSetReplacementSettings(settings),
            settings => $"已派发武器集替换（{WeaponSetListOptions[WeaponSetListIndex]}）：{UnitCodeParser.Format(settings.TargetUnitId)} <- {UnitCodeParser.Format(settings.DonorUnitId)}。",
            "武器集替换");
    }

    public async Task AttachWeaponAsync()
    {
        await DispatchAsync(
            _weaponAttachFeature,
            () => GetWeaponAttachSettings(),
            (controller, settings) => controller.WriteWeaponAttachSettings(settings),
            settings => $"已派发武器直挂：{UnitCodeParser.Format(settings.TargetUnitId)} <- 武器 {UnitCodeParser.Format(settings.WeaponHash)}。",
            "武器直挂");
    }

    public void RaiseCommandStates()
    {
        ReplaceModelCommand.RaiseCanExecuteChanged();
        ReplaceWeaponCommand.RaiseCanExecuteChanged();
        ReplaceWeaponSetCommand.RaiseCanExecuteChanged();
        AttachWeaponCommand.RaiseCanExecuteChanged();
        ReadWeaponSlotsCommand.RaiseCanExecuteChanged();
        ShuffleWeaponsCommand.RaiseCanExecuteChanged();
    }

    private bool CanExecute()
    {
        return _canExecute() && _controllerProvider() is not null;
    }

    private async Task DispatchAsync<TSettings>(
        TrainerFeature feature,
        Func<TSettings> createSettings,
        Action<ITrainerFeatureController, TSettings> writeSettings,
        Func<TSettings, string> successMessage,
        string operationName)
    {
        var controller = _controllerProvider();
        if (controller is null)
        {
            _setStatusMessage("请先检测进程并安装 patch。");
            return;
        }

        try
        {
            var settings = createSettings();
            writeSettings(controller, settings);
            var result = await controller.TriggerActionAndWaitForConsumptionAsync(
                feature,
                timeout: FeatureDispatchDefaults.Timeout,
                pollInterval: FeatureDispatchDefaults.PollInterval);
            _setStatusMessage(result == ActionDispatchResult.Consumed
                ? successMessage(settings)
                : $"{operationName}动作已写入，但尚未被游戏循环消费。");
        }
        catch (Exception ex)
        {
            _setStatusMessage($"{operationName}失败：{ex.Message}");
        }
    }

    private uint ResolveListSelector()
    {
        return WeaponSetListIndex switch
        {
            1 => TemplateWeaponSetReplacementSettings.ListHardpoint,
            2 => TemplateWeaponSetReplacementSettings.ListTurret,
            _ => TemplateWeaponSetReplacementSettings.ListAll
        };
    }

    private static readonly string[] ListNames = ["Hardpoint", "炮塔", "层级炮塔", "载具内"];

    public async Task AttachSlotEntryAsync(WeaponSlotLine? line)
    {
        if (line is null || line.Entry.UpgradeGated)
        {
            return;
        }

        var controller = _controllerProvider();
        if (controller is null)
        {
            _setStatusMessage("请先检测进程并安装 patch。");
            return;
        }

        try
        {
            var target = UnitCodeParser.Parse(TargetUnitIdText);
            var selector = 1u << line.Entry.ListIndex;
            var status = await Task.Run(() => controller.AttachWeaponTemplate(
                target, line.Entry.WeaponTemplateAddress, line.Entry.Ordering, selector));
            if (status == GameApiDispatchStatus.Completed)
            {
                _setStatusMessage(
                    $"已直挂：来源武器 0x{line.Entry.WeaponTemplateAddress:X8} -> 目标 0x{target:X8} 槽位序 {line.Entry.Ordering}（只影响之后创建的同模板单位）。");
            }
            else
            {
                _setStatusMessage($"直挂动作已写入但未完成（{status}）。");
            }
        }
        catch (Exception ex)
        {
            _setStatusMessage($"一键直挂失败：{ex.Message}");
        }
    }

    public async Task ReadWeaponSlotsAsync()
    {
        var controller = _controllerProvider();
        if (controller is null)
        {
            _setStatusMessage("请先检测进程并安装 patch。");
            return;
        }

        try
        {
            var donor = UnitCodeParser.Parse(DonorUnitIdText);
            var snapshot = await Task.Run(
                () => controller.ReadTemplateWeaponSetSnapshot(donor, TemplateWeaponSetReplacementSettings.ListAll));
            WeaponSlotLines.Clear();
            if (snapshot.Entries.Count == 0)
            {
                WeaponSlotLines.Add(new WeaponSlotLine("（该模板没有武器集模块，或所选列表为空。）", default));
            }
            else
            {
                foreach (var entry in snapshot.Entries)
                {
                    var orderingName = entry.Ordering <= 4
                        ? WeaponOrderingOptions[(int)entry.Ordering]
                        : $"槽位序 {entry.Ordering}";
                    var gate = entry.UpgradeGated ? "（升级条件，不参与直挂）" : string.Empty;
                    WeaponSlotLines.Add(new WeaponSlotLine(
                        $"{orderingName}  [{ListNames[Math.Min(entry.ListIndex, ListNames.Length - 1)]}]  武器 0x{entry.WeaponTemplateAddress:X8}{gate}",
                        entry));
                }
                if (snapshot.Truncated)
                {
                    WeaponSlotLines.Add(new WeaponSlotLine("（条目超过 10 条，已截断。）", default));
                }
            }
            _setStatusMessage($"已读取来源 0x{donor:X8} 的 {snapshot.Entries.Count} 个武器槽条目。");
        }
        catch (Exception ex)
        {
            _setStatusMessage($"读取武器槽失败：{ex.Message}");
        }
    }

    public async Task ShuffleWeaponsAsync()
    {
        var controller = _controllerProvider();
        if (controller is null)
        {
            _setStatusMessage("请先检测进程并安装 patch。");
            return;
        }

        try
        {
            var poolSize = Math.Clamp(ShufflePoolSize, 2, 16);
            var pool = await Task.Run(BuildShufflePool);
            if (pool.Count < 2)
            {
                _setStatusMessage("增援目录可用单位不足，无法打乱。");
                return;
            }

            // 取样后交给编排器：按槽位序做跨模板指针置换。
            var sampled = new List<uint>(pool);
            var rng = new Random();
            for (var i = sampled.Count - 1; i > 0 && sampled.Count > poolSize; i--)
            {
                if (rng.Next(i + 1) < sampled.Count - poolSize)
                {
                    sampled.RemoveAt(rng.Next(sampled.Count));
                }
            }

            var outcome = await Task.Run(() => WeaponSetShuffler.ShuffleDefaults(
                controller, sampled, TemplateWeaponSetReplacementSettings.ListAll, rng));
            _setStatusMessage(
                $"武器打乱完成：读取 {outcome.TemplatesRead} 个模板，直挂 {outcome.AttachSucceeded}/{outcome.AttachAttempted} 次成功。");
        }
        catch (Exception ex)
        {
            _setStatusMessage($"武器打乱失败：{ex.Message}");
        }
    }

    private IReadOnlyList<uint> BuildShufflePool()
    {
        // 池 = 增援目录（含用户自定义文件与 mod 资产包）的全部单位码。
        return ReinforcementUnitCatalog.LoadWithCustomFile()
            .Select(u => u.Code)
            .Distinct()
            .ToList();
    }
}

/// <summary>读取结果列表的一行：展示文本 + 背后的武器槽条目（供一键直挂）。</summary>
public sealed record WeaponSlotLine(string Display, TemplateWeaponSlotEntry Entry);
